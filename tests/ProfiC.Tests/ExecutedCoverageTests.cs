using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every reserved word and every node kind is reached by a sample that <em>runs</em>.
/// </para>
/// <para>Three layers of coverage already exist and two of them are complete: every reserved
/// word lexes, and <see cref="Parsing.ParsedCorpusTests"/> asserts the corpus builds every node
/// the grammar can. Both are measured over the whole corpus, which includes
/// <c>samples/reference</c> — files that declare no <c>Program</c> and are never executed. A
/// construct reached only there is proved to scan and to parse, and nothing at all is claimed
/// about what it does.</para>
/// <para>That is the gap this closes. The set here is the samples whose output is recorded and
/// asserted on every build, so a word reaching it has been run and its result compared against
/// a recording. It is a weaker claim than "correct" and a much stronger one than "parses".
/// </para>
/// </summary>
[TestFixture]
public sealed class ExecutedCoverageTests : LexerTestBase
{
    /// <summary>
    /// Every source file belonging to a sample that runs: the single files that declare a
    /// <c>Program</c>, and every file of every multi-file sample. The reference corpus is
    /// absent by construction, which is the whole point.
    /// </summary>
    private static IEnumerable<SourceText> ExecutedSources()
    {
        foreach (string name in Interpreting.SampleProgramTests.RunnableSampleNames)
        {
            yield return LoadSample(name);
        }

        foreach (string entry in Interpreting.MultiFileSampleTests.EntryPoints)
        {
            // An entry point is named the way a reader would name it, relative to "samples".
            string folder = Path.Combine(
                RepositoryRoot, "samples", Path.GetDirectoryName(entry)!);

            foreach (string file in Directory.EnumerateFiles(folder, "*.pc",
                                                             SearchOption.AllDirectories))
            {
                yield return SourceText.FromFile(file);
            }
        }
    }

    /// <summary>
    /// Nodes are collected as types rather than by <c>NodeKind</c>, which some override to say
    /// something more useful than their name — a <see cref="ReceiverExpr"/> calls itself
    /// <c>ThisExpr</c> or <c>BaseExpr</c>. Comparing names against kinds would report that one
    /// as never reached while every sample in the corpus builds it.
    /// </summary>
    private static (HashSet<TokenType> Tokens, HashSet<Type> Nodes) Reached()
    {
        HashSet<TokenType> tokens = [];
        HashSet<Type> nodes = [];

        foreach (SourceText source in ExecutedSources())
        {
            DiagnosticBag diagnostics = new();

            foreach (Token token in new Lexer(source, diagnostics).Scan())
            {
                tokens.Add(token.Type);
            }

            CompilationUnit unit = Parser.Parse(source, diagnostics);
            nodes.Add(unit.GetType());
            nodes.UnionWith(unit.Descendants().Select(node => node.GetType()));
        }

        return (tokens, nodes);
    }

    /// <summary>
    /// <para>A reserved word no running program uses is one whose behavior nothing has ever
    /// checked. It scans, it parses, and what it does when a program reaches it is unproven.
    /// </para>
    /// <para>Every word earns its place by being worth running, so there is no exempt list.
    /// </para>
    /// </summary>
    [Test]
    public void EveryReservedWordIsReachedByASampleThatRuns()
    {
        HashSet<TokenType> reached = Reached().Tokens;

        Assert.That(
            ReservedWords.Keywords
                         .Where(entry => !reached.Contains(entry.Value))
                         .Select(entry => entry.Key)
                         .Order(StringComparer.Ordinal),
            Is.Empty,
            "reserved words no executing sample uses");
    }

    /// <summary>
    /// The same question of the parser's output. A node kind built only by the reference corpus
    /// has been printed and compared against a recorded tree, and never evaluated.
    /// </summary>
    /// <summary>
    /// The same question of everything that is not a word: operators, punctuation, and each
    /// form a literal takes. A symbol no running program contains is one whose meaning has
    /// been parsed and never evaluated.
    /// </summary>
    [Test]
    public void EveryTokenTypeIsReachedByASampleThatRuns()
    {
        HashSet<TokenType> reached = Reached().Tokens;

        // '|' is only ever part of a fraction literal, which scans whole as a single token, so
        // the bare one exists to recover from a stray mark and cannot appear in valid source.
        TokenType[] absentByDesign = [TokenType.Pipe];

        Assert.That(
            Enum.GetValues<TokenType>()
                .Except(absentByDesign)
                .Where(type => !reached.Contains(type))
                .Select(type => type.ToString())
                .Order(StringComparer.Ordinal),
            Is.Empty,
            "token types no executing sample contains");
    }

    [Test]
    public void EveryNodeKindIsReachedByASampleThatRuns()
    {
        HashSet<Type> reached = Reached().Nodes;

        // Nodes the parser never builds, so no source of any kind reaches them. The two
        // Missing ones stand in for a failed parse and belong to the negatives; a conversion
        // is made explicit during lowering, and a walk is marked there too, so both exist only
        // in the tree the interpreter is handed and never in the one read from a file.
        Type[] absentByDesign =
            [typeof(MissingExpr), typeof(MissingType), typeof(ConversionExpr), typeof(WalkStmt)];

        IEnumerable<Type> everything = typeof(SyntaxNode).Assembly
            .GetTypes()
            .Where(type => type.IsSealed && type.IsSubclassOf(typeof(SyntaxNode)))
            .Except(absentByDesign);

        Assert.That(
            everything.Where(type => !reached.Contains(type))
                      .Select(type => type.Name)
                      .Order(StringComparer.Ordinal),
            Is.Empty,
            "node kinds no executing sample builds");
    }
}
