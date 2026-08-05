using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILVerify;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Emitting;

/// <summary>
/// <para>Every emitted assembly is valid CIL, checked by the runtime's own verifier.</para>
/// <para><b>Running a program does not establish this.</b>
/// <see cref="CorpusAgreementTests"/> compares what the two engines print, so it only ever
/// exercises the instructions a sample happens to reach, and only proves they did not crash on
/// this machine today. An unbalanced stack on a branch nothing takes, a local read as the wrong
/// type, a <c>ret</c> leaving something behind — all of those print the right answer until the
/// jit changes its mind about them.</para>
/// <para>It also says what went wrong. A method the jit refuses arrives as
/// <c>InvalidProgramException</c> and nothing else — no method, no offset, no reason — so
/// finding the instruction at fault means bisecting the emitter. The verifier names the method
/// and the rule it broke.</para>
/// <para>Run as a test rather than a step in CI, so it answers on the machine that produced the
/// assembly rather than only on a runner.</para>
/// </summary>
[TestFixture]
public sealed class IlVerificationTests : LexerTestBase
{
    private static IEnumerable<string> Programs =>
        ProfiC.Tests.Interpreting.SampleProgramTests.RunnableSampleNames;

    /// <summary>
    /// <para>The programs made of several files, entered the way a reader enters them.</para>
    /// <para>Read off the same discovery the running fixture uses rather than listed here, so a
    /// folder added to the corpus is verified from the moment it exists. Listed, a fifth one
    /// would run and never reach the back end, which is the failure this fixture is for.</para>
    /// </summary>
    private static IEnumerable<string> MultiFilePrograms =>
        Interpreting.MultiFileSampleTests.EntryPoints.Select(
            entry => entry.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// <para>The compilations in the corpus that are not programs, entered the way a reader
    /// would enter them.</para>
    /// <para>Both declare no <c>Program</c>, so neither runs and neither is reached by any
    /// fixture that starts from one. <c>books</c> exists to be referenced, which is what a
    /// library is; the reference folder exists to be read, and putting it through the emitter is
    /// the only thing that asks whether every <em>declaration</em> form in the grammar can be
    /// laid out — a different question from whether every construct has been run, and the one
    /// that went unasked while nested types crashed the emitter.</para>
    /// <para>Written out rather than discovered, and safe to write out because
    /// <see cref="EverySampleFileIsReachedByABuild"/> fails naming any file that no build here
    /// gathers.</para>
    /// </summary>
    private static IEnumerable<string> Libraries =>
    [
        Path.Combine("reference", "tour.pc"),
        Path.Combine("library", "books", "books.pcp"),
    ];

    [TestCaseSource(nameof(Programs))]
    public void Sample_EmitsVerifiableCil(string name) =>
        AssertVerifies(name, () => FrontEndOf(name));

    [TestCaseSource(nameof(MultiFilePrograms))]
    public void MultiFileSample_EmitsVerifiableCil(string entry) =>
        AssertVerifies(entry, () => FrontEndOfProgramAt(entry, requireEntryPoint: true));

    [TestCaseSource(nameof(Libraries))]
    public void Library_EmitsVerifiableCil(string entry) =>
        AssertVerifies(entry, () => FrontEndOfProgramAt(entry, requireEntryPoint: false));

    /// <summary>
    /// <para>Every sample file is part of something the back end is asked to build.</para>
    /// <para>The ratchet under the three lists above. A file reached by no build is one the whole
    /// emitter half of this suite is blind to — which is exactly what the reference corpus was
    /// for as long as it existed, and how a nested type came to crash <c>pc build</c> while
    /// several thousand tests passed.</para>
    /// <para>The negatives are left out, since what they hold is mistakes and a file that will
    /// not check has nothing to emit.</para>
    /// </summary>
    [Test]
    public void EverySampleFileIsReachedByABuild()
    {
        HashSet<string> reached = new(ProfiC.Cli.SourceDiscovery.PathComparer);

        foreach (string entry in Programs.Select(name => Path.Combine("samples", name))
                                         .Concat(MultiFilePrograms.Concat(Libraries)
                                             .Select(e => Path.Combine("samples", e))))
        {
            DiagnosticBag diagnostics = new();
            string path = Path.Combine(RepositoryRoot, entry);

            if (ProfiC.Cli.SourceDiscovery.Gather(path, diagnostics) is not { } gathered)
            {
                continue;
            }

            reached.UnionWith(gathered.Units.Select(unit => Path.GetFullPath(unit.Source.FileName)));
        }

        Assert.That(
            EverySampleFile.Where(file => !reached.Contains(Path.GetFullPath(file)))
                           .Select(file => Path.GetRelativePath(RepositoryRoot, file))
                           .Order(StringComparer.Ordinal),
            Is.Empty,
            "sample files no build in this fixture reaches, so nothing ever emits them");
    }

    private static void AssertVerifies(
        string name, Func<(IReadOnlyList<CompilationUnit> Units, SemanticModel Model)> frontEnd)
    {
        (IReadOnlyList<CompilationUnit> units, SemanticModel model) = frontEnd();

        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-ilverify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            string assembly = Path.Combine(folder, "Emitted.dll");

            CilEmitter.Emit(
                ClosureConversion.Convert(Lowering.Lower(units, model), model),
                model,
                "Emitted",
                assembly);

            Assert.That(
                Verify(assembly),
                Is.Empty,
                $"{name} emits CIL the runtime's own verifier rejects");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model) FrontEndOf(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        AssertCompiles(name, diagnostics);
        return ([unit], model);
    }

    /// <summary>
    /// Gathered through discovery rather than parsed here, so that what is verified is what a
    /// <c>pc build</c> of the same path would produce, folder rules and project file and all.
    /// </summary>
    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model) FrontEndOfProgramAt(
        string entry, bool requireEntryPoint)
    {
        DiagnosticBag diagnostics = new();
        string path = Path.Combine(RepositoryRoot, "samples", entry);

        ProfiC.Cli.SourceDiscovery.Compilation compilation =
            ProfiC.Cli.SourceDiscovery.Gather(path, diagnostics)!;

        Assert.That(compilation, Is.Not.Null, $"{entry} was not gathered");

        // Carried through as a build carries it. A project naming which of its programs begins
        // is compiled as though it had not when this is left out.
        SemanticModel model = Resolver.Resolve(
            compilation.Units,
            diagnostics,
            requireEntryPoint,
            projects: compilation.Projects,
            entryPoint: compilation.EntryPoint);

        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        AssertCompiles(entry, diagnostics);
        return (compilation.Units, model);
    }

