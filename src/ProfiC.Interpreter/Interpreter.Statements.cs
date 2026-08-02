using System.Runtime.CompilerServices;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

public sealed partial class Interpreter
{
    /// <summary>
    /// Set on an exception a program threw itself, which is the only way to tell one from a
    /// fault in the interpreter when both are a plain <c>System.Exception</c>.
    /// </summary>
    private const string RaisedByProgram = "ProfiC.RaisedByProgram";

    /// <summary>
    /// <para>Runs a run of statements, the functions it declares in place first.</para>
    /// <para>A function declared among statements is in scope throughout the run rather than
    /// from its own line onward, so a call may sit above it and two of them may call each
    /// other. Making them all before the first statement runs is what makes that true at run
    /// time as well as to the resolver.</para>
    /// <para>Each closes over this same scope, so a local declared later is one they can read
    /// once it holds something — and calling one before it does is refused while checking
    /// (<c>PC0405</c>) rather than answered with whatever the cell happened to hold.</para>
    /// </summary>
    private ExecutionResult ExecuteStatements(
        IReadOnlyList<Statement> statements,
        Environment scope,
        Instance? receiver)
    {
        foreach (Statement statement in statements)
        {
            if (statement is LocalDeclStmt local)
            {
                ExecuteLocalFunction(local, scope, receiver);
            }
        }

        foreach (Statement statement in statements)
        {
            ExecutionResult result = ExecuteStatement(statement, scope, receiver);

            if (result.Exits)
            {
                return result;
            }
        }

        return ExecutionResult.Normal;
    }

    /// <summary>
    /// <para>Runs one statement, telling a watching debugger where it is first.</para>
    /// <para>The gate is here rather than anywhere else because this is the one place every
    /// statement passes through, whatever construct it sits in. Nothing is asked of the host
    /// but that it return when the program should go on, so a run with no host attached does
    /// one null check per statement and nothing else.</para>
    /// </summary>
    private ExecutionResult ExecuteStatement(
        Statement statement,
        Environment scope,
        Instance? receiver)
    {
        if (_host is not null)
        {
            Announce(statement, scope);
        }

        // The switch is written out here rather than called through a second method, and that
        // is not style. Statements nest, so a frame added here is added once per level of
        // nesting per call — and 512 calls deep, which is what the recursion guard allows, one
        // extra frame per statement was enough to overflow the real stack before the guard
        // could report. Measured: it crashed the test host about one run in three.
        return statement switch
        {
            // Already made, before the first statement of the run it belongs to.
            LocalDeclStmt => ExecutionResult.Normal,

            BlockStmt block => ExecuteStatements(block.Statements, scope.Push(), receiver),
            VarDeclStmt declaration => ExecuteVarDecl(declaration, scope, receiver),
            IfStmt branch => ExecuteIf(branch, scope, receiver),
            WhileStmt loop => ExecuteWhile(loop, scope, receiver),
            LoopUntilStmt loop => ExecuteLoopUntil(loop, scope, receiver),
            LoopForeverStmt loop => ExecuteLoopForever(loop, scope, receiver),
            ForStmt loop => ExecuteFor(loop, scope, receiver),
            WalkStmt walk => ExecuteWalk(walk, scope, receiver),
            SwitchStmt switchStmt => ExecuteSwitch(switchStmt, scope, receiver),
            TryStmt tryStmt => ExecuteTry(tryStmt, scope, receiver),
            ThrowStmt throwStmt => ExecuteThrow(throwStmt, scope, receiver),
            YieldStmt yieldStmt => ExecutionResult.Yield(
                yieldStmt.Value is null ? null : Evaluate(yieldStmt.Value, scope, receiver)),
            BreakStmt => ExecutionResult.Break,
            ContinueStmt => ExecutionResult.Continue,
            ExpressionStmt expression => Discard(expression, scope, receiver),
            AssignmentStmt assignment => ExecuteAssignment(assignment, scope, receiver),
            _ => ExecutionResult.Normal,
        };
    }

