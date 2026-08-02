using ProfiC.Compiler.Ast;

namespace ProfiC.Compiler.Semantics;

/// <summary>What a condition tells us about optionals, depending on how it turns out.</summary>
/// <param name="WhenTrue">Locals known to hold a value when the condition holds.</param>
/// <param name="WhenFalse">Locals known to hold a value when it does not.</param>
public readonly record struct NarrowingFacts(
    IReadOnlySet<Symbol> WhenTrue,
    IReadOnlySet<Symbol> WhenFalse)
{
    public static readonly NarrowingFacts None =
        new(new HashSet<Symbol>(), new HashSet<Symbol>());
}

public sealed partial class TypeChecker
{
    /// <summary>
    /// <para>Optional locals known to hold a value at this point in the walk.</para>
    /// <para>Only locals and parameters are ever recorded. A field would be unsound: any call
    /// in between could replace it, so a check made before the call would say nothing about
    /// after it. Kotlin declines to narrow mutable properties for the same reason, and the fix
    /// is the same — copy it into a local first.</para>
    /// </summary>
    private readonly HashSet<Symbol> _narrowed = [];

    /// <summary>
    /// <para>Locals and parameters that are never narrowed, however plainly they were proved,
    /// because something that captured them assigns them.</para>
    /// <para>The same reasoning that keeps a field out of <see cref="_narrowed"/>, arrived at
    /// from the other side: a lambda holding the name can be called from anywhere, so any call
    /// in between may have emptied it and a proof made before that call says nothing about
    /// after. Narrowing is about a value nothing else can reach, and one written into a closure
    /// is no longer that.</para>
    /// </summary>
    private readonly HashSet<Symbol> _reachableFromAClosure = [];

    /// <summary>
    /// The type an expression has here, accounting for what has been proven about it. A
    /// narrowed optional is its underlying type, which is what makes a guarded block able to
    /// use the value without unwrapping it again.
    /// </summary>
    private TypeSymbol ApplyNarrowing(Expression expression, TypeSymbol type)
    {
        if (type is not OptionalType optional)
        {
            return type;
        }

        Symbol? symbol = _model.GetSymbol(expression);

        return symbol is not null && _narrowed.Contains(symbol)
            ? optional.UnderlyingType
            : type;
    }

    /// <summary>
    /// <para>Reads what a condition proves.</para>
    /// <para>Only the shapes that carry real information are recognized. Everything else
    /// proves nothing, which is the safe answer.</para>
    /// </summary>
    private NarrowingFacts AnalyzeCondition(Expression condition)
    {
        switch (condition)
        {
            case ParenthesizedExpr parenthesized:
                return AnalyzeCondition(parenthesized.Inner);

            // "n.HasValue()" is the whole point of the analysis.
            case CallExpr { Callee: MemberExpr { MemberName: "HasValue" } member, Arguments.Count: 0 }:
            {
                Symbol? symbol = _model.GetSymbol(member.Receiver);

                if (symbol is not (LocalSymbol or ParameterSymbol)
                    || _reachableFromAClosure.Contains(symbol))
                {
                    return NarrowingFacts.None;
                }

                return new NarrowingFacts(new HashSet<Symbol> { symbol }, new HashSet<Symbol>());
            }

            // Negation swaps what each outcome proves.
            case UnaryExpr { Operator: UnaryOperator.Not } negation:
            {
                NarrowingFacts inner = AnalyzeCondition(negation.Operand);
                return new NarrowingFacts(inner.WhenFalse, inner.WhenTrue);
            }

            // Both sides hold when an "and" holds; either may have failed when it does not.
            case BinaryExpr { Operator: BinaryOperator.And } conjunction:
            {
                NarrowingFacts left = AnalyzeCondition(conjunction.Left);
                NarrowingFacts right = AnalyzeCondition(conjunction.Right);

                return new NarrowingFacts(
                    Union(left.WhenTrue, right.WhenTrue),
                    Intersect(left.WhenFalse, right.WhenFalse));
            }

            // Mirror image: both sides failed when an "or" fails.
            case BinaryExpr { Operator: BinaryOperator.Or } disjunction:
            {
                NarrowingFacts left = AnalyzeCondition(disjunction.Left);
                NarrowingFacts right = AnalyzeCondition(disjunction.Right);

                return new NarrowingFacts(
                    Intersect(left.WhenTrue, right.WhenTrue),
                    Union(left.WhenFalse, right.WhenFalse));
            }

            default:
                return NarrowingFacts.None;
        }
    }