    private static void AssertCompiles(string name, DiagnosticBag diagnostics) =>
        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                       .Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            $"{name} should compile before its CIL is verified");

    /// <summary>
    /// <para>Everything the verifier objected to, each named the way a reader would look it
    /// up: the type and method it is in, and the message.</para>
    /// <para>The handles a result carries are metadata rows rather than names, so the names are
    /// read back out of the same file. Without that a failure says only which sample, and the
    /// sample is the one thing already in the test's name.</para>
    /// </summary>
    private static IReadOnlyList<string> Verify(string path)
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;

        // Guarded rather than assumed. Without the system module the verifier has nothing to
        // check a type against, and what it reports then is a handful of unresolved-reference
        // complaints — which read as the emitter's fault and are not.
        Assert.That(
            File.Exists(Path.Combine(runtime, "System.Private.CoreLib.dll")),
            Is.True,
            $"no System.Private.CoreLib beside the running runtime ('{runtime}'), so nothing "
            + "here would be verified against anything");

        using SearchPathResolver resolver = new(runtime, AppContext.BaseDirectory, Path.GetDirectoryName(path)!);

        Verifier verifier = new(resolver, new VerifierOptions { IncludeMetadataTokensInErrorMessages = true });
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Private.CoreLib"));

        using FileStream file = File.OpenRead(path);
        using PEReader assembly = new(file);

        MetadataReader metadata = assembly.GetMetadataReader();

        return
        [
            .. verifier.Verify(assembly)
                       .Select(result => $"{Where(metadata, result)}: {result.Code}: {result.Message}"),
        ];
    }

    private static string Where(MetadataReader metadata, VerificationResult result)
    {
        if (result.Method.IsNil)
        {
            return result.Type.IsNil
                ? "the assembly"
                : metadata.GetString(metadata.GetTypeDefinition(result.Type).Name);
        }

        MethodDefinition method = metadata.GetMethodDefinition(result.Method);
        TypeDefinition declaring = metadata.GetTypeDefinition(method.GetDeclaringType());

        return $"{metadata.GetString(declaring.Name)}.{metadata.GetString(method.Name)}";
    }

    /// <summary>
    /// <para>Finds the assemblies an emitted one refers to, by looking beside the things that
    /// produced it.</para>
    /// <para>Three folders, and each is there for a reason the others do not cover: the running
    /// runtime holds <c>System.Private.CoreLib</c> and the framework, the test's own output
    /// holds <c>ProfiC.Runtime</c>, and the folder being verified holds the assembly
    /// itself.</para>
    /// </summary>
    private sealed class SearchPathResolver(params string[] folders) : IResolver, IDisposable
    {
        private readonly Dictionary<string, PEReader?> _opened = new(StringComparer.OrdinalIgnoreCase);

        public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName) => Open(assemblyName.Name!);

        public PEReader? ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) =>
            Open(Path.GetFileNameWithoutExtension(fileName));

        private PEReader? Open(string name)
        {
            if (_opened.TryGetValue(name, out PEReader? found))
            {
                return found;
            }

            string? path = folders
                .Select(folder => Path.Combine(folder, name + ".dll"))
                .FirstOrDefault(File.Exists);

            PEReader? reader = path is null ? null : new PEReader(File.OpenRead(path));

            _opened[name] = reader;
            return reader;
        }

        public void Dispose()
        {
            foreach (PEReader? reader in _opened.Values)
            {
                reader?.Dispose();
            }

            _opened.Clear();
        }
    }
}
