using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Lexing;
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
        IReadOnlyList<CompilationUnit> units,
        CompilationUnit unit,
        SemanticModel model,
        SourceText source,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(units);
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

        string written = $"```profi-c\n{said}\n```";

        // Where it came from, which a bare name cannot say. Two models called Circle are told
        // apart by where they were declared and by nothing else, and the reader is looking at the
        // half of that which is not written down.
        if (Whence(model, node) is { Length: > 0 } from)
        {
            written += $"\n\n*{from}*";
        }

        // Under the signature rather than beside it, so the shape a reader came for is the first
        // thing on screen and the prose is there for whoever reads on.
        if (Documented(units, model, node) is { Length: > 0 } summary)
        {
            written += $"\n\n{summary}";
        }

        return new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["kind"] = "markdown",
                ["value"] = written,
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
        // A member the language provides is not a symbol, so without this it falls through to the
        // type of the expression around it — and every call to something that yields nothing then
        // describes itself as nothing, which is true of the call and says nothing about the
        // member.
        if (model.GetBuiltIn(node) is { } provided && BuiltIns.Find(provided) is { } member)
        {
            return Written(member);
        }

        if (model.GetSymbol(node) is { } symbol)
        {
            return Said(symbol);
        }

        return model.GetType(node)?.ToString();
    }

    /// <summary>
    /// <para>The full name of what is under the cursor, or nothing for something that has no
    /// such name.</para>
    /// <para>A local, a parameter and a loop's variable are reached from one place and named from
    /// nowhere else, so qualifying them would add a line that says only where the reader already
    /// is. A type, a member, and anything the language provides all sit somewhere a program could
    /// have to write out, and that is the thing worth saying.</para>
    /// </summary>
    private static string? Whence(SemanticModel model, SyntaxNode node)
    {
        if (model.GetBuiltIn(node) is { } provided && BuiltIns.Find(provided) is not null)
        {
            return Providing(provided);
        }

        if (node is TypeSyntax && model.GetType(node) is DeclaredTypeSymbol named)
        {
            return named.Within;
        }

        return model.GetSymbol(node) switch
        {
            DeclaredTypeSymbol type => type.Within,
            FunctionSymbol { DeclaringType: { } owner } => owner.QualifiedName,
            FieldSymbol { DeclaringType: { } owner } => owner.QualifiedName,
            EnumMemberSymbol member => member.Owner.QualifiedName,
            _ => null,
        };
    }

    /// <summary>
    /// Which model a member the language provides is reached through. Worked out from the catalog
    /// rather than from a symbol, because a provided member has none — and the model's own
    /// namespace is what makes <c>Standard</c> appear in front of it.
    /// </summary>
    private static string? Providing(BuiltInId id)
    {
        foreach (BuiltInModelInfo model in BuiltIns.Models)
        {
            foreach (BuiltInMember member in model.Members.Concat(model.Constructors))
            {
                if (member.Id == id)
                {
                    return $"{model.Namespace}.{model.Name}";
                }
            }
        }

        // Answered on a value rather than through a name — what every string, set or optional
        // knows. There is no model to qualify it with, and the type it is answered on is already
        // on the line above.
        return null;
    }

    /// <summary>
    /// <para>A member the language provides, written the way a program would declare one.</para>
    /// <para>Nothing in front of the name where it yields nothing, which is how the language
    /// writes it: a function with no result has no type before <c>function</c>, so a word there
    /// is a word no program would put there.</para>
    /// </summary>
    private static string Written(BuiltInMember member)
    {
        string yields = member.ReturnType is { } returned ? $"{returned} " : string.Empty;

        if (member.IsValue)
        {
            return $"{yields}{member.Name}";
        }

        string takes = string.Join(
            ", ", member.ParameterTypes.Select(p => p?.ToString() ?? "anything"));

        return $"{yields}function {member.Name}({takes})";
    }

    /// <summary>
    /// <para>What is written about the thing under the cursor, from whichever of the two places
    /// says it.</para>
    /// <para><b>A member the language provides and one a program declared are documented in
    /// different places, for a reason that is not going away.</b> A program writes a
    /// <c>@summary:</c> above a declaration, and the compiler reads it out of the file. Nothing
    /// writes one above <c>Console.WriteLine</c>, because nobody declares it — what it is for is
    /// recorded in the compiler beside its shape.</para>
    /// <para>The file is scanned again to find a documentation comment, since the tree does not
    /// carry them: they belong to no node, they attach to whatever line follows, and every pass
    /// after the scanner has no use for them. Scanning is the cheap half of the front end and
    /// this is asked once, when somebody rests a pointer somewhere.</para>
    /// </summary>
    private static string? Documented(
        IReadOnlyList<CompilationUnit> units, SemanticModel model, SyntaxNode node)
    {
        if (model.GetBuiltIn(node) is { } provided)
        {
            return BuiltInDocs.Summary(provided);
        }

        // A type the language provides, named rather than keyed by an id: nothing resolves to a
        // type the way a call resolves to a member, so its name is what there is to go on.
        //
        // Asked of the syntax rather than of whatever the expression came to. A type written on
        // the left of a declaration, or after 'as', is a type and nothing else — while the type
        // of an ordinary expression is a fact about the value, and answering with the type's line
        // would tell somebody hovering a local what Random is for.
        if (node is TypeSyntax && model.GetType(node) is { Declaration: null } named)
        {
            return BuiltInDocs.SummaryOf(named.Name);
        }

        if (model.GetSymbol(node) is not { } symbol)
        {
            return null;
        }

        if (symbol is TypeSymbol && symbol.Declaration is null)
        {
            return BuiltInDocs.SummaryOf(symbol.Name);
        }

        if (symbol.Declaration is not { } declared || Holding(units, declared) is not { } where)
        {
            return null;
        }

        DiagnosticBag aside = new();
        Lexer scanner = new(where.Source, aside);

        _ = scanner.Scan();

        // A parameter is documented on the function that takes it, not above itself — so what is
        // looked for is the comment on the enclosing declaration, and the label naming this
        // parameter within it. Read as a declaration of its own it finds the function's comment
        // and answers with what the whole function is for, which is true of the function and
        // says nothing about the parameter.
        if (symbol is ParameterSymbol)
        {
            return scanner.Documentation
                .SelectMany(d => d.Parameters)
                .FirstOrDefault(label => string.Equals(
                    label.Name, symbol.Name, StringComparison.Ordinal))
                ?.Text;
        }

        if (scanner.Documentation.FirstOrDefault(d => d.Documents == declared.Span.Start.Line)
            is not { } written)
        {
            return null;
        }

        // What it yields and what it raises are part of what a reader came to find out, and both
        // are written where the summary is. Left out, hovering a function that documents a thrown
        // exception says nothing about it and looks as though nothing was written.
        string said = written.Summary;

        foreach (DocLabel label in written.Labels)
        {
            if (label.Name is DocComment.Yields or DocComment.Throws)
            {
                said += $"\n\n**{Capitalized(label.Name)}:** {label.Text}";
            }
        }

        return said;
    }

    private static string Capitalized(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

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
