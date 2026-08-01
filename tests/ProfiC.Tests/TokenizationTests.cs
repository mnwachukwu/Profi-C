using System.Diagnostics;
using System.Text.Json;

namespace ProfiC.Tests;

/// <summary>
/// <para>What a reader's editor actually colors, run through the engine it runs.</para>
/// <para><see cref="EditorGrammarTests"/> reads the grammar's JSON and holds what it
/// <i>says</i> to what the language is. That is worth having and it is not this. "The file
/// names this scope" and "a reader sees this scope" are different claims, and only the second
/// one reaches anybody — the gap between them is where several confident statements about the
/// editor turned out to be wrong.</para>
/// <para>The engine is vscode-textmate over Oniguruma, which is what VS Code runs. A rule
/// behaving differently here behaves differently there.</para>
/// </summary>
[TestFixture]
public sealed class TokenizationTests : LexerTestBase
{
    private static string Extension =>
        Path.Combine(RepositoryRoot, "editors", "vscode");

    /// <summary>One token, and every scope it carries.</summary>
    private sealed record Token(string Text, string[] Scopes);

    /// <summary>
    /// <para>Tokenizes lines and returns what came back, or skips where the engine is not
    /// installed.</para>
    /// <para>Skipped rather than failed, because the packages are fetched rather than
    /// committed and a checkout without them is an ordinary state to be in. Restoring them is
    /// <c>npm install</c> in the extension's folder.</para>
    /// </summary>
    private static Token[][] Scopes(params string[] lines)
    {
        if (!Directory.Exists(Path.Combine(Extension, "node_modules")))
        {
            Assert.Ignore("run 'npm install' in editors/vscode to tokenize");
        }

        ProcessStartInfo start = new()
        {
            FileName = "node",
            WorkingDirectory = Extension,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(Path.Combine("tools", "scopes.js"));

        using Process node = StartOrIgnore(start);

        node.StandardInput.Write(JsonSerializer.Serialize(lines));
        node.StandardInput.Close();

        string output = node.StandardOutput.ReadToEnd();
        string failed = node.StandardError.ReadToEnd();

        node.WaitForExit();

        Assert.That(node.ExitCode, Is.Zero, failed);

        return JsonSerializer.Deserialize<Token[][]>(
            output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>Starts node, or skips where there is no node to start.</summary>
    private static Process StartOrIgnore(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)!;
        }
        catch (Exception unavailable)
        {
            Assert.Ignore($"node is needed to tokenize: {unavailable.Message}");
            throw;
        }
    }

    /// <summary>Every scope carried by the first token whose text matches.</summary>
    private static string[] Carried(Token[][] lines, string text) =>
        lines.SelectMany(line => line)
             .First(token => token.Text.Trim() == text)
             .Scopes;

    // ---- Documentation labels ---------------------------------------------------------

    /// <summary>
    /// A label is colored whole — the mark, the name and the colon together — because the
    /// thing acting on the documentation is the word, not the punctuation around it.
    /// </summary>
    [Test]
    public void ALabelInABlockIsScopedWhole() => Assert.That(
        Carried(Scopes("##", "    @summary: One person's money.", "##"), "@summary:"),
        Does.Contain("constant.language.documentation.profi-c"));

    [Test]
    public void ALabelInALineCommentIsScopedTheSameWay() => Assert.That(
        Carried(Scopes("# @summary: Whose account this is."), "@summary:"),
        Does.Contain("constant.language.documentation.profi-c"));

    /// <summary>A label keeps the comment scope under its own, so a theme coloring all
    /// comments still colors the line it sits on.</summary>
    [Test]
    public void ALabelStaysInsideItsComment() => Assert.That(
        Carried(Scopes("##", "    @summary: A thing.", "##"), "@summary:"),
        Does.Contain("comment.block.profi-c"));

    /// <summary>
    /// <para>The case that decided the design, checked where it counts.</para>
    /// <para>Prose wraps, and a wrapped line often begins with a word and a colon. Coloring
    /// one would tell a reader the compiler is acting on a sentence it passes over.</para>
    /// </summary>
    [Test]
    public void WrappedProseIsNotALabel() => Assert.That(
        Scopes("##", "    That is why it yields an", "    optional: an answer.", "##")
            .SelectMany(line => line)
            .SelectMany(token => token.Scopes),
        Has.None.EqualTo("constant.language.documentation.profi-c"));

    // ---- Ignore directives ---------------------------------------------------------------

    [Test]
    public void ADirectiveIsScopedApartFromAnOrdinaryComment() => Assert.That(
        Carried(Scopes("# ignore opinion"), "# ignore opinion"),
        Does.Contain("comment.line.number-sign.directive.profi-c"));

    /// <summary>
    /// A remark opening with the word stays a remark, which is the rule the scanner reads by
    /// and the one a reader would have no way to check if the coloring disagreed.
    /// </summary>
    [Test]
    public void ProseBeginningWithTheWordIsNotADirective() => Assert.That(
        Carried(Scopes("# ignore the sign for now"), "# ignore the sign for now"),
        Does.Not.Contain("comment.line.number-sign.directive.profi-c"));

    // ---- Ordinary code, so the grammar is not merely quiet ---------------------------------

    /// <summary>
    /// A control against the others: a grammar matching nothing at all would pass every test
    /// above that asserts a scope is absent.
    /// </summary>
    [Test]
    public void OrdinaryCodeStillCarriesItsScopes()
    {
        Token[][] scanned = Scopes("model Account");

        Assert.Multiple(() =>
        {
            Assert.That(Carried(scanned, "model"), Does.Contain("keyword.declaration.profi-c"));
            Assert.That(Carried(scanned, "Account"), Does.Contain("entity.name.type.profi-c"));
        });
    }
}
