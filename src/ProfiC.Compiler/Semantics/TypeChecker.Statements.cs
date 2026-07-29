using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    private void CheckStatements(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
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
                WithNarrowing(AnalyzeCondition(loop.Condition).WhenTrue,
                              () => CheckStatements(loop.Body));
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
                CheckStatements(tryStmt.Body);

                foreach (CatchClause clause in tryStmt.Catches)
                {
                    CheckStatements(clause.Body);
                }

                if (tryStmt.FinallyBody is not null)
                {
                    CheckStatements(tryStmt.FinallyBody);
                }

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
    /// </summary>
    private void CheckIf(IfStmt branch)
    {
        RequireBoolean(CheckExpression(branch.Condition), branch.Condition, "An if condition");

        NarrowingFacts facts = AnalyzeCondition(branch.Condition);
        WithNarrowing(facts.WhenTrue, () => CheckStatements(branch.ThenBody));

        // Everything an earlier condition proved by failing still holds further down.
        HashSet<Symbol> failedSoFar = [.. facts.WhenFalse];

        foreach (ElseIfClause clause in branch.ElseIfClauses)
        {
            WithNarrowing(failedSoFar, () =>
            {
                RequireBoolean(CheckExpression(clause.Condition), clause.Condition, "An else-if condition");

                NarrowingFacts clauseFacts = AnalyzeCondition(clause.Condition);
                WithNarrowing(clauseFacts.WhenTrue, () => CheckStatements(clause.Body));
                failedSoFar.UnionWith(clauseFacts.WhenFalse);
            });
        }

        if (branch.ElseBody is not null)
        {
            WithNarrowing(failedSoFar, () => CheckStatements(branch.ElseBody));
        }
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

        CheckStatements(loop.Body);
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

        CheckStatements(loop.Body);
    }

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

        foreach (CaseGroup group in switchStmt.Cases)
        {
            foreach (Expression label in group.Labels)
            {
                TypeSymbol labelType = CheckExpression(label);
                RequireAssignable(labelType, subject, label);

                object? value = ConstantFolder.TryFold(label, _model);

                if (value is null)
                {
                    if (!labelType.IsError)
                    {
                        Report(DiagnosticDescriptors.CaseLabelNotConstant, label);
                    }

                    continue;
                }

                if (!seen.Add(value))
                {
                    Report(DiagnosticDescriptors.DuplicateCaseLabel, label, value);
                }
            }

            CheckStatements(group.Body);
        }

        if (switchStmt.DefaultBody is not null)
        {
            CheckStatements(switchStmt.DefaultBody);
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
