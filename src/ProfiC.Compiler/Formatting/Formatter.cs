using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Formatting;

/// <summary>
/// <para>Lines a program up. Never moves it.</para>
/// <para><b>Formatted from the tokens rather than from the tree, which is the decision the rest
/// of this follows from.</b> A formatter that printed the tree would delete every ordinary
/// comment, because the tree carries the documentation kind and not the others — and recovering
/// them by slicing the gaps between tokens means judging, hundreds of times a file, whether a
/// comment belongs to the line above it or the one below. Getting that wrong moves somebody's
/// comment.</para>
/// <para>So nothing here removes a character. Every line keeps its content exactly, and only the
/// whitespace in front of it is rewritten. That makes two promises worth more than reflowing: a
/// comment cannot be lost, and <b>a file that does not parse is formatted anyway</b> — which
/// matters, since a file is being written most of the time it is being formatted.</para>
/// <para>What it gives up is the long line. Nothing here wraps one or joins two, so a
/// two-hundred-character call stays as it is. That is the trade, and it is the right way round
/// for a language people are learning: "it lines your code up and never moves it" is a promise
/// that can be kept in every case, where "it lays your code out well" cannot.</para>
/// </summary>
public static class Formatter
{
    /// <summary>How far one level of nesting reaches.</summary>
    public const int IndentWidth = 4;

    /// <summary>
    /// <para>The file, lined up.</para>
    /// <para>Line endings are the ones the file already used, decided by whether it has any of
    /// the two-character kind, so formatting on Windows does not rewrite every line of a file
    /// written on Linux.</para>
    /// </summary>
    public static string Format(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Layout layout = new(source);
        string ending = source.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        List<string> lines = [];

        for (int line = 1; line <= source.LineCount; line++)
        {
            lines.Add(layout.Rewrite(line));
        }

        // Whether the file ends with a newline is the writer's business rather than this one's,
        // and it is already answered: a file that ends with one has an empty line after it, so
        // joining puts the terminator back. Adding one here would add a second every time.
        return string.Join(ending, lines);
    }

    /// <summary>
    /// <para>Where one line begins, in spaces, for a caller placing that line rather than
    /// rewriting the file.</para>
    /// <para><b>Not the same question <see cref="Format"/> answers.</b> Format writes an empty
    /// line empty, because spaces on a line with nothing on it are not layout — and somebody who
    /// has just pressed Enter is standing on exactly such a line, wanting to know where it
    /// begins. Asking Format would move their cursor to the left margin.</para>
    /// <para>Null where the line is not this formatter's to place: inside a block string those
    /// spaces are characters the program holds, and inside a block comment they are prose
    /// somebody laid out.</para>
    /// </summary>
    public static int? IndentOf(SourceText source, int line)
    {
        ArgumentNullException.ThrowIfNull(source);

        return line >= 1 && line <= source.LineCount ? new Layout(source).Placing(line) : null;
    }

    /// <summary>
    /// <para>Where each line's content should begin, in spaces.</para>
    /// <para>Worked out in one walk down the file, because a wrapped line is placed against the
    /// bracket it is inside and that bracket's own line may have been wrapped too. Reading the
    /// file in order is what makes each answer available to the line that needs it.</para>
    /// </summary>
    private sealed class Layout
    {
        private readonly SourceText _source;
        private readonly int[] _indent;
        private readonly bool[] _asWritten;
        private readonly bool[] _inAValue;

        public Layout(SourceText source)
        {
            _source = source;

            DiagnosticBag aside = new();
            Lexer lexer = new(source, aside);

            IReadOnlyList<Token> tokens = lexer.Scan();

            _indent = new int[source.LineCount + 2];
            _asWritten = new bool[source.LineCount + 2];
            _inAValue = new bool[source.LineCount + 2];

            MarkWhatIsNotLayout(tokens, lexer.Comments);
            Walk(tokens);
        }

