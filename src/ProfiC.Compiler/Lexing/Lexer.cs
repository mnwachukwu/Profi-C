using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>Scans a source program in one pass and produces an ordered list of tokens,
/// terminated by an end-of-file token.</para>
/// <para>The scanner never throws on malformed input. It reports a diagnostic, resynchronizes
/// at a well-defined point, and carries on. This matters beyond tidiness: a file being edited
/// is malformed most of the time, and a scanner that gives up at the first error yields no
/// tokens at all, which in an editor means no highlighting and no completion.</para>
/// </summary>
public sealed class Lexer
{
    /// <summary>
    /// <para>Character sequences a C# author is likely to reach for that have no reading at
    /// all in Profi-C, together with what to write instead.</para>
    /// <para>Every entry here is unambiguous, which is what makes reporting it a scanner's
    /// business rather than a parser's. "++" qualifies because Profi-C has no unary plus, so
    /// "a++b" cannot be read as an addition of a positive "b"; the compound assignments
    /// qualify because "=" can never follow an arithmetic operator.</para>
    /// <para><b>"--" is deliberately absent.</b> Unary minus does exist, so "x--1" is a
    /// perfectly good subtraction of negative one, and telling that apart from a decrement
    /// requires knowing whether an operand follows. That is grammatical context the scanner
    /// does not have, so the decrement diagnostic belongs to the parser.</para>
    /// </summary>
    private static readonly (string Operator, string Advice)[] NonOperators =
    [
        ("&&", "Use 'and'."),
        ("||", "Use 'or'."),
        ("+=", "Profi-C has no compound assignment. Write 'x = x + y'."),
        ("-=", "Profi-C has no compound assignment. Write 'x = x - y'."),
        ("*=", "Profi-C has no compound assignment. Write 'x = x * y'."),
        ("/=", "Profi-C has no compound assignment. Write 'x = x / y'."),
        ("%=", "Profi-C has no compound assignment. Write 'x = x % y'."),
        ("++", "Profi-C has no increment operator. Write 'x = x + 1'."),
    ];

    private readonly SourceText _source;
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _index;

    /// <summary>Creates a scanner over a source file, reporting into a shared bag.</summary>
    public Lexer(SourceText source, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _source = source;
        _text = source.Text;
        _diagnostics = diagnostics;
        _index = 0;
    }

    /// <summary>Convenience constructor for scanning a bare string.</summary>
    public Lexer(string source, DiagnosticBag diagnostics)
        : this(new SourceText(source), diagnostics)
    {
    }

    // ---- Character access ---------------------------------------------------------------

    private bool IsAtEnd() => _index >= _text.Length;

    private char Current() => _index < _text.Length ? _text[_index] : '\0';

    private char Peek(int offset = 1) =>
        _index + offset < _text.Length ? _text[_index + offset] : '\0';

    // ---- Spans and reporting ------------------------------------------------------------

    private SourceSpan SpanFrom(int start) => _source.SpanAt(start, _index - start);

    private SourceSpan SpanOf(int start, int length) => _source.SpanAt(start, length);

    private Token MakeToken(TokenType type, int start) =>
        new(type, _text[start.._index], SpanFrom(start));

    // ---- Scanning -----------------------------------------------------------------------

    /// <summary>
    /// <para>Scans the whole source and returns its tokens, always ending with an
    /// end-of-file token.</para>
    /// <para>Whitespace and comments produce no tokens. Errors produce diagnostics rather
    /// than exceptions.</para>
    /// </summary>
    public List<Token> Scan()
    {
        List<Token> tokens = [];

        while (true)
        {
            SkipTrivia();

            if (IsAtEnd())
            {
                break;
            }

            Token? token = ScanNext();

            if (token is not null)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token(TokenType.EndOfFile, string.Empty, SpanOf(_text.Length, 0)));
        return tokens;
    }

