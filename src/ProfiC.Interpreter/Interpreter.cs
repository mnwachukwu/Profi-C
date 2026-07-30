using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

/// <summary>
/// <para>Runs a Profi-C program by walking its lowered tree.</para>
/// <para>It proves the resolver and the type checker with no code generation in the way, and
/// serves as the oracle the emitter is measured against: where the two disagree about what a
/// program means, the compiler has the bug.</para>
/// <para>It runs the <em>lowered</em> tree, so it never meets a <c>for each</c> and never has
/// to work out which conversion a value needs. Lowering settles both, in one place.</para>
/// </summary>
public sealed partial class Interpreter
{
    private readonly SemanticModel _model;
    private readonly TextWriter _output;

    /// <summary>Global storage, for the fields of a global model.</summary>
    private readonly Environment _globals = new(parent: null);

    /// <summary>
    /// <para>The generator behind <c>Random.Integer</c> and the two beside it, for every
    /// program that did not ask for one of its own.</para>
    /// <para>One per run rather than one per process, so two programs run in the same session
    /// — as the tests do — cannot draw from each other's sequence.</para>
    /// </summary>
    private readonly ProfiCRandom _chance = new();

    /// <summary>Every type declared, so that construction can find one by name.</summary>
    private readonly Dictionary<string, DeclaredTypeSymbol> _types = new(StringComparer.Ordinal);

    /// <summary>
    /// <para>The lowered body of each function.</para>
    /// <para>A symbol's own <c>Declaration</c> points at the tree the resolver saw, which is
    /// the tree <em>before</em> lowering. Running that would mean meeting the shapes lowering
    /// exists to remove, so bodies are looked up here instead.</para>
    /// </summary>
    private readonly Dictionary<FunctionSymbol, FunctionDecl> _bodies = [];

    /// <summary>The lowered initializer of each field, for the same reason as <see cref="_bodies"/>.</summary>
    private readonly Dictionary<FieldSymbol, Expression> _initializers = [];

    /// <summary>How deep the call stack is, so that runaway recursion fails cleanly.</summary>
    private int _depth;

    private const int MaximumDepth = 512;

    private Interpreter(SemanticModel model, TextWriter output)
    {
        _model = model;
        _output = output;
    }

    /// <summary>
    /// <para>Runs a program, returning what <c>Main</c> yielded, or zero.</para>
    /// <para>The tree must already be lowered; running an unlowered one would meet shapes
    /// this deliberately does not handle.</para>
    /// </summary>
    public static int Run(
        IReadOnlyList<CompilationUnit> lowered,
        SemanticModel model,
        TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(lowered);
        ArgumentNullException.ThrowIfNull(model);

        Interpreter interpreter = new(model, output ?? Console.Out);

        try
        {
            return interpreter.Execute(lowered);
        }
        catch (ProfiCThrow uncaught)
        {
            // A declared exception travels inside ProfiCThrow, which is the interpreter's own
            // business. Past the top of the program it is the program's exception again.
            throw new UncaughtProfiCException(
                uncaught.Thrown.Type.Name,
                uncaught.Thrown.Message ?? string.Empty);
        }
    }

    /// <summary>Runs one lowered file, which is a program of one.</summary>
    public static int Run(
        CompilationUnit lowered,
        SemanticModel model,
        TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(lowered);

        return Run([lowered], model, output);
    }

    private int Execute(IReadOnlyList<CompilationUnit> units)
    {
        // Types across every file are collected before any global is initialized, so that an
        // initializer in one file may name a type declared in another.
        foreach (Declaration declaration in units.SelectMany(unit => unit.Declarations))
        {
            CollectTypes(declaration);
        }

        foreach (Declaration declaration in units.SelectMany(unit => unit.Declarations))
        {
            InitializeGlobals(declaration);
        }

        if (_model.EntryPoint is not { } main)
        {
            throw new ProfiCRuntimeException("This program has no entry point to run.");
        }

        if (BodyOf(main) is not { } declarationOfMain)
        {
            throw new ProfiCRuntimeException("The entry point has no body.");
        }

        // Main takes no arguments, or a set of them; either way nothing is passed in yet.
        object? result = Invoke(
            new FunctionValue(
                declarationOfMain.Parameters,
                declarationOfMain.Body,
                expressionBody: null,
                _globals,
                receiver: null),
            arguments: [],
            declarationOfMain.Parameters.Count == 0 ? [] : [new ProfiCSet<object?>()]);

        return result is long code ? (int)code : 0;
    }

    /// <summary>
    /// The lowered declaration of a function, which is the one that actually runs. Falls back
    /// to the symbol's own declaration only for a function lowering never saw.
    /// </summary>
    private FunctionDecl? BodyOf(FunctionSymbol function) =>
        _bodies.TryGetValue(function, out FunctionDecl? lowered)
            ? lowered
            : function.Declaration as FunctionDecl;

    /// <summary>
    /// Walks the lowered tree once, noting every type by name and every function body and field
    /// initializer by symbol. This walk is the only thing that knows the lowered declarations,
    /// so everything the run needs from them is taken here.
    /// </summary>
    private void CollectTypes(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    CollectTypes(member);
                }

                break;

            case ModelDecl or StructureDecl when _model.GetSymbol(declaration) is DeclaredTypeSymbol type:
                _types[type.Name] = type;

                foreach (Declaration member in MembersOf(declaration))
                {
                    CollectTypes(member);
                }

                break;

            case FunctionDecl function when _model.GetSymbol(function) is FunctionSymbol symbol:
                _bodies[symbol] = function;
                break;

