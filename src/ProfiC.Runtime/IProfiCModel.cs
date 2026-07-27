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
    /// <summary>How many members take part in equality.</summary>
    int DeepMemberCount { get; }

    /// <summary>One member, by position. Order must be stable across instances of a type.</summary>
    object? GetDeepMember(int index);
}