    /// <summary>Advances past any run of whitespace and comments.</summary>
    private void SkipTrivia()
    {
        while (!IsAtEnd())
        {
            if (char.IsWhiteSpace(Current()))
            {
                _index++;
                continue;
            }

            if (!TrySkipComment())
            {
                return;
            }
        }
    }

    /// <summary>Advances past spaces and tabs only, leaving line breaks in place.</summary>
    private void SkipInlineWhitespace()
    {
        while (!IsAtEnd() && (Current() == ' ' || Current() == '\t'))
        {
            _index++;
        }
    }

    /// <summary>
    /// <para>Returns true if the given word sits at the given position as a whole word.</para>
    /// <para>Both edges are checked, which is what lets the word-delimited comment syntax
    /// work without mistaking "commentary" for a comment.</para>
    /// </summary>
    private bool MatchesWordAt(int position, string word)
    {
        if (position < 0 || position + word.Length > _text.Length)
        {
            return false;
        }

        if (string.CompareOrdinal(_text, position, word, 0, word.Length) != 0)
        {
            return false;
        }

        if (position > 0 && IsIdentifierPart(_text[position - 1]))
        {
            return false;
        }

        int after = position + word.Length;
        return after >= _text.Length || !IsIdentifierPart(_text[after]);
    }

    /// <summary>
    /// <para>Skips a comment if one begins here, returning whether it did.</para>
    /// <para>"comment begin" opens a block closed by "end comment"; "comment" followed by
    /// anything else runs to the end of the line.</para>
    /// </summary>
    private bool TrySkipComment()
    {
        if (!MatchesWordAt(_index, ReservedWords.Comment))
        {
            return false;
        }

        int start = _index;
        _index += ReservedWords.Comment.Length;

        // The block opener must sit on the same line as the word that introduces it.
        SkipInlineWhitespace();

        if (MatchesWordAt(_index, "begin"))
        {
            _index += "begin".Length;
            SkipBlockComment(start);
            return true;
        }

        while (!IsAtEnd() && Current() != '\n')
        {
            _index++;
        }

        return true;
    }

    /// <summary>Consumes a block comment body up to and including its "end comment" closer.</summary>
    private void SkipBlockComment(int start)
    {
        while (!IsAtEnd())
        {
            if (MatchesWordAt(_index, "end"))
            {
                int probe = _index + "end".Length;

                while (probe < _text.Length && char.IsWhiteSpace(_text[probe]))
                {
                    probe++;
                }

                if (MatchesWordAt(probe, ReservedWords.Comment))
                {
                    _index = probe + ReservedWords.Comment.Length;
                    return;
                }
            }

            _index++;
        }

        // Point at the opener rather than at end of file; that is where the fix goes.
        _diagnostics.Report(
            DiagnosticDescriptors.UnterminatedBlockComment,
            SpanOf(start, ReservedWords.Comment.Length));
    }

    /// <summary>
    /// Scans the next token. Returns null when the input was consumed without producing
    /// one, which happens only on an unrecognized character.
    /// </summary>
    private Token? ScanNext()
    {
        char c = Current();

        if (c == '\'')
        {
            return ScanCharacterLiteral();
        }

        if (c == '"')
        {
            return ScanStringLiteral();
        }

        if (IsIdentifierStart(c))
        {
            return ScanWord();
        }

        if (char.IsDigit(c))
        {
            return ScanNumber();
        }

        return ScanSymbol();
    }

    // ---- Identifiers --------------------------------------------------------------------

