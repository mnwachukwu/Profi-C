using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Works out the type of every expression, and rejects the ones that do not fit.</para>
/// <para>Runs after the resolver, so every name already refers to something. What is left is
/// whether the things those names refer to can be combined the way the program combines
/// them.</para>
/// <para>An <see cref="ErrorType"/> anywhere in an expression makes the whole expression an
/// error type, silently. That is what keeps a single mistake from being reported once by the
/// resolver and then again by every operator that touches it.</para>
/// </summary>
public sealed partial class TypeChecker
{
    private readonly SemanticModel _model;

    /// <summary>
    /// <para>What the surrounding type wants each lambda to hand back, where it said.</para>
    /// <para>Kept beside the lambda rather than passed down, because a lambda's body is
    /// reached through the ordinary expression walk and threading an expected type through
    /// every node would say something about all of them to serve one.</para>
    /// </summary>
    private readonly Dictionary<LambdaExpr, TypeSymbol> _wantedResults = [];
    private readonly DiagnosticBag _diagnostics;

    /// <summary>The function being checked, for deciding what <c>yield</c> may carry.</summary>
    private FunctionSymbol? _currentFunction;

    /// <summary>The type whose members are being checked, for <c>this</c>.</summary>
    private DeclaredTypeSymbol? _currentType;

    /// <summary>
    /// The types yielded by the block-bodied lambda being checked, or null when not inside
    /// one. A lambda declares no result, so this is where its result comes from.
    /// </summary>
    private List<TypeSymbol>? _lambdaYields;

    private TypeChecker(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    /// <summary>Checks every file of a resolved compilation.</summary>
    public static void Check(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        TypeChecker checker = new(model, diagnostics);

        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            foreach (Declaration declaration in unit.Declarations)
            {
                checker.CheckDeclaration(declaration);
            }
        }
    }

    /// <summary>Checks one file, which is a compilation of one.</summary>
    public static void Check(
        CompilationUnit unit,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);

