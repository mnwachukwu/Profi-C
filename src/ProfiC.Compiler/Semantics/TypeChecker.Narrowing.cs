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

                if (symbol is not (LocalSymbol or ParameterSymbol))
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

    /// <summary>
    /// Runs an action with some extra facts in force, restoring what was known afterwards.
    /// Narrowing holds only inside the block it was proven for.
    /// </summary>
    private void WithNarrowing(IReadOnlySet<Symbol> extra, Action body)
    {
        List<Symbol> added = [.. extra.Where(s => _narrowed.Add(s))];

        try
        {
            body();
        }
        finally
        {
            foreach (Symbol symbol in added)
            {
                _narrowed.Remove(symbol);
            }
        }
    }

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

        if (symbol is not (LocalSymbol or ParameterSymbol))
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
