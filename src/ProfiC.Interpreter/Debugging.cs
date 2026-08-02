using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Interpreter;

/// <summary>
/// <para>One name a paused program can be asked about, and what it holds.</para>
/// <para><see cref="Invented"/> separates a name the program wrote from one lowering made up.
/// A <c>loop each</c> puts <c>&lt;source$0&gt;</c>, <c>&lt;count$2&gt;</c> and
/// <c>&lt;index$1&gt;</c> in scope beside the element, and showing those to a beginner would be
/// worse than showing nothing. The interpreter says which is which rather than leaving every
/// caller to work it out from the spelling.</para>
/// </summary>
/// <param name="Name">The name as it appears in the scope.</param>
/// <param name="Value">What it holds at the moment of the pause.</param>
/// <param name="Invented">Whether lowering made this name up rather than the program writing it.</param>
public sealed record Local(string Name, object? Value, bool Invented);

/// <summary>
/// <para>One call on the stack: what it is, and where inside it the program is.</para>
/// <para><see cref="Name"/> is null for a lambda, which has none. Inventing one would put a
/// word on screen that appears nowhere in the program, so the choice of what to show instead is
/// left to whatever is doing the showing.</para>
/// </summary>
/// <param name="Name">The function's name, or null for a lambda.</param>
/// <param name="File">The file this call's body was written in. Per frame rather than per stop,
/// because a call from one file into another is two files at once and an editor opening the
/// wrong one at a frame is worse than opening none.</param>
/// <param name="Line">The line being run in this call, which for all but the innermost is the
/// line of the call that is still waiting.</param>
public sealed record CallFrame(string? Name, string File, int Line);

/// <summary>
/// <para>Where a paused program is, and what can be seen from there.</para>
/// <para><b>A live view, not a snapshot.</b> <see cref="Locals"/> reads the scope at the moment
/// it is called, so it must be called while the program is still paused — that is, from inside
/// <see cref="IDebugHost.Reached"/> and before it returns. Keeping a point and asking it
/// afterwards answers about a program that has since moved on, which is not obviously wrong to
/// look at and is entirely wrong to believe.</para>
/// <para>Live rather than snapshotted because most statements are stepped past without anybody
/// looking, and copying every scope on the chance somebody might would make the gate cost
/// something on every statement of every run.</para>
/// </summary>
public sealed class ExecutionPoint
{
    private readonly Environment _scope;
    private readonly IReadOnlyList<CallFrame> _stack;

    internal ExecutionPoint(
        Statement statement,
        string file,
        int depth,
        Environment scope,
        IReadOnlyList<CallFrame> stack)
    {
        Statement = statement;
        Span = statement.Span;
        File = file;
        Depth = depth;
        _scope = scope;
        _stack = stack;
    }

    /// <summary>The file the statement about to run was written in.</summary>
    public string File { get; }

    /// <summary>
    /// <para>The calls that led here, innermost first.</para>
    /// <para>Taken when the point is made rather than read later, because the stack unwinds as
    /// the program goes on and a debugger asking afterwards would be told about a call that had
    /// already returned. The locals are the other way round and deliberately so — those are a
    /// live view of one scope, and this is a copy of the shape of the whole run.</para>
    /// </summary>
    public IReadOnlyList<CallFrame> Stack => _stack;

    /// <summary>
    /// <para>The statement about to run, which is what tells two identical-looking stops
    /// apart.</para>
    /// <para>Several lowered statements can share one source line — a <c>loop each</c> makes
    /// six — and a loop body reaches the same line once per turn. By line alone those are
    /// indistinguishable, and collapsing the first would collapse the second: a breakpoint in a
    /// loop would fire once and never again. Different statements on one line are one stop; the
    /// same statement again is another turn.</para>
    /// </summary>
    public Statement Statement { get; }

    /// <summary>Where in the source the statement about to run was written.</summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// How many calls deep this is. What "step over" and "step out" are about: a step over waits
    /// for a depth no greater than the one it started at, and a step out for a smaller one.
    /// </summary>
    public int Depth { get; }

    /// <summary>The line the statement starts on, which is what a breakpoint is set against.</summary>
    public int Line => Span.Start.Line;

    /// <summary>
    /// <para>Every name in scope here, innermost first, each said once.</para>
    /// <para>A name declared in an inner scope hides one further out, so the inner is the one
    /// reported — the same answer the program itself would get by reading the name.</para>
    /// </summary>
    public IReadOnlyList<Local> Locals()
    {
        List<Local> found = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (Environment? scope = _scope; scope is not null; scope = scope.Parent)
        {
            foreach ((Symbol symbol, Cell cell) in scope.Declared)
            {
                if (seen.Add(symbol.Name))
                {
                    found.Add(new Local(symbol.Name, cell.Value, IsInvented(symbol.Name)));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// <para>Whether lowering made this name up.</para>
    /// <para>Every invented name is wrapped in angle brackets, and no name a program may write
    /// is — an identifier begins with a letter or an underscore. So the test is the spelling,
    /// and it cannot collide with anything a reader wrote.</para>
    /// </summary>
    private static bool IsInvented(string name) =>
        name.StartsWith('<') && name.EndsWith('>');
}

/// <summary>
/// <para>Something watching a program run, one statement at a time.</para>
/// <para>Deliberately says nothing about breakpoints, stepping, or any protocol. The
/// interpreter's whole part in debugging is announcing where it is and waiting for as long as
/// it is told to; deciding whether <em>this</em> is a place to stop is the debugger's, and
/// keeping that decision out of here is what stops the interpreter growing a second job.</para>
/// <para>It follows that a debug adapter can be written, replaced, or run twice over without
/// this file changing.</para>
/// </summary>
public interface IDebugHost
{
    /// <summary>
    /// <para>Called before each statement runs. Returning is what lets the program go on, so an
    /// implementation that means to pause simply does not return yet.</para>
    /// <para>Called once per statement, which is more often than a reader would call a stop: a
    /// <c>loop each</c> lowers to six statements sharing one line. Collapsing those into one
    /// stop is a question about what a person expects to see, so it is answered here rather
    /// than in the interpreter.</para>
    /// </summary>
    void Reached(ExecutionPoint point);
}