    private static HashSet<Symbol> Union(IReadOnlySet<Symbol> left, IReadOnlySet<Symbol> right)
    {
        HashSet<Symbol> result = [.. left];
        result.UnionWith(right);
        return result;
    }

    private static HashSet<Symbol> Intersect(IReadOnlySet<Symbol> left, IReadOnlySet<Symbol> right)
    {
        HashSet<Symbol> result = [.. left];
        result.IntersectWith(right);
        return result;
    }

    /// <summary>Nothing proven, for a body reached without a condition to read.</summary>
    private static readonly HashSet<Symbol> NothingProven = [];

    /// <summary>What is known here, to come back to or to branch from.</summary>
    private HashSet<Symbol> Known() => [.. _narrowed];

    /// <summary>Puts back what was known at some earlier point, exactly.</summary>
    private void KnowOnly(IReadOnlySet<Symbol> known)
    {
        _narrowed.Clear();
        _narrowed.UnionWith(known);
    }

    /// <summary>
    /// <para>Runs an expression with some extra facts in force, and keeps neither them nor
    /// anything established while they held.</para>
    /// <para>An expression cannot assign, so ordinarily there is nothing to established — but a
    /// lambda written inside one has a body, and a body can. Whether that body ever runs is not
    /// something the code after the expression knows.</para>
    /// </summary>
    private void WithNarrowing(IReadOnlySet<Symbol> extra, Action body)
    {
        HashSet<Symbol> before = Known();

        _narrowed.UnionWith(extra);

        try
        {
            body();
        }
        finally
        {
            // Intersected rather than replaced, so that a fact the body took away stays away.
            _narrowed.IntersectWith(before);
        }
    }

    /// <summary>
    /// <para>Checks one way a branch may go, and reports what is known at the end of that
    /// way.</para>
    /// <para>Started from the state the branch was reached in rather than from wherever the arm
    /// before it finished, so that arms cannot see each other's assignments.</para>
    /// </summary>
    private HashSet<Symbol> CheckArm(
        IReadOnlySet<Symbol> entry,
        IReadOnlySet<Symbol> proven,
        Action arm)
    {
        KnowOnly(entry);
        _narrowed.UnionWith(proven);
        arm();

        return Known();
    }

    /// <summary>
    /// <para>Keeps what every way through agrees on, and nothing else.</para>
    /// <para>This is what stops an assignment inside a branch outliving the branch. A value
    /// stored where the condition held is not there when it did not hold, so it is known
    /// afterwards only where every arm stored one.</para>
    /// <para>Only arms that arrive are passed in. Where none does, nothing after the branch runs
    /// and what is known there settles nothing.</para>
    /// </summary>
    private void KeepWhatEveryArmAgreesOn(
        IReadOnlySet<Symbol> whenNoneArrive,
        List<HashSet<Symbol>> arms)
    {
        if (arms.Count == 0)
        {
            KnowOnly(whenNoneArrive);
            return;
        }

        KnowOnly(arms[0]);

        foreach (HashSet<Symbol> arm in arms)
        {
            _narrowed.IntersectWith(arm);
        }
    }

    /// <summary>
    /// <para>Whether control reaches the end of some statements, or always leaves before it.
    /// </para>
    /// <para>An arm that always leaves never arrives at the join after it, so what it knew has
    /// nothing to say about what holds past the join. That is what makes a guard written as an
    /// early exit narrow everything after it: where the empty case has already left, the only
    /// way to be standing here is the other one.</para>
    /// <para>Whatever this cannot tell it answers no to. An arm counted that does not really
    /// arrive keeps less than it could have, which costs a reader some convenience; one left out
    /// that does arrive keeps more than is true, which is a wrong program.</para>
    /// </summary>
    private static bool ReachesItsEnd(IReadOnlyList<Statement> statements) =>
        !statements.Any(AlwaysLeaves);

    private static bool AlwaysLeaves(Statement statement) => statement switch
    {
        // Out of the function, out of the loop, or on to its next turn. None of them arrive.
        YieldStmt or ThrowStmt or BreakStmt or ContinueStmt => true,

        BlockStmt block => !ReachesItsEnd(block.Statements),

        // Every way through has to leave — and a chain with no else has a way through that runs
        // none of the arms at all.
        IfStmt branch =>
            branch.ElseBody is not null
            && !ReachesItsEnd(branch.ThenBody)
            && branch.ElseIfClauses.All(clause => !ReachesItsEnd(clause.Body))
            && !ReachesItsEnd(branch.ElseBody),

        // A finally that leaves takes the whole statement with it, however the rest went.
        // Otherwise the body and every catch have to leave, since any of them may be what runs.
        TryStmt tryStmt =>
            (tryStmt.FinallyBody is not null && !ReachesItsEnd(tryStmt.FinallyBody))
            || (!ReachesItsEnd(tryStmt.Body)
                && tryStmt.Catches.All(clause => !ReachesItsEnd(clause.Body))),

        // A loop may run no turns, and a switch is arrived at past by a break inside it. Neither
        // is worth reading further for the little it would add.
        _ => false,
    };