            case FieldDecl { Initializer: { } start } field
                when _model.GetSymbol(field) is FieldSymbol symbol:
                _initializers[symbol] = start;
                break;
        }
    }

    private static IReadOnlyList<Declaration> MembersOf(Declaration declaration) => declaration switch
    {
        ModelDecl model => model.Members,
        StructureDecl structure => structure.Members,
        _ => [],
    };

    /// <summary>
    /// Gives every global field its starting value. Field initializers run before anything
    /// else, so a global constant is ready by the time the entry point begins.
    /// </summary>
    private void InitializeGlobals(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    InitializeGlobals(member);
                }

                break;

            case ModelDecl or StructureDecl:
                foreach (Declaration member in MembersOf(declaration))
                {
                    if (member is FieldDecl field
                        && _model.GetSymbol(field) is FieldSymbol { IsGlobal: true } symbol)
                    {
                        object? value = field.Initializer is null
                            ? DefaultFor(symbol.Type)
                            : Evaluate(field.Initializer, _globals, receiver: null);

                        _globals.Declare(symbol, value);
                    }
                    else
                    {
                        InitializeGlobals(member);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// <para>The starting value of a variable nobody has assigned.</para>
    /// <para>Only an optional really has one — empty. Everything else is rejected before it
    /// can be read, so this is a placeholder that a correct program never observes.</para>
    /// </summary>
    private static object? DefaultFor(TypeSymbol type) => type switch
    {
        OptionalType => null,
        _ when ReferenceEquals(type, PrimitiveType.Integer) => 0L,
        _ when ReferenceEquals(type, PrimitiveType.Real) => 0.0,
        _ when ReferenceEquals(type, PrimitiveType.Boolean) => false,
        _ when ReferenceEquals(type, PrimitiveType.Character) => '\0',
        _ when ReferenceEquals(type, PrimitiveType.String) => string.Empty,
        _ when ReferenceEquals(type, PrimitiveType.Fraction) => Fraction.Zero,
        _ => null,
    };

    // ---- Calling ----------------------------------------------------------------------------

    /// <summary>
    /// Runs a function with the given arguments, returning what it yielded.
    /// </summary>
    private object? Invoke(
        FunctionValue function,
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<object?> values)
    {
        _ = arguments;

        if (++_depth > MaximumDepth)
        {
            _depth--;
            throw new ProfiCRuntimeException(
                $"Too many nested calls; stopped after {MaximumDepth}. This usually means a "
                + "function calls itself without ever reaching a base case.");
        }

        try
        {
            Environment scope = function.Closure.Push();

            for (int i = 0; i < function.Parameters.Count; i++)
            {
                object? value = i < values.Count ? values[i] : null;

                if (_model.GetSymbol(function.Parameters[i]) is { } parameter)
                {
                    scope.Declare(parameter, CopyIfValue(value));
                }
            }

            if (function.ExpressionBody is not null)
            {
                return Evaluate(function.ExpressionBody, scope, function.Receiver);
            }

            ExecutionResult result = ExecuteStatements(function.Body ?? [], scope, function.Receiver);
            return result.Completion == Completion.Yield ? result.Value : null;
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// <para>How instances of a type render, or null where the default stands.</para>
    /// <para>Settled once per instance rather than at every print, and by the same walk a call
    /// takes, so that <c>x.ToString()</c> and printing <c>x</c> can never reach two different
    /// functions. Zero arguments, because a <c>ToString</c> taking any is a different function
    /// that happens to share a name.</para>
    /// </summary>
    private Func<Instance, string>? RendererFor(DeclaredTypeSymbol type)
    {
        if (FindMethod(type, "ToString", arity: 0) is not { } declared
            || BodyOf(declared) is not { } body)
        {
            return null;
        }

        return instance => Invoke(
            new FunctionValue(body.Parameters, body.Body, null, _globals, instance),
            [],
            []) as string ?? string.Empty;
    }

    /// <summary>
    /// <para>Copies a structure wherever one is stored or passed.</para>
    /// <para>This is the whole of value semantics. Models are references and are never copied,
    /// which is why one method can serve both.</para>
    /// </summary>
    private static object? CopyIfValue(object? value) =>
        value is Instance { Type.IsValueType: true } structure ? structure.Copy() : value;
}

/// <summary>
/// <para>An exception a program declared and threw, on its way to a catch clause.</para>
/// <para>The exceptions the language raises itself are real .NET ones, so they travel as
/// themselves. A model a program declared is not one, so it rides inside this — .NET's
/// unwinding is what carries a throw to its handler either way.</para>
/// </summary>
internal sealed class ProfiCThrow(Instance thrown)
    : Exception($"An unhandled {thrown.Type.Name} reached the top of the program.")
{
    public Instance Thrown { get; } = thrown;
}

/// <summary>
/// <para>An exception a program declared and threw that no catch clause took.</para>
/// <para>Carries the name the program gave its exception model and the message it was built
/// with, so this reads the same way as an exception the language raises itself.</para>
/// </summary>
public sealed class UncaughtProfiCException(string typeName, string text)
    : Exception($"unhandled {typeName}: {text}")
{
    /// <summary>The name of the exception model the program threw.</summary>
    public string TypeName { get; } = typeName;

    /// <summary>The message the thrown exception carries.</summary>
    public string Text { get; } = text;
}

/// <summary>Something went wrong while running a program, rather than while compiling it.</summary>
public sealed class ProfiCRuntimeException : Exception
{
    public ProfiCRuntimeException(string message)
        : base(message)
    {
    }

    public ProfiCRuntimeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
