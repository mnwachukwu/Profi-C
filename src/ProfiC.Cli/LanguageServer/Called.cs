using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>What is being called where the cursor sits, and which argument it is writing.</para>
/// <para>Asked by two answers that would each otherwise work it out: what the place will take, and
/// what to say about the parameter being written. Two accounts of "which function is this, and
/// which argument am I in" agree until the day they do not, and that day shows up as a hover
/// naming one parameter above a list ordered by another one's type.</para>
/// <para>Asked of one node rather than found from the cursor, because who claims the cursor is
/// the caller's question: a lambda written as an argument is a position in the lambda and not in
/// the call, and only something walking outward knows it has passed one.</para>
/// </summary>
public sealed class Called
{
    private Called(
        IReadOnlyList<IReadOnlyList<TypeSymbol?>> forms, int at, FunctionSymbol? function)
    {
        Forms = forms;
        At = at;
        Function = function;
    }

    /// <summary>
    /// <para>What each form of the call takes, one list of parameter types per form.</para>
    /// <para>More than one where several overloads could still be meant, which is the ordinary
    /// state of a call halfway through being written. A null entry accepts any type, which is how
    /// <c>Console.Write</c> is written.</para>
    /// </summary>
    public IReadOnlyList<IReadOnlyList<TypeSymbol?>> Forms { get; }

    /// <summary>Which argument the cursor is in, counted from zero.</summary>
    public int At { get; }

    /// <summary>
    /// The function being called where a program declared it, and null for one the language
    /// provides, for a value that happens to be a function, and for a constructor chosen from
    /// several — none of which offers a parameter's name to show.
    /// </summary>
    public FunctionSymbol? Function { get; }

    /// <summary>The parameter being written, where there is a declared one to name.</summary>
    public ParameterSymbol? Parameter =>
        Function is { } function && At < function.Parameters.Count ? function.Parameters[At] : null;

    /// <summary>
    /// How many parameters the call takes, where every form agrees, and null where they differ —
    /// saying "of 3" while another form takes two would be worse than saying nothing.
    /// </summary>
    public int? Count =>
        Forms.Select(form => form.Count).Distinct().ToArray() is [int only] ? only : null;

    /// <summary>
    /// The type the argument being written has to be, where every form that reaches this position
    /// asks for the same one.
    /// </summary>
    public TypeSymbol? Type =>
        Accepted().Distinct().ToArray() is [TypeSymbol only] ? only : null;

    /// <summary>Every type a form accepts at this position, for the forms that reach it.</summary>
    public IEnumerable<TypeSymbol> Accepted() =>
        Forms.Where(form => At < form.Count).Select(form => form[At]).OfType<TypeSymbol>();

    /// <summary>
    /// What one node says is being called, or null where the cursor is not among its arguments.
    /// </summary>
    public static Called? Of(SyntaxNode node, SemanticModel model, int offset)
    {
        ArgumentNullException.ThrowIfNull(model);

        return node switch
        {
            CallExpr call when offset > call.Callee.Span.EndOffset =>
                Reading(model, call.Callee, NodeAt.ArgumentAt(call.Arguments, offset)),

            NewExpr construction when construction.HasName
                                      && offset > construction.NameSpan.EndOffset =>
                Building(
                    model,
                    construction,
                    NodeAt.ArgumentAt(construction.Arguments, offset)),

            _ => null,
        };
    }

    /// <summary>
    /// A call, read from whichever of the three things a callee can be resolved to. Nothing found
    /// is still a call — the cursor is in an argument list whether or not the compiler worked out
    /// whose.
    /// </summary>
    private static Called Reading(SemanticModel model, Expression callee, int at)
    {
        if (model.GetSymbol(callee) is FunctionSymbol function)
        {
            return new Called([Takes(function)], at, function);
        }

        if (model.GetBuiltIn(callee) is { } provided && BuiltIns.Find(provided) is { } member)
        {
            return new Called([member.ParameterTypes], at, null);
        }

        return model.GetType(callee) is FunctionType signature
            ? new Called([[.. signature.ParameterTypes.Select(p => (TypeSymbol?)p)]], at, null)
            : new Called([], at, null);
    }

    /// <summary>
    /// <para>A <c>new</c>, read from the forms the constructed type accepts.</para>
    /// <para>A type the language owns declares nothing a program can read, so its forms come from
    /// the catalog rather than from among its members.</para>
    /// </summary>
    private static Called Building(SemanticModel model, NewExpr construction, int at)
    {
        if (model.GetSymbol(construction) is not TypeSymbol type)
        {
            return new Called([], at, null);
        }

        if (BuiltIns.FindModel(type.Name) is { } known)
        {
            return new Called([.. known.Constructors.Select(form => form.ParameterTypes)], at, null);
        }

        if (type is not DeclaredTypeSymbol declared)
        {
            return new Called([], at, null);
        }

        FunctionSymbol[] constructors =
        [
            .. declared.Lookup(declared.Name)
                .OfType<FunctionSymbol>()
                .Where(candidate => candidate.IsConstructor),
        ];

        // Named only where the type declares one way to build it. Which of several a program gets
        // is settled by what it writes, and half of that is not written yet — so every form is
        // offered to whatever is ranking, and nothing is named as though the choice were made.
        return new Called(
            [.. constructors.Select(Takes)],
            at,
            constructors is [FunctionSymbol only] ? only : null);
    }

    private static IReadOnlyList<TypeSymbol?> Takes(FunctionSymbol function) =>
        [.. function.Parameters.Select(parameter => (TypeSymbol?)parameter.Type)];
}
