using System.Text.RegularExpressions;

using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests;

/// <summary>
/// <para>Keeps the precedence table in <c>docs/grammar.ebnf</c> agreeing with the one the
/// expression parser climbs.</para>
/// <para>The grammar file describes the syntax, and the sample goldens are what pin that. The
/// table is the exception: it restates <c>Operators.cs</c> rather than describing anything, so
/// it is the one part of the file that can be flatly wrong rather than merely out of date, and
/// nothing about editing either copy says the other exists. A reader consults it precisely
/// because working the answer out from the source is what they were trying to avoid.</para>
/// </summary>
[TestFixture]
public sealed class GrammarPrecedenceTests : LexerTestBase
{
    private static string Path =>
        System.IO.Path.Combine(RepositoryRoot, "docs", "grammar.ebnf");

    /// <summary>
    /// A row of the table: the level, the operators on it, where they sit, and how they group.
    /// The operator column runs up to the position word, which is the only reliable boundary —
    /// the columns are aligned by eye and one row closes the gap to a single space.
    /// </summary>
    private static readonly Regex Row = new(
        @"^\s*(\d+)\s+(.*?)\s+(infix|prefix|postfix)\s+(left|right)\s*$",
        RegexOptions.IgnoreCase);

    /// <summary>Words in the operator column that label an operator rather than spelling one.</summary>
    private static readonly HashSet<string> Labels =
        new(StringComparer.Ordinal) { "call", "index", "member" };

    private sealed record Level(int Number, string[] Operators, string Position, string Grouping);

    private static List<Level> Table()
    {
        List<Level> levels = [];

        foreach (string line in File.ReadAllLines(Path))
        {
            if (Row.Match(line) is not { Success: true } row)
            {
                continue;
            }

            // 'bitwise' is the one operator written as two words, so the word after it belongs
            // to it rather than being an operator of its own.
            List<string> operators = [];
            string[] words = row.Groups[2].Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < words.Length; i++)
            {
                if (Labels.Contains(words[i]))
                {
                    continue;
                }

                if (words[i] == "bitwise" && i + 1 < words.Length)
                {
                    operators.Add($"bitwise {words[++i]}");
                    continue;
                }

                operators.Add(words[i]);
            }

            levels.Add(new Level(
                int.Parse(row.Groups[1].Value),
                [.. operators],
                row.Groups[3].Value.ToLowerInvariant(),
                row.Groups[4].Value.ToLowerInvariant()));
        }

