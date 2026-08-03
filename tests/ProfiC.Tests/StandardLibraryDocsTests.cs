using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>That every member the language provides says what it is for.</para>
/// <para><b>A member with nothing written about it is invisible rather than wrong</b>, which is
/// why this is checked rather than noticed. Nothing fails to compile, no diagnostic appears, and
/// the only sign is a tooltip that says a signature and nothing else — for one member out of two
/// hundred, in a list nobody reads end to end. The catalog is the place a member is added, so
/// this is the place adding one without a line about it is caught.</para>
/// </summary>
[TestFixture]
public sealed class StandardLibraryDocsTests
{
    /// <summary>Every member reachable through a model's name, or on a value of some type.</summary>
    private static IEnumerable<BuiltInMember> Everything() =>
    [
        .. BuiltIns.Models.SelectMany(m => m.Members),
        .. BuiltIns.Models.SelectMany(m => m.Constructors),
        .. BuiltIns.OnEveryType(),
        .. BuiltIns.OnString(),
        .. BuiltIns.OnInteger(),
        .. BuiltIns.OnReal(),
        .. BuiltIns.OnFloat(),
        .. BuiltIns.OnFraction(),
        .. BuiltIns.OnEnumeration(),
        .. BuiltIns.OnException(),
        .. BuiltIns.OnSet(new SetType(PrimitiveType.Integer)),

        // A set of optionals answers four more, which is the only place they exist — a set of
        // anything else has no empties to drop.
        .. BuiltIns.OnSet(new SetType(new OptionalType(PrimitiveType.Integer))),
        .. BuiltIns.OnOptional(new OptionalType(PrimitiveType.Integer)),
    ];

    [Test]
    public void EveryMemberSaysWhatItIsFor()
    {
        List<string> silent =
        [
            .. Everything()
                .Where(m => m.Id is { } id && !BuiltInDocs.Describes(id))
                .Select(m => $"{m.Id} ({m.Name})")
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];

        Assert.That(
            silent,
            Is.Empty,
            "these members have no line saying what they are for; add one to BuiltInDocs");
    }

    /// <summary>
    /// <para>Nothing is written about a member that no longer exists.</para>
    /// <para>The other direction, and the one that rots quietly: a member renamed or removed
    /// leaves its line behind, where it is read by nobody and looks like documentation for
    /// something real.</para>
    /// </summary>
    [Test]
    public void NothingIsSaidAboutAMemberThatIsNotThere()
    {
        HashSet<BuiltInId> real = [.. Everything().Where(m => m.Id is not null).Select(m => m.Id!.Value)];

        List<string> orphaned =
        [
            .. Enum.GetValues<BuiltInId>()
                .Where(id => BuiltInDocs.Describes(id) && !real.Contains(id))
                .Select(id => id.ToString())
                .Order(StringComparer.Ordinal),
        ];

        Assert.That(orphaned, Is.Empty, "these describe a member the catalog does not have");
    }

    /// <summary>
    /// <para>Each line reads as a label rather than as a sentence.</para>
    /// <para>The same words go into a table in the reference, a tooltip, and a completion list.
    /// One that ends in a full stop reads as a fragment in all three, and one that starts lower
    /// case reads as a continuation of whatever precedes it.</para>
    /// </summary>
    [Test]
    public void EveryLineIsWrittenTheSameWay()
    {
        Assert.Multiple(() =>
        {
            foreach (BuiltInMember member in Everything().Where(m => m.Id is not null))
            {
                string said = BuiltInDocs.Summary(member.Id!.Value);

                if (said.Length == 0)
                {
                    continue;
                }

                Assert.That(said, Does.Not.EndWith("."), $"{member.Id}");
                Assert.That(char.IsUpper(said[0]), Is.True, $"{member.Id}: '{said}'");
            }
        });
    }
}
