using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>The tour's opening sentence, held as a test.</para>
/// <para><c>samples/reference/tour.pc</c> says every construct in the grammar appears in it at
/// least once. Nothing measured that. The corpus-wide checks in
/// <see cref="Lexing.SampleCorpusTests"/> and <see cref="Parsing.ParsedCorpusTests"/> read every
/// sample in the repository, so a word written in one program and nowhere else satisfies them
/// while the tour goes without it — which is what happened: the bitwise operators lived in
/// <c>bits.pc</c> alone, <c>import</c> in <c>toolkit</c> alone, and two of the six loop forms
/// were missing from the file whose job is to hold all of them.</para>
/// <para>Measured on <c>tour.pc</c> by itself, since that is what the sentence claims. The
/// corpus-wide checks stay where they are and answer the wider question of whether a construct
/// is written down anywhere at all.</para>
/// </summary>
[TestFixture]
public sealed class TourCoverageTests : LexerTestBase
{
    private static (HashSet<TokenType> Tokens, HashSet<Type> Nodes) TheTour()
    {
        SourceText source = LoadSample("tour.pc");
        DiagnosticBag diagnostics = new();

        HashSet<TokenType> tokens = [.. new Lexer(source, diagnostics).Scan().Select(t => t.Type)];

        CompilationUnit unit = Parser.Parse(source, diagnostics);

        // Collected as types rather than by NodeKind, which a receiver overrides to say whether
        // it is a 'this' or a 'base'. Comparing names to kinds would report ReceiverExpr as
        // never written while the tour writes both of them.
        HashSet<Type> nodes = [unit.GetType(), .. unit.Descendants().Select(node => node.GetType())];

        return (tokens, nodes);
    }

    /// <summary>
    /// Every reserved word, in the one file that promises to write them all. A word missing here
    /// is a construct a reader working through the tour never meets.
    /// </summary>
    [Test]
    public void TheTourWritesEveryReservedWord() => Assert.That(
        ReservedWords.Keywords
                     .Where(entry => !TheTour().Tokens.Contains(entry.Value))
                     .Select(entry => entry.Key)
                     .Order(StringComparer.Ordinal),
        Is.Empty,
        "reserved words the tour never writes");

    /// <summary>
    /// The same of everything that is not a word: operators, punctuation, and each form a
    /// literal takes.
    /// </summary>
    [Test]
    public void TheTourWritesEverySymbolAndLiteralForm()
    {
        // A '|' occurs only inside a fraction literal, which scans whole as one token, so the
        // bare one exists to recover from a stray mark and cannot appear in valid source.
        TokenType[] absentByDesign = [TokenType.Pipe];

        Assert.That(
            Enum.GetValues<TokenType>()
                .Except(absentByDesign)
                .Where(type => !TheTour().Tokens.Contains(type))
                .Select(type => type.ToString())
                .Order(StringComparer.Ordinal),
            Is.Empty,
            "token types the tour never contains");
    }

    /// <summary>
    /// Every node the parser can build from valid source. This is the check that catches a
    /// production reachable by the grammar and written down in no tour of it.
    /// </summary>
    [Test]
    public void TheTourBuildsEveryNodeTheGrammarCan()
    {
        // The two Missing ones stand in for a failed parse and belong to the negatives. A
        // conversion is made explicit during lowering and a walk is marked there too, so both
        // exist only in the tree an engine is handed, never in one read from a file.
        Type[] absentByDesign =
            [typeof(MissingExpr), typeof(MissingType), typeof(ConversionExpr), typeof(WalkStmt)];

        HashSet<Type> built = TheTour().Nodes;

        Assert.That(
            typeof(SyntaxNode).Assembly
                .GetTypes()
                .Where(type => type.IsSealed && type.IsSubclassOf(typeof(SyntaxNode)))
                .Except(absentByDesign)
                .Where(type => !built.Contains(type))
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal),
            Is.Empty,
            "node kinds the tour never builds");
    }

    /// <summary>
    /// <para>What a node kind cannot see: a modifier is a flag on a declaration and an operator
    /// is a field on a node, so <c>sealed</c> and <c>shiftleft</c> each leave a tree of exactly
    /// the same shape as the thing beside them.</para>
    /// <para>Held per dimension, so a failure names which kind of thing went unwritten rather
    /// than only that something did.</para>
    /// </summary>
    [Test]
    public void TheTourWritesEveryModifierOperatorLiteralAndReceiver()
    {
        SourceText source = LoadSample("tour.pc");
        DiagnosticBag diagnostics = new();

        HashSet<DeclarationModifiers> modifiers = [];
        HashSet<UnaryOperator> unary = [];
        HashSet<BinaryOperator> binary = [];
        HashSet<LiteralKind> literals = [];
        HashSet<ReceiverKind> receivers = [];

        foreach (SyntaxNode node in Parser.Parse(source, diagnostics).Descendants())
        {
            switch (node)
            {
                case ModelDecl d: Mark(d.Modifiers); break;
                case StructureDecl d: Mark(d.Modifiers); break;
                case EnumerationDecl d: Mark(d.Modifiers); break;
                case FieldDecl d: Mark(d.Modifiers); break;
                case FunctionDecl d: Mark(d.Modifiers); break;
                case UnaryExpr e: unary.Add(e.Operator); break;
                case BinaryExpr e: binary.Add(e.Operator); break;
                case LiteralExpr e: literals.Add(e.Kind); break;
                case ReceiverExpr e: receivers.Add(e.Receiver); break;
            }
        }

        Assert.Multiple(() =>
        {
            // None is a modifier's absence rather than one of them, so it is not a thing to
            // write down and asking for it would be asking for nothing.
            Unwritten("modifiers", modifiers, DeclarationModifiers.None);
            Unwritten("unary operators", unary);
            Unwritten("binary operators", binary);
            Unwritten("kinds of literal", literals);
            Unwritten("receivers", receivers);
        });

        void Mark(DeclarationModifiers written)
        {
            foreach (DeclarationModifiers one in Enum.GetValues<DeclarationModifiers>())
            {
                if (one != DeclarationModifiers.None && written.HasFlag(one))
                {
                    modifiers.Add(one);
                }
            }
        }

        static void Unwritten<T>(string what, HashSet<T> written, params T[] excused)
            where T : struct, Enum =>
            Assert.That(
                Enum.GetValues<T>().Where(one => !written.Contains(one) && !excused.Contains(one)),
                Is.Empty,
                $"{what} the tour never writes");
    }

    /// <summary>
    /// <para>The one construct the tour cannot hold, said out loud so that its absence reads as
    /// a decision rather than as an oversight.</para>
    /// <para>A file-scoped namespace claims the whole file it is written in, so no file can hold
    /// one alongside the block form. The tour opens with blocks, and demonstrating the other
    /// form would cost it every construct that lives inside them.</para>
    /// </summary>
    [Test]
    public void TheTourWritesBlockNamespacesAndNotTheFileScopedForm()
    {
        SourceText source = LoadSample("tour.pc");
        DiagnosticBag diagnostics = new();

        NamespaceDecl[] declared =
            [.. Parser.Parse(source, diagnostics).Descendants().OfType<NamespaceDecl>()];

        Assert.Multiple(() =>
        {
            Assert.That(declared.Any(one => !one.IsFileScoped), Is.True,
                        "the tour should declare a block namespace");

            Assert.That(declared.Any(one => one.IsFileScoped), Is.False,
                        "a file-scoped namespace claims its whole file, so the tour cannot hold "
                        + "one; namespaces.pc is where that form is written");
        });
    }
}
