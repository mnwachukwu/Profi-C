using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Documentation;

/// <summary>
/// <para>Matches each documentation comment to what it documents, and holds it to that.</para>
/// <para>Documentation drifts from code silently and constantly: a parameter is renamed, and
/// the line describing it is not. Nothing else would ever notice, which is the same argument
/// the test suite makes about the specification, applied where a reader will meet it.</para>
/// <para>A missing doc is never reported. Requiring one everywhere is how documentation stops
/// being a help and becomes a tax, and the language has no interest in that.</para>
/// <para>The walk is over syntax alone — a parameter list and a written return type are all it
/// needs — so this runs without symbols and cannot be confused by a name it fails to resolve.
/// </para>
/// </summary>
public static class DocumentationChecker
{
    /// <summary>Checks every documentation comment in a file.</summary>
    public static void Check(CompilationUnit unit, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (unit.Documentation.Count == 0)
        {
            return;
        }

        using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

        Dictionary<int, Declaration> eligible = [];

        foreach (Declaration declaration in Eligible(unit.Declarations).Concat(Locals(unit)))
        {
            eligible.TryAdd(declaration.Span.Start.Line, declaration);
        }

        foreach (DocComment comment in unit.Documentation)
        {
            if (!eligible.TryGetValue(comment.Documents, out Declaration? documented))
            {
                diagnostics.Report(DiagnosticDescriptors.DocumentsNothing, comment.Span);
                continue;
            }

            CheckRepeats(comment, diagnostics);

            if (documented is FunctionDecl function)
            {
                CheckAgainst(comment, function, diagnostics);
            }
        }
    }

    /// <summary>
    /// <para>Everything that can carry documentation: a namespace, a type, a member of one,
    /// and an enumeration's members. A local, a parameter, and a statement cannot, which is
    /// what makes a comment above one of those report rather than quietly do nothing.</para>
    /// <para>A namespace is included because it is a name a reader meets and can ask about,
    /// even though nothing is declared in it directly. Leaving it out would mean a file whose
    /// first declaration is a namespace could not carry a documented heading at all.</para>
    /// </summary>
    private static IEnumerable<Declaration> Eligible(IEnumerable<Declaration> declarations)
    {
        foreach (Declaration declaration in declarations)
        {
            switch (declaration)
            {
                case NamespaceDecl inside:
                    yield return inside;

                    foreach (Declaration nested in Eligible(inside.Declarations))
                    {
                        yield return nested;
                    }

                    break;

                case ModelDecl model:
                    yield return model;

                    foreach (Declaration member in Eligible(model.Members))
                    {
                        yield return member;
                    }

                    break;

                case StructureDecl structure:
                    yield return structure;

                    foreach (Declaration member in Eligible(structure.Members))
                    {
                        yield return member;
                    }

                    break;

                case EnumerationDecl enumeration:
                    yield return enumeration;

                    foreach (EnumMemberDecl member in enumeration.Members)
                    {
                        yield return member;
                    }

                    break;

                case FieldDecl or FunctionDecl:
                    yield return declaration;
                    break;
            }
        }
    }

    /// <summary>
    /// <para>Every local declared anywhere in the file.</para>
    /// <para>A local carries documentation as readily as a member does. Nothing outside the
    /// function can see it, so nothing will complete on it — but a reader inside that function
    /// asks what a name holds far more often than they ask about anything else, and that is
    /// the question a hover answers.</para>
    /// <para>Found by walking the whole tree rather than by threading through the structured
    /// walk above, since a local may be nested inside any number of blocks, loops and branches
    /// and every one of those is just another statement holding statements.</para>
    /// </summary>
    private static IEnumerable<Declaration> Locals(SyntaxNode node)
    {
        foreach (SyntaxNode child in node.Children)
        {
            if (child is LocalDeclStmt local)
            {
                yield return local.Declaration;
            }

            foreach (Declaration nested in Locals(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>A label written more than once, whichever kind it is.</summary>
    private static void CheckRepeats(DocComment comment, DiagnosticBag diagnostics)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (DocLabel label in comment.Labels)
        {
            if (!seen.Add(label.Name))
            {
                diagnostics.Report(
                    DiagnosticDescriptors.DocumentsTheSameThingTwice, label.Span, label.Name);
            }
        }
    }

    /// <summary>What a function's documentation claims, against what the function is.</summary>
    private static void CheckAgainst(
        DocComment comment,
        FunctionDecl function,
        DiagnosticBag diagnostics)
    {
        HashSet<string> parameters =
            [.. function.Parameters.Select(p => p.Name)];

        foreach (DocLabel label in comment.Parameters.Where(l => !parameters.Contains(l.Name)))
        {
            diagnostics.Report(
                DiagnosticDescriptors.DocumentsUnknownParameter,
                label.Span,
                label.Name,
                function.Name,
                Written(function));
        }

        if (function.ReturnType is null
            && comment.Labels.Any(l => l.Name == DocComment.Yields))
        {
            diagnostics.Report(
                DiagnosticDescriptors.DocumentsNothingYielded,
                comment.Labels.First(l => l.Name == DocComment.Yields).Span,
                function.Name);
        }
    }

    /// <summary>
    /// The parameters a function takes, written out for the message. Naming them is what makes
    /// the report actionable: a reader who mistyped one sees the right spelling beside it.
    /// </summary>
    private static string Written(FunctionDecl function) =>
        function.Parameters.Count == 0
            ? "none"
            : string.Join(", ", function.Parameters.Select(p => $"'{p.Name}'"));
}
