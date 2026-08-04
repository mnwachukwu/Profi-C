using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Parsing;

/// <summary>
/// <para>Properties asserted over every sample program, plus a recorded tree for each.</para>
/// <para>The golden files carry the same weight here as the token goldens did: they assert
/// everything nobody thought to write a test for, and a diff on one is a readable account of
/// what changed about parsing.</para>
/// </summary>
[TestFixture]
public sealed class ParsedCorpusTests : LexerTestBase
{
    private static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Parsing", "Golden");

    private static (SourceText Source, CompilationUnit Unit, DiagnosticBag Diagnostics) ParseSample(
        string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);
        return (source, unit, diagnostics);
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ParsesWithoutAnyDiagnostic(string name)
    {
        (SourceText source, _, DiagnosticBag diagnostics) = ParseSample(name);

        Assert.That(
            diagnostics.Sorted()
                .Select(d => $"({d.Span.Start.Line},{d.Span.Start.Column}) {d.Id}: {d.Message}"),
            Is.Empty,
            $"{source.FileName} should parse cleanly");
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ProducesNoMissingNodes(string name)
    {
        (_, CompilationUnit unit, _) = ParseSample(name);

        Assert.That(unit.ContainsMissing(), Is.False,
                    "a clean parse should leave nothing standing in for absent syntax");
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_EveryNodeSpanLiesWithinItsParent(string name)
    {
        // Catches a node built from the wrong start token, which is invisible until a
        // diagnostic points somewhere baffling.
        (_, CompilationUnit unit, _) = ParseSample(name);

        Check(unit);

        static void Check(SyntaxNode parent)
        {
            foreach (SyntaxNode child in parent.Children)
            {
                Assert.That(
                    child.Span.Start.Offset,
                    Is.GreaterThanOrEqualTo(parent.Span.Start.Offset),
                    $"{child.NodeKind} starts before its parent {parent.NodeKind}");

                Assert.That(
                    child.Span.EndOffset,
                    Is.LessThanOrEqualTo(parent.Span.EndOffset),
                    $"{child.NodeKind} ends after its parent {parent.NodeKind}");

                Check(child);
            }
        }
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_EveryNodeCarriesARealPosition(string name)
    {
        (_, CompilationUnit unit, _) = ParseSample(name);

        foreach (SyntaxNode node in unit.Descendants())
        {
            Assert.That(node.Line, Is.GreaterThanOrEqualTo(1), $"{node.NodeKind} has no line");
            Assert.That(node.Column, Is.GreaterThanOrEqualTo(1), $"{node.NodeKind} has no column");
        }
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ParsingIsDeterministic(string name)
    {
        (_, CompilationUnit first, _) = ParseSample(name);
        (_, CompilationUnit second, _) = ParseSample(name);

        Assert.That(AstPrinter.Print(second), Is.EqualTo(AstPrinter.Print(first)));
    }

    /// <summary>
    /// The tour is meant to reach every production, so every node type the grammar can
    /// produce should appear somewhere across the corpus. This is what catches a construct
    /// that parses but was never actually written down anywhere.
    /// </summary>
    [Test]
    public void Corpus_ProducesEveryNodeTypeTheGrammarCanBuild()
    {
        HashSet<string> seen = [];

        foreach (string name in SampleNames)
        {
            (_, CompilationUnit unit, _) = ParseSample(name);
            seen.Add(unit.NodeKind);
            seen.UnionWith(unit.Descendants().Select(n => n.NodeKind));
        }

        // Absent by design: the two Missing nodes only appear on a failed parse. Everything
        // the grammar can build from valid source should show up somewhere.
        string[] expected =
        [
            nameof(CompilationUnit), nameof(UsingDirective), nameof(QualifiedName),
            nameof(NamespaceDecl), nameof(ModelDecl), nameof(StructureDecl),
            nameof(EnumerationDecl), nameof(EnumMemberDecl), nameof(FieldDecl),
            nameof(FunctionDecl), nameof(ParameterDecl),
            nameof(NamedTypeSyntax), nameof(SetTypeSyntax), nameof(OptionalTypeSyntax),
            nameof(FunctionTypeSyntax),
            nameof(BlockStmt), nameof(VarDeclStmt), nameof(LocalDeclStmt), nameof(IfStmt),
            nameof(ElseIfClause), nameof(WhileStmt), nameof(ForStmt), nameof(ForEachStmt),
            nameof(SwitchStmt), nameof(CaseGroup), nameof(TryStmt), nameof(CatchClause),
            nameof(ThrowStmt), nameof(YieldStmt), nameof(BreakStmt), nameof(ContinueStmt),
            nameof(ExpressionStmt), nameof(AssignmentStmt),
            nameof(LiteralExpr), nameof(IdentifierExpr), nameof(ParenthesizedExpr),
            nameof(UnaryExpr), nameof(BinaryExpr), nameof(TypeTestExpr), nameof(TypeCastExpr),
            nameof(IfExpr), nameof(CollectionExpr), nameof(NewExpr), nameof(CallExpr),
            nameof(IndexExpr), nameof(MemberExpr), nameof(LambdaExpr),
            "ThisExpr", "BaseExpr",
        ];

        Assert.That(expected.Where(kind => !seen.Contains(kind)), Is.Empty,
                    "these node kinds appear in no sample");
    }

    /// <summary>
    /// <para>Both ways of declaring a namespace, which the check above cannot see.</para>
    /// <para>The two forms are one node kind told apart by a property, so the set of kinds is
    /// the same whichever a sample writes. Without this, the file-scoped form could leave the
    /// corpus and nothing would fail.</para>
    /// </summary>
    [Test]
    public void Corpus_WritesBothNamespaceForms()
    {
        List<NamespaceDecl> declarations = [.. SampleNames
            .SelectMany(name => ParseSample(name).Unit.Descendants())
            .OfType<NamespaceDecl>()];

        Assert.Multiple(() =>
        {
            Assert.That(declarations.Any(n => n.IsFileScoped), Is.True,
                        "no sample declares a file-scoped namespace");

            Assert.That(declarations.Any(n => !n.IsFileScoped), Is.True,
                        "no sample declares a block namespace");
        });
    }

    /// <summary>
    /// <para>Every way a declaration can be marked, every operator, every kind of literal and
    /// every receiver appears somewhere in the corpus.</para>
    /// <para>What the node-kind check above cannot see. A modifier is a flag on a declaration
    /// and an operator is a field on a node, so <c>sealed</c>, <c>shiftleft</c> and a binary
    /// literal each leave a tree of exactly the same shape as the thing beside them — and a
    /// construct the grammar has, the compiler implements, and no program ever writes is one
    /// nothing runs and nobody learns exists.</para>
    /// <para>Held per dimension rather than as one list, so a failure names which kind of thing
    /// went unwritten instead of only that something did.</para>
    /// </summary>
    [Test]
    public void Corpus_WritesEveryModifierOperatorLiteralAndReceiver()
    {
        HashSet<DeclarationModifiers> modifiers = [];
        HashSet<UnaryOperator> unary = [];
        HashSet<BinaryOperator> binary = [];
        HashSet<LiteralKind> literals = [];
        HashSet<ReceiverKind> receivers = [];

        foreach (string name in SampleNames)
        {
            foreach (SyntaxNode node in ParseSample(name).Unit.Descendants())
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
                $"{what} no sample writes");
    }

    // ---- Golden trees ---------------------------------------------------------------------

    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("PROFIC_UPDATE_GOLDEN") == "1";

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_MatchesItsRecordedTree(string name)
    {
        (_, CompilationUnit unit, _) = ParseSample(name);

        string actual = AstPrinter.Print(unit).ReplaceLineEndings("\n");
        string goldenPath = Path.Combine(GoldenDirectory, Path.ChangeExtension(name, ".ast"));

        if (UpdateRequested || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);

            if (!UpdateRequested)
            {
                Assert.Fail($"No golden tree for {name}; one was written to {goldenPath}.");
            }

            return;
        }

        string expected = File.ReadAllText(goldenPath).ReplaceLineEndings("\n");

        Assert.That(actual, Is.EqualTo(expected),
                    $"the tree for {name} changed; re-run with PROFIC_UPDATE_GOLDEN=1 if intended");
    }
}