    /// <summary>
    /// <para>A character that may begin an identifier: any Unicode letter, or an
    /// underscore.</para>
    /// <para>This follows C#, minus its rarer allowances. Combining marks, format
    /// characters, unicode escapes within names, and verbatim identifiers written with a
    /// leading "@" are all absent, since none of them serves a reader.</para>
    /// </summary>
    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    /// <summary>
    /// A character that may continue an identifier: a letter, a digit, or an underscore.
    /// Also used to test word boundaries when recognizing comments, which is why an
    /// identifier such as "comment_text" is correctly not read as a comment.
    /// </summary>
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Scans an identifier or a reserved word.</summary>
    private Token ScanWord()
    {
        int start = _index;

        while (!IsAtEnd() && IsIdentifierPart(Current()))
        {
            _index++;
        }

        string word = _text[start.._index];

        return ReservedWords.Keywords.TryGetValue(word, out TokenType keyword)
            ? new Token(keyword, word, SpanFrom(start))
            : new Token(TokenType.Identifier, word, SpanFrom(start));
    }

    // ---- Numbers ------------------------------------------------------------------------

    /// <summary>
    /// <para>Scans an integer, a real, or a fraction literal.</para>
    /// <para>A real is digits, a dot, then digits; a fraction is digits, a pipe, then
    /// digits. Requiring digits on both sides is what keeps "3." scanning as an integer
    /// followed by a dot, which is what member access needs.</para>
    /// </summary>
    private Token ScanNumber()
    {
        int start = _index;

        while (!IsAtEnd() && char.IsDigit(Current()))
        {
            _index++;
        }

        if (Current() == '.' && char.IsDigit(Peek()))
        {
            _index++;

            while (!IsAtEnd() && char.IsDigit(Current()))
            {
                _index++;
            }

            return MakeToken(TokenType.RealLiteral, start);
        }

        if (Current() == '|' && char.IsDigit(Peek()))
        {
            _index++;

            while (!IsAtEnd() && char.IsDigit(Current()))
            {
                _index++;
            }

            return MakeToken(TokenType.FractionLiteral, start);
        }

        return MakeToken(TokenType.IntegerLiteral, start);
    }

    // ---- Literals with escapes ----------------------------------------------------------

    /// <summary>
    /// <para>Scans a character literal such as 'x' or '\n'.</para>
    /// <para>On any error a token is still produced, so that the parser sees the shape of
    /// the program even when a literal is wrong.</para>
    /// </summary>
    private Token ScanCharacterLiteral()
    {
        int start = _index;
        _index++;

        int contentCount = 0;

        while (!IsAtEnd() && Current() != '\'' && Current() != '\n')
        {
            if (Current() == '\\')
            {
                ScanEscape();
            }
            else
            {
                _index++;
            }

            contentCount++;
        }

        if (IsAtEnd() || Current() != '\'')
        {
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedCharacter, SpanFrom(start));
            return MakeToken(TokenType.CharLiteral, start);
        }

        _index++;

        if (contentCount != 1)
        {
            _diagnostics.Report(DiagnosticDescriptors.MalformedCharacterLiteral, SpanFrom(start));
        }

