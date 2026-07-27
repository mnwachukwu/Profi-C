namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>Scans a source program and produces an ordered list of tokens.</para>
/// <para>The whole program is consumed in one pass, skipping whitespace and comments.</para>
/// <para>Throws a syntax error if an unrecognized character sequence is encountered.</para>
/// </summary>
public class Lexer
{
    // Reserved keywords mapped to their token types.
    // Note: "comment" is reserved but handled separately, so it is not listed here.
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
            { "if",        TokenType.If },
            { "else",      TokenType.Else },
            { "for",       TokenType.For },
            { "while",     TokenType.While },
            { "yield",     TokenType.Yield },
            { "let",       TokenType.Let },
            { "write",     TokenType.Write },
            { "read",      TokenType.Read },
            { "function",  TokenType.Function },
            { "model",     TokenType.Model },
            { "break",     TokenType.Break },
            { "continue",  TokenType.Continue },
            { "begin",     TokenType.Begin },
            { "end",       TokenType.End },
            { "true",      TokenType.True },
            { "false",     TokenType.False },
            { "or",        TokenType.Or },
            { "and",       TokenType.And },
            { "not",       TokenType.Not },
            { "integer",   TokenType.Integer },
            { "real",      TokenType.Real },
            { "character", TokenType.Character },
            { "bool",      TokenType.Bool },
            { "string",    TokenType.String }
        };

    // The reserved word for comments
    private const string CommentKeyword = "comment";

    private readonly string _source;
    private int _index;

    public Lexer(string source)
    {
        _source = source;
        _index = 0;
    }

    /// <summary>Peeks at the current character without advancing.</summary>
    private char Current()
    {
        return _source[_index];
    }

    /// <summary>Peeks at the next character without advancing.</summary>
    private char Peek()
    {
        if (_index + 1 < _source.Length)
        {
            return _source[_index + 1];
        }

        return '\0';
    }

    /// <summary>Returns true if the scanner has reached the end of the source.</summary>
    private bool IsAtEnd()
    {
        return _index >= _source.Length;
    }

    /// <summary>Advances past any run of whitespace between tokens.</summary>
    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Current()))
        {
            _index++;
        }
    }

    /// <summary>Advances past spaces and tabs only; leaves newlines in place.</summary>
    private void SkipInlineWhitespace()
    {
        while (!IsAtEnd() && (Current() == ' ' || Current() == '\t'))
        {
            _index++;
        }
    }

    /// <summary>
    /// <para>Returns true if the given word sits at the given position as a whole word.</para>
    /// <para>Checks both the left and right edges so that we get whole word matching.</para>
    /// </summary>
    private bool MatchesKeywordAt(int position, string word)
    {
        if (position + word.Length > _source.Length)
        {
            return false;
        }

        if (_source.Substring(position, word.Length) != word)
        {
            return false;
        }

        if (position > 0 && char.IsLetterOrDigit(_source[position - 1]))
        {
            return false;
        }

        int after = position + word.Length;

        if (after < _source.Length && char.IsLetterOrDigit(_source[after]))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// <para>Scans the full source string and returns an ordered list of tokens.</para>
    /// <para>Whitespace and comments are skipped; neither becomes a token.</para>
    /// <para>Throws a FormatException on unrecognized character sequences.</para>
    /// </summary>
    public List<Token> Scan()
    {
        List<Token> tokens = new List<Token>();

        while (!IsAtEnd())
        {
            SkipWhitespace();

            if (IsAtEnd())
            {
                break;
            }

            // Comments are skipped the same way whitespace is.
            if (TrySkipComment())
            {
                continue;
            }

            Token token = ScanNext();
            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// <para>Skips a comment if one begins at the current position.</para>
    /// <para>"comment begin" opens a block comment closed by "end comment".</para>
    /// <para>"comment" followed by anything else is a line comment to end of line.</para>
    /// <para>Returns true if a comment was skipped; leaves the position untouched otherwise.</para>
    /// </summary>
    private bool TrySkipComment()
    {
        if (!MatchesKeywordAt(_index, CommentKeyword))
        {
            return false;
        }

        // Consume the "comment" keyword.
        _index += CommentKeyword.Length;

        // Look on the same line for the block opener "begin".
        SkipInlineWhitespace();

        if (MatchesKeywordAt(_index, "begin"))
        {
            _index += "begin".Length;
            SkipBlockComment();
            return true;
        }

        SkipLineComment();
        return true;
    }

    /// <summary>Consumes the rest of the current line, stopping at the newline.</summary>
    private void SkipLineComment()
    {
        while (!IsAtEnd() && Current() != '\n')
        {
            _index++;
        }
    }

    /// <summary>
    /// <para>Consumes a block comment body up to and including "end comment".</para>
    /// <para>Throws if the closer is never found.</para>
    /// </summary>
    private void SkipBlockComment()
    {
        while (!IsAtEnd())
        {
            if (MatchesKeywordAt(_index, "end"))
            {
                int probe = _index + "end".Length;

                while (probe < _source.Length && char.IsWhiteSpace(_source[probe]))
                {
                    probe++;
                }

                if (MatchesKeywordAt(probe, CommentKeyword))
                {
                    // Consume through the closing "comment".
                    _index = probe + CommentKeyword.Length;
                    return;
                }
            }

            _index++;
        }

        throw new FormatException("Syntax error: unclosed block comment; expected 'end comment'.");
    }

    /// <summary>Scans and returns the next token from the source.</summary>
    private Token ScanNext()
    {
        char c = Current();

        // Scan a char literal: consume opening quote, one character, closing quote.
        if (c == '\'')
        {
            return ScanCharLiteral();
        }

        // Scan a string literal: consume opening double quote, characters, closing double quote.
        if (c == '"')
        {
            return ScanStringLiteral();
        }

        // Scan alphanumeric sequences: keywords, types, bool literals, or identifiers.
        if (char.IsLetter(c))
        {
            return ScanWord();
        }

        // Scan numeric sequences: integer or real literals.
        if (char.IsDigit(c))
        {
            return ScanNumber();
        }

        // Scan operators and punctuation, longest match first.
        return ScanSymbol();
    }

    /// <summary>
    /// <para>Scans a char literal of the form 'x'.</para>
    /// <para>Throws if the literal is malformed or unclosed.</para>
    /// </summary>
    private Token ScanCharLiteral()
    {
        // Consume the opening quote.
        _index++;

        if (IsAtEnd())
        {
            throw new FormatException("Syntax error: unclosed char literal at end of input.");
        }

        char value = Current();
        _index++;

        if (IsAtEnd() || Current() != '\'')
        {
            throw new FormatException($"Syntax error: expected closing quote after '{value}'.");
        }

        // Consume the closing quote.
        _index++;

        return new Token($"'{value}'", TokenType.CharLiteral);
    }

    /// <summary>
    /// <para>Scans a string literal of the form "text".</para>
    /// <para>Internal spaces are allowed since the lexer runs in a single pass.</para>
    /// <para>Throws if the literal is unclosed.</para>
    /// </summary>
    private Token ScanStringLiteral()
    {
        // Consume the opening double quote.
        _index++;

        int start = _index;

        while (!IsAtEnd() && Current() != '"')
        {
            _index++;
        }

        if (IsAtEnd())
        {
            throw new FormatException("Syntax error: unclosed string literal at end of input.");
        }

        string value = _source.Substring(start, _index - start);

        // Consume the closing double quote.
        _index++;

        return new Token($"\"{value}\"", TokenType.StringLiteral);
    }

    /// <summary>
    /// <para>Scans a contiguous alphanumeric sequence beginning with a letter.</para>
    /// <para>Names like "var10" therefore form a single identifier.</para>
    /// <para>Checks against the keyword table; falls through to IDENTIFIER.</para>
    /// </summary>
    private Token ScanWord()
    {
        int start = _index;

        while (!IsAtEnd() && char.IsLetterOrDigit(Current()))
        {
            _index++;
        }

        string word = _source.Substring(start, _index - start);

        if (Keywords.TryGetValue(word, out TokenType keywordType))
        {
            return new Token(word, keywordType);
        }

        return new Token(word, TokenType.Identifier);
    }

    /// <summary>
    /// <para>Scans a contiguous digit sequence.</para>
    /// <para>If followed by a dot and more digits, produces a REAL_LITERAL.</para>
    /// <para>If followed by a pipe and more digits, produces a FRACTION_LITERAL.</para>
    /// <para>Otherwise produces an INTEGER_LITERAL.</para>
    /// </summary>
    private Token ScanNumber()
    {
        int start = _index;

        while (!IsAtEnd() && char.IsDigit(Current()))
        {
            _index++;
        }

        // Check for a real literal: digits . digits.
        if (!IsAtEnd() && Current() == '.' && char.IsDigit(Peek()))
        {
            // Consume the dot.
            _index++;

            while (!IsAtEnd() && char.IsDigit(Current()))
            {
                _index++;
            }

            string real = _source.Substring(start, _index - start);
            return new Token(real, TokenType.RealLiteral);
        }

        // Check for a fraction literal: digits | digits.
        if (!IsAtEnd() && Current() == '|' && char.IsDigit(Peek()))
        {
            // Consume the pipe.
            _index++;

            while (!IsAtEnd() && char.IsDigit(Current()))
            {
                _index++;
            }

            string fraction = _source.Substring(start, _index - start);
            return new Token(fraction, TokenType.FractionLiteral);
        }

        string integer = _source.Substring(start, _index - start);
        return new Token(integer, TokenType.IntegerLiteral);
    }

    /// <summary>
    /// <para>Scans a single or multi-character symbol.</para>
    /// <para>Applies longest-match-first for operators like == != &lt;= &gt;=</para>
    /// <para>Throws a FormatException on unrecognized characters.</para>
    /// </summary>
    private Token ScanSymbol()
    {
        char c = Current();
        char next = Peek();

        // Two-character operators; checked before single-character fallbacks.
        if (c == '=' && next == '=') { _index += 2; return new Token("==", TokenType.EqualEqual); }
        if (c == '!' && next == '=') { _index += 2; return new Token("!=", TokenType.NotEqual); }
        if (c == '<' && next == '=') { _index += 2; return new Token("<=", TokenType.LessThanOrEqual); }
        if (c == '>' && next == '=') { _index += 2; return new Token(">=", TokenType.GreaterThanOrEqual); }

        // Single-character symbols.
        _index++;

        switch (c)
        {
            case '+': return new Token("+", TokenType.Plus);
            case '-': return new Token("-", TokenType.Minus);
            case '*': return new Token("*", TokenType.Star);
            case '/': return new Token("/", TokenType.Slash);
            case '%': return new Token("%", TokenType.Percent);
            case '|': return new Token("|", TokenType.Pipe);
            case '<': return new Token("<", TokenType.LessThan);
            case '>': return new Token(">", TokenType.GreaterThan);
            case '=': return new Token("=", TokenType.Equal);
            case '(': return new Token("(", TokenType.LeftParen);
            case ')': return new Token(")", TokenType.RightParen);
            case '{': return new Token("{", TokenType.LeftBrace);
            case '}': return new Token("}", TokenType.RightBrace);
            case '[': return new Token("[", TokenType.LeftBracket);
            case ']': return new Token("]", TokenType.RightBracket);
            case ',': return new Token(",", TokenType.Comma);
            case ';': return new Token(";", TokenType.Semicolon);
            case '.': return new Token(".", TokenType.Dot);

            default:
                throw new FormatException(
                    $"Syntax error: unrecognized character '{c}' at position {_index - 1}."
                );
        }
    }
}