        /// <summary>
        /// <para>The line as it should be written.</para>
        /// <para>A blank line becomes empty rather than blank, which is the one thing here that
        /// removes characters — and trailing whitespace is not content.</para>
        /// </summary>
        public string Rewrite(int line)
        {
            string text = _source.GetLine(line).ToString().TrimEnd('\r', '\n');

            // Trailing spaces inside a block string are characters the program holds, so even
            // that much is not this formatter's to remove. Inside a comment they are nothing,
            // and go like any others.
            if (_asWritten[line])
            {
                return _inAValue[line] ? text : text.TrimEnd();
            }

            string content = text.TrimStart(' ', '\t').TrimEnd();

            return content.Length == 0
                ? string.Empty
                : new string(' ', Math.Max(0, _indent[line])) + content;
        }

        /// <summary>
        /// <para>Lines whose spacing is not this formatter's to decide.</para>
        /// <para>The inside of a block string is the string: those spaces are characters the
        /// program holds, and moving them changes what it prints. The inside of a block comment
        /// is prose somebody laid out by hand. Neither is layout, and the difference between the
        /// two reasons does not matter — what matters is that a line running through one is left
        /// exactly as it was found.</para>
        /// </summary>
        private void MarkWhatIsNotLayout(
            IReadOnlyList<Token> tokens, IReadOnlyList<SourceSpan> comments)
        {
            foreach ((SourceSpan span, bool value) in tokens.Select(t => (t.Span, true))
                .Concat(comments.Select(c => (c, false))))
            {
                int first = span.Start.Line;
                int last = _source.PositionAt(Math.Max(span.Start.Offset, span.EndOffset - 1)).Line;

                // The line it begins on is still this formatter's, since what comes before it
                // there is ordinary code. Only the lines it runs into belong to the writer.
                for (int line = first + 1; line <= last && line < _asWritten.Length; line++)
                {
                    _asWritten[line] = true;
                    _inAValue[line] |= value;
                }
            }
        }

        /// <summary>
        /// Where this line begins, or nothing where its spacing is the writer's rather than
        /// this formatter's.
        /// </summary>
        public int? Placing(int line) =>
            _asWritten[line] ? null : Math.Max(0, _indent[line]);

        /// <summary>The indent a line was written with, for the lines this one does not place.</summary>
        private int AsFound(int line)
        {
            string text = _source.GetLine(line).ToString();

            return text.Length - text.TrimStart(' ', '\t').Length;
        }

        /// <summary>
        /// <para>A bracket that is still open, and what lines inside it are measured against.
        /// </para>
        /// <para>Two columns, because the two kinds of line inside a bracket answer to different
        /// things. <c>Anchor</c> is where the first item begins, and every later item lines up
        /// under it. <c>Column</c> is the bracket itself, and a line carrying another on steps in
        /// from there.</para>
        /// </summary>
        private readonly record struct Bracket(
            TokenType Closer, int Opener, int Anchor, int Column, int Constructs);

        /// <summary>
        /// <para>Reads the file a line at a time and settles where each one begins.</para>
        /// <para>Two questions, and which is being asked depends only on whether a bracket is
        /// open: inside one, a line is placed against that bracket; outside, against what
        /// constructs are open above it.</para>
        /// </summary>
        private void Walk(IReadOnlyList<Token> tokens)
        {
            Stack<Opened> open = new();
            Stack<Bracket> brackets = new();

            IReadOnlyList<Token>? previous = null;
            int at = 0;

            for (int line = 1; line < _indent.Length; line++)
            {
                List<Token> onThisLine = [];

                while (at < tokens.Count
                    && tokens[at].Type != TokenType.EndOfFile
                    && tokens[at].Span.Start.Line == line)
                {
                    onThisLine.Add(tokens[at]);
                    at++;
                }

                // A case group has no closer of its own: it ends where the next label begins, or
                // where the switch does. So it is taken off before the line that ends it is
                // placed, rather than by anything on that line saying so.
                if (open.Count > 0
                    && open.Peek() == Opened.Case
                    && Starts(onThisLine, TokenType.Case, TokenType.Default, TokenType.End))
                {
                    open.Pop();
                }

                // A bracket cannot outlive the body it was opened in, so an 'end' closes any
                // left open above it. Without this one unclosed paren carries the rest of the
                // file off to the right — and a file being written has one in it constantly.
                while (Starts(onThisLine, TokenType.End)
                    && brackets.Count > 0
                    && brackets.Peek().Constructs >= open.Count)
                {
                    brackets.Pop();
                }

                // A line left as written keeps the indent it was written with, so that anything
                // measured against it is measured against where it will actually be.
                _indent[line] = _asWritten[line]
                    ? AsFound(line)
                    : brackets.Count > 0
                        ? Inside(onThisLine, previous, brackets.Peek(), open)
                        : Math.Max(0, open.Count - (StepsOut(onThisLine, open) ? 1 : 0))
                            * IndentWidth;

                // Run even for a line left as written. What is on it still opens and closes
                // things: the last line of a block string carries the bracket that the call
                // around it closes with, and skipping it leaves that bracket open for the rest
                // of the file.
                Nest(onThisLine, open, brackets, _indent[line]);

                if (onThisLine.Count > 0)
                {
                    previous = onThisLine;
                }
            }
        }