        return MakeToken(TokenType.CharLiteral, start);
    }

    /// <summary>
    /// <para>Scans a string literal.</para>
    /// <para>A literal may not span a line break. Letting it do so means one missing closing
    /// quote swallows the rest of the file, turning a single real error into a cascade of
    /// invented ones.</para>
    /// </summary>
    private Token ScanStringLiteral()
    {
        int start = _index;
        _index++;

        while (!IsAtEnd() && Current() != '"' && Current() != '\n')
        {
            if (Current() == '\\')
            {
                ScanEscape();
            }
            else
            {
                _index++;
            }
        }

        if (IsAtEnd() || Current() != '"')
        {
            // Report at the opening quote, which is where the missing one belongs.
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, SpanOf(start, 1));
            return MakeToken(TokenType.StringLiteral, start);
        }

        _index++;
        return MakeToken(TokenType.StringLiteral, start);
    }

    /// <summary>
    /// <para>Consumes one escape sequence, reporting if it is not recognized.</para>
    /// <para>Validation happens here; turning the sequence into a character is the parser's
    /// job, since a token's lexeme is always the exact source text.</para>
    /// </summary>
    private void ScanEscape()
    {
        int start = _index;
        _index++;

        if (IsAtEnd() || Current() == '\n')
        {
            _diagnostics.Report(DiagnosticDescriptors.UnrecognizedEscape, SpanOf(start, 1), "");
            return;
        }

        char c = Current();
        _index++;

        switch (c)
        {
            case 'n':
            case 't':
            case 'r':
            case '0':
            case '\\':
            case '"':
            case '\'':
                return;

            case 'u':
                ScanUnicodeEscape(start);
                return;

            default:
                _diagnostics.Report(
                    DiagnosticDescriptors.UnrecognizedEscape,
                    SpanOf(start, 2),
                    c);
                return;
        }
    }

    /// <summary>Consumes the four hexadecimal digits of a Unicode escape.</summary>
    private void ScanUnicodeEscape(int start)
    {
        int digits = 0;

        while (digits < 4 && !IsAtEnd() && Uri.IsHexDigit(Current()))
        {
            _index++;
            digits++;
        }

        if (digits != 4)
        {
            _diagnostics.Report(DiagnosticDescriptors.MalformedUnicodeEscape, SpanFrom(start));
        }
    }

    // ---- Symbols ------------------------------------------------------------------------

    /// <summary>
    /// <para>Scans an operator or punctuation mark, longest match first.</para>
    /// <para>Sequences that are operators in C# but not here are reported with the Profi-C
    /// spelling rather than being silently scanned as two tokens.</para>
    /// </summary>
    private Token? ScanSymbol()
    {
        int start = _index;

        foreach ((string op, string advice) in NonOperators)
        {
            if (MatchesAt(start, op))
            {
                _index += op.Length;
                _diagnostics.Report(
                    DiagnosticDescriptors.NotAnOperator,
                    SpanOf(start, op.Length),
                    op,
                    advice);
                return null;
            }
        }

        char c = Current();
        char next = Peek();

        // Two-character operators, checked before the single-character forms.
        if (c == '=' && next == '=') { _index += 2; return MakeToken(TokenType.EqualEqual, start); }
        if (c == '=' && next == '>') { _index += 2; return MakeToken(TokenType.Arrow, start); }
        if (c == '!' && next == '=') { _index += 2; return MakeToken(TokenType.NotEqual, start); }
        if (c == '<' && next == '=') { _index += 2; return MakeToken(TokenType.LessThanOrEqual, start); }
        if (c == '>' && next == '=') { _index += 2; return MakeToken(TokenType.GreaterThanOrEqual, start); }

        _index++;

        TokenType? type = c switch
        {
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Star,
            '/' => TokenType.Slash,
            '%' => TokenType.Percent,
            '|' => TokenType.Pipe,
            '?' => TokenType.Question,
            ':' => TokenType.Colon,
            '<' => TokenType.LessThan,
            '>' => TokenType.GreaterThan,
            '=' => TokenType.Equal,
            '(' => TokenType.LeftParen,
            ')' => TokenType.RightParen,
            '{' => TokenType.LeftBrace,
            '}' => TokenType.RightBrace,
            '[' => TokenType.LeftBracket,
            ']' => TokenType.RightBracket,
            ',' => TokenType.Comma,
            ';' => TokenType.Semicolon,
            '.' => TokenType.Dot,
            _ => null,
        };

        if (type is null)
        {
            // "!" on its own only reaches here when it is not part of "!=".
            if (c == '!')
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.NotAnOperator,
                    SpanOf(start, 1),
                    "!",
                    "Use 'not'.");
            }
            else
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.UnrecognizedCharacter,
                    SpanOf(start, 1),
                    c);
            }

            return null;
        }

        return MakeToken(type.Value, start);
    }

    /// <summary>True if the given text sits at the given position.</summary>
    private bool MatchesAt(int position, string value) =>
        position + value.Length <= _text.Length
        && string.CompareOrdinal(_text, position, value, 0, value.Length) == 0;
}
