using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

/// <summary>
/// <para>A storage location holding one value.</para>
/// <para>Variables are cells rather than plain values because capture is by reference: a
/// lambda that closes over a local and the code around it must see the same storage, so that
/// an assignment through one is visible through the other.</para>
/// </summary>
public sealed class Cell(object? value)
{
    public object? Value { get; set; } = value;
}

/// <summary>
/// <para>An instance of a model or a structure.</para>
/// <para>One class serves both. What differs is not the shape but the handling: a structure is
/// copied wherever it is assigned, passed, or returned, and a model is not.</para>
/// </summary>
public sealed class Instance(DeclaredTypeSymbol type) : IProfiCModel
{
    /// <summary>The type this is an instance of.</summary>
    public DeclaredTypeSymbol Type { get; } = type;

    /// <summary>
    /// <para>How to render this, when its type declares a <c>ToString</c> of its own.</para>
    /// <para>Running one means evaluating a Profi-C function, which only the interpreter can
    /// do — so it hands the work back rather than this class reaching for it. Null where no
    /// type in the chain declares one, and the default below stands.</para>
    /// <para>Carried per instance rather than looked up per call because everything that
    /// prints a value ends at <c>ToString</c>: a value on its own, a value inside a set, a
    /// field of a structure. Anything that reaches this reaches the declared one too.</para>
    /// </summary>
    internal Func<Instance, string>? Renderer { get; set; }

    /// <summary>Field storage, by the symbol that declared each field.</summary>
    public Dictionary<FieldSymbol, object?> Fields { get; } = [];

    /// <summary>
    /// <para>The message an exception was constructed with, for a model extending
    /// <c>Exception</c>.</para>
    /// <para>Its own rather than an ordinary field because the built-in <c>Exception</c>
    /// declares no fields for a program to see — this is the one piece of state it
    /// contributes, and <c>Message()</c> is the only way to reach it.</para>
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// <para>The fields in the order they were declared, which deep equality walks and a
    /// default rendering prints.</para>
    /// <para>Declaration order is available because a type is declared in exactly one place,
    /// so position in the source totally orders its fields. Equality needs only that both
    /// sides agree; printing needs the order the author wrote, which is this one.</para>
    /// </summary>
    private FieldSymbol[]? _ordered;

    private FieldSymbol[] Ordered =>
        _ordered ??=
        [
            .. Fields.Keys
                     .OrderBy(f => f.Declaration?.Span.Start.Offset ?? 0)
                     .ThenBy(f => f.Name, StringComparer.Ordinal),
        ];

    /// <summary>
    /// The Profi-C type, since every instance the interpreter makes is this one host class and
    /// so the host type says nothing about which type a value is.
    /// </summary>
    public object DeepTypeIdentity => Type;

    public int DeepMemberCount => Fields.Count;

    public object? GetDeepMember(int index) =>
        index >= 0 && index < Ordered.Length ? Fields[Ordered[index]] : null;

    /// <summary>Copies a structure. Models are never copied, so this is only ever used on values.</summary>
    public Instance Copy()
    {
        Instance copy = new(Type) { Renderer = Renderer };

        foreach ((FieldSymbol field, object? value) in Fields)
        {
            // A structure holding a model copies the reference, not the model.
            copy.Fields[field] = value is Instance { Type.IsValueType: true } nested
                ? nested.Copy()
                : value;
        }

        return copy;
    }

    public override bool Equals(object? obj) => DeepEquality.Equals(this, obj);

    public override int GetHashCode() =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>
    /// <para>A declared <c>ToString</c> if the type has one, and the default otherwise.</para>
    /// <para>The defaults differ, and the difference is forced: a structure prints field by
    /// field because a structure cannot contain itself and the walk therefore ends, while a
    /// model prints its type name because a model can take part in a cycle and printing has no
    /// equivalent of the trick equality uses. Overriding is how an author says more.</para>
    /// </summary>
    public override string ToString() =>
        Renderer is { } declared
            ? declared(this)
            : Type.IsValueType
                ? ModelOperations.StructureToString(Type.Name, this)
                : Type.Name;
}

/// <summary>
/// <para>A function used as a value: a lambda, or a named function referred to by name.</para>
/// <para>The environment it was created in is carried along, which is what capture is.</para>
/// </summary>
public sealed class FunctionValue(
    IReadOnlyList<ParameterDecl> parameters,
    IReadOnlyList<Statement>? body,
    Expression? expressionBody,
    Environment closure,
    Instance? receiver,
    string? name = null,
    string? file = null)
{
    /// <summary>
    /// <para>The file this body was written in, for a debugger that has to say where a pause is.
    /// </para>
    /// <para>Carried here because nothing else knows it. A source span holds a
    /// position and not the file it is a position in, and by the time a call is running, the
    /// unit it was declared in is long out of reach. Across several files a line number alone
    /// names nothing.</para>
    /// </summary>
    public string? File { get; } = file;

    public IReadOnlyList<ParameterDecl> Parameters { get; } = parameters;

    /// <summary>
    /// <para>What to call this in a call stack, or null where there is nothing to call it.</para>
    /// <para>A lambda has no name, and giving one an invented one would put a word on screen
    /// that appears nowhere in the program. Null says so, and a debugger can render it however
    /// suits it.</para>
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>The statements of a block-bodied function, or null for an expression body.</summary>
    public IReadOnlyList<Statement>? Body { get; } = body;

    /// <summary>The expression of an arrow lambda, or null for a block body.</summary>
    public Expression? ExpressionBody { get; } = expressionBody;

    /// <summary>The environment in force where this was written.</summary>
    public Environment Closure { get; } = closure;

    /// <summary>The instance it belongs to, if it is an instance member.</summary>
    public Instance? Receiver { get; } = receiver;

    public override string ToString() => "function";
}

/// <summary>How a statement finished, which tells the caller what to do next.</summary>
public enum Completion
{
    /// <summary>Ran to the end; carry on with the next statement.</summary>
    Normal,

    /// <summary>Leave the innermost loop.</summary>
    Break,

    /// <summary>Begin the innermost loop's next iteration.</summary>
    Continue,

    /// <summary>Leave the function, carrying a value.</summary>
    Yield,
}

/// <summary>
/// <para>What a statement did.</para>
/// <para>Signals are returned rather than thrown. Throwing is the shorter way to write a tree
/// walker, but it makes leaving a loop as costly as an exception, and every loop in the
/// language pays for it.</para>
/// </summary>
public readonly record struct ExecutionResult(Completion Completion, object? Value)
{
    public static readonly ExecutionResult Normal = new(Completion.Normal, null);

    public static readonly ExecutionResult Break = new(Completion.Break, null);

    public static readonly ExecutionResult Continue = new(Completion.Continue, null);

    public static ExecutionResult Yield(object? value) => new(Completion.Yield, value);

    /// <summary>True when this ends the enclosing statement list.</summary>
    public bool Exits => Completion != Completion.Normal;
}