        /// <summary>
        /// <para>Where a line inside a bracket begins, anchored to the line above it.</para>
        /// <para>Three cases. A line that <b>closes</b> the bracket goes back to the line that
        /// opened it, so a closing brace sits level with the statement rather than with the
        /// contents it is ending.</para>
        /// <para>A line that <b>begins something new</b> lines up with the first thing in the
        /// bracket. What says it is new is the character the line above it ended on: a comma, or
        /// the bracket itself. So the rows of a matrix line up with the first row, and the
        /// arguments of a call line up with the first argument.</para>
        /// <para>Anything else <b>carries on</b> the line above, and is written one indent in
        /// from where that line began. It is not another item and should not line up as though
        /// it were — the indent is what says so, and it says it in the one place a reader is
        /// already looking.</para>
        /// </summary>
        /// <summary>
        /// <para>Where a line inside a bracket begins, once it is known whether that line is
        /// inside a <em>body</em> as well.</para>
        /// <para><b>A lambda written across several lines is the case this exists for.</b> Its
        /// body is a body like any other — statements, an <c>if</c> with something inside it, an
        /// <c>end function</c> — and lining those up with an argument would push a whole function
        /// off to wherever the call's bracket happened to fall, and flatten its nesting on the way.
        /// So once a construct is open inside the bracket, the lines belong to it and are placed
        /// by nesting from the statement, exactly as they would be had the lambda been written
        /// anywhere else.</para>
        /// </summary>
        private static int Inside(
            IReadOnlyList<Token> line,
            IReadOnlyList<Token>? previous,
            Bracket bracket,
            Stack<Opened> open)
        {
            if (open.Count <= bracket.Constructs)
            {
                return Wrapped(line, previous, bracket);
            }

            int depth = open.Count - bracket.Constructs - (StepsOut(line, open) ? 1 : 0);

            return bracket.Opener + (Math.Max(0, depth) * IndentWidth);
        }

        private static int Wrapped(
            IReadOnlyList<Token> line, IReadOnlyList<Token>? previous, Bracket bracket)
        {
            if (line.Count > 0 && line[0].Type == bracket.Closer)
            {
                return bracket.Opener;
            }

            bool beginning = previous is { Count: > 0 }
                && previous[^1].Type is TokenType.Comma or TokenType.LeftParen
                    or TokenType.LeftBracket or TokenType.LeftBrace;

            // An item lines up with the item above it, wherever that fell. A continuation takes
            // the first tab stop past the bracket instead — past it, so it cannot be read as an
            // item, and on a stop, because that is the column an editor puts a wrapped line at
            // and anything else leaves every one of them a space or two adrift of where typing
            // it would have landed.
            return beginning
                ? bracket.Anchor
                : bracket.Column + IndentWidth - (bracket.Column % IndentWidth);
        }

