using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    private void CheckStatements(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
            _cancellation.ThrowIfCancellationRequested();
            CheckStatement(statement);
        }
    }

    private void CheckStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStmt block:
                CheckStatements(block.Statements);
                break;

            case VarDeclStmt declaration:
                CheckVarDecl(declaration);
                break;

            case LocalDeclStmt { Declaration: FunctionDecl function }:
                CheckFunction(function);
                break;

            case IfStmt branch:
                CheckIf(branch);
                break;

            case WhileStmt loop:
                RequireBoolean(CheckExpression(loop.Condition), loop.Condition, "A while condition");

                // What the condition proves holds inside the body, since the body only runs
                // while it does.
                CheckRepeatedly(loop.Body, AnalyzeCondition(loop.Condition).WhenTrue);
                break;

            // No narrowing to carry inward: the condition is tested after the body, so nothing
            // it proves was known when the body ran.
            case LoopUntilStmt loop:
                CheckRepeatedly(loop.Body, NothingProven);
                RequireBoolean(
                    CheckExpression(loop.Condition), loop.Condition, "An until condition");
                break;

            // No condition to check. That is the whole of it.
            case LoopForeverStmt loop:
                CheckRepeatedly(loop.Body, NothingProven);
                break;

            case ForStmt loop:
                CheckRangeLoop(loop);
                break;

            case ForEachStmt loop:
                CheckForEach(loop);
                break;

            case SwitchStmt switchStmt:
                CheckSwitch(switchStmt);
                break;

            case TryStmt tryStmt:
                CheckTry(tryStmt);
                break;

            case ThrowStmt throwStmt:
                CheckExpression(throwStmt.Exception);
                break;

            case YieldStmt yieldStmt:
                CheckYield(yieldStmt);
                break;

            case ExpressionStmt expression:
                CheckExpression(expression.Expression);
                break;

            case AssignmentStmt assignment:
                CheckAssignment(assignment);
                break;
        }
    }

    /// <summary>
    /// <para>Checks an if and its whole chain, carrying what each condition proves into the
    /// body it guards.</para>
    /// <para>An else-if sees that every condition before it failed, which is what lets a
    /// chain of checks narrow progressively.</para>
    /// <para>Afterwards only what every arm agrees on is known. A chain with no <c>else</c> has
    /// one more way through than it has arms — the one where nothing ran — and counting it is
    /// what stops a value stored under a condition being trusted where the condition
    /// failed.</para>
    /// </summary>
    private void CheckIf(IfStmt branch)
    {
        RequireBoolean(CheckExpression(branch.Condition), branch.Condition, "An if condition");

        NarrowingFacts facts = AnalyzeCondition(branch.Condition);
        HashSet<Symbol> entry = Known();
        List<HashSet<Symbol>> arms = [];

        // Checked either way, since an arm that leaves still has to be right about what it does.
        // Only what arrives at the join is collected.
        Arrive(branch.ThenBody, CheckArm(entry, facts.WhenTrue, () => CheckStatements(branch.ThenBody)));

        // Everything an earlier condition proved by failing still holds further down.
        HashSet<Symbol> failedSoFar = [.. facts.WhenFalse];

        foreach (ElseIfClause clause in branch.ElseIfClauses)
        {
            // The condition is reached only where every one before it failed, so it is read
            // knowing that much and nothing an arm did.
            KnowOnly(entry);
            _narrowed.UnionWith(failedSoFar);

            RequireBoolean(
                CheckExpression(clause.Condition), clause.Condition, "An else-if condition");

            NarrowingFacts clauseFacts = AnalyzeCondition(clause.Condition);

            Arrive(
                clause.Body,
                CheckArm(Known(), clauseFacts.WhenTrue, () => CheckStatements(clause.Body)));

            failedSoFar.UnionWith(clauseFacts.WhenFalse);
        }

        if (branch.ElseBody is not null)
        {
            Arrive(
                branch.ElseBody,
                CheckArm(entry, failedSoFar, () => CheckStatements(branch.ElseBody)));
        }
        else
        {
            // The way through where no arm ran: every condition failed, and none of the bodies
            // happened. It always arrives, being the one that did nothing.
            HashSet<Symbol> nothingRan = [.. entry];
            nothingRan.UnionWith(failedSoFar);
            arms.Add(nothingRan);
        }

        KeepWhatEveryArmAgreesOn(entry, arms);

        void Arrive(IReadOnlyList<Statement> body, HashSet<Symbol> ended)
        {
            if (ReachesItsEnd(body))
            {
                arms.Add(ended);
            }
        }
    }

    /// <summary>
    /// <para>Checks a try, its catches and its finally.</para>
    /// <para>An exception may leave the body from anywhere in it, so a catch begins knowing only
    /// what the body could not have taken away, and so does what follows the whole
    /// statement.</para>
    /// </summary>
    private void CheckTry(TryStmt tryStmt)
    {
        HashSet<Symbol> throughout = Known();
        throughout.ExceptWith(AssignedIn(tryStmt.Body));

        foreach (CatchClause clause in tryStmt.Catches)
        {
            throughout.ExceptWith(AssignedIn(clause.Body));
        }

        if (tryStmt.FinallyBody is not null)
        {
            throughout.ExceptWith(AssignedIn(tryStmt.FinallyBody));
        }

        // The body itself does begin where the statement was reached: it is the one part that
        // is not entered part-way through.
        CheckStatements(tryStmt.Body);

        foreach (CatchClause clause in tryStmt.Catches)
        {
            CheckCatchIsReachable(clause);
            KnowOnly(throughout);
            CheckStatements(clause.Body);
        }

        if (tryStmt.FinallyBody is not null)
        {
            KnowOnly(throughout);
            CheckStatements(tryStmt.FinallyBody);
        }

        KnowOnly(throughout);
    }

    private void CheckVarDecl(VarDeclStmt declaration)
    {
        LocalSymbol? local = _model.GetSymbol(declaration) as LocalSymbol;

        if (declaration.IsInferred)
        {
            if (declaration.Initializer is null)
            {
                Report(DiagnosticDescriptors.InferredDeclarationNeedsInitializer, declaration);
                return;
            }

            TypeSymbol inferred = CheckExpression(declaration.Initializer);

            // An empty set says nothing about what it holds, so there is nothing to infer.
            if (inferred is SetType { ElementType.IsError: true })
            {
                Report(DiagnosticDescriptors.CannotInferEmptyCollection, declaration.Initializer);
            }

            // A call that yields nothing has no result to give the name, and inferring from it
            // would quietly declare a variable holding nothing at all.
            if (ReferenceEquals(inferred, PrimitiveType.Void))
            {
                Report(DiagnosticDescriptors.ValueExpected, declaration.Initializer);
            }

            if (local is not null)
            {
                local.Type = inferred;
            }

            return;
        }

        TypeSymbol declared = local?.Type ?? ErrorType.Instance;

        if (declaration.Initializer is null)
        {
            if (declaration.IsConstant)
            {
                Report(DiagnosticDescriptors.ConstantNeedsInitializer, declaration, declaration.Name);
            }

            return;
        }

        // The declared type is what a collection literal is measured against, so that a set of
        // shapes may be written as a literal of the several kinds of shape it holds.
        TypeSymbol actual = CheckExpressionAgainst(declaration.Initializer, declared);

        // An empty set takes the declared element type, which is why writing the type is what
        // makes an empty literal usable.
        if (actual is not SetType { ElementType.IsError: true })
        {
            RequireAssignable(actual, declared, declaration.Initializer);
        }

        if (declaration.IsConstant)
        {
            CheckConstant(declaration.Name, declared, declaration.Initializer, declaration);
        }
    }

    private void CheckRangeLoop(ForStmt loop)
    {
        // The counter is an integer by construction, so only the bounds can disagree.
        RequireInteger(CheckExpression(loop.Start), loop.Start);
        RequireInteger(CheckExpression(loop.Bound), loop.Bound);

        if (loop.Step is not null)
        {
            RequireInteger(CheckExpression(loop.Step), loop.Step);
        }

        CheckRepeatedly(loop.Body, NothingProven);
    }

    private void RequireInteger(TypeSymbol type, SyntaxNode node)
    {
        if (!type.IsError && !ReferenceEquals(type, PrimitiveType.Integer))
        {
            Report(DiagnosticDescriptors.RangeLoopNeedsInteger, node, type.WithArticle());
        }
    }

    /// <summary>
    /// Iterating works over a set, and over a string, which yields its characters. The two
    /// read alike deliberately.
    /// </summary>
    private void CheckForEach(ForEachStmt loop)
    {
        TypeSymbol sequence = CheckExpression(loop.Sequence);

        TypeSymbol element = sequence switch
        {
            SetType set => set.ElementType,
            PrimitiveType p when ReferenceEquals(p, PrimitiveType.String) => PrimitiveType.Character,
            _ when sequence.IsError => ErrorType.Instance,
            _ => ReportNotIterable(loop, sequence),
        };

        if (_model.GetSymbol(loop) is LocalSymbol variable)
        {
            variable.Type = element;
        }

        CheckRepeatedly(loop.Body, NothingProven);

        // After the body, not before: which member a call reaches is settled by checking it,
        // and nothing in the body is bound to a built-in until then.
        RequireSequenceLeftAlone(loop);
    }

    /// <summary>The set members that change what a set holds, rather than yielding a new one.</summary>
    private static readonly BuiltInId[] Mutating =
    [
        BuiltInId.SetInsert,
        BuiltInId.SetInsertAt,
        BuiltInId.SetRemove,
        BuiltInId.SetRemoveAt,
        BuiltInId.SetClear,
    ];

    /// <summary>
    /// <para>Refuses a change to the sequence a <c>for each</c> is walking.</para>
    /// <para>Matched by the name written: the loop's sequence has to be a plain name or a
    /// field, and a call in the body has to be on that same symbol. A set reached through
    /// something else — handed to a function, or held under a second name — is not caught, and
    /// nothing here claims otherwise. What this catches is the case a reader writes by
    /// accident, which is the one worth a message.</para>
    /// </summary>
    private void RequireSequenceLeftAlone(ForEachStmt loop)
    {
        if (SymbolOf(loop.Sequence) is not { } walked)
        {
            return;
        }

        IEnumerable<SyntaxNode> body = loop.Body
            .SelectMany(statement => new[] { (SyntaxNode)statement }.Concat(statement.Descendants()));

        foreach (SyntaxNode node in body)
        {
            if (node is not CallExpr { Callee: MemberExpr member } call
                || _model.GetBuiltIn(member) is not { } which
                || !Mutating.Contains(which)
                || SymbolOf(member.Receiver) is not { } receiver
                || !ReferenceEquals(receiver, walked))
            {
                continue;
            }

            Report(
                DiagnosticDescriptors.SequenceChangedWhileWalked,
                call,
                walked.Name,
                member.MemberName);
        }
    }

    /// <summary>
    /// <para>A <c>catch</c> clause naming an exception nothing ever hands to one.</para>
    /// <para>Nothing in the line separates a name that can be caught from one that cannot, so
    /// without this the clause reads as a handler and silently never runs — which is worse than
    /// the name being unavailable, because the author believes they covered the case.</para>
    /// </summary>
    private void CheckCatchIsReachable(CatchClause clause)
    {
        if (_model.GetType(clause.ExceptionType) is { } named
            && !Runtime.BuiltInExceptions.MayBeCaught(named.Name))
        {
            Report(DiagnosticDescriptors.ExceptionCannotBeCaught, clause.ExceptionType, named.Name);
        }
    }

    /// <summary>
    /// The symbol a plain name or a field access denotes, or null for anything else. Anything
    /// else cannot be compared for sameness by looking at it, which is the whole basis of the
    /// check above.
    /// </summary>
    private Symbol? SymbolOf(Expression expression) => expression switch
    {
        IdentifierExpr identifier => _model.GetSymbol(identifier),
        MemberExpr { Receiver: ReceiverExpr } member => _model.GetSymbol(member),
        _ => null,
    };

    private TypeSymbol ReportNotIterable(ForEachStmt loop, TypeSymbol sequence)
    {
        Report(DiagnosticDescriptors.ForEachNeedsSequence, loop.Sequence, sequence.WithArticle());
        return ErrorType.Instance;
    }

    /// <summary>
    /// Checks a switch, including that every label is a constant and that no value is handled
    /// twice. Reals and fractions cannot be examined at all, since equality on them is a trap.
    /// </summary>
    private void CheckSwitch(SwitchStmt switchStmt)
    {
        TypeSymbol subject = CheckExpression(switchStmt.Subject);

        if (!Conversions.IsSwitchable(subject))
        {
            Report(DiagnosticDescriptors.NotSwitchable, switchStmt.Subject, subject.WithArticle());
        }

        HashSet<object> seen = [];

        // A label that did not land leaves "seen" short of what was written, so what is
        // missing cannot be worked out from it. Recorded rather than reported again: the label
        // has already been named, and a second message about the switch as a whole would point
        // at the same mistake from further away.
        bool everyLabelLanded = true;

        HashSet<Symbol> entry = Known();
        List<HashSet<Symbol>> arms = [];

        foreach (CaseGroup group in switchStmt.Cases)
        {
            // Labels are reached however the switch was, not by way of the arm before them.
            KnowOnly(entry);

            foreach (Expression label in group.Labels)
            {
                TypeSymbol labelType = CheckExpression(label);

                if (!RequireAssignable(labelType, subject, label))
                {
                    everyLabelLanded = false;
                }

                object? value = ConstantFolder.TryFold(label, _model);

                if (value is null)
                {
                    if (!labelType.IsError)
                    {
                        Report(DiagnosticDescriptors.CaseLabelNotConstant, label);
                    }

                    everyLabelLanded = false;
                    continue;
                }

                if (!seen.Add(value))
                {
                    Report(DiagnosticDescriptors.DuplicateCaseLabel, label, value);
                }
            }

            HashSet<Symbol> ended = CheckArm(entry, NothingProven, () => CheckStatements(group.Body));

            if (ReachesItsEnd(group.Body))
            {
                arms.Add(ended);
            }
        }

        if (switchStmt.DefaultBody is not null)
        {
            HashSet<Symbol> ended =
                CheckArm(entry, NothingProven, () => CheckStatements(switchStmt.DefaultBody));

            if (ReachesItsEnd(switchStmt.DefaultBody))
            {
                arms.Add(ended);
            }
        }
        else
        {
            // Nothing says one of the cases matched, so the way through where none did counts.
            arms.Add(entry);
        }

        KeepWhatEveryArmAgreesOn(entry, arms);

        if (switchStmt.DefaultBody is not null || !everyLabelLanded)
        {
            return;
        }

        RequireEveryMemberHandled(switchStmt, subject, seen);
    }

    /// <summary>
    /// <para>Reports an enumeration switch that leaves members unhandled and writes no
    /// default.</para>
    /// <para>Members are compared by the value each carries rather than by name, because two
    /// members may name one value and a case for either handles both.</para>
    /// </summary>
    private void RequireEveryMemberHandled(
        SwitchStmt switchStmt,
        TypeSymbol subject,
        HashSet<object> handled)
    {
        if (subject is not EnumerationSymbol enumeration)
        {
            return;
        }

        List<string> unhandled =
            [.. enumeration.Members.Values
                .SelectMany(members => members)
                .OfType<EnumMemberSymbol>()
                .OrderBy(member => member.Value)
                .Where(member => !handled.Contains(member.Value))
                .Select(member => member.Name)];

        if (unhandled.Count > 0)
        {
            Report(
                DiagnosticDescriptors.SwitchNotExhaustive,
                switchStmt.Subject,
                enumeration.Name,
                Wording.List(unhandled),
                unhandled.Count == 1 ? "has" : "have");
        }
    }

    private void CheckYield(YieldStmt yieldStmt)
    {
        // Inside a block-bodied lambda there is nothing to check against: the lambda's result
        // is whatever it yields, so the yield defines the answer rather than being measured
        // against one.
        if (_lambdaYields is not null)
        {
            if (yieldStmt.Value is not null)
            {
                _lambdaYields.Add(CheckExpression(yieldStmt.Value));
            }

            return;
        }

        TypeSymbol? expected = _currentFunction?.ReturnType;
        string name = _currentFunction?.Name ?? "this function";

        if (yieldStmt.Value is null)
        {
            if (expected is not null)
            {
                Report(DiagnosticDescriptors.YieldMissingValue, yieldStmt, name, expected.WithArticle());
            }

            return;
        }

        TypeSymbol actual = expected is null
            ? CheckExpression(yieldStmt.Value)
            : CheckExpressionAgainst(yieldStmt.Value, expected);

        if (expected is null)
        {
            Report(DiagnosticDescriptors.YieldValueInVoidFunction, yieldStmt, name);
            return;
        }

        RequireAssignable(actual, expected, yieldStmt.Value);
    }

    private void CheckAssignment(AssignmentStmt assignment)
    {
        // A throwaway on the left names nothing, so there is no type to fit into. The value is
        // still checked, since it still runs.
        if (assignment.Target is IdentifierExpr name && Throwaway.Is(name.Name))
        {
            CheckExpression(assignment.Value);
            return;
        }

        // The declared type, not the narrowed one: assigning to a narrowed optional may put
        // an empty value back into it, and the declaration is what says whether that fits.
        TypeSymbol target = DeclaredTypeOf(assignment.Target);
        TypeSymbol value = CheckExpressionAgainst(assignment.Value, target);

        RequireAssignable(value, target, assignment.Value);
        UpdateNarrowingAfterAssignment(assignment.Target, value);
    }

    /// <summary>The type a target was declared with, ignoring anything proven about it.</summary>
    private TypeSymbol DeclaredTypeOf(Expression target)
    {
        TypeSymbol checkedType = CheckExpression(target);

        return _model.GetSymbol(target) switch
        {
            LocalSymbol local => local.Type,
            ParameterSymbol parameter => parameter.Type,
            FieldSymbol field => field.Type,
            _ => checkedType,
        };
    }
}