        return levels;
    }

    /// <summary>The token an operator is written with, or null if the spelling is not one.</summary>
    private static TokenType? TokenFor(string spelling) => spelling switch
    {
        "or" => TokenType.Or,
        "and" => TokenType.And,
        "bitwise or" or "bitwise and" => TokenType.Bitwise,
        "xor" => TokenType.Xor,
        "not" => TokenType.Not,
        "==" => TokenType.EqualEqual,
        "!=" => TokenType.NotEqual,
        "<" => TokenType.LessThan,
        ">" => TokenType.GreaterThan,
        "<=" => TokenType.LessThanOrEqual,
        ">=" => TokenType.GreaterThanOrEqual,
        "is" => TokenType.Is,
        "as" => TokenType.As,
        "shiftleft" => TokenType.ShiftLeft,
        "shiftright" => TokenType.ShiftRight,
        "+" => TokenType.Plus,
        "-" => TokenType.Minus,
        "*" => TokenType.Star,
        "/" => TokenType.Slash,
        "%" => TokenType.Percent,
        "^" => TokenType.Caret,
        "(" => TokenType.LeftParen,
        "[" => TokenType.LeftBracket,
        "." => TokenType.Dot,
        _ => null,
    };

    /// <summary>
    /// What the parser will actually bind an operator with, taken from the same methods the
    /// parser calls rather than from a second copy of the numbers.
    /// </summary>
    private static (int Left, int Right)? PowerOf(string spelling, string position)
    {
        if (TokenFor(spelling) is not { } token)
        {
            return null;
        }

        return (spelling, position) switch
        {
            ("bitwise and", _) => Operators.BitwisePower(TokenType.And),
            ("bitwise or", _) => Operators.BitwisePower(TokenType.Or),
            (_, "prefix") => Operators.PrefixBindingPower(token) is { } prefix
                ? (prefix, prefix)
                : null,
            (_, "postfix") => Operators.PostfixBindingPower(token) is { } postfix
                ? (postfix, postfix)
                : null,
            _ => Operators.InfixBindingPower(token),
        };
    }

    /// <summary>Every spelling in the table is one the parser knows.</summary>
    [Test]
    public void EveryOperatorInTheTableIsOneTheParserBinds()
    {
        List<string> unknown =
            [.. Table()
                .SelectMany(level => level.Operators.Select(op => (level, op)))
                .Where(pair => PowerOf(pair.op, pair.level.Position) is null)
                .Select(pair => $"level {pair.level.Number}: '{pair.op}' binds nothing "
                                + $"as a {pair.level.Position} operator")];

        Assert.That(unknown, Is.Empty);
    }

    /// <summary>
    /// <para>The levels run 1 upward with no gaps, and every operator sharing a level really
    /// does bind with the same power.</para>
    /// <para>The numbers in <c>Operators.cs</c> are spaced so a band can be inserted without
    /// renumbering, so they are not the level numbers and cannot be compared to them directly.
    /// What has to hold is the order, and that operators written together bind together.</para>
    /// </summary>
    [Test]
    public void TheLevelsRunInTheOrderTheParserBindsIn()
    {
        List<Level> table = Table();

        Assert.That(table, Is.Not.Empty, "no precedence table was found in the grammar");
        Assert.That(
            table.Select(level => level.Number),
            Is.EqualTo(Enumerable.Range(1, table.Count)),
            "the levels are numbered with a gap, a repeat, or out of order");

        List<string> wrong = [];
        int previous = int.MinValue;

        foreach (Level level in table)
        {
            int[] powers =
                [.. level.Operators
                    .Select(op => PowerOf(op, level.Position))
                    .Where(power => power is not null)
                    .Select(power => power!.Value.Left)
                    .Distinct()];

            if (powers.Length != 1)
            {
                wrong.Add($"level {level.Number} writes {level.Operators.Length} operators "
                          + $"together that bind with {powers.Length} different powers");
                continue;
            }

            if (powers[0] <= previous)
            {
                wrong.Add($"level {level.Number} binds no tighter than the level above it");
            }

            previous = powers[0];
        }

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// Every operator the parser binds is written down. Adding one to <c>Operators.cs</c> and
    /// leaving the table alone is the failure this catches, and it is the likelier direction:
    /// the code is what has to change for the operator to work at all.
    /// </summary>
    [Test]
    public void EveryOperatorTheParserBindsIsInTheTable()
    {
        Dictionary<string, HashSet<TokenType>> written = new(StringComparer.Ordinal)
        {
            ["infix"] = [],
            ["prefix"] = [],
            ["postfix"] = [],
        };

        foreach (Level level in Table())
        {
            foreach (string spelling in level.Operators)
            {
                if (TokenFor(spelling) is { } token)
                {
                    written[level.Position].Add(token);
                }
            }
        }

        List<string> missing = [];

        foreach (TokenType token in Enum.GetValues<TokenType>())
        {
            if (Operators.InfixBindingPower(token) is not null
                && !written["infix"].Contains(token))
            {
                missing.Add($"{token} binds as an infix operator and is not in the table");
            }

            if (Operators.PrefixBindingPower(token) is not null
                && !written["prefix"].Contains(token))
            {
                missing.Add($"{token} binds as a prefix operator and is not in the table");
            }

            if (Operators.PostfixBindingPower(token) is not null
                && !written["postfix"].Contains(token))
            {
                missing.Add($"{token} binds as a postfix operator and is not in the table");
            }
        }

        Assert.That(missing.Order(StringComparer.Ordinal), Is.Empty);
    }

    /// <summary>
    /// <para>The associativity column agrees with the powers.</para>
    /// <para>A right power below the left is what makes an operator group rightward, so the
    /// column is not a second fact to keep in step — it is this one, spelled out.</para>
    /// </summary>
    [Test]
    public void TheGroupingColumnAgreesWithThePowers()
    {
        List<string> wrong = [];

        foreach (Level level in Table().Where(level => level.Position == "infix"))
        {
            foreach (string spelling in level.Operators)
            {
                if (PowerOf(spelling, level.Position) is not { } power)
                {
                    continue;
                }

                string grouping = power.Right < power.Left ? "right" : "left";

                if (grouping != level.Grouping)
                {
                    wrong.Add($"level {level.Number}: '{spelling}' groups {grouping} and the "
                              + $"table says {level.Grouping}");
                }
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// <para>A level number is written in the table and nowhere else.</para>
    /// <para>Three of them had gone stale in the prose — a call "at level 10", a leading minus
    /// "at level 8" — because inserting the bitwise operators renumbered everything below them
    /// and only the table was renumbered with it. Naming the operator instead says the same
    /// thing and cannot go out of date, so the rule is that the prose names operators and the
    /// table carries the numbers.</para>
    /// </summary>
    [Test]
    public void NoLevelNumberIsWrittenOutsideTheTable()
    {
        List<string> written = [];
        string[] lines = File.ReadAllLines(Path);

        for (int i = 0; i < lines.Length; i++)
        {
            if (Row.IsMatch(lines[i]))
            {
                continue;
            }

            foreach (Match cited in Regex.Matches(lines[i], @"level\s+\d+", RegexOptions.IgnoreCase))
            {
                written.Add($"line {i + 1}: '{cited.Value}'");
            }
        }

        Assert.That(written, Is.Empty,
                    "a level is written in the precedence table and named by its operators "
                    + "everywhere else");
    }
}
