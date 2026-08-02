using System.Runtime.CompilerServices;

namespace ProfiC.Runtime;

/// <summary>
/// <para>Structural equality that terminates on cyclic graphs.</para>
/// <para>Profi-C's <c>==</c> compares models and sets by their contents rather than by
/// identity, which matches what a beginner expects and is what C# gives only for records.
/// Doing that safely on a graph that can contain itself is the difficulty this class
/// exists for.</para>
/// </summary>
public static class DeepEquality
{
    /// <summary>
    /// <para>How many pairs may be compared before the visited set is allocated.</para>
    /// <para>Most comparisons are shallow and never revisit anything, so allocating a set for
    /// them is pure waste. A cycle cannot be confirmed in fewer comparisons than it has nodes,
    /// and running a few extra laps before tracking begins costs bounded work.</para>
    /// </summary>
    private const int TrackingThreshold = 8;

    /// <summary>
    /// <para>Compares two values structurally.</para>
    /// <para>The traversal is iterative, so that a chain of a hundred thousand nodes —
    /// perfectly ordinary data — does not exhaust the stack.</para>
    /// </summary>
    public static new bool Equals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        Stack<(object Left, object Right)> pending = new();
        pending.Push((left, right));

        // Allocated only once the comparison proves to be more than shallow.
        HashSet<(object, object)>? assumedEqual = null;
        int compared = 0;

        while (pending.Count > 0)
        {
            (object a, object b) = pending.Pop();

            if (ReferenceEquals(a, b))
            {
                continue;
            }

            // A pair already under comparison is assumed equal. This is what makes the walk
            // terminate on a cycle: equality is taken as true unless something contradicts
            // it, rather than being built up from the leaves, which a cycle never reaches.
            if (assumedEqual is not null && !assumedEqual.Add((a, b)))
            {
                continue;
            }

            if (++compared == TrackingThreshold)
            {
                assumedEqual = new HashSet<(object, object)>(ReferencePairComparer.Instance);
                assumedEqual.Add((a, b));
            }

            if (!PushMembers(a, b, pending))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two values shallowly and queues whatever needs comparing in turn. Returns
    /// false as soon as the two cannot be equal.
    /// </summary>
    private static bool PushMembers(
        object a,
        object b,
        Stack<(object Left, object Right)> pending)
    {
        // Runtime types must match. A Dog and an Animal are never equal even when every
        // shared field agrees, which is the rule C# records follow.
        if (a.GetType() != b.GetType())
        {
            return false;
        }

        switch (a)
        {
            case string text:
                return text.Equals((string)b, StringComparison.Ordinal);

            // Two optionals are equal when both are empty, or both hold values that are.
            // Compared through the interface rather than by the struct's own Equals, so that
            // what they hold is compared deeply too — a set inside an optional is still a set.
            case IProfiCOptional optionalA:
            {
                IProfiCOptional optionalB = (IProfiCOptional)b;

                if (optionalA.HasValue != optionalB.HasValue)
                {
                    return false;
                }

                return !optionalA.HasValue
                    || Queue(optionalA.GetValue(), optionalB.GetValue(), pending);
            }

            case IProfiCSet setA:
            {
                IProfiCSet setB = (IProfiCSet)b;

                if (setA.Count != setB.Count)
                {
                    return false;
                }

                for (int i = 0; i < setA.Count; i++)
                {
                    if (!Queue(setA.GetElement(i), setB.GetElement(i), pending))
                    {
                        return false;
                    }
                }

                return true;
            }

            case IProfiCModel modelA:
            {
                IProfiCModel modelB = (IProfiCModel)b;

                // Asked of the value rather than of its host type: the interpreter runs every
                // model and structure as one class, so the check above cannot tell two Profi-C
                // types apart.
                if (!modelA.DeepTypeIdentity.Equals(modelB.DeepTypeIdentity))
                {
                    return false;
                }

                if (modelA.DeepMemberCount != modelB.DeepMemberCount)
                {
                    return false;
                }

                for (int i = 0; i < modelA.DeepMemberCount; i++)
                {
                    if (!Queue(modelA.GetDeepMember(i), modelB.GetDeepMember(i), pending))
                    {
                        return false;
                    }
                }

                return true;
            }

            default:
                // Numbers, characters, booleans, fractions, enumerations: values whose
                // equality is already decided by the type itself.
                return a.Equals(b);
        }
    }

    /// <summary>
    /// Queues a pair for comparison, settling the cases that need no traversal immediately so
    /// the worklist stays small.
    /// </summary>
    private static bool Queue(
        object? a,
        object? b,
        Stack<(object Left, object Right)> pending)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        pending.Push((a, b));
        return true;
    }

    /// <summary>
    /// <para>Compares pairs by reference identity.</para>
    /// <para>Using the default comparer would call <c>Equals</c> on the members while deciding
    /// whether their comparison is already in progress, which is the very question being
    /// asked. Identity is the only thing safe to ask here.</para>
    /// </summary>
    private sealed class ReferencePairComparer : IEqualityComparer<(object, object)>
    {
        public static readonly ReferencePairComparer Instance = new();

        public bool Equals((object, object) x, (object, object) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) pair) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(pair.Item1),
            RuntimeHelpers.GetHashCode(pair.Item2));
    }
}
