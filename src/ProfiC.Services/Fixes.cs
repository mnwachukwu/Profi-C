using System.Text.Json.Nodes;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Services;

/// <summary>
/// <para>The one-click fixes an editor offers beside a problem.</para>
/// <para>Offered only where the compiler said what would settle it. A diagnostic carries a
/// replacement when one substitution does the whole job — <c>&amp;&amp;</c> for <c>and</c> — and
/// nothing where the rewrite needs to know something the compiler does not, as
/// <c>x += 1</c> does.</para>
/// <para><b>Nothing here decides what the fix is.</b> Working it out from the message would tie
/// every fix to the wording of a sentence, and working it out from the source text would be a
/// second table of the same mapping. The compiler knows, so the compiler says, and this only
/// writes it down the way the protocol writes it.</para>
/// </summary>
public static class Fixes
{
    /// <summary>
    /// <para>What can be done about the problems in a range.</para>
    /// <para>The diagnostics come back from the editor rather than being found again here, which
    /// is what the protocol asks for and is also the more honest arrangement: the fix offered is
    /// against the problem the reader is looking at, not one this decided they had.</para>
    /// </summary>
    public static JsonArray For(string uri, JsonArray? diagnostics, IReadOnlyList<Diagnostic> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        JsonArray actions = [];

        foreach (JsonNode? reported in diagnostics ?? [])
        {
            if (reported is not JsonObject one || (string?)one["code"] is not { } code)
            {
                continue;
            }

            // Matched by identifier and by where it is, because one line may carry two problems
            // and only one of them may have a fix.
            Diagnostic? matching = found.FirstOrDefault(
                d => d.Id == code && Same(one["range"], d));

            if (matching is not { FixedBy: { } fix })
            {
                continue;
            }

            actions.Add(new JsonObject
            {
                ["title"] = $"Replace with '{fix}'",

                // "quickfix", which is what puts it behind the lightbulb rather than in the
                // refactoring list.
                ["kind"] = "quickfix",
                ["diagnostics"] = new JsonArray(one.DeepClone()),
                ["isPreferred"] = true,
                ["edit"] = new JsonObject
                {
                    ["changes"] = new JsonObject
                    {
                        [uri] = new JsonArray(new JsonObject
                        {
                            ["range"] = one["range"]?.DeepClone(),
                            ["newText"] = fix,
                        }),
                    },
                },
            });
        }

        return actions;
    }

    /// <summary>
    /// Whether a range the editor sent is the one a diagnostic reported. Compared on where it
    /// starts, which is enough to tell two problems on a line apart and does not depend on the
    /// editor having kept the end exactly.
    /// </summary>
    private static bool Same(JsonNode? range, Diagnostic diagnostic)
    {
        SourcePosition start = diagnostic.Span.Start;

        return (int?)range?["start"]?["line"] == start.Line - 1
            && (int?)range?["start"]?["character"] == start.Column - 1;
    }
}