    /// <summary>
    /// <para>Tells a watching host where the program is.</para>
    /// <para>Its own method for the same reason the switch above is written out: stack. A frame
    /// is sized at entry for everything the method might need, a branch not taken included — so
    /// building the point inside the gate made every statement of every call reserve room for a
    /// debugger that is usually not there. Statements nest and calls nest, so that room was
    /// paid per level of both, and 512 calls deep it overflowed the real stack before the
    /// recursion guard could report. Measured: it crashed the test host about half the time.
    /// </para>
    /// <para>Separating the method is what fixes that. <c>NoInlining</c> is not — the same runs
    /// pass without it, because this is already far past the size the inliner will take. It is
    /// here to keep the property rather than to leave it to a heuristic, since what goes wrong
    /// when it is lost is a crash in something else entirely, a long way from this line.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Announce(Statement statement, Environment scope) =>
        _host!.Reached(new ExecutionPoint(statement, _file, _depth, scope, StackHere(statement)));

    /// <summary>
    /// <para>The calls in progress, innermost first, with this statement's line put on the
    /// innermost.</para>
    /// <para>A frame's line is only known when a statement inside it runs, so it is written
    /// here rather than when the call was entered. Every frame below the innermost keeps the
    /// line of whichever statement it was last on, which is the call that is still waiting —
    /// exactly what a stack trace should show.</para>
    /// <para>Copied rather than handed over live: the stack unwinds as the program goes on, and
    /// a debugger reading it later would be told about a call that had already returned.</para>
    /// </summary>
    private IReadOnlyList<CallFrame> StackHere(Statement statement)
    {
        if (_frames.Count > 0)
        {
            _frames[^1].Line = statement.Span.Start.Line;
        }

        CallFrame[] stack = new CallFrame[_frames.Count];

        for (int i = 0; i < _frames.Count; i++)
        {
            stack[i] = new CallFrame(
                _frames[^(i + 1)].Name, _frames[^(i + 1)].File, _frames[^(i + 1)].Line);
        }

        return stack;
    }