        /// <summary>
        /// <para>What one line's tokens leave open for the lines below it.</para>
        /// <para>Order matters within: a construct closed on this line comes off before the line
        /// below is placed, and one opened on it goes on after — which is what puts a body
        /// between its opener and its <c>end</c> rather than level with either.</para>
        /// </summary>
        private void Nest(
            IReadOnlyList<Token> line,
            Stack<Opened> open,
            Stack<Bracket> brackets,
            int indent)
        {
            // How far this line moved. What a wrapped line lines up with is a column in the
            // formatted file, not in the one that was read — and measuring it in the file that
            // was read is what would leave a second run of this with more to do than the first.
            int shift = line.Count > 0 ? indent - (line[0].Span.Start.Column - 1) : 0;

            for (int at = 0; at < line.Count; at++)
            {
                Token token = line[at];

                if (Bracketing(token) is { } closer)
                {
                    brackets.Push(Measure(line, at, closer, indent, shift, open.Count));
                    continue;
                }

                if (brackets.Count > 0 && token.Type == brackets.Peek().Closer)
                {
                    brackets.Pop();
                    continue;
                }

                // The word after 'end' says what is being closed, and is the same word that
                // opened it. Read on its own it would open the construct again, so the closer
                // would leave the file one level deeper than it found it.
                if (at > 0 && line[at - 1].Type == TokenType.End)
                {
                    continue;
                }

                if (Closes(token, open))
                {
                    if (open.Count > 0)
                    {
                        open.Pop();
                    }
                }
                else if (Opens(token, line, at) is { } opened)
                {
                    open.Push(opened);
                }
            }
        }

        /// <summary>
        /// <para>What lines inside this bracket are measured against.</para>
        /// <para>Where something follows the bracket on its own line, that something is what
        /// later items line up with, and the bracket is what a carried-on line steps in from.
        /// The two differ by one column — the bracket, and the character after it — and that one
        /// column is doing work: an item sits level with the item above it, and a continuation
        /// sits at a depth no item ever occupies, so neither can be read as the other.</para>
        /// <para>Where the bracket ends its line there is nothing to line up with, so both take
        /// one level in from the line that opened it instead — which has the advantage of not
        /// depending on how long the name in front of the bracket is.</para>
        /// </summary>
        private static Bracket Measure(
            IReadOnlyList<Token> line,
            int at,
            TokenType closer,
            int indent,
            int shift,
            int constructs) =>
            at + 1 < line.Count
                ? new Bracket(
                    closer,
                    indent,
                    line[at + 1].Span.Start.Column - 1 + shift,
                    line[at].Span.Start.Column - 1 + shift,
                    constructs)
                : new Bracket(
                    closer, indent, indent + IndentWidth, indent + IndentWidth, constructs);

        private static TokenType? Bracketing(Token token) => token.Type switch
        {
            TokenType.LeftParen => TokenType.RightParen,
            TokenType.LeftBracket => TokenType.RightBracket,
            TokenType.LeftBrace => TokenType.RightBrace,
            _ => null,
        };

        /// <summary>
        /// <para>Whether a line is written one level out from what is open above it.</para>
        /// <para>Two kinds do, for the same reason and by different words. A line that
        /// <b>closes</b> something belongs level with what opened it, so <c>end model</c> lines
        /// up with <c>model</c> rather than with the body between them. A line that
        /// <b>carries a construct on</b> — <c>else</c>, <c>catch</c>, <c>finally</c> — is not in
        /// the body it follows and not in the one it begins, and belongs level with the word
        /// that opened both.</para>
        /// <para><c>case</c> is neither, which is why it is missing here: a label sits in the
        /// switch's body among the statements, and what comes out is the group before it, which
        /// has already been taken off by the time this is asked.</para>
        /// </summary>
        private static bool StepsOut(IReadOnlyList<Token> line, Stack<Opened> open) =>
            Starts(line, TokenType.End, TokenType.Else, TokenType.Catch, TokenType.Finally)
            || (Starts(line, TokenType.Until)
                && open.Count > 0
                && open.Peek() == Opened.LoopUntil);

        private static bool Starts(IReadOnlyList<Token> line, params TokenType[] any) =>
            line.Count > 0 && any.Contains(line[0].Type);

