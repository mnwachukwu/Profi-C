using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using TypeConversions = ProfiC.Compiler.Semantics.Conversions;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>What the place the cursor sits in would accept.</para>
/// <para><b>A list of names in scope is the answer to a question nobody asked.</b> Somebody
/// typing after <c>Animal frank = </c> is not choosing among everything the file can reach; they
/// are choosing among the few things that could stand there, and the compiler knows which those
/// are because it is about to check exactly that. Saying so is the difference between a list to
/// scroll and a list to pick from.</para>
/// <para><b>Worked out from the tree as it parsed, with nothing inserted into it.</b> A position
/// the reader has not filled in is not a hole in the syntax: the parser stands an expression in
/// its place and carries on, so the statement around the cursor is there to be asked. That is
/// what makes this cheap — the compilation the server already ran answers it — and it is what
/// keeps a half-written line from being reasoned about twice, once by the parser and once
/// here.</para>
/// <para>The one thing it costs is that a stood-in expression begins at the <em>next</em> token
/// rather than at the cursor, since that is where the parser noticed it was missing. So a
/// position is claimed by where the cursor sits among a node's parts rather than by which part
/// contains it, and every rule below is written that way.</para>
/// <para>The first node claiming the cursor settles it, even where it turns out to know nothing.
/// A call whose function did not resolve is still an argument position, and walking outward past
/// it would answer about the statement the call is in — offering what fits an <c>if</c> at a spot
/// where the <c>if</c> has no say.</para>
/// </summary>
public sealed class Wanted
{
    private readonly IReadOnlyList<TypeSymbol> _types;

    private Wanted(IReadOnlyList<TypeSymbol> types) => _types = types;

    /// <summary>
    /// <para>What could be written at an offset, or null where nothing is known about it.</para>
    /// <para>Null is the ordinary answer and not a failure. Most of a file is somewhere the
    /// language accepts any value at all, and a caller that has nothing to sort by should sort by
    /// nothing rather than by a guess.</para>
    /// </summary>
    public static Wanted? At(CompilationUnit unit, SemanticModel model, int offset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);

        int settled = Settled(unit.Source.Text, offset);
        IReadOnlyList<SyntaxNode> spine = NodeAt.Enclosing(unit, settled);

        if (From(spine, 0, model, settled) is not { } accepted)
        {
            return null;
        }

        // A type the compiler could not work out says nothing about what fits it, and every type
        // is assignable to it — so keeping one would sort the whole list to the top and read as an
        // opinion where there is none.
        TypeSymbol[] known = [.. accepted.Where(type => !type.IsError)];