    private ExecutionResult Discard(ExpressionStmt statement, Environment scope, Instance? receiver)
    {
        Evaluate(statement.Expression, scope, receiver);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecuteVarDecl(
        VarDeclStmt declaration,
        Environment scope,
        Instance? receiver)
    {
        if (_model.GetSymbol(declaration) is not { } symbol)
        {
            return ExecutionResult.Normal;
        }

        object? value = declaration.Initializer is null
            ? DefaultFor((symbol as LocalSymbol)?.Type ?? ErrorType.Instance)
            : Evaluate(declaration.Initializer, scope, receiver);

        scope.Declare(symbol, CopyIfValue(value));
        return ExecutionResult.Normal;
    }

    /// <summary>
    /// A local function becomes a value in the scope it was written in, which is how its body
    /// comes to see the locals around it.
    /// </summary>
    private ExecutionResult ExecuteLocalFunction(
        LocalDeclStmt local,
        Environment scope,
        Instance? receiver)
    {
        if (local.Declaration is FunctionDecl function && _model.GetSymbol(function) is { } symbol)
        {
            scope.Declare(symbol, new FunctionValue(
                function.Parameters, function.Body, expressionBody: null, scope, receiver,
                function.Name, _file));
        }

        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecuteIf(IfStmt branch, Environment scope, Instance? receiver)
    {
        if (IsTrue(Evaluate(branch.Condition, scope, receiver)))
        {
            return ExecuteStatements(branch.ThenBody, scope.Push(), receiver);
        }

        foreach (ElseIfClause clause in branch.ElseIfClauses)
        {
            if (IsTrue(Evaluate(clause.Condition, scope, receiver)))
            {
                return ExecuteStatements(clause.Body, scope.Push(), receiver);
            }
        }

        return branch.ElseBody is null
            ? ExecutionResult.Normal
            : ExecuteStatements(branch.ElseBody, scope.Push(), receiver);
    }

    private ExecutionResult ExecuteWhile(WhileStmt loop, Environment scope, Instance? receiver)
    {
        while (IsTrue(Evaluate(loop.Condition, scope, receiver)))
        {
            ExecutionResult result = ExecuteStatements(loop.Body, scope.Push(), receiver);

            if (result.Completion == Completion.Break)
            {
                break;
            }

            if (result.Completion == Completion.Yield)
            {
                return result;
            }
        }

        return ExecutionResult.Normal;
    }

    /// <summary>
    /// <para>The loop whose condition is tested after the body, so the body always runs once.
    /// </para>
    /// <para>The condition is evaluated in the scope around the loop rather than the body's own,
    /// which is pushed fresh each turn and gone by the time the test happens. A name the
    /// condition reads therefore has to outlive a turn, which is what the resolver enforces.
    /// </para>
    /// </summary>
    private ExecutionResult ExecuteLoopUntil(
        LoopUntilStmt loop,
        Environment scope,
        Instance? receiver)
    {
        while (true)
        {
            ExecutionResult result = ExecuteStatements(loop.Body, scope.Push(), receiver);

            if (result.Completion == Completion.Break)
            {
                break;
            }

            if (result.Completion == Completion.Yield)
            {
                return result;
            }

            if (IsTrue(Evaluate(loop.Condition, scope, receiver)))
            {
                break;
            }
        }

        return ExecutionResult.Normal;
    }

    /// <summary>
    /// A loop with no condition. Only a <c>break</c>, a <c>yield</c>, or something thrown
    /// leaves it, which is what the program said by writing no condition.
    /// </summary>
    private ExecutionResult ExecuteLoopForever(
        LoopForeverStmt loop,
        Environment scope,
        Instance? receiver)
    {
        while (true)
        {
            ExecutionResult result = ExecuteStatements(loop.Body, scope.Push(), receiver);

            if (result.Completion == Completion.Break)
            {
                return ExecutionResult.Normal;
            }

            if (result.Completion == Completion.Yield)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// <para>The range loop.</para>
    /// <para>Two details matter. The bounds and the step are worked out once, on entry, so a
    /// loop cannot change its own extent while running. And the step's sign decides the
    /// comparison at that same moment, rather than being fixed while compiling, since the
    /// step may be any expression.</para>
    /// <para>Each iteration gets a fresh scope, so a lambda made inside the body captures that
    /// iteration's variable rather than one shared by all of them.</para>
    /// </summary>
    /// <summary>
    /// <para>Runs a loop that is walking a sequence, with the sequence marked for as long as it
    /// runs.</para>
    /// <para>Unmarked in a finally, so a <c>break</c>, a <c>yield</c>, or an exception leaves
    /// the set usable again. A string is walked too and cannot be changed at all, so only a set
    /// has anything to mark.</para>
    /// </summary>
    private ExecutionResult ExecuteWalk(WalkStmt walk, Environment scope, Instance? receiver)
    {
        if (Evaluate(walk.Sequence, scope, receiver) is not IProfiCSet sequence)
        {
            return ExecuteStatement(walk.Body, scope, receiver);
        }

        sequence.BeginWalk();

        try
        {
            return ExecuteStatement(walk.Body, scope, receiver);
        }
        finally
        {
            sequence.EndWalk();
        }
    }

    private ExecutionResult ExecuteFor(ForStmt loop, Environment scope, Instance? receiver)
    {
        if (_model.GetSymbol(loop) is not { } variable)
        {
            return ExecutionResult.Normal;
        }

        long current = AsInteger(Evaluate(loop.Start, scope, receiver));

        while (true)
        {
            // The header is read again at the top of every turn, so a loop counts as far as
            // what it says now rather than as far as what it said when it began. The step is
            // read here rather than at the bottom so that one turn reads the header once.
            long bound = AsInteger(Evaluate(loop.Bound, scope, receiver));
            long step = loop.Step is null ? 1 : AsInteger(Evaluate(loop.Step, scope, receiver));

            bool ascending = step >= 0;

            bool inRange = loop.IsInclusive
                ? (ascending ? current <= bound : current >= bound)
                : (ascending ? current < bound : current > bound);

            if (!inRange)
            {
                return ExecutionResult.Normal;
            }

            Environment iteration = scope.Push();
            iteration.Declare(variable, current);

            ExecutionResult result = ExecuteStatements(loop.Body, iteration, receiver);

            if (result.Completion == Completion.Break)
            {
                return ExecutionResult.Normal;
            }

            if (result.Completion == Completion.Yield)
            {
                return result;
            }

            // A step of zero loops forever, and that is allowed: the program said so.
            current += step;
        }
    }

    /// <summary>
    /// A switch runs one arm and stops. Nothing falls through, which is what keeps
    /// <c>break</c> meaning exactly one thing in the language.
    /// </summary>
    private ExecutionResult ExecuteSwitch(
        SwitchStmt switchStmt,
        Environment scope,
        Instance? receiver)
    {
        object? subject = Evaluate(switchStmt.Subject, scope, receiver);

        foreach (CaseGroup group in switchStmt.Cases)
        {
            foreach (Expression label in group.Labels)
            {
                if (!Runtime.DeepEquality.Equals(subject, Evaluate(label, scope, receiver)))
                {
                    continue;
                }

                ExecutionResult result = ExecuteStatements(group.Body, scope.Push(), receiver);
                return result.Completion == Completion.Break ? ExecutionResult.Normal : result;
            }
        }

        if (switchStmt.DefaultBody is null)
        {
            return ExecutionResult.Normal;
        }

        ExecutionResult fallback = ExecuteStatements(switchStmt.DefaultBody, scope.Push(), receiver);
        return fallback.Completion == Completion.Break ? ExecutionResult.Normal : fallback;
    }

    /// <summary>
    /// <para>Try, catch, and finally.</para>
    /// <para>Profi-C's exceptions are .NET exceptions, so catching means matching the runtime
    /// type of one against the type a clause names. The finally body runs whichever way the
    /// try turned out, including when it is leaving through a yield.</para>
    /// </summary>
    private ExecutionResult ExecuteTry(TryStmt tryStmt, Environment scope, Instance? receiver)
    {
        ExecutionResult result = ExecutionResult.Normal;

        try
        {
            try
            {
                result = ExecuteStatements(tryStmt.Body, scope.Push(), receiver);
            }
            catch (Exception thrown) when (thrown is not ProfiCRuntimeException)
            {
                CatchClause? handler = FindHandler(tryStmt, thrown);

                if (handler is null)
                {
                    throw;
                }

                Environment caught = scope.Push();

                if (_model.GetSymbol(handler) is { } bound)
                {
                    // The clause binds what the program threw, not the wrapper it traveled in.
                    caught.Declare(bound, thrown is ProfiCThrow custom ? custom.Thrown : thrown);
                }

                result = ExecuteStatements(handler.Body, caught, receiver);
            }
        }
        finally
        {
            if (tryStmt.FinallyBody is not null)
            {
                ExecuteStatements(tryStmt.FinallyBody, scope.Push(), receiver);
            }
        }

        return result;
    }

    /// <summary>
    /// <para>Finds the first clause whose named type the thrown exception belongs to.</para>
    /// <para>Two kinds arrive. The exceptions the language raises itself are real .NET ones and
    /// are matched against the .NET type behind the name. One a program declared is an ordinary
    /// instance, and is matched up its own inheritance chain — which reaches the built-in
    /// <c>Exception</c>, so <c>catch Exception</c> takes both.</para>
    /// <para>A third kind must not be matched at all: a fault in the interpreter itself. Every
    /// .NET exception answers to <c>Exception</c>, so without the built-in test a bug in here
    /// would be handed to the program as though the program had caused it — caught, described
    /// by a <c>catch</c> block that has nothing to do with it, and hidden from the person who
    /// could fix it. This is the same division the top of a program already draws, so a failure
    /// that is ours rather than the program's is treated the same way wherever it is met.</para>
    /// </summary>
    private CatchClause? FindHandler(TryStmt tryStmt, Exception thrown)
    {
        if (!RaisedByTheProgram(thrown))
        {
            return null;
        }

        foreach (CatchClause clause in tryStmt.Catches)
        {
            if (_model.GetType(clause.ExceptionType) is not { } declared)
            {
                continue;
            }

            bool matches = thrown is ProfiCThrow custom
                ? custom.Thrown.Type is ModelSymbol model
                  && declared is ModelSymbol wanted
                  && model.SelfAndAncestors().Contains(wanted)
                : Runtime.BuiltInExceptions.Resolve(declared.Name) is { } expected
                  && expected.IsInstanceOfType(thrown);

            if (matches)
            {
                return clause;
            }
        }

        return null;
    }

    private ExecutionResult ExecuteThrow(ThrowStmt statement, Environment scope, Instance? receiver)
    {
        object? value = Evaluate(statement.Exception, scope, receiver);

        throw value switch
        {
            Exception built => Owned(built),
            Instance instance => new ProfiCThrow(instance),
            _ => new ProfiCRuntimeException(Runtime.ModelOperations.ToDisplayString(value)),
        };
    }

    /// <summary>
    /// <para>Marks an exception as one the program raised, so that a catch clause will take
    /// it.</para>
    /// <para>Needed because a program may write <c>throw new Exception("...")</c>, and the
    /// result is a plain <c>System.Exception</c> — the same type any fault in the interpreter
    /// answers to. Nothing about the value tells the two apart, so the throw that raised it
    /// says so here.</para>
    /// </summary>
    private static Exception Owned(Exception raised)
    {
        raised.Data[RaisedByProgram] = true;
        return raised;
    }

    /// <summary>
    /// <para>Whether a failure is the program's own, and so something a catch clause may
    /// take.</para>
    /// <para>Three are: one the program threw itself, one carrying a model it declared, and one
    /// the language raised on its behalf. Anything else is a fault in the interpreter, and
    /// letting a catch clause have it would hide the bug behind a handler written for something
    /// else entirely.</para>
    /// <para>One the language raises is still excluded if it is uncatchable. Nesting too deep
    /// has a name so that a reader can be told what stopped their program, not so that a
    /// program can carry on from it.</para>
    /// </summary>
    private static bool RaisedByTheProgram(Exception thrown) =>
        Runtime.BuiltInExceptions.MayBeCaught(thrown.GetType().Name)
        && (thrown is ProfiCThrow
            || Runtime.BuiltInExceptions.IsBuiltIn(thrown)
            || thrown.Data.Contains(RaisedByProgram));

    private ExecutionResult ExecuteAssignment(
        AssignmentStmt assignment,
        Environment scope,
        Instance? receiver)
    {
        object? value = CopyIfValue(Evaluate(assignment.Value, scope, receiver));

        switch (assignment.Target)
        {
            case IdentifierExpr identifier
                when _model.GetSymbol(identifier) is { } symbol:
            {
                Cell? cell = scope.Lookup(symbol) ?? _shared.Lookup(symbol);

                if (cell is not null)
                {
                    cell.Value = value;
                }
                else if (symbol is FieldSymbol field && receiver is not null)
                {
                    receiver.Fields[field] = value;
                }

                break;
            }

            // A shared field, written through the name of the model that holds it.
            case MemberExpr member
                when _model.GetSymbol(member) is FieldSymbol { IsShared: true } sharedField:
            {
                if (_shared.Lookup(sharedField) is { } cell)
                {
                    cell.Value = value;
                }
                else
                {
                    _shared.Declare(sharedField, value);
                }

                break;
            }

            case MemberExpr member:
            {
                object? target = Evaluate(member.Receiver, scope, receiver);

                if (target is Instance instance
                    && _model.GetSymbol(member) is FieldSymbol field)
                {
                    instance.Fields[field] = value;
                }

                break;
            }

            case IndexExpr index:
            {
                object? target = Evaluate(index.Receiver, scope, receiver);
                long position = AsInteger(Evaluate(index.Index, scope, receiver));

                if (target is Runtime.ProfiCSet<object?> set)
                {
                    set[(int)position] = value;
                }

                break;
            }
        }

        return ExecutionResult.Normal;
    }

    private static bool IsTrue(object? value) => value is true;

    private static long AsInteger(object? value) => value switch
    {
        long number => number,
        int number => number,
        _ => 0,
    };
}