        Check([unit], model, diagnostics);
    }

    private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[] args) =>
        _diagnostics.Report(descriptor, node.Span, args);

    // ---- Declarations ---------------------------------------------------------------------

    private void CheckDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    CheckDeclaration(member);
                }

                break;

            case ModelDecl model:
                CheckTypeMembers(model, model.Members);
                break;

            case StructureDecl structure:
                CheckTypeMembers(structure, structure.Members);
                break;
        }
    }

    private void CheckTypeMembers(Declaration declaration, IReadOnlyList<Declaration> members)
    {
        DeclaredTypeSymbol? saved = _currentType;
        _currentType = _model.GetSymbol(declaration) as DeclaredTypeSymbol;

        try
        {
            foreach (Declaration member in members)
            {
                switch (member)
                {
                    case FieldDecl field:
                        CheckField(field);
                        break;

                    case FunctionDecl function:
                        CheckFunction(function);
                        break;

                    case ModelDecl nestedModel:
                        CheckTypeMembers(nestedModel, nestedModel.Members);
                        break;

                    case StructureDecl nestedStructure:
                        CheckTypeMembers(nestedStructure, nestedStructure.Members);
                        break;
                }
            }
        }
        finally
        {
            _currentType = saved;
        }
    }

    private void CheckField(FieldDecl field)
    {
        TypeSymbol declared = _model.GetType(field.Type) ?? ErrorType.Instance;
        bool isConstant = field.Modifiers.Has(DeclarationModifiers.Constant);

        if (field.Initializer is null)
        {
            if (isConstant)
            {
                Report(DiagnosticDescriptors.ConstantNeedsInitializer, field, field.Name);
            }

            return;
        }

        TypeSymbol actual = CheckExpressionAgainst(field.Initializer, declared);
        RequireAssignable(actual, declared, field.Initializer);

        if (isConstant)
        {
            CheckConstant(field.Name, declared, field.Initializer, field);
        }
    }

    /// <summary>
    /// Checks the two things a constant must satisfy: a type where an immutable binding
    /// really means an unchanging value, and a value known while compiling.
    /// </summary>
    private void CheckConstant(
        string name,
        TypeSymbol declared,
        Expression initializer,
        SyntaxNode declaration)
    {
        if (!Conversions.CanBeConstant(declared))
        {
            Report(DiagnosticDescriptors.ConstantTypeNotAllowed, declaration, declared.WithArticleCapitalized());
            return;
        }

        if (ConstantFolder.TryFold(initializer, _model) is null)
        {
            Report(DiagnosticDescriptors.ConstantNotFoldable, initializer, name);
        }
    }

    private void CheckFunction(FunctionDecl function)
    {
        FunctionSymbol? saved = _currentFunction;
        List<TypeSymbol>? savedYields = _lambdaYields;
        HashSet<Symbol> savedNarrowing = Known();

        _currentFunction = _model.GetSymbol(function) as FunctionSymbol;

        // A named function nested inside a lambda is checked against its own signature.
        _lambdaYields = null;

        // Nothing proven outside reaches inside. A function nested in another one is written
        // here and called somewhere else, so what was known where it was written says nothing
        // about what is true where it runs.
        _narrowed.Clear();

        // Worked out before a line of the body is read, since a closure written at the bottom
        // may be called from the top.
        // Only what this body adds is taken away again, so that a nested function finishing does
        // not forget what the one around it had established.
        Symbol[] closedOver =
            [.. AssignedByAClosureIn(function.Body ?? [])
                .Where(symbol => _reachableFromAClosure.Add(symbol))];

        try
        {
            CheckStatements(function.Body ?? []);
        }
        finally
        {
            _currentFunction = saved;
            _lambdaYields = savedYields;
            KnowOnly(savedNarrowing);
            _reachableFromAClosure.ExceptWith(closedOver);
        }
    }

    // ---- Conversions ------------------------------------------------------------------------

    /// <summary>
    /// <para>Requires that a value fit where it is being used, reporting if it does not. A
    /// conversion that exists but must be written gets its own message naming the call.</para>
    /// <para>False means it did not fit and has been reported, which lets a caller holding
    /// several of these stop before saying something further that rests on all of them
    /// having landed.</para>
    /// </summary>
    private bool RequireAssignable(TypeSymbol from, TypeSymbol to, SyntaxNode node)
    {
        if (from.IsError || to.IsError)
        {
            return true;
        }

        // A call that yields nothing has no result to convert, so naming its type would
        // describe the types correctly and the mistake badly.
        if (ReferenceEquals(from, PrimitiveType.Void))
        {
            Report(DiagnosticDescriptors.ValueExpected, node);
            return false;
        }

        switch (Conversions.Classify(from, to))
        {
            case ConversionKind.Identity:
                return true;

            case ConversionKind.Implicit:
                RecordConversion(node, from, to);
                return true;

            case ConversionKind.Explicit:
                Report(
                    DiagnosticDescriptors.ConversionMustBeExplicit,
                    node,
                    from.WithArticleCapitalized(),
                    to.WithArticle(),
                    ExplicitConversionCall(from, to));
                return false;

            default:
                // Reading an optional where a plain value is wanted has its own message,
                // since the fix is one of three named members rather than a conversion.
                if (from is OptionalType optional
                    && Conversions.IsAssignable(optional.UnderlyingType, to))
                {
                    // Where a closure can change it, the three members are not all equally
                    // useful, and one of them is no use at all.
                    if (node is Expression read
                        && _model.GetSymbol(read) is { } named
                        && _reachableFromAClosure.Contains(named))
                    {
                        Report(
                            DiagnosticDescriptors.OptionalIsReachableFromAClosure,
                            node,
                            from.WithArticle(),
                            named.Name);

                        return false;
                    }

                    Report(DiagnosticDescriptors.OptionalMustBeUnwrapped, node, from.WithArticle());
                    return false;
                }

                Report(DiagnosticDescriptors.CannotConvert, node, from.WithArticle(), to.WithArticle());
                return false;
        }
    }

    /// <summary>
    /// Writes down what a conversion will have to do, so that lowering can make it a node and
    /// the emitter can turn it into an instruction. Nothing is recorded when the value is
    /// already what is wanted.
    /// </summary>
    private void RecordConversion(SyntaxNode node, TypeSymbol from, TypeSymbol to)
    {
        ConversionOperation? operation = (from, to) switch
        {
            _ when to is OptionalType optional && !Conversions.SameType(from, optional)
                   && from is not OptionalType => ConversionOperation.WrapOptional,

            _ when ReferenceEquals(from, PrimitiveType.Integer)
                   && ReferenceEquals(to, PrimitiveType.Real) => ConversionOperation.IntegerToReal,

            _ when ReferenceEquals(from, PrimitiveType.Integer)
                   && ReferenceEquals(to, PrimitiveType.Fraction) => ConversionOperation.IntegerToFraction,

            _ when ReferenceEquals(from, PrimitiveType.Fraction)
                   && ReferenceEquals(to, PrimitiveType.Real) => ConversionOperation.FractionToReal,

            // Written to see through an optional on both sides, since a string? reaching a
            // character[]? has the same work to do on a value that is there and none at all
            // on one that is not.
            _ when IsText(from) && IsCharacters(to) => ConversionOperation.StringToCharacters,

            _ when IsCharacters(from) && IsText(to) => ConversionOperation.CharactersToString,

            // Reaching an ancestor changes nothing at run time, so it is not recorded.
            _ => null,
        };

        if (operation is { } needed)
        {
            _model.RecordConversion(node, needed, to);
        }
    }

    /// <summary>A string, or an optional one.</summary>
    private static bool IsText(TypeSymbol type) =>
        ReferenceEquals(Underlying(type), PrimitiveType.String);

    /// <summary>A set of characters, or an optional one.</summary>
    private static bool IsCharacters(TypeSymbol type) =>
        Underlying(type) is SetType set && ReferenceEquals(set.ElementType, PrimitiveType.Character);

    private static TypeSymbol Underlying(TypeSymbol type) =>
        type is OptionalType optional ? optional.UnderlyingType : type;

    private static string ExplicitConversionCall(TypeSymbol from, TypeSymbol to)
    {
        if (ReferenceEquals(from, PrimitiveType.Fraction) && ReferenceEquals(to, PrimitiveType.Real))
        {
            return "ToReal()";
        }

        if (ReferenceEquals(from, PrimitiveType.Real) && ReferenceEquals(to, PrimitiveType.Fraction))
        {
            return "ToFraction()";
        }

        return $"To{to.Display}()";
    }
}
