using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Parsing;

/// <summary>
/// <para>Builds a syntax tree from a token stream.</para>
/// <para>Recursive descent for declarations and statements, precedence climbing for
/// expressions. Chosen over a generated table parser for the error messages, which is the
/// practical difference between the two and the reason this is written by hand.</para>
/// <para>Like the scanner, the parser never throws on malformed input. It reports, resynchronizes
/// at a defined point, and carries on, so an editor always gets a tree back.</para>
/// </summary>
public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly SourceText _source;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    private Parser(SourceText source, IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        _source = source;
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    /// <summary>Scans and parses a source file.</summary>
    public static CompilationUnit Parse(SourceText source, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        List<Token> tokens = new Lexer(source, diagnostics).Scan();
        return new Parser(source, tokens, diagnostics).ParseCompilationUnit();
    }

    /// <summary>Parses an already-scanned token stream.</summary>
    public static CompilationUnit Parse(
        SourceText source,
        IReadOnlyList<Token> tokens,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new Parser(source, tokens, diagnostics).ParseCompilationUnit();
    }

    // ---- Cursor -------------------------------------------------------------------------

    private Token Current => _tokens[_position];

    private TokenType Kind => Current.Type;

    private bool AtEnd => Current.Type == TokenType.EndOfFile;

    private Token Peek(int offset = 1)
    {
        int index = _position + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private bool Check(TokenType type) => Current.Type == type;

    private bool CheckNext(TokenType type) => Peek().Type == type;

    private Token Advance()
    {
        Token token = Current;

        if (!AtEnd)
        {
            _position++;
        }

        return token;
    }

    /// <summary>Consumes the current token if it has the given type.</summary>
    private bool Match(TokenType type)
    {
        if (!Check(type))
        {
            return false;
        }

        Advance();
        return true;
    }

    /// <summary>
    /// Consumes a token of the expected type, reporting and consuming nothing if it is
    /// absent. Returning the token either way keeps callers free of null checks.
    /// </summary>
    private Token Expect(TokenType type, string? description = null)
    {
        if (Check(type))
        {
            return Advance();
        }

        _diagnostics.Report(
            DiagnosticDescriptors.UnexpectedToken,
            Current.Span,
            description ?? Describe(type),
            Describe(Current));

        return new Token(type, string.Empty, EmptySpanHere());
    }

    /// <summary>Consumes an identifier, reporting if one is not there.</summary>
    private string ExpectIdentifier()
    {
        if (Check(TokenType.Identifier))
        {
            return Advance().Lexeme;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.ExpectedIdentifier,
            Current.Span,
            Describe(Current));

        return string.Empty;
    }

    // ---- Spans --------------------------------------------------------------------------

    /// <summary>A zero-width span at the current token, for a node that stands in for nothing.</summary>
    private SourceSpan EmptySpanHere() => new(Current.Span.Start, 0);

    /// <summary>The span running from a start token through the token last consumed.</summary>
    private SourceSpan SpanFrom(Token start)
    {
        Token last = _position > 0 ? _tokens[_position - 1] : start;
        int end = Math.Max(last.Span.EndOffset, start.Span.EndOffset);
        return new SourceSpan(start.Span.Start, end - start.Span.Start.Offset);
    }

    // ---- Describing tokens for diagnostics ----------------------------------------------

    private static string Describe(Token token) => token.Type switch
    {
        TokenType.EndOfFile => "the end of the file",
        TokenType.Identifier => $"the name '{token.Lexeme}'",
        _ when token.Type.IsLiteral() => $"the literal {token.Lexeme}",
        _ => $"'{token.Lexeme}'",
    };

    private static string Describe(TokenType type) =>
        type.Text() is { } text ? $"'{text}'" : type switch
        {
            TokenType.Identifier => "a name",
            TokenType.EndOfFile => "the end of the file",
            _ => type.ToString(),
        };

    // ---- Recovery -----------------------------------------------------------------------

    /// <summary>
    /// <para>Discards the remainder of a statement that has already been reported, up to and
    /// including its terminating <c>;</c>.</para>
    /// <para>It deliberately does not stop at the first token that could begin a statement.
    /// Nearly every expression starts with one — an identifier, a literal — so stopping there
    /// would resume in the middle of the broken statement and report the same mistake several
    /// more times. One error should cost one diagnostic.</para>
    /// <para><c>end</c> stops the skip and is never consumed: it closes an enclosing
    /// construct, and swallowing one would leave everything above it unterminated.</para>
    /// </summary>
    private void SkipRestOfStatement()
    {
        int start = _position;

        while (!AtEnd && !Check(TokenType.End))
        {
            if (Match(TokenType.Semicolon))
            {
                return;
            }

            Advance();
        }

        EnsureProgress(start);
    }

    /// <summary>Discards tokens until something that plausibly starts the next member.</summary>
    private void RecoverToNextMember()
    {
        int start = _position;

        while (!AtEnd && !Check(TokenType.End) && !StartsMember(Kind))
        {
            Advance();
        }

        EnsureProgress(start);
    }

    /// <summary>
    /// The guard that keeps a malformed file from looping forever. If a recovery step
    /// consumed nothing, force one token through so the parser cannot stall.
    /// </summary>
    private void EnsureProgress(int positionBefore)
    {
        if (_position == positionBefore && !AtEnd)
        {
            Advance();
        }
    }

    /// <summary>True once so many errors have been reported that continuing is pointless.</summary>
    private bool ShouldStop => _diagnostics.IsFull;

    private static bool StartsStatement(TokenType type) => type switch
    {
        TokenType.Begin or TokenType.Let or TokenType.If or TokenType.While
            or TokenType.For or TokenType.Switch or TokenType.Try or TokenType.Throw
            or TokenType.Yield or TokenType.Break or TokenType.Continue
            or TokenType.Constant => true,
        _ => StartsType(type),
    };

    private static bool StartsMember(TokenType type) => type switch
    {
        TokenType.Public or TokenType.Protected or TokenType.Global or TokenType.Virtual
            or TokenType.Override or TokenType.Sealed or TokenType.Abstract
            or TokenType.Function or TokenType.Model or TokenType.Structure
            or TokenType.Enumeration or TokenType.Namespace or TokenType.Using => true,
        _ => false,
    };

    // ---- Block closers ------------------------------------------------------------------

    /// <summary>
    /// <para>Consumes the qualified <c>end</c> that closes a construct.</para>
    /// <para>On a mismatch the closer is still treated as closing this construct, on the
    /// assumption that the qualifier is the typo rather than the structure. Rejecting it
    /// would leave every enclosing construct unterminated and produce a cascade of errors
    /// from one mistake.</para>
    /// </summary>
    private void ExpectEnd(TokenType qualifier, string constructName, Token opener)
    {
        if (!Check(TokenType.End))
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnterminatedConstruct,
                Current.Span,
                constructName,
                opener.Line);
            return;
        }

        Advance();

        if (Match(qualifier))
        {
            return;
        }

        string found = Current.Type.Text() ?? Current.Lexeme;

        _diagnostics.Report(
            DiagnosticDescriptors.MismatchedEnd,
            Current.Span,
            constructName,
            opener.Line,
            found);

        // Consume the wrong qualifier so the construct is still considered closed.
        if (Current.Type.IsKeyword())
        {
            Advance();
        }
    }

    /// <summary>Consumes the bare <c>end</c> that closes an anonymous block.</summary>
    private void ExpectBareEnd(Token opener)
    {
        if (!Check(TokenType.End))
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnterminatedConstruct,
                Current.Span,
                "block",
                opener.Line);
            return;
        }

        Advance();
    }
}
