using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Services;

/// <summary>
/// <para>A file, and what the front end worked out about the program it belongs to.</para>
/// <para>Both, because neither answers a question on its own: the model says what every name in
/// the program means, and the unit says which of them are in this file.</para>
/// </summary>
/// <param name="Model">What the resolver and the type checker made of the whole program.</param>
/// <param name="Unit">The file that was asked about, as it appears in that program.</param>
public sealed record Around(SemanticModel Model, CompilationUnit Unit);

/// <summary>
/// <para>Finds the program a file belongs to and takes it through the front end, with the text
/// being edited standing in for whatever is stored.</para>
/// <para><b>The one thing here that cannot be answered from a tree.</b> Everything else an editor
/// asks is a question about a program it has already been handed — where a name is declared, what
/// type it has, what could be written next. This is the question of which files the program is,
/// and that has a different answer everywhere: on a machine it is a folder or a project, read off
/// a disk; in a browser it is the one buffer on the page, and there is no disk to ask.</para>
/// <para>A delegate rather than something to implement, because that is all it is — one call, and
/// each caller already has the pieces.</para>
/// <para>Null where the file is not part of any program that could be gathered, which is not the
/// same as a program with mistakes in it. A file half-typed still compiles to something, and
/// every question here is answered about that something.</para>
/// </summary>
public delegate Around? Surrounding(string path, SourceText text, CancellationToken cancellation);
