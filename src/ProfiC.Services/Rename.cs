using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Services;

/// <summary>
/// <para>Changing a name everywhere it is written.</para>
/// <para><b>The one answer here that edits somebody's file, which is what makes it different in
/// kind from the rest.</b> A hover that is wrong is a tooltip nobody reads twice; a rename that is
/// wrong writes over code. So every edit comes from a span the parser recorded — the identifier
/// token as it was consumed — and nothing here works out where a name sits by counting from
/// either end of what encloses it.</para>
/// <para>What to change is the resolver's answer, not this one's. A use and its declaration are
/// bound to one symbol, so every name to edit is every node bound to the same symbol as the one
/// under the cursor. Deciding again here which declaration a name reaches would be a second
/// answer about scope, and the two would agree until they did not.</para>
/// </summary>
public static class Rename
{
    /// <summary>
    /// <para>Whether the name under the cursor can be renamed, and where it is.</para>
    /// <para>Asked before the editor prompts for a new name, so that a cursor on a keyword or a
    /// number says so at once rather than after somebody has typed a replacement. It also gives
    /// the editor the old name to put in the box.</para>
    /// </summary>
    public static JsonObject? Prepare(
        CompilationUnit asking, SemanticModel model, int offset)
    {
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentNullException.ThrowIfNull(model);

        if (Naming(asking, model, offset) is not var (node, symbol))
        {
            return null;
        }

        return new JsonObject
        {
            ["range"] = Lsp.RangeOf(node.NameSpan, asking.Source),
            ["placeholder"] = symbol.Name,
        };
    }

    /// <summary>
    /// <para>Every edit that renaming the name under the cursor takes, across every file.</para>
    /// <para>Null where there is nothing to rename, which an editor shows as "cannot rename
    /// here" rather than as an empty change nobody asked for.</para>
    /// <para>A name declared in one file and used in three is four edits in four files. That is
    /// the ordinary case rather than the awkward one — a program is a compilation — and it is why
    /// this answers with a change per file rather than a list of spans.</para>
    /// </summary>
    public static JsonObject? Edits(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        CompilationUnit asking,
        int offset,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(asking);

        if (Naming(asking, model, offset) is not var (_, symbol))
        {
            return null;
        }

        JsonObject changes = [];

        foreach (CompilationUnit unit in units)
        {
            JsonArray edits = [];

            foreach (SyntaxNode node in Everything(unit))
            {
                if (!TheSameNameAs(symbol, model.GetSymbol(node)))
                {
                    continue;
                }

                // Only where a name is actually written. A call is bound to the same symbol as
                // the name it calls, and a call records no name of its own — so writing over its
                // span would replace 'Greeting.Length("hello")' with 'Size'.
                if (!node.HasName)
                {
                    continue;
                }

                edits.Add(new JsonObject
                {
                    ["range"] = Lsp.RangeOf(node.NameSpan, unit.Source),
                    ["newText"] = newName,
                });
            }

            if (edits.Count > 0)
            {
                changes[Lsp.UriOf(unit.Source.FileName)] = edits;
            }
        }

        return new JsonObject { ["changes"] = changes };
    }

    /// <summary>
    /// <para>The node under the cursor that names something a program declared, and what it
    /// names.</para>
    /// <para>Which name the cursor is in is <see cref="NodeAt.NamesAt"/>'s question, asked the
    /// same way by everything that asks it. What is added here is the one restriction peculiar to
    /// renaming: <b>nothing for a name the language owns.</b> <c>Console</c> and <c>Count</c> are
    /// the compiler's, not this program's, so renaming one would edit the uses and leave the
    /// declaration where it is — a program that no longer compiles, arrived at by a command that
    /// looked like it worked, and not undone by renaming back.</para>
    /// </summary>
    private static (SyntaxNode Node, Symbol Symbol)? Naming(
        CompilationUnit asking, SemanticModel model, int offset)
    {
        foreach (SyntaxNode node in NodeAt.NamesAt(asking, offset))
        {
            if (model.GetSymbol(node) is not { } symbol)
            {
                continue;
            }

            return symbol.Declaration is null ? null : (node, TheNameItself(symbol));
        }

        return null;
    }

    /// <summary>
    /// <para>A constructor answers as the type it builds, because the two share one name and the
    /// language requires it: a constructor is the function named for its model.</para>
    /// <para>So there is one name here rather than two, and renaming either has to write both.
    /// Renaming only the type leaves <c>function Book</c> inside <c>model Volume</c>, which is no
    /// longer a constructor but an ordinary function that cannot be called and whose model can no
    /// longer be built. Renaming only the constructor is the same wreck from the other side. In
    /// both cases the editor reports a rename that worked.</para>
    /// </summary>
    private static Symbol TheNameItself(Symbol symbol) =>
        symbol is FunctionSymbol { IsConstructor: true } && symbol.DeclaringType is { } built
            ? built
            : symbol;

    /// <summary>Whether a node names the thing being renamed, counting a constructor as its type.</summary>
    private static bool TheSameNameAs(Symbol wanted, Symbol? found) =>
        ReferenceEquals(found, wanted)
        || (found is FunctionSymbol { IsConstructor: true }
            && ReferenceEquals(found.DeclaringType, wanted));

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
