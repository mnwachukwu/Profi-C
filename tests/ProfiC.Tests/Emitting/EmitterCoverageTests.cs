using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests.Emitting;

/// <summary>
/// <para>What the emitter can take, held against what the language offers.</para>
/// <para><b>The ratchet for built-ins, as the corpus count is the ratchet for samples.</b> A
/// member added to the language and not to the emitter would otherwise show up as a program that
/// runs and will not build — found by whoever wrote that program rather than by whoever added the
/// member.</para>
/// </summary>
[TestFixture]
public sealed class EmitterCoverageTests
{
    /// <summary>
    /// <para>Every built-in the language offers is one the emitter knows a sequence for.</para>
    /// <para>Read off the enumeration rather than off a list written here, so a new member is in
    /// this test the moment it exists.</para>
    /// </summary>
    [Test]
    public void EveryBuiltInTheLanguageOffersCanBeEmitted()
    {
        string[] missing =
        [
            .. Enum.GetValues<BuiltInId>()
                   .Where(id => !CilBuiltIns.IsSupported(id))
                   .Select(id => id.ToString())
                   .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.That(
            missing,
            Is.Empty,
            "built-ins the emitter has no instruction sequence for — add each to CilBuiltIns "
            + "and give it a case, or the programs that use them will run and not build");
    }
}
