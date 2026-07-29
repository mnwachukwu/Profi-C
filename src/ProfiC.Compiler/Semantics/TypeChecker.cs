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

    /// <summary>Checks a resolved compilation unit.</summary>
    public static void Check(
        CompilationUnit unit,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        TypeChecker checker = new(model, diagnostics);

        foreach (Declaration declaration in unit.Declarations)
        {
            checker.CheckDeclaration(declaration);
        }
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

        _currentFunction = _model.GetSymbol(function) as FunctionSymbol;

        // A named function nested inside a lambda is checked against its own signature.
        _lambdaYields = null;

        try
        {
            CheckStatements(function.Body);
        }
        finally
        {
            _currentFunction = saved;
            _lambdaYields = savedYields;
        }
    }

    // ---- Conversions ------------------------------------------------------------------------

    /// <summary>
    /// Requires that a value fit where it is being used, reporting if it does not. A
    /// conversion that exists but must be written gets its own message naming the call.
    /// </summary>
    private void RequireAssignable(TypeSymbol from, TypeSymbol to, SyntaxNode node)
    {
        if (from.IsError || to.IsError)
        {
            return;
        }

        // A call that yields nothing has no result to convert, so naming its type would
        // describe the types correctly and the mistake badly.
        if (ReferenceEquals(from, PrimitiveType.Void))
        {
            Report(DiagnosticDescriptors.ValueExpected, node);
            return;
        }

        switch (Conversions.Classify(from, to))
        {
            case ConversionKind.Identity:
                return;

            case ConversionKind.Implicit:
                RecordConversion(node, from, to);
                return;

            case ConversionKind.Explicit:
                Report(
                    DiagnosticDescriptors.ConversionMustBeExplicit,
                    node,
                    from.WithArticleCapitalized(),
                    to.WithArticle(),
                    ExplicitConversionCall(from, to));
                return;

            default:
                // Reading an optional where a plain value is wanted has its own message,
                // since the fix is one of three named members rather than a conversion.
                if (from is OptionalType optional
                    && Conversions.IsAssignable(optional.UnderlyingType, to))
                {
                    Report(DiagnosticDescriptors.OptionalMustBeUnwrapped, node, from.WithArticle());
                    return;
                }

                Report(DiagnosticDescriptors.CannotConvert, node, from.WithArticle(), to.WithArticle());
                return;
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

            _ when ReferenceEquals(from, PrimitiveType.String)
                   && to is SetType => ConversionOperation.StringToCharacters,

            _ when from is SetType
                   && ReferenceEquals(to, PrimitiveType.String) => ConversionOperation.CharactersToString,

            // Reaching an ancestor changes nothing at run time, so it is not recorded.
            _ => null,
        };

        if (operation is { } needed)
        {
            _model.RecordConversion(node, needed, to);
        }
    }

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
