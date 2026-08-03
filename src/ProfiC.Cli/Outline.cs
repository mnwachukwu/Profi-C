using System.Text.Json;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli;

/// <summary>
/// <para>What a file declares, as a tree, for an editor to show.</para>
/// <para>Written for the same reason <c>vocabulary</c> and <c>platforms</c> are: the answer is
/// the compiler's, and anything else that wanted it would have to parse Profi-C a second time.
/// A second parser is a second definition of the language, and the two would agree until the
/// day they did not — which for an outline shows up as a member that quietly stops appearing.
/// </para>
/// <para>Built from the parse alone, with nothing resolved. That is deliberate: an outline is
/// wanted most while a file is being written, which is exactly when it does not yet compile.
/// The parser recovers, so a file with a mistake in it still yields the shape around the
/// mistake.</para>
/// </summary>
public static class Outline
{
    /// <summary>
    /// <para>One declaration, and whatever it declares inside itself.</para>
    /// <para><see cref="NameLine"/> and <see cref="NameColumn"/> are where the name is written,
    /// which is not where the declaration begins: a function starts at its first modifier and is
    /// named several words later. An editor revealing an entry wants the name, so that clicking
    /// one in the Outline puts the cursor on it rather than on the word <c>public</c>.</para>
    /// </summary>
    public sealed record Entry(
        string Name,
        string Kind,
        string Detail,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
        int NameLine,
        int NameColumn,
        IReadOnlyList<Entry> Children);

    /// <summary>
    /// <para>The declarations of one unit, as a tree.</para>
    /// <para>Positions are one-based, as everything a reader sees in Profi-C is. Whatever counts
    /// from zero converts at its own boundary rather than being handed a convention that matches
    /// no diagnostic.</para>
    /// <para>Given apart from <see cref="AsJson"/> because two things want it and only one of
    /// them wants text: the <c>outline</c> command prints it, and the language server answers
    /// <c>textDocument/documentSymbol</c> from it. Serializing and reading back would be a
    /// second shape to keep true.</para>
    /// </summary>
    public static IReadOnlyList<Entry> Of(CompilationUnit unit, SourceText source)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(source);

        return Walk(unit.Declarations, source);
    }

    /// <summary>The same tree as text, for the command that prints it.</summary>
    public static string AsJson(CompilationUnit unit, SourceText source) =>
        JsonSerializer.Serialize(
            Of(unit, source),
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

    /// <summary>
    /// Walks a run of declarations. The name of whatever holds them is carried along, because a
    /// node does not know its parent and a constructor is only recognizable beside the model it
    /// belongs to.
    /// </summary>
    private static List<Entry> Walk(
        IEnumerable<Declaration> declarations,
        SourceText source,
        string? within = null) =>
        [.. declarations.Select(d => Describe(d, source, within)).OfType<Entry>()];

    private static Entry? Describe(
        Declaration declaration,
        SourceText source,
        string? within) => declaration switch
    {
        // The name as written, dots and all. A node's ToString describes the node.
        NamespaceDecl inner => Made(
            inner, source, inner.Name.Text, "namespace",
            string.Empty, Walk(inner.Declarations, source)),

        ModelDecl model => Made(
            model, source, model.Name, "model",
            Detail(model.Modifiers, model.BaseTypeName), Walk(model.Members, source, model.Name)),

        StructureDecl structure => Made(
            structure, source, structure.Name, "structure",
            Detail(structure.Modifiers, extends: null),
            Walk(structure.Members, source, structure.Name)),

        EnumerationDecl enumeration => Made(
            enumeration, source, enumeration.Name, "enumeration",
            Detail(enumeration.Modifiers, extends: null),
            Walk(enumeration.Members, source, enumeration.Name)),

        EnumMemberDecl member => Made(member, source, member.Name, "enumMember", string.Empty, []),

        FieldDecl field => Made(
            field, source, field.Name, "field", Detail(field.Modifiers, extends: null), []),

        // A constructor is a function named for the type that holds it. Told apart here rather
        // than left to the editor, which would have to know that rule to show the right icon.
        FunctionDecl function => Made(
            function,
            source,
            function.Name,
            string.Equals(function.Name, within, StringComparison.Ordinal)
                ? "constructor"
                : "function",
            Detail(function.Modifiers, extends: null),
            []),

        _ => null,
    };

    private static string Detail(DeclarationModifiers modifiers, string? extends)
    {
        string written = modifiers.ToDisplayString();

        return extends is null ? written : $"{written} extends {extends}".TrimStart();
    }

    /// <summary>
    /// <para>One entry, spanning the whole declaration.</para>
    /// <para>The end is worked out from the start and the length, because a span carries an
    /// offset rather than two positions — and an editor needs a line and a column at both ends
    /// to know what to highlight.</para>
    /// </summary>
    private static Entry Made(
        SyntaxNode node,
        SourceText source,
        string name,
        string kind,
        string detail,
        IReadOnlyList<Entry> children)
    {
        SourcePosition start = node.Span.Start;
        SourcePosition end = source.PositionAt(
            Math.Min(start.Offset + node.Span.Length, source.Text.Length));

        SourcePosition named = node.NameSpan.Start;

        return new Entry(
            name,
            kind,
            detail,
            start.Line,
            start.Column,
            end.Line,
            end.Column,
            named.Line,
            named.Column,
            children);
    }
}
