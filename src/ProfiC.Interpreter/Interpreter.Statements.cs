using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Interpreter;

public sealed partial class Interpreter
{
    private ExecutionResult ExecuteStatements(
        IReadOnlyList<Statement> statements,
        Environment scope,
        Instance? receiver)
    {
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

    private ExecutionResult ExecuteStatement(
        Statement statement,
        Environment scope,
        Instance? receiver) => statement switch
    {
        BlockStmt block => ExecuteStatements(block.Statements, scope.Push(), receiver),
        VarDeclStmt declaration => ExecuteVarDecl(declaration, scope, receiver),
        LocalDeclStmt local => ExecuteLocalFunction(local, scope, receiver),
        IfStmt branch => ExecuteIf(branch, scope, receiver),
        WhileStmt loop => ExecuteWhile(loop, scope, receiver),
        ForStmt loop => ExecuteFor(loop, scope, receiver),
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
                function.Parameters, function.Body, expressionBody: null, scope, receiver));
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
    /// <para>The range loop.</para>
    /// <para>Two details matter. The bounds and the step are worked out once, on entry, so a
    /// loop cannot change its own extent while running. And the step's sign decides the
    /// comparison at that same moment, rather than being fixed while compiling, since the
    /// step may be any expression.</para>
    /// <para>Each iteration gets a fresh scope, so a lambda made inside the body captures that
    /// iteration's variable rather than one shared by all of them.</para>
    /// </summary>
    private ExecutionResult ExecuteFor(ForStmt loop, Environment scope, Instance? receiver)
    {
        if (_model.GetSymbol(loop) is not { } variable)
        {
            return ExecutionResult.Normal;
        }

        long current = AsInteger(Evaluate(loop.Start, scope, receiver));
        long bound = AsInteger(Evaluate(loop.Bound, scope, receiver));
        long step = loop.Step is null ? 1 : AsInteger(Evaluate(loop.Step, scope, receiver));

        bool ascending = step >= 0;

        while (true)
        {
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
                    // The clause binds what the program threw, not the wrapper it travelled in.
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
    /// </summary>
    private CatchClause? FindHandler(TryStmt tryStmt, Exception thrown)
    {
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
            Exception built => built,
            Instance instance => new ProfiCThrow(instance),
            _ => new ProfiCRuntimeException(Runtime.ModelOperations.ToDisplayString(value)),
        };
    }

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
                Cell? cell = scope.Lookup(symbol) ?? _globals.Lookup(symbol);

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

            // A global field, written through the name of the model that holds it.
            case MemberExpr member
                when _model.GetSymbol(member) is FieldSymbol { IsGlobal: true } globalField:
            {
                if (_globals.Lookup(globalField) is { } cell)
                {
                    cell.Value = value;
                }
                else
                {
                    _globals.Declare(globalField, value);
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