        /// <summary>What a token opened, so that what closes it can be recognized.</summary>
        private enum Opened
        {
            /// <summary>Closed by <c>end</c> and the word that opened it.</summary>
            Named,

            /// <summary>A loop closed by <c>end loop</c>.</summary>
            Loop,

            /// <summary>
            /// The one loop <c>end</c> does not close: its condition is written after the body,
            /// so <c>until</c> is doing the closing itself.
            /// </summary>
            LoopUntil,

            /// <summary>
            /// A run of statements under one or more <c>case</c> labels. Closed by nothing: the
            /// next label ends it, or the <c>end switch</c> does.
            /// </summary>
            Case,
        }

        private static Opened? Opens(Token token, IReadOnlyList<Token> line, int at) =>
            token.Type switch
            {
                TokenType.Model or TokenType.Structure or TokenType.Enumeration
                    or TokenType.Switch or TokenType.Try or TokenType.Begin =>
                    Opened.Named,

                // A namespace is written either way: as a block that ends, or as a line that
                // claims the rest of the file. The second has nothing to close, and nothing in
                // it is indented — there is no body, only the file.
                TokenType.Namespace when !EndsWith(line, TokenType.Semicolon) => Opened.Named,

                // A label opens the run of statements under it, which is what indents them past
                // the label. Several labels may share one run, so only the first opens it.
                TokenType.Case or TokenType.Default when at == 0 => Opened.Case,

                // A body, or a semicolon where one would have been: an abstract function is
                // declared and not written, so there is nothing to close.
                TokenType.Function when !Declared(line, at) => Opened.Named,

                // The word after 'loop' says which kind, and one of the five is closed by
                // something other than 'end'.
                TokenType.Loop => Next(line, at) is TokenType.While or TokenType.For
                        or TokenType.Each
                    ? Opened.Loop
                    : Opened.LoopUntil,

                // 'if' is also an expression, and that one opens nothing. The two are told apart
                // by 'then', which only the expression has. An 'else if' opens nothing either:
                // the body it begins belongs to the 'if' already open.
                TokenType.If when !Has(line, at, TokenType.Then)
                    && !(at > 0 && line[at - 1].Type == TokenType.Else) => Opened.Named,

                _ => null,
            };

        private static bool Closes(Token token, Stack<Opened> open) => token.Type switch
        {
            TokenType.End => true,

            // Only where a loop written without a condition is waiting for one. Every other
            // 'until' is the bound of a counted loop, which is a different word doing a
            // different job in the same place.
            TokenType.Until => open.Count > 0 && open.Peek() == Opened.LoopUntil,

            _ => false,
        };

        /// <summary>
        /// <para>Whether a function is declared here rather than written: the semicolon that
        /// stands where its body would have been.</para>
        /// <para>Found by walking to the end of the parameter list rather than to the end of the
        /// line, because a lambda written on one line ends with a semicolon too —
        /// <c>Show(function() let a = 1 / zero; end function);</c> has one, and has a body. Read
        /// as a declaration it opens nothing, the <c>end function</c> then closes something real,
        /// and every line below it drifts one level out.</para>
        /// </summary>
        private static bool Declared(IReadOnlyList<Token> line, int at)
        {
            int depth = 0;

            for (int here = at; here < line.Count; here++)
            {
                if (line[here].Type == TokenType.LeftParen)
                {
                    depth++;
                }
                else if (line[here].Type == TokenType.RightParen && --depth == 0)
                {
                    return Next(line, here) == TokenType.Semicolon;
                }
            }

            return false;
        }

        private static TokenType? Next(IReadOnlyList<Token> line, int at) =>
            at + 1 < line.Count ? line[at + 1].Type : null;

        private static bool Has(IReadOnlyList<Token> line, int from, TokenType wanted) =>
            line.Skip(from).Any(t => t.Type == wanted);

        private static bool EndsWith(IReadOnlyList<Token> line, TokenType wanted) =>
            line.Count > 0 && line[^1].Type == wanted;
    }
}