    /// <summary>
    /// <para>Checks a body that may run any number of times — none, once, or many — and leaves
    /// known only what holds however it went.</para>
    /// <para>Everything the body assigns is forgotten before it is checked, because a turn after
    /// the first begins wherever the one before it ended, and a value stored late in the body was
    /// not there early in it. What is left afterwards is that same set and no more: the body may
    /// not have run at all, and a <c>break</c> may have left it from anywhere inside.</para>
    /// <para>So nothing assigned in a loop is narrowed after it. That is not a shortcut — a set
    /// walked by a loop may be empty, and the one bound name in the language that says otherwise
    /// is a body that always runs, which <c>break</c> takes away again.</para>
    /// </summary>
    private void CheckRepeatedly(IReadOnlyList<Statement> body, IReadOnlySet<Symbol> proven)
    {
        HashSet<Symbol> throughout = Known();
        throughout.ExceptWith(AssignedIn(body));

        KnowOnly(throughout);
        _narrowed.UnionWith(proven);
        CheckStatements(body);
        KnowOnly(throughout);
    }

    /// <summary>
    /// <para>The locals and parameters some statements assign to, anywhere inside them.</para>
    /// <para>Read from the names the resolver bound rather than from checking, so that it can be
    /// asked before the statements are walked. That is the point of it: what it answers is what
    /// the walk must not begin by trusting.</para>
    /// </summary>
    /// <summary>
    /// <para>The locals and parameters assigned by a lambda or a nested function written
    /// anywhere in here.</para>
    /// <para>Read before the body is walked, and kept for the whole of it — a closure written at
    /// the bottom can be called from the top, so where in the body it sits says nothing about
    /// which lines it makes unsafe.</para>
    /// </summary>
    private IEnumerable<Symbol> AssignedByAClosureIn(IReadOnlyList<Statement> statements) =>
        statements
            .SelectMany(statement => statement.Descendants().Prepend(statement))
            .SelectMany(node => node switch
            {
                LambdaExpr lambda => AssignedIn(lambda.Body ?? []),
                LocalDeclStmt { Declaration: FunctionDecl nested } => AssignedIn(nested.Body ?? []),
                _ => [],
            });

    private IEnumerable<Symbol> AssignedIn(IReadOnlyList<Statement> statements) =>
        statements
            .SelectMany(statement => statement.Descendants().Prepend(statement))
            .OfType<AssignmentStmt>()
            .Select(assignment => _model.GetSymbol(assignment.Target))
            .Where(symbol => symbol is LocalSymbol or ParameterSymbol)
            .Select(symbol => symbol!);

    /// <summary>
    /// <para>The type an expression was declared with, ignoring anything proven about it.</para>
    /// <para>Member lookup needs this: inside a guarded block a <c>Node?</c> reads as a
    /// <c>Node</c>, but writing <c>n.Value()</c> anyway must still work. Narrowing is a
    /// convenience, not a removal of the members the optional actually has.</para>
    /// </summary>
    private TypeSymbol? UnnarrowedTypeOf(Expression expression) =>
        _model.GetSymbol(expression) switch
        {
            LocalSymbol local => local.Type,
            ParameterSymbol parameter => parameter.Type,
            _ => null,
        };

    /// <summary>
    /// Records that a local now holds a value, or no longer does, after being assigned. An
    /// assignment is the other way presence becomes known.
    /// </summary>
    private void UpdateNarrowingAfterAssignment(Expression target, TypeSymbol assignedType)
    {
        Symbol? symbol = _model.GetSymbol(target);

        if (symbol is not (LocalSymbol or ParameterSymbol)
            || _reachableFromAClosure.Contains(symbol))
        {
            return;
        }

        if (assignedType is OptionalType)
        {
            // An optional was stored, so nothing is known about presence any more.
            _narrowed.Remove(symbol);
            return;
        }

        if (!assignedType.IsError)
        {
            _narrowed.Add(symbol);
        }
    }
}
