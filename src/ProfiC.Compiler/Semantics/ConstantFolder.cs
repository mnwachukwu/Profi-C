using ProfiC.Compiler.Ast;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Works out the value of an expression while compiling, or reports that it cannot.</para>
/// <para>Needed in three places, which is why it is worth having: a <c>constant</c> must have
/// a value known while compiling, a <c>switch</c> case label must be one, and division by an
/// obvious zero should be caught before the program runs rather than after.</para>
/// </summary>
public static class ConstantFolder
{
    /// <summary>
    /// The value of an expression, or null when it cannot be worked out. A null is not an
    /// error on its own — most expressions are not constant, and only some places require it.
    /// </summary>
    public static object? TryFold(Expression expression, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(model);

        return Fold(expression, model, depth: 0);
    }

    private static object? Fold(Expression expression, SemanticModel model, int depth)
    {
        // A guard rather than a limit on what programs may write: constant expressions are
        // short in practice, and a runaway here would be a compiler hang.
        if (depth > 64)
        {
            return null;
        }

        switch (expression)
        {
            case LiteralExpr literal:
                return LiteralDecoder.Decode(literal);

            case ParenthesizedExpr parenthesized:
                return Fold(parenthesized.Inner, model, depth + 1);

            case UnaryExpr unary:
                return FoldUnary(unary, model, depth);

            case BinaryExpr binary:
                return FoldBinary(binary, model, depth);

            case IdentifierExpr identifier:
                return FoldName(model.GetSymbol(identifier), model, depth);

            case MemberExpr member:
                return FoldName(model.GetSymbol(member), model, depth);

            default:
                return null;
        }
    }

    /// <summary>
    /// <para>What a name stands for, where it stands for something known while compiling.</para>
    /// <para>An enumeration member carries its value. A <c>constant</c> does not: it carries the
    /// expression it was written with, and that expression is folded here — which is what lets one
    /// constant be written in terms of another, and what <see cref="Fold"/>'s depth limit is
    /// guarding when it is.</para>
    /// <para>Anything else folds to nothing. A plain local holds whatever it last held, and no
    /// amount of reading the declaration says what that is.</para>
    /// </summary>
    private static object? FoldName(Symbol? symbol, SemanticModel model, int depth) => symbol switch
    {
        EnumMemberSymbol member => member.Value,

        FieldSymbol { IsConstant: true, Declaration: FieldDecl { Initializer: { } written } } =>
            Fold(written, model, depth + 1),

        LocalSymbol { IsConstant: true, Declaration: VarDeclStmt { Initializer: { } written } } =>
            Fold(written, model, depth + 1),

        _ => null,
    };

    private static object? FoldUnary(UnaryExpr unary, SemanticModel model, int depth)
    {
        object? operand = Fold(unary.Operand, model, depth + 1);

        return (unary.Operator, operand) switch
        {
            (UnaryOperator.Negate, long value) => Negate(value),
            (UnaryOperator.Negate, decimal value) => -value,
            (UnaryOperator.Negate, double value) => -value,
            (UnaryOperator.Negate, Fraction value) => -value,
            (UnaryOperator.Not, bool value) => !value,
            _ => null,
        };
    }

    private static object? Negate(long value)
    {
        try
        {
            return checked(-value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static object? FoldBinary(BinaryExpr binary, SemanticModel model, int depth)
    {
        object? left = Fold(binary.Left, model, depth + 1);
        object? right = Fold(binary.Right, model, depth + 1);

        if (left is null || right is null)
        {
            return null;
        }

        try
        {
            return (left, right) switch
            {
                (long a, long b) => FoldIntegers(binary.Operator, a, b),
                (decimal a, decimal b) => FoldReals(binary.Operator, a, b),
                (double a, double b) => FoldFloats(binary.Operator, a, b),
                (Fraction a, Fraction b) => FoldFractions(binary.Operator, a, b),
                (bool a, bool b) => FoldBooleans(binary.Operator, a, b),
                (string a, string b) => FoldStrings(binary.Operator, a, b),
                _ => null,
            };
        }
        catch (OverflowException)
        {
            // An expression that overflows has no constant value to report.
            return null;
        }
        catch (DivideByZeroException)
        {
            return null;
        }
    }

    /// <summary>By squaring, checked, so an overflowing constant declines to fold.</summary>
    private static long FoldIntegerPower(long value, long exponent)
    {
        long result = 1;
        long factor = value;

        for (long remaining = exponent; remaining > 0; remaining /= 2)
        {
            if (remaining % 2 == 1)
            {
                result = checked(result * factor);
            }

            if (remaining > 1)
            {
                factor = checked(factor * factor);
            }
        }

        return result;
    }

    private static object? FoldIntegers(BinaryOperator op, long a, long b) => op switch
    {
        BinaryOperator.Add => checked(a + b),
        BinaryOperator.Subtract => checked(a - b),
        BinaryOperator.Multiply => checked(a * b),

        // Integer division truncates, so "1 / 3" really is zero.
        BinaryOperator.Divide => b == 0 ? null : checked(a / b),
        BinaryOperator.Remainder => b == 0 ? null : checked(a % b),

        // A negative exponent has no whole answer; the type checker reports it, so folding
        // declines rather than producing one.
        BinaryOperator.Power => b < 0 ? null : FoldIntegerPower(a, b),

        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    /// <summary>
    /// <para>Folding on a real, which counts in tens — so what is worked out here is what the
    /// program would have worked out, digit for digit.</para>
    /// <para>Division by zero is not folded away: <c>IsZero</c> catches a written divisor first
    /// and reports it, and anything reaching here with a zero would raise rather than answer.
    /// </para>
    /// </summary>
    private static object? FoldReals(BinaryOperator op, decimal a, decimal b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide when b != 0 => a / b,
        BinaryOperator.Remainder when b != 0 => a % b,
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    /// <summary>
    /// <para>Folding on a float, which is binary floating point and folds to exactly what it
    /// would produce when run — infinities included.</para>
    /// <para>Nothing is guarded, for the same reason nothing is guarded when it runs.</para>
    /// </summary>
    private static object? FoldFloats(BinaryOperator op, double a, double b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide => a / b,
        BinaryOperator.Remainder => a % b,

        // '==' and not Equals, which answers true for two values that are not numbers.
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private static object? FoldFractions(BinaryOperator op, Fraction a, Fraction b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide => b.Numerator == 0 ? null : a / b,
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private static object? FoldBooleans(BinaryOperator op, bool a, bool b) => op switch
    {
        BinaryOperator.And => a && b,
        BinaryOperator.Or => a || b,
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        _ => null,
    };

    private static object? FoldStrings(BinaryOperator op, string a, string b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Equal => string.Equals(a, b, StringComparison.Ordinal),
        BinaryOperator.NotEqual => !string.Equals(a, b, StringComparison.Ordinal),
        _ => null,
    };

    /// <summary>
    /// True when an expression is an obvious zero, which is what makes a division by it worth
    /// rejecting before the program ever runs.
    /// </summary>
    public static bool IsZero(object? value) => value switch
    {
        long number => number == 0,
        decimal real => real == 0,
        double binary => binary == 0,
        Fraction fraction => fraction.Numerator == 0,
        _ => false,
    };
}
