using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Services;

namespace ProfiC.Cli;

/// <summary>
/// <para>The program around a file, on a machine that has somewhere to look for one.</para>
/// <para>This is the half of an editor's questions that the language services cannot answer for
/// themselves. Which files a program is made of is a question about a disk — a folder, or a
/// project listing files across several — and the services are written to be asked the same
/// questions in a browser, where there is no disk and the program is the one buffer on the page.
/// </para>
/// </summary>
public static class Gathering
{
    /// <summary>
    /// <para>Gathers the program a file belongs to and takes it through the front end, with the
    /// given text standing in for what is stored.</para>
    /// <para>The whole program rather than the one file: a name in scope may be a type declared
    /// next door, and a compilation of one file would not have it.</para>
    /// <para>Checked and not merely resolved, because the questions asked of this are about types
    /// — a local's, and what a function yields — and those are the checker's answers. The passes
    /// after it are left out: nothing an editor asks depends on definite assignment or on what is
    /// unused, and a file being typed in trips both constantly.</para>
    /// </summary>
    public static Around? Around(
        string path, SourceText text, CancellationToken cancellation) =>
        Around(path, text, SourceDiscovery.FromDisk, cancellation);

    /// <summary>
    /// The same, where the file's neighbors come from somewhere other than the disk — a language
    /// server's store of what is open, which holds edits nothing has saved yet.
    /// </summary>
    public static Around? Around(
        string path, SourceText text, SourceReader read, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(read);

        DiagnosticBag aside = new();

        SourceReader instead = asked =>
            SourceDiscovery.PathComparer.Equals(Path.GetFullPath(asked), Path.GetFullPath(path))
                ? text
                : read(asked);

        if (SourceDiscovery.Gather(path, aside, instead) is not { } compilation)
        {
            return null;
        }

        // Found by path, the way every other question here finds it. By reference it depends on
        // the text handed to the reader being the very object that comes back on the unit, which
        // holds when the reader is asked for exactly the path it was given and not otherwise —
        // and a file reached a second way is then a unit that looks unrelated to the one asked
        // about.
        CompilationUnit? unit = compilation.Units.FirstOrDefault(
            u => SourceDiscovery.PathComparer.Equals(
                Path.GetFullPath(u.Source.FileName), Path.GetFullPath(path)));

        if (unit is null)
        {
            return null;
        }

        SemanticModel model = Resolver.Resolve(
            compilation.Units,
            aside,
            projects: compilation.Projects,
            entryPoint: compilation.EntryPoint,
            cancellation: cancellation);

        TypeChecker.Check(compilation.Units, model, aside, cancellation);

        return new Around(model, unit);
    }

    /// <summary>
    /// Reading the neighbors from a store, as a <see cref="Surrounding"/> to hand the services.
    /// </summary>
    public static Surrounding Through(SourceReader read) =>
        (path, text, cancellation) => Around(path, text, read, cancellation);
}
