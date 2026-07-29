namespace ProfiC.Runtime;

/// <summary>
/// <para>How the runtime reaches the parts of a value that take part in deep equality.</para>
/// <para>The compiler implements this on every model and structure it emits. It exists
/// because <c>Equals(object)</c> has nowhere to carry the set of pairs already being
/// compared, and cycle-safe comparison cannot work without threading that through.</para>
/// <para>Indexed rather than returning a collection, so walking a value allocates
/// nothing — which matters, since equality on a large graph visits every field of every
/// node.</para>
/// </summary>
public interface IProfiCModel
{
    /// <summary>
    /// <para>What this is an instance of, for telling two types apart.</para>
    /// <para>The host type cannot answer this on its own. Emitted code gives each Profi-C type
    /// a .NET type of its own, but the interpreter runs every model and structure as one class,
    /// so comparing host types there would find a structure equal to an unrelated one that
    /// happened to hold the same values. Whatever is returned is compared with
    /// <see cref="object.Equals(object)"/>, so a type symbol or a <see cref="Type"/> both
    /// serve.</para>
    /// </summary>
    object DeepTypeIdentity { get; }

    /// <summary>How many members take part in equality.</summary>
    int DeepMemberCount { get; }

    /// <summary>One member, by position. Order must be stable across instances of a type.</summary>
    object? GetDeepMember(int index);
}
