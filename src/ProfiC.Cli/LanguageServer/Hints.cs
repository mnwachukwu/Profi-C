using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>The type a name was given, written in beside it where the program does not say.</para>
/// <para><b>Profi-C writes its types down, and <c>let</c> is the one place it does not.</b> That
/// makes this narrower here than in a language where inference is everywhere, and more worth
/// doing: there is exactly one construct whose type a reader cannot see, so filling it in
/// completes the page rather than decorating it. A student writing <c>let total = 1|3 + 1|6</c>
/// and reading <c>fraction</c> appear has been taught something, and was not going to look it
/// up.</para>
/// <para>The loop bindings are the same case. <c>loop for i</c> and <c>loop each item in items</c>
/// declare a name with no type on it, and where the element type came from is a real question
/// about somebody else's code.</para>
/// <para><b>Nothing is shown where the program already says it.</b> A hint beside a written type
/// is the editor reading the line back, which is noise wherever it appears and is worst in the
/// place a reader is most likely to be looking.</para>
/// </summary>
public static class Hints
{
    /// <summary>Type, in the protocol's numbering.</summary>
    private const int OfAType = 1;

    /// <summary>A parameter's name, written in front of what is being passed to it.</summary>
    private const int OfAParameter = 2;

    /// <summary>
    /// <para>Which hints somebody asked for.</para>
    /// <para><b>Both are off until asked for.</b> A hint is text the editor writes into code
    /// nobody wrote, and a reader who has not asked for that should not have to work out where it
    /// came from — least of all a beginner, who has no way to tell the language's own syntax from
    /// their editor's opinion about it.</para>
    /// <para>Off by default is not the same as absent. Types are worth turning on while learning
    /// what the checker concludes, and parameter names while reading unfamiliar code with
    /// four-argument calls in it. The reason to keep both off is that most reading is neither.
    /// </para>
    /// </summary>
    public sealed record Wants(bool Types, bool ParameterNames)
    {
        public static Wants Default { get; } = new(Types: false, ParameterNames: false);

        /// <summary>Whether there is nothing to look for, so the file need not be read at all.</summary>
        public bool Nothing => !Types && !ParameterNames;
    }

    /// <summary>
    /// What the editor's settings ask for, falling back to the default for anything they leave
    /// out — which is everything, for a client that sends no settings at all.
    /// </summary>
    public static Wants Wanted(JsonObject? settings)
    {
        JsonNode? asked = settings?["profi-c"]?["inlayHints"] ?? settings?["inlayHints"];

        return new Wants(
            (bool?)asked?["types"] ?? Wants.Default.Types,
            (bool?)asked?["parameterNames"] ?? Wants.Default.ParameterNames);
    }

    /// <summary>
    /// <para>Every hint in a stretch of a file.</para>
    /// <para>A stretch rather than the file, because an editor asks about what is on screen and
    /// asks again as it scrolls. Answering about the whole file would be correct and would do the
    /// work over on every scroll of a long one.</para>
    /// </summary>
    public static JsonArray In(
        CompilationUnit unit,
        SemanticModel model,
        SourceText source,
        int from,
        int to,
        Wants wants)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(wants);

        JsonArray hints = [];

        foreach (SyntaxNode node in Everything(unit))
        {
            if (node.Span.Start.Offset < from || node.Span.Start.Offset > to)
            {
                continue;
            }

            if (wants.Types && Inferred(node, model) is var (where, type) && !type.IsError)
            {
                // Against the name rather than floating after it, the way a written type sits.
                hints.Add(Written(source, where, $": {type.Display}", OfAType, ahead: false));
            }

            if (wants.ParameterNames)
            {
                foreach ((int at, string name) in Passed(node, model))
                {
                    hints.Add(Written(source, at, $"{name}:", OfAParameter, ahead: true));
                }
            }
        }

        return hints;
    }

    private static JsonObject Written(
        SourceText source, int at, string label, int kind, bool ahead) => new()
    {
        ["position"] = Conversions.PositionOf(source.PositionAt(at)),
        ["label"] = label,
        ["kind"] = kind,
        ["paddingLeft"] = false,
        ["paddingRight"] = ahead,
    };

    /// <summary>
    /// <para>The parameter each argument of a call is being passed to.</para>
    /// <para>Only where a program declared the function, since that is where a parameter has a
    /// name to show — a member the language provides is a catalog entry with types and no names,
    /// and inventing them would be inventing them.</para>
    /// <para>Left off where the argument is already the parameter's own name. <c>Show(label)</c>
    /// annotated as <c>Show(label: label)</c> is the editor reading the line back.</para>
    /// </summary>
    private static IEnumerable<(int At, string Name)> Passed(SyntaxNode node, SemanticModel model)
    {
        IReadOnlyList<Expression> arguments = node switch
        {
            CallExpr call => call.Arguments,
            NewExpr construction => construction.Arguments,
            _ => [],
        };

        for (int at = 0; at < arguments.Count; at++)
        {
            Expression argument = arguments[at];

            if (argument.Span.Length == 0
                || Called.Of(node, model, argument.Span.Start.Offset) is not { Parameter: { } named })
            {
                continue;
            }

            if (argument is IdentifierExpr written
                && string.Equals(written.Name, named.Name, StringComparison.Ordinal))
            {
                continue;
            }

            yield return (argument.Span.Start.Offset, named.Name);
        }
    }

    /// <summary>
    /// <para>Where a hint goes and what it says, or null for a node that says its own type.</para>
    /// <para>Each of these declares a name the program gave no type to, and each is answered from
    /// the symbol the resolver made rather than from the initializer beside it — the type of a
    /// <c>let</c> is what the checker concluded, which is not always the type of the expression as
    /// written.</para>
    /// </summary>
    private static (int At, TypeSymbol Type)? Inferred(SyntaxNode node, SemanticModel model) =>
        node switch
        {
            // Only the 'let' form. A declaration with a type on it has its answer already.
            VarDeclStmt { IsInferred: true, HasName: true } declared
                when model.GetSymbol(declared) is LocalSymbol local =>
                (declared.NameSpan.EndOffset, local.Type),

            ForStmt { HasName: true } loop when model.GetSymbol(loop) is LocalSymbol counter =>
                (loop.NameSpan.EndOffset, counter.Type),

            ForEachStmt { HasName: true } walk when model.GetSymbol(walk) is LocalSymbol item =>
                (walk.NameSpan.EndOffset, item.Type),

            _ => null,
        };

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
