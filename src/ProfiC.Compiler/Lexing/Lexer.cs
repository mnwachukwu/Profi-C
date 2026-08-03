using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
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
    /// <summary>
    /// <para>Sequences that are operators in C# but not here, each with the token it stands in
    /// for while recovering.</para>
    /// <para>The substitution is what keeps one mistake to one message. Emitting no token
    /// would leave two operands side by side, and the parser would then report on every shape
    /// that follows. Standing in the intended operator lets the rest of the line parse, so the
    /// only thing reported is the thing that is wrong.</para>
    /// <para>Several of these have an exact stand-in, since Profi-C spells the same operation
    /// differently. The compound assignments do not: their meaning needs a statement rather
    /// than a token, so <c>=</c> stands in to keep the shape and the message carries the
    /// rewrite. A null means nothing sensible stands in and the sequence is dropped.</para>
    /// </summary>
    /// <summary>
    /// <para><c>FixedBy</c> is what an editor would write over the operator to settle it, or null
    /// where nothing so simple would.</para>
    /// <para>Only four of these have one. <c>&amp;&amp;</c> is <c>and</c> and nothing else needs
    /// to change; <c>x += 1</c> becomes <c>x = x + 1</c>, which needs to know what <c>x</c> is and
    /// is a rewrite rather than a swap. Offering a fix that is wrong half the time is worse than
    /// offering none, so those carry null and the message explains instead.</para>
    /// </summary>
    private static readonly (string Operator, TokenType? StandsIn, string? FixedBy, string Advice)[]
        NonOperators =
    [
        ("&&", TokenType.And, "and", "Use 'and'."),
        ("||", TokenType.Or, "or", "Use 'or'."),
        ("+=", TokenType.Equal, null, "Profi-C has no compound assignment. Write 'x = x + y'."),
        ("-=", TokenType.Equal, null, "Profi-C has no compound assignment. Write 'x = x - y'."),
        ("*=", TokenType.Equal, null, "Profi-C has no compound assignment. Write 'x = x * y'."),
        ("/=", TokenType.Equal, null, "Profi-C has no compound assignment. Write 'x = x / y'."),
        ("%=", TokenType.Equal, null, "Profi-C has no compound assignment. Write 'x = x % y'."),

        // "x++" leaves "x", which is already a well-formed expression statement, so dropping
        // it cascades no further than standing something in would.
        ("++", null, null, "Profi-C has no increment operator. Write 'x = x + 1'."),

        // Raising to a power is written "^". A reader arriving from Python reaches for this
        // spelling, so it is named rather than scanned as two multiplications.
        ("**", TokenType.Caret, "^", "Profi-C raises to a power with '^'. Write 'base ^ exponent', "
               + "or 'Math.Pow(base, exponent)' for a real result."),

        // A lambda's body follows "yield", the same word every other function uses to say what
        // it produces. Standing "yield" in means the rest of the lambda parses, so a reader
        // arriving from C# or Java gets one sentence rather than a cascade.
        //
        // No fix offered: "=>" sits where a body begins, and swapping the token in leaves the
        // parameter list before it written the way C# writes one.
        ("=>", TokenType.Yield, null, "A function's body follows 'yield'. "
               + "Write '(integer n) yield n + 1'."),
        ("->", TokenType.Yield, null, "A function's body follows 'yield'. "
               + "Write '(integer n) yield n + 1'."),
    ];

    private readonly SourceText _source;
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _index;

    /// <summary>
    /// <para>The interpolated strings currently open, innermost last.</para>
    /// <para>A stack rather than a flag because a hole may hold a string, and that string may
    /// interpolate in turn. <see cref="Hole.Depth"/> counts the braces opened inside the hole
    /// and not yet closed, which is what tells the <c>}}</c> that ends the hole apart from the
    /// one ending a set literal written inside it.</para>
    /// </summary>
    private readonly Stack<Hole> _holes = new();

    /// <summary>
    /// Directives read from line comments, kept until the token list can say which line each
    /// one covers.
    /// </summary>
    private readonly List<(SourceSpan Span, SuppressionTarget Target)> _directives = [];

    /// <summary>Where a comment began and ended, and whether it was written with one mark.</summary>
    private readonly record struct Comment(int Start, int End, bool Line);

    /// <summary>
    /// Every comment scanned, kept until the tokens can say which line each documents. What
    /// they say is read once, at the end; read from <see cref="Documentation"/>.
    /// </summary>
    private readonly List<Comment> _comments = [];

    /// <summary>What the file documents, in the order written. Empty until <see cref="Scan"/>.</summary>
    public IReadOnlyList<DocComment> Documentation { get; private set; } = [];

    /// <summary>
    /// <para>Where every comment sits, in the order written. Empty until <see cref="Scan"/>.
    /// </para>
    /// <para>Comments produce no tokens, which is right for every pass that reads the program:
    /// none of them means anything to the language. It leaves one question nothing else can
    /// answer, though — <b>which lines are inside a comment</b> — and formatting has to know,
    /// since re-indenting the middle of a block comment reflows prose somebody laid out by
    /// hand.</para>
    /// </summary>
    public IReadOnlyList<SourceSpan> Comments { get; private set; } = [];

    private sealed class Hole
    {
        /// <summary>Whether scanning is inside the braces rather than in the text around them.</summary>
        public bool InExpression { get; set; }

        /// <summary>Braces opened within the expression and not yet closed.</summary>
        public int Depth { get; set; }

        /// <summary>Where the open <c>{{</c> was, so an unterminated one is reported there.</summary>
        public int Opened { get; set; }

        /// <summary>Where the string's own quote was, for the same reason.</summary>
        public int Quote { get; init; }
    }

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
        using DiagnosticBag.FileScope reporting = _diagnostics.InFile(_source);

        List<Token> tokens = [];

        while (true)
        {
            // Inside a string but outside its braces, whitespace is content and a '#' is a
            // '#'. Trivia is only trivia where code is expected.
            if (_holes.TryPeek(out Hole? open) && !open.InExpression)
            {
                tokens.Add(ContinueInterpolatedString(open));
                continue;
            }

            // A hole ends on the line it opened, as a string literal does, and for the same
            // reason: one missing '}}' should cost one diagnostic rather than turn every
            // line below it into an expression nobody wrote.
            if (open is not null && EndOfLineInsideAHole(open))
            {
                continue;
            }

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

        // A string left open at end of file has already been reported by whichever scan
        // opened it; the stack is cleared so nothing outlives the file.
        _holes.Clear();

        tokens.Add(new Token(TokenType.EndOfFile, string.Empty, SpanOf(_text.Length, 0)));

        RecordDirectives(tokens);

        Documentation = [.. ReadDocumentation(tokens)];
        Comments = [.. _comments.Select(c => SpanOf(c.Start, c.End - c.Start))];

        return tokens;
    }

    /// <summary>
    /// <para>Turns the directives read while scanning into suppressions the bag can apply.</para>
    /// <para>It happens here rather than where each was read because a line-scoped one covers
    /// the next line carrying code, and which line that is takes the token list to answer.</para>
    /// <para>An identifier is checked against the diagnostic table now, since whether it names
    /// anything, and whether what it names can be silenced, are both answerable without seeing
    /// what the rest of the compilation reports.</para>
    /// </summary>
    private void RecordDirectives(List<Token> tokens)
    {
        foreach ((SourceSpan span, SuppressionTarget target) in _directives)
        {
            if (target.Id is { } id && !Suppressible(id, span))
            {
                continue;
            }

            _diagnostics.Suppress(new Suppression(
                target.WholeFile ? SuppressionScope.File : SuppressionScope.Line,
                _source,
                target.WholeFile ? 0 : NextLineCarryingCode(tokens, span.Start.Line),
                target.Severity,
                target.Id,
                span));
        }
    }

    /// <summary>
    /// <para>Reads what each comment says, keeping the ones that document something and
    /// passing over the remarks.</para>
    /// <para><b>A documentation comment sits immediately above what it documents</b>, with
    /// nothing between but the newline that ends it. Reaching further would mean a heading
    /// about a whole file, written above a blank line and a second remark, silently became the
    /// documentation of whichever declaration happened to come next — which is the accident
    /// the <c>@summary:</c> marker exists to prevent, arriving by another route.</para>
    /// </summary>
    private IEnumerable<DocComment> ReadDocumentation(List<Token> tokens)
    {
        foreach (Comment comment in _comments)
        {
            if (DocComment.TryRead(_source, comment.Start, comment.End, out DocComment? read))
            {
                yield return read.Documenting(LineBelow(tokens, comment));
            }
        }
    }

    /// <summary>
    /// The line directly under a comment, where that line carries code. Zero where anything at
    /// all comes between — a blank line, another comment, or the end of the file.
    /// </summary>
    private int LineBelow(List<Token> tokens, Comment comment)
    {
        int below = _source.PositionAt(Math.Max(comment.Start, comment.End - 1)).Line + 1;

        return tokens.Exists(t => t.Type != TokenType.EndOfFile && t.Line == below) ? below : 0;
    }

    /// <summary>Whether an identifier names something that exists and does not stop compilation.</summary>
    private bool Suppressible(string id, SourceSpan span)
    {
        if (!DiagnosticDescriptors.ById.TryGetValue(id, out DiagnosticDescriptor? named))
        {
            _diagnostics.Report(DiagnosticDescriptors.IgnoreNamesNoDiagnostic, span, id);
            return false;
        }

        if (named.DefaultSeverity == DiagnosticSeverity.Error)
        {
            _diagnostics.Report(DiagnosticDescriptors.IgnoreCannotSilenceAnError, span, id);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The first line below the given one that carries code, or zero where none does. Blank
    /// lines and further comments are passed over, so a directive may be written above the
    /// thing it is about rather than jammed against it. The end-of-file token is not code.
    /// </summary>
    private static int NextLineCarryingCode(List<Token> tokens, int below) =>
        tokens.Find(t => t.Type != TokenType.EndOfFile && t.Line > below)?.Line ?? 0;

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

    /// <summary>
    /// <para>Skips a comment if one begins here, returning whether it did.</para>
    /// <para><c>##</c> opens a block and <c>#</c> alone runs to the end of the line.</para>
    /// </summary>
    private bool TrySkipComment()
    {
        if (Current() != '#')
        {
            return false;
        }

        int opened = _index;

        if (Peek() == '#')
        {
            SkipBlockComment(opened);
            KeepComment(opened, line: false);
            return true;
        }

        SkipToEndOfLine();
        ReadDirective(opened);
        KeepComment(opened, line: true);
        return true;
    }

    /// <summary>
    /// <para>Keeps the reach of a comment, so that what it says can be read once scanning is
    /// done.</para>
    /// <para><b>A run of line comments is one comment.</b> Anything but a summary needs a line
    /// of its own, so reading each <c>#</c> separately would leave every line after the first
    /// opening with something other than <c>@summary:</c> — read as prose and silently
    /// dropped. Nothing separates the lines of a run but the newline between them, so they are
    /// what a reader plainly means them to be: one thing.</para>
    /// </summary>
    private void KeepComment(int start, bool line)
    {
        if (line && _comments.Count > 0 && _comments[^1].Line && Adjoins(_comments[^1].End, start))
        {
            _comments[^1] = _comments[^1] with { End = _index };
            return;
        }

        _comments.Add(new Comment(start, _index, line));
    }

    /// <summary>
    /// Whether one comment runs straight into the next: nothing between them but the single
    /// newline that ends the first. A blank line, or anything else, starts a new comment.
    /// </summary>
    private bool Adjoins(int end, int start) =>
        _text[end..start].Count(c => c == '\n') == 1
        && _text[end..start].All(char.IsWhiteSpace);

    /// <summary>
    /// <para>Reads the one directive a line comment may carry, and keeps it if it is one.</para>
    /// <para>This is the whole of the language talking to its own compiler, and it goes no
    /// further than here: the parser never learns comments existed, so nothing about the
    /// grammar changes to admit it. Only a line comment is read — a <c>##</c> block is prose,
    /// and a directive buried in a paragraph is one nobody sees.</para>
    /// </summary>
    private void ReadDirective(int start)
    {
        if (SuppressionDirective.TryRead(_text[start.._index].TrimStart('#'), out var target))
        {
            _directives.Add((SpanOf(start, _index - start), target));
        }
    }

    /// <summary>
    /// <para>Consumes a block comment: everything from its opening <c>##</c> to the end of the
    /// line carrying the next one.</para>
    /// <para>Taking the whole closing line is what removes nesting as an idea rather than as a
    /// rule. There is no depth to count, so a block cannot be half-closed by something written
    /// inside it, and a run of marks — <c>########</c> above and below — is a heading rather
    /// than a syntax error.</para>
    /// <para>It also settles where a comment may sit: since the closer takes the rest of its
    /// line with it, nothing can follow one and still be code. A comment is a line of its own
    /// or the end of a line, never a parenthesis in the middle of one.</para>
    /// </summary>
    private void SkipBlockComment(int start)
    {
        // Past the opening pair, so that the very next mark can close it and "## ##" is an
        // empty comment rather than one that never ends.
        _index += 2;

        while (!IsAtEnd())
        {
            if (Current() == '#' && Peek() == '#')
            {
                _index += 2;
                SkipToEndOfLine();
                return;
            }

            _index++;
        }

        // Point at the opener rather than at end of file; that is where the fix goes.
        _diagnostics.Report(DiagnosticDescriptors.UnterminatedBlockComment, SpanOf(start, 2));
    }

    private void SkipToEndOfLine()
    {
        while (!IsAtEnd() && Current() != '\n')
        {
            _index++;
        }
    }

    /// <summary>
    /// Scans the next token. Returns null when the input was consumed without producing
    /// one, which happens only on an unrecognized character.
    /// </summary>
    private Token? ScanNext()
    {
        char c = Current();

        if (_holes.TryPeek(out Hole? hole) && hole.InExpression && ScanHoleEdge(hole, c) is { } edge)
        {
            return edge;
        }

        if (c == '\'')
        {
            return ScanCharacterLiteral();
        }

        if (c == '"')
        {
            return Peek() == '"' && Peek(2) == '"'
                ? ScanBlockString()
                : ScanStringLiteral();
        }

        if (c == '@')
        {
            return ScanEscapedName();
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
    /// characters, and unicode escapes within names are all absent, since none of them serves
    /// a reader.</para>
    /// </summary>
    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    /// <summary>
    /// A character that may continue an identifier: a letter, a digit, or an underscore.
    /// Also used to test word boundaries when recognizing comments, which is why an
    /// identifier such as "comment_text" is correctly not read as a comment.
    /// </summary>
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// <para>Scans a reserved word being used as a name, written with a leading <c>@</c>.</para>
    /// <para>Several reserved words are ordinary things to call a variable — <c>end</c>,
    /// <c>base</c>, <c>to</c> — and a language cannot give every such word back without
    /// leaving the grammar guessing. The mark is the way to take one back deliberately, and it
    /// is the only place a name may begin with something other than a letter.</para>
    /// <para>The token is always an identifier, since that is the whole point. Its lexeme keeps
    /// the mark, because a lexeme is the exact source slice; the name without it is
    /// <see cref="Token.Name"/>.</para>
    /// </summary>
    private Token ScanEscapedName()
    {
        int start = _index;
        _index++;

        if (IsAtEnd() || !IsIdentifierStart(Current()))
        {
            _diagnostics.Report(DiagnosticDescriptors.EscapeNeedsAName, SpanFrom(start));
            return new Token(TokenType.Identifier, _text[start.._index], SpanFrom(start));
        }

        while (!IsAtEnd() && IsIdentifierPart(Current()))
        {
            _index++;
        }

        string written = _text[start.._index];
        string name = written[1..];

        if (!ReservedWords.IsReserved(name))
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnnecessaryEscapedName, SpanFrom(start), name);
        }

        return new Token(TokenType.Identifier, written, SpanFrom(start));
    }

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

        // A base other than ten, which only a whole number may be written in.
        if (Current() == '0' && Peek() is 'x' or 'X' or 'b' or 'B')
        {
            return ScanNumberInBase(start);
        }

        ScanDigits(char.IsDigit);

        if (Current() == '.' && char.IsDigit(Peek()))
        {
            _index++;
            ScanDigits(char.IsDigit);

            ScanExponent();
            return FinishNumber(Suffixed(TokenType.RealLiteral), start);
        }

        if (Current() == '|' && char.IsDigit(Peek()))
        {
            _index++;
            ScanDigits(char.IsDigit);

            return FinishNumber(TokenType.FractionLiteral, start);
        }

        // An exponent makes a whole number a real, since what it names is a scale rather than a
        // count: 1e3 is 1000.0. Writing 1000 is how the integer is asked for.
        return ScanExponent()
            ? FinishNumber(Suffixed(TokenType.RealLiteral), start)
            : FinishNumber(Suffixed(TokenType.IntegerLiteral), start);
    }

    /// <summary>
    /// <para>Reads an <c>f</c> against a number, which asks for binary floating point.</para>
    /// <para>Consumed here rather than left to <see cref="FinishNumber"/>, which would otherwise
    /// see a letter touching digits and report the name it is written to catch — <c>1each</c> and
    /// <c>40var</c> are the mistakes that check exists for, and a suffix is not one of them.</para>
    /// <para>Only a lone <c>f</c> counts. <c>3.14foo</c> is still a name against a number, since
    /// what follows the letter decides whether a suffix was written or a word was.</para>
    /// </summary>
    private TokenType Suffixed(TokenType plain)
    {
        if (Current() is not ('f' or 'F') || IsIdentifierPart(Peek()))
        {
            return plain;
        }

        _index++;
        return TokenType.FloatLiteral;
    }

    /// <summary>
    /// <para>Closes a number, reporting a name written against it.</para>
    /// <para>A digit run touching a letter is never two things: nothing in the language puts
    /// two values side by side, so <c>1each</c> and <c>40var</c> are one mistake however they
    /// were meant. The word is taken into the number's lexeme rather than left to scan on its
    /// own, which is what turns three complaints about a statement that could not start into
    /// the one about what was actually written.</para>
    /// </summary>
    private Token FinishNumber(TokenType type, int start)
    {
        if (IsAtEnd() || !IsIdentifierStart(Current()))
        {
            return MakeToken(type, start);
        }

        int name = _index;

        while (!IsAtEnd() && IsIdentifierPart(Current()))
        {
            _index++;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.NameAgainstNumber,
            SpanFrom(start),
            _text[name.._index]);

        return MakeToken(type, start);
    }

    /// <summary>
    /// <para>Consumes a run of digits, in which an underscore may separate one from the next.
    /// </para>
    /// <para>A separator has to sit between digits: it groups them and there is nothing to
    /// group at either end. One that does not is reported and consumed, since <c>1_</c> would
    /// otherwise read as a number beside the name <c>_</c>, and no reader means that.</para>
    /// </summary>
    private void ScanDigits(Func<char, bool> isDigit)
    {
        while (!IsAtEnd() && isDigit(Current()))
        {
            _index++;

            if (Current() != '_')
            {
                continue;
            }

            int separator = _index;

            while (Current() == '_')
            {
                _index++;
            }

            if (!isDigit(Current()))
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.SeparatorNeedsDigits, SpanFrom(separator));

                return;
            }
        }
    }

    /// <summary>
    /// <para>Reads an exponent if one is there — <c>e</c>, an optional sign, then digits — and
    /// answers whether it did, since that is what turns a whole number into a real.</para>
    /// <para>Nothing is consumed unless digits follow, so a name that begins with <c>e</c> is
    /// still a name and <c>1 e</c> is still two things. Where an <c>e</c> is written and the
    /// digits are missing there is no reading in which it was meant as a name, so that is
    /// reported and consumed rather than left to arrive as an undefined one.</para>
    /// </summary>
    private bool ScanExponent()
    {
        if (Current() is not ('e' or 'E'))
        {
            return false;
        }

        int after = _index + 1;

        if (after < _text.Length && (_text[after] == '+' || _text[after] == '-'))
        {
            after++;
        }

        if (after < _text.Length && char.IsDigit(_text[after]))
        {
            _index = after;
            ScanDigits(char.IsDigit);

            return true;
        }

        // An 'e' followed by a letter is the start of a name, not a broken exponent: "1else"
        // is nothing anyone writes, but "e" as a name is, and a number cannot touch one.
        if (after < _text.Length && IsIdentifierPart(_text[after]))
        {
            return false;
        }

        _diagnostics.Report(DiagnosticDescriptors.ExponentNeedsDigits, SpanFrom(_index));
        _index = after;

        return true;
    }

    /// <summary>
    /// <para>Reads a whole number written in hexadecimal or binary, the prefix already in
    /// sight.</para>
    /// <para>Every letter and digit after the prefix is taken before any is judged, so
    /// <c>0b12</c> and <c>0xG</c> are each one mistake with one message rather than a number
    /// that stops early and a name that starts in the middle of it.</para>
    /// </summary>
    private Token ScanNumberInBase(int start)
    {
        bool hexadecimal = Peek() is 'x' or 'X';
        _index += 2;

        int digits = _index;

        while (!IsAtEnd() && (char.IsLetterOrDigit(Current()) || Current() == '_'))
        {
            _index++;
        }

        string raw = _text[digits.._index];
        string written = raw.Replace("_", string.Empty, StringComparison.Ordinal);

        // The same rule the other bases follow: a separator sits between digits, so one at
        // either end has nothing to group.
        if (raw.StartsWith('_') || raw.EndsWith('_'))
        {
            _diagnostics.Report(DiagnosticDescriptors.SeparatorNeedsDigits, SpanFrom(start));
        }
        else if (written.Length == 0)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.BaseNeedsDigits,
                SpanFrom(start),
                hexadecimal ? "0x" : "0b",
                hexadecimal ? "hexadecimal" : "binary");
        }
        else if (written.FirstOrDefault(c => !IsDigitInBase(c, hexadecimal)) is var stray
                 && stray != '\0')
        {
            _diagnostics.Report(
                DiagnosticDescriptors.DigitOutsideBase,
                SpanFrom(start),
                stray,
                hexadecimal ? "hexadecimal" : "binary",
                hexadecimal ? "0 to 9 and a to f" : "0 and 1");
        }

        return MakeToken(TokenType.IntegerLiteral, start);
    }

    private static bool IsDigitInBase(char c, bool hexadecimal) =>
        hexadecimal ? char.IsAsciiHexDigit(c) : c is '0' or '1';

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

    // ---- Interpolation ------------------------------------------------------------------

    /// <summary>
    /// <para>Handles the three places inside a hole where an ordinary scan would read the
    /// wrong thing, and returns null everywhere else so the rest of the scanner runs
    /// unchanged.</para>
    /// <para>Those places are the <c>}}</c> that ends the hole, the <c>:</c> that introduces a
    /// format, and the braces of a set literal written inside it — which have to be counted,
    /// or <c>{{ {1, 2}.Count() }}</c> would end at the wrong pair.</para>
    /// </summary>
    private Token? ScanHoleEdge(Hole hole, char c)
    {
        if (hole.Depth == 0 && c == '}' && Peek() == '}')
        {
            int start = _index;
            _index += 2;
            hole.InExpression = false;
            return MakeToken(TokenType.InterpolationEnd, start);
        }

        if (hole.Depth == 0 && c == ':')
        {
            return ScanFormatSpecifier();
        }

        // A quote inside a hole is usually a string written in it, which is why one is allowed
        // there at all. But it is also what the enclosing string closes with, and when the
        // hole was never closed that is what this one is. Told apart by whether a matching
        // quote follows on the line: a string that never closes is not the reading to take,
        // and taking it swallows the rest of the line as text.
        if (c == '"' && !AQuoteClosesOnThisLine())
        {
            int quote = _index;
            _diagnostics.Report(
                DiagnosticDescriptors.UnterminatedInterpolation, SpanOf(hole.Opened, 2));

            _index++;
            _holes.Pop();
            return MakeToken(TokenType.InterpolatedStringEnd, quote);
        }

        if (c == '{')
        {
            hole.Depth++;
        }
        else if (c == '}')
        {
            // Guarded so a stray closer cannot drive the count below zero and take the hole's
            // own '}}' with it.
            hole.Depth = Math.Max(0, hole.Depth - 1);
        }

        return null;
    }

    /// <summary>
    /// <para>Scans how a hole's value should be written out: the <c>:</c> and everything to
    /// the <c>}}</c>.</para>
    /// <para>Taken whole rather than tokenized, because a format is a pattern rather than
    /// code — <c>F2</c>, <c>yyyy-MM-dd</c>, <c>#,##0.00</c> — and scanning it as though it
    /// were code would read most of those as several tokens and some as mistakes.</para>
    /// </summary>
    private Token ScanFormatSpecifier()
    {
        int start = _index;
        _index++;

        while (!IsAtEnd() && Current() != '\n')
        {
            if (Current() == '}' && Peek() == '}')
            {
                break;
            }

            _index++;
        }

        if (_index == start + 1)
        {
            _diagnostics.Report(DiagnosticDescriptors.EmptyFormatSpecifier, SpanFrom(start));
        }

        return MakeToken(TokenType.InterpolationFormat, start);
    }

    /// <summary>
    /// <para>Scans the text of an interpolated string up to whichever comes first: the next
    /// hole, the closing quote, or the end of the line.</para>
    /// <para>Called from the scanning loop rather than from a single method that reads the
    /// whole literal, because what sits between the holes and what sits inside them are
    /// scanned by different rules, and the loop is what alternates between them.</para>
    /// </summary>
    private Token ContinueInterpolatedString(Hole hole)
    {
        int start = _index;

        while (!IsAtEnd() && Current() != '\n')
        {
            if (Current() == '"')
            {
                if (_index > start)
                {
                    return MakeToken(TokenType.InterpolatedStringText, start);
                }

                _index++;
                _holes.Pop();
                return MakeToken(TokenType.InterpolatedStringEnd, start);
            }

            if (Current() == '{' && Peek() == '{')
            {
                if (_index > start)
                {
                    return MakeToken(TokenType.InterpolatedStringText, start);
                }

                _index += 2;
                hole.InExpression = true;
                hole.Depth = 0;
                hole.Opened = start;

                if (Current() == '}' && Peek() == '}')
                {
                    _diagnostics.Report(
                        DiagnosticDescriptors.EmptyInterpolation, SpanOf(start, 4));
                }

                return MakeToken(TokenType.InterpolationStart, start);
            }

            if (Current() == '\\')
            {
                ScanEscape();
                continue;
            }

            _index++;
        }

        // Ran out of line with the string still open. Whatever text had accumulated is handed
        // over first, so it is not lost and does not end up standing in for the closing quote.
        if (_index > start)
        {
            return MakeToken(TokenType.InterpolatedStringText, start);
        }

        // Reported at the opening quote, which is where the missing one belongs. The string is
        // then closed off with an empty token, so the parser is handed a whole shape rather
        // than a dangling one.
        _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, SpanOf(hole.Quote, 1));
        _holes.Pop();
        return MakeToken(TokenType.InterpolatedStringEnd, start);
    }

    /// <summary>
    /// Ends a hole that reached the end of its line, so that the tokens after it are read as
    /// the code they are rather than as more of the expression.
    /// </summary>
    private bool EndOfLineInsideAHole(Hole hole)
    {
        int probe = _index;

        while (probe < _text.Length && (_text[probe] == ' ' || _text[probe] == '\t'
                                        || _text[probe] == '\r'))
        {
            probe++;
        }

        if (probe < _text.Length && _text[probe] != '\n')
        {
            return false;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.UnterminatedInterpolation, SpanOf(hole.Opened, 2));

        _index = probe;
        _holes.Pop();
        return true;
    }

    /// <summary>
    /// <para>Scans a block string: everything between an opening run of quotes and the next
    /// run of the same length.</para>
    /// <para>Verbatim, as C#'s is. No escape is read and no hole is looked for, so a path, a
    /// brace, a backslash or a quote survives being pasted in — which is the whole reason to
    /// reach for one. It is also why the language needs no separate verbatim form: this is
    /// that form.</para>
    /// <para>Three quotes open one, and more than three also do. Any shorter run inside is
    /// text, so a block that has to hold three quotes of its own is opened and closed with
    /// four, and one holding four with five. Without that there is a string no delimiter can
    /// write.</para>
    /// </summary>
    private Token ScanBlockString()
    {
        int start = _index;
        int delimiter = ScanQuoteRun();

        // A run of three or more that is still too short to close: text by the rule, and the
        // closer that was meant whenever the block turns out never to close at all.
        int nearMissAt = -1;
        int nearMiss = 0;

        while (!IsAtEnd())
        {
            if (Current() != '"')
            {
                _index++;
                continue;
            }

            int at = _index;
            int run = ScanQuoteRun();

            if (run == delimiter)
            {
                return MakeToken(TokenType.BlockStringLiteral, start);
            }

            if (run >= 3 && run < delimiter && nearMissAt < 0)
            {
                nearMissAt = at;
                nearMiss = run;
            }

            if (run > delimiter)
            {
                // Which quotes close and which are held is unanswerable here, so the run ends
                // the string and the message says how to write what was meant.
                _diagnostics.Report(
                    DiagnosticDescriptors.BlockStringRunIsTooLong,
                    SpanOf(at, run),
                    run,
                    delimiter,
                    new string('"', run + 1));

                return MakeToken(TokenType.BlockStringLiteral, start);
            }
        }

        if (nearMissAt >= 0)
        {
            // One mistake, so one message: a run of quotes that all but closed the block is
            // what went wrong, and saying "unterminated" at the opener buries it.
            _diagnostics.Report(
                DiagnosticDescriptors.BlockStringDelimitersDiffer,
                SpanOf(nearMissAt, nearMiss),
                nearMiss,
                delimiter,
                new string('"', nearMiss),
                new string('"', delimiter));
        }
        else
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnterminatedBlockString,
                SpanOf(start, delimiter),
                new string('"', delimiter));
        }

        return MakeToken(TokenType.BlockStringLiteral, start);
    }

    /// <summary>
    /// Consumes a run of quotes and answers how many there were. Both delimiters of a block
    /// string are read with this, which is what makes them comparable.
    /// </summary>
    private int ScanQuoteRun()
    {
        int start = _index;

        while (!IsAtEnd() && Current() == '"')
        {
            _index++;
        }

        return _index - start;
    }

    /// <summary>
    /// <para>Scans a string literal.</para>
    /// <para>A literal may not span a line break. Letting it do so means one missing closing
    /// quote swallows the rest of the file, turning a single real error into a cascade of
    /// invented ones.</para>
    /// <para>One holding an interpolation is handed to the loop instead, which takes it apart
    /// into text and holes. Looking ahead for a <c>{{</c> first means a string without one is
    /// still a single token, so nothing that never interpolates changes shape.</para>
    /// </summary>
    private Token ScanStringLiteral()
    {
        if (HoldsAnInterpolation())
        {
            int quote = _index;
            _index++;
            _holes.Push(new Hole { Quote = quote });
            return MakeToken(TokenType.InterpolatedStringStart, quote);
        }

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
    /// Whether the quote at the current position has a closing one after it on the same line.
    /// </summary>
    private bool AQuoteClosesOnThisLine()
    {
        for (int probe = _index + 1; probe < _text.Length && _text[probe] != '\n'; probe++)
        {
            if (_text[probe] == '\\')
            {
                probe++;
                continue;
            }

            if (_text[probe] == '"')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the literal starting here holds a <c>{{</c> before it ends. Looked at without
    /// consuming anything, since the answer decides which of two shapes the literal takes.
    /// </summary>
    private bool HoldsAnInterpolation()
    {
        for (int probe = _index + 1; probe < _text.Length; probe++)
        {
            char c = _text[probe];

            if (c == '\n' || c == '"')
            {
                return false;
            }

            // A '\{' is a literal brace and cannot open a hole, so it is stepped over whole
            // rather than letting its second character be read as the start of one.
            if (c == '\\')
            {
                probe++;
                continue;
            }

            if (c == '{' && probe + 1 < _text.Length && _text[probe + 1] == '{')
            {
                return true;
            }
        }

        return false;
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

            // A single brace is already literal, so this is needed only to write two in a row
            // without opening a hole. Rare, and the alternative is a sentence about
            // interpolation that no ordinary string can say.
            case '{':
            case '}':
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

        foreach ((string op, TokenType? standsIn, string? fixedBy, string advice) in NonOperators)
        {
            if (MatchesAt(start, op))
            {
                _index += op.Length;
                _diagnostics.ReportFixable(
                    DiagnosticDescriptors.NotAnOperator,
                    SpanOf(start, op.Length),
                    fixedBy,
                    op,
                    advice);

                // The stand-in keeps its own lexeme, so the token still slices exactly out of
                // the source and a printed stream shows what was really written.
                return standsIn is { } replacement ? MakeToken(replacement, start) : null;
            }
        }

        char c = Current();
        char next = Peek();

        // Two-character operators, checked before the single-character forms.
        if (c == '=' && next == '=') { _index += 2; return MakeToken(TokenType.EqualEqual, start); }
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
            '^' => TokenType.Caret,
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
            // "!" on its own only reaches here when it is not part of "!=", which is why it
            // cannot live in the table above: that is checked first and would swallow the "!"
            // of a "!=". It stands in as "not" for the same reason the others do, and here
            // there is a second reason — dropping it would leave a condition meaning the
            // opposite of what was written.
            if (c == '!')
            {
                _diagnostics.ReportFixable(
                    DiagnosticDescriptors.NotAnOperator,
                    SpanOf(start, 1),
                    "not",
                    "!",
                    "Use 'not'.");

                return MakeToken(TokenType.Not, start);
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
