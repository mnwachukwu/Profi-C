using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>The questions an editor asks about a place in a file, answered from a compilation.</para>
/// <para>All three are the same shape: find the syntax at the cursor, ask the model what it
/// worked out about it, and write that down the way the protocol writes it. None of them
/// re-derives anything — the answers were all settled while checking, and a second opinion here
/// would be a second definition of the language.</para>
/// <para>Held apart from the server so that what is asked and what is answered can be tested
/// without a protocol in the way.</para>
/// </summary>
public static class Answers
{
    /// <summary>
    /// <para>What a file declares, as a tree the editor shows in breadcrumbs and the Outline.
    /// </para>
    /// <para>Built from the parse alone, with nothing resolved, which is deliberate: an outline
    /// is wanted most while a file is being written, and that is exactly when it does not
    /// compile. The parser recovers, so the shape around a mistake still arrives.</para>
    /// </summary>
    public static JsonArray Symbols(CompilationUnit unit, SourceText source)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return [.. Outline.Of(unit, source).Select(AsSymbol)];
    }

    private static JsonNode AsSymbol(Outline.Entry entry)
    {
        JsonObject range = new()
        {
            ["start"] = new JsonObject
            {
                ["line"] = Math.Max(0, entry.Line - 1),
                ["character"] = Math.Max(0, entry.Column - 1),
            },
            ["end"] = new JsonObject
            {
                ["line"] = Math.Max(0, entry.EndLine - 1),
                ["character"] = Math.Max(0, entry.EndColumn - 1),
            },
        };

        return new JsonObject
        {
            ["name"] = entry.Name,
            ["detail"] = entry.Detail,
            ["kind"] = Conversions.SymbolKindOf(entry.Kind),
            ["range"] = range,

            // What is revealed and selected when the entry is clicked: the name, so that
            // clicking a function in the Outline puts the cursor on its name rather than on the
            // word 'public'. The protocol requires this to sit inside the range above, which the
            // name does by construction.
            ["selectionRange"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = Math.Max(0, entry.NameLine - 1),
                    ["character"] = Math.Max(0, entry.NameColumn - 1),
                },
                ["end"] = new JsonObject
                {
                    ["line"] = Math.Max(0, entry.NameLine - 1),
                    ["character"] = Math.Max(0, entry.NameColumn - 1 + entry.Name.Length),
                },
            },
            ["children"] = new JsonArray([.. entry.Children.Select(AsSymbol)]),
        };
    }

    /// <summary>
    /// <para>What is under the cursor, as a line of Profi-C rather than a paragraph about it.
    /// </para>
    /// <para>A type where the thing has one, the declaration where it is a declaration, and
    /// nothing at all where the model recorded nothing — an operator, a comment, a piece of
    /// punctuation. Answering "unknown" for those would put a tooltip over every character in
    /// the file.</para>
    /// </summary>
    public static JsonObject? Hover(
        CompilationUnit unit, SemanticModel model, SourceText source, int offset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);

        if (NodeAt.Innermost(unit, offset) is not { } node)
        {
            return null;
        }

        if (Describe(model, node) is not { } said)
        {
            return null;
        }

        return new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["kind"] = "markdown",
                ["value"] = $"```profi-c\n{said}\n```",
            },
            // The name rather than the whole node, so hovering a local underlines the local and
            // not the declaration it sits in. A node with no name in it answers with itself,
            // which is what an expression wants.
            ["range"] = Conversions.RangeOf(node.NameSpan, source),
        };
    }

    /// <summary>
    /// <para>One line saying what a node is, or null where nothing was recorded about it.</para>
    /// <para>Walked outward from the innermost node, because the innermost is often a piece of
    /// syntax nothing was recorded for while what encloses it is the thing being asked about —
    /// a name inside a member access, a type inside a declaration.</para>
    /// </summary>
    private static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (model.GetSymbol(node) is { } symbol)
        {
            return Said(symbol);
        }

        return model.GetType(node)?.ToString();
    }

    /// <summary>What a symbol is, written the way a program would declare it.</summary>
    private static string Said(Symbol symbol) => symbol switch
    {
        LocalSymbol local => $"{local.Type} {local.Name}",
        FieldSymbol field => $"{field.Type} {field.Name}",
        ParameterSymbol parameter => $"{parameter.Type} {parameter.Name}",
        FunctionSymbol function => Signature(function),
        TypeSymbol type => type.ToString() ?? type.Name,
        _ => symbol.Name,
    };

    /// <summary>
    /// A function as it is written, which says more than its name: what it yields, what it
    /// takes, and the words in front of it that decide who may call it.
    /// </summary>
    private static string Signature(FunctionSymbol function)
    {
        string modifiers = function.Modifiers.ToDisplayString();
        string yields = function.ReturnType is { } returned ? $"{returned} " : string.Empty;
        string takes = string.Join(
            ", ", function.Parameters.Select(p => $"{p.Type} {p.Name}"));

        return $"{modifiers} {yields}function {function.Name}({takes})".TrimStart();
    }

    /// <summary>
    /// <para>Where the name under the cursor was declared, in whichever file that is.</para>
    /// <para>A symbol already carries the syntax that declared it — the resolver records it as
    /// it declares each one — so this asks what the name refers to and reads where that was
    /// written. No search, and no second answer about scope: the resolver settled which
    /// declaration a name reaches, and asking again here could only disagree with it.</para>
    /// <para>Which file that syntax is in is found by matching it against each unit's tree,
    /// because a node does not carry the file it came from. A program is a compilation, so the
    /// answer is often not the file being looked at.</para>
    /// </summary>
    public static JsonArray Definition(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        CompilationUnit asking,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(asking);

        JsonArray found = [];

        foreach (SyntaxNode node in NodeAt.NamesAt(asking, offset))
        {
            if (model.GetSymbol(node) is not { Declaration: { } declared })
            {
                continue;
            }

            if (Holding(units, declared) is { } unit)
            {
                found.Add(new JsonObject
                {
                    ["uri"] = Conversions.UriOf(unit.Source.FileName),

                    // The name, so that following one lands the cursor on it. Selecting the
                    // whole function instead would highlight fifteen lines to answer "where is
                    // this declared".
                    ["range"] = Conversions.RangeOf(declared.NameSpan, unit.Source),
                });
            }

            // The innermost name that refers to something is the answer. Carrying on outward
            // would land on whatever encloses it, which is where the cursor is rather than what
            // it is pointing at.
            break;
        }

        return found;
    }

    /// <summary>
    /// <para>The call the cursor is inside, and which argument it is at.</para>
    /// <para>Shown while the arguments are being typed, which is when somebody most wants to
    /// know what the function takes — and is exactly when they cannot see the declaration,
    /// because they are looking at the call.</para>
    /// <para>Null where the cursor is not inside a call at all. The editor keeps asking as each
    /// character is typed, so saying nothing has to be as cheap and as ordinary as saying
    /// something.</para>
    /// </summary>
    public static JsonObject? Signature(
        CompilationUnit unit, SemanticModel model, SourceText source, int offset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(source);

        if (NodeAt.Innermost<CallExpr>(unit, offset) is not { } call)
        {
            return null;
        }

        if (model.GetSymbol(call.Callee) is not FunctionSymbol function)
        {
            return null;
        }

        JsonArray parameters = [];

        foreach (ParameterSymbol parameter in function.Parameters)
        {
            parameters.Add(new JsonObject { ["label"] = $"{parameter.Type} {parameter.Name}" });
        }

        return new JsonObject
        {
            ["signatures"] = new JsonArray(new JsonObject
            {
                ["label"] = Signature(function),
                ["parameters"] = parameters,
            }),
            ["activeSignature"] = 0,
            ["activeParameter"] = Math.Min(
                At(call, offset), Math.Max(0, function.Parameters.Count - 1)),
        };
    }

    /// <summary>
    /// <para>Which argument the cursor is in, counted by the arguments that begin before it.
    /// </para>
    /// <para>Counted from the arguments rather than by looking for commas, because a comma inside
    /// a nested call or a string belongs to something else, and counting those would highlight
    /// the wrong parameter exactly where the code is hardest to read.</para>
    /// </summary>
    private static int At(CallExpr call, int offset)
    {
        int at = 0;

        foreach (Expression argument in call.Arguments)
        {
            if (offset > argument.Span.EndOffset)
            {
                at++;
            }
        }

        return at;
    }

    /// <summary>The file a piece of syntax came from, or null where none of these holds it.</summary>
    private static CompilationUnit? Holding(
        IReadOnlyList<CompilationUnit> units, SyntaxNode wanted) =>
        units.FirstOrDefault(unit => Everything(unit).Any(node => ReferenceEquals(node, wanted)));

    private static IEnumerable<SyntaxNode> Everything(SyntaxNode node)
    {
        yield return node;

        foreach (SyntaxNode child in node.Children)
        {
            foreach (SyntaxNode inside in Everything(child))
            {
                yield return inside;
            }
        }
    }
}
