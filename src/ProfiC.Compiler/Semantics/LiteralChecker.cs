using System.Globalization;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Reports every number written in a program that names no value.</para>
/// <para>A walk of the whole tree rather than a hook into the type checker's, because a literal
/// can sit where an expression is never typed — an enumeration's ordinal is read by the resolver
/// and nowhere else — and a number too large to hold is wrong wherever it is written. Walking
/// <see cref="SyntaxNode.Children"/> reaches all of them by construction, and reaches each one
/// once, which is what keeps a single mistake from being reported by all three of the things
/// that decode a literal.</para>
/// <para>Everything downstream depends on this having run. <see cref="LiteralDecoder.Decode"/>
/// answers null for these, the interpreter turns that null into an absent value, and the emitter
/// has no sequence for it and says so — none of which is a message anybody can act on. With this
/// pass reporting, no program that checks reaches any of them.</para>
/// </summary>
public static class LiteralChecker
{
    /// <summary>One number that names no value, and where to point at it.</summary>
    /// <param name="Literal">The literal itself, which carries the digits and the kind.</param>
    /// <param name="Span">
    /// What the caret covers. The literal's own span, except where a minus sign in front of it
    /// is part of what went wrong.
    /// </param>
    /// <param name="Negated">Whether a minus sign in front of it is part of what went wrong.</param>
    private readonly record struct Faulty(LiteralExpr Literal, SourceSpan Span, bool Negated);

    /// <summary>The first whole number an integer cannot hold, which is the interesting one.</summary>
    private static readonly string PastTheLargest =
        ((ulong)long.MaxValue + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Checks one file. The bag is already scoped to it.</summary>
    public static void Check(CompilationUnit unit, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (Faulty faulty in FaultsIn(unit))
        {
            Report(faulty, diagnostics);
        }
    }

    /// <summary>
    /// <para>Whether anything under this node is a number that names no value.</para>
    /// <para>Asked by the checks that would otherwise blame the wrong thing. A constant built
    /// from a number too large to hold does not fold, and saying it "can only be built from
    /// literals and other constants" of an expression that is a literal sends a reader looking
    /// for a second mistake that is not there.</para>
    /// </summary>
    public static bool HasAFaultyNumber(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return FaultsIn(node).Any();
    }

    private static IEnumerable<Faulty> FaultsIn(SyntaxNode node)
    {
        // A negated number is read as one thing, so that the caret covers the minus sign and
        // the message can talk about it. Taken the other way round, '-9223372036854775808'
        // reports against the digits alone and tells somebody that their most negative integer
        // is too large — true of the digits, and useless as an explanation.
        if (node is UnaryExpr { Operator: UnaryOperator.Negate, Operand: LiteralExpr negated })
        {
            if (LiteralDecoder.FaultIn(negated) is not LiteralFault.None)
            {
                yield return new Faulty(negated, node.Span, Negated: true);
            }

            yield break;
        }

        if (node is LiteralExpr literal)
        {
            if (LiteralDecoder.FaultIn(literal) is not LiteralFault.None)
            {
                yield return new Faulty(literal, literal.Span, Negated: false);
            }

            yield break;
        }

        foreach (SyntaxNode child in node.Children)
        {
            foreach (Faulty faulty in FaultsIn(child))
            {
                yield return faulty;
            }
        }
    }

    private static void Report(Faulty faulty, DiagnosticBag diagnostics)
    {
        switch (LiteralDecoder.FaultIn(faulty.Literal))
        {
            case LiteralFault.TooLarge:
                diagnostics.Report(
                    DiagnosticDescriptors.NumberTooLarge,
                    faulty.Span,
                    faulty.Literal.Text,
                    Article(faulty.Literal.Kind),
                    Advice(faulty));
                break;

            case LiteralFault.OverZero:
                diagnostics.Report(
                    DiagnosticDescriptors.FractionOverZero, faulty.Span, faulty.Literal.Text);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Whether a whole number names the base its digits are in. The scanner accepts the prefix
    /// in either case, so both are read here.
    /// </summary>
    private static bool HasABase(string text) =>
        text.Length > 2 && text[0] == '0' && text[1] is 'x' or 'X' or 'b' or 'B';

    private static string Article(LiteralKind kind) => kind switch
    {
        LiteralKind.Integer => "an integer",
        LiteralKind.Real => "a real",
        _ => "a fraction",
    };

    /// <summary>
    /// What to write instead, which is the half of the message worth reading. Each names the
    /// bound in the language's own words rather than only in digits, so the name to reach for is
    /// in front of the reader.
    /// </summary>
    private static string Advice(Faulty faulty) => faulty.Literal.Kind switch
    {
        // The most negative integer, spelled out. The minus is a separate operator, so the
        // digits after it are one past the largest and there is no literal for this value at
        // all — which is exactly why Integer.MinValue is a name.
        LiteralKind.Integer when faulty.Negated && faulty.Literal.Text == PastTheLargest =>
            "The minus is a separate operator here, so what follows it is one past the largest "
            + "an integer holds. Integer.MinValue names the most negative one.",

        // Written in base sixteen or base two, where a decimal point is not something the
        // number can be given. What a reader of one of these has is too many digits, so the
        // bound worth naming is how many there is room for.
        LiteralKind.Integer when HasABase(faulty.Literal.Text) =>
            "An integer holds 64 bits, which is 16 digits in base sixteen and 64 in base two.",

        LiteralKind.Integer =>
            "An integer stops at Integer.MaxValue, which is "
            + $"{long.MaxValue.ToString(CultureInfo.InvariantCulture)}. Write a decimal point "
            + "against the number to read it as a real instead.",

        LiteralKind.Real =>
            "A real stops at Real.MaxValue. A float reaches further and keeps fewer digits: "
            + "write an f against the number to read it as one.",

        _ =>
            "Both halves of a fraction are whole numbers, and this one has a half that outgrows "
            + "Integer.MaxValue.",
    };
}