        return known.Length > 0 ? new Wanted(known) : null;
    }

    /// <summary>
    /// <para>The walk outward from a point on the spine, stopping at the first node that claims
    /// the cursor.</para>
    /// <para>Taken from a point rather than always from the innermost so that a node can ask what
    /// the place <em>it</em> sits in would take — which is how a lambda learns what it has to
    /// yield. Each such question starts further out than the node asking it, so the walk always
    /// gets shorter and cannot circle.</para>
    /// </summary>
    private static IReadOnlyList<TypeSymbol>? From(
        IReadOnlyList<SyntaxNode> spine, int at, SemanticModel model, int offset)
    {
        for (int index = at; index < spine.Count; index++)
        {
            if (Accepted(spine, index, model, offset) is { } accepted)
            {
                return accepted;
            }
        }

        return null;
    }

    /// <summary>
    /// <para>Whether a value of a type could be written where the cursor is.</para>
    /// <para>More than one type where the position is reached by several overloads. Fitting any
    /// of them is fitting, since which one a program gets is settled by what it writes.</para>
    /// </summary>
    public bool Fits(TypeSymbol? type) =>
        type is { IsError: false } given
        && _types.Any(wanted => TypeConversions.IsAssignable(given, wanted));

    /// <summary>
    /// <para>The types one node would accept at an offset: null where the cursor is not in a
    /// position that node governs, and empty where it is and the node knows nothing.</para>
    /// <para>The distinction is the whole reason this is not just a type. Both answers offer no
    /// sorting, but only one of them lets the walk continue outward.</para>
    /// </summary>
    private static IReadOnlyList<TypeSymbol>? Accepted(
        IReadOnlyList<SyntaxNode> spine, int at, SemanticModel model, int offset) =>
        spine[at] switch
        {
            VarDeclStmt declaration when Named(declaration, offset) =>
                Only(declaration.Type is { } written ? model.GetType(written) : null),

            FieldDecl field when Named(field, offset) => Only(model.GetType(field.Type)),

            AssignmentStmt assignment when offset > assignment.Target.Span.EndOffset =>
                Only(model.GetType(assignment.Target)),

            IfStmt statement when Ahead(statement.Condition, offset) => Truth,

            ElseIfClause clause when Ahead(clause.Condition, offset) => Truth,

            WhileStmt loop when Ahead(loop.Condition, offset) => Truth,

            LoopUntilStmt loop when Behind(loop.Body, offset) => Truth,

            ForStmt loop when Ahead(loop.Step ?? loop.Bound, offset) => Counting,

            CallExpr or NewExpr => Called.Of(spine[at], model, offset) is { } call
                ? [.. call.Accepted()]
                : null,

            // Nowhere inside a lambda is a position in the call the lambda was handed to, so this
            // claims the cursor whichever form it is. What the place would take is known only for
            // the inline one, where the body is the expression: a line in a block body is a place
            // for a statement, and a statement is not a value.
            LambdaExpr lambda =>
                lambda.IsExpressionBodied ? Promised(spine, at, model, offset, lambda) : [],

            YieldStmt { Value: not null } => Yielded(spine, at, model, offset),

            // Nothing else may be thrown, and every exception a program declares descends from
            // it, so this is the whole of what fits rather than a guess at the nearest one.
            ThrowStmt => Only(BuiltInTypes.Of("Exception")),

            _ => null,
        };

    /// <summary>
    /// <para>Where the cursor is, counted back over the blank space in front of it to the last
    /// thing somebody typed.</para>
    /// <para><b>A node's span ends at the last token the parser read</b>, so a caret parked one
    /// space past an <c>=</c> is outside the declaration it is plainly part of. That is not an
    /// edge: a space is what you type after a keyword or an operator, so the caret is there for
    /// every position that has not been filled in yet — which is every position this exists
    /// for.</para>
    /// <para>Blank space on the line only. Reaching back across a line break would land in the
    /// statement above, and an empty line inside a function is not a position in that statement.
    /// It is a position in the function, which wants nothing in particular.</para>
    /// </summary>
    private static int Settled(string text, int offset)
    {
        int at = Math.Clamp(offset, 0, text.Length);

        while (at > 0 && text[at - 1] is ' ' or '\t')
        {
            at--;
        }

        return at;
    }

    private static readonly IReadOnlyList<TypeSymbol> Truth = [PrimitiveType.Boolean];

    private static readonly IReadOnlyList<TypeSymbol> Counting = [PrimitiveType.Integer];

    private static IReadOnlyList<TypeSymbol> Only(TypeSymbol? type) => type is null ? [] : [type];

    /// <summary>
    /// Whether the cursor is past the name a node declares or constructs, which is the whole of
    /// what follows it: an initializer for a declaration, arguments for a <c>new</c>.
    /// </summary>
    private static bool Named(SyntaxNode node, int offset) =>
        node.HasName && offset > node.NameSpan.EndOffset;

    /// <summary>
    /// <para>Whether the cursor is at or before the end of an expression written in front of the
    /// body it governs.</para>
    /// <para>Everything ahead of that expression inside the node is the word that opened it, so
    /// this is the whole of the expression's position — including the case where none has been
    /// typed and the one standing in for it begins after the cursor rather than at it.</para>
    /// </summary>
    private static bool Ahead(Expression expression, int offset) =>
        offset <= expression.Span.EndOffset;

    /// <summary>
    /// The same reading for a condition written after the body it governs, where everything past
    /// the last statement is the word and the condition.
    /// </summary>
    private static bool Behind(IReadOnlyList<Statement> body, int offset) =>
        body.Count == 0 || offset > body[^1].Span.EndOffset;

    /// <summary>
    /// <para>What the function a <c>yield</c> belongs to promises.</para>
    /// <para>Found by walking outward rather than by asking the statement, since a
    /// <c>yield</c> belongs to the nearest function around it and a lambda written inside another
    /// function is nearer than the function is.</para>
    /// </summary>
    private static IReadOnlyList<TypeSymbol> Yielded(
        IReadOnlyList<SyntaxNode> spine, int at, SemanticModel model, int offset)
    {
        for (int index = at + 1; index < spine.Count; index++)
        {
            switch (spine[index])
            {
                case LambdaExpr lambda:
                    return Promised(spine, index, model, offset, lambda);

                case FunctionDecl declaration:
                    return model.GetSymbol(declaration) is FunctionSymbol { ReturnType: { } result }
                        ? [result]
                        : [];
            }
        }

        return [];
    }

    /// <summary>
    /// <para>What a lambda has to yield.</para>
    /// <para><b>Its own worked-out type is asked first and is usually no help.</b> A lambda has no
    /// type of its own: the one it ends up with is read off its body, and the body is the thing
    /// not written yet — so what comes back while somebody is typing is a function type yielding
    /// an error, which is the state this exists to answer in.</para>
    /// <para>What settles it is what settles it for the compiler: the type the lambda is being
    /// written into. That is whatever the place the lambda sits in would take, read through to
    /// what it yields — so <c>(word) yield </c> handed to a parameter of
    /// <c>boolean delegate(string)</c> knows a boolean belongs there.</para>
    /// </summary>
    private static IReadOnlyList<TypeSymbol> Promised(
        IReadOnlyList<SyntaxNode> spine,
        int at,
        SemanticModel model,
        int offset,
        LambdaExpr lambda)
    {
        if (Result(model, lambda) is { IsError: false } known)
        {
            return [known];
        }

        return
        [
            .. (From(spine, at + 1, model, offset) ?? [])
                .Select(Targeted)
                .OfType<FunctionType>()
                .Select(signature => signature.ReturnType)
                .OfType<TypeSymbol>(),
        ];
    }

    private static TypeSymbol? Result(SemanticModel model, LambdaExpr lambda) =>
        model.GetType(lambda) is FunctionType signature ? signature.ReturnType : null;

    /// <summary>
    /// The function type a lambda is being written into, looked at through an optional — the
    /// reading the checker gives it too, since one written into <c>boolean delegate(string)?</c>
    /// is wrapped on the way in and what it has to be is the type underneath.
    /// </summary>
    private static FunctionType? Targeted(TypeSymbol type) => type switch
    {
        FunctionType signature => signature,
        OptionalType optional => Targeted(optional.UnderlyingType),
        _ => null,
    };
}
