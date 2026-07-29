namespace ProfiC.Runtime;

/// <summary>
/// <para>A member of an enumeration, as it exists while a program runs.</para>
/// <para>Carrying the name alongside the ordinal is what lets
/// <c>Console.WriteLine(Color.Green)</c> print <c>Green</c> rather than <c>1</c>. A bare
/// integer would be cheaper, but a student reading a number where they wrote a name learns
/// nothing from it.</para>
/// <para>The type name is part of the value so that two enumerations sharing an ordinal are
/// still different values. Comparing across enumerations is rejected at compile time, so this
/// only matters for the interpreter's own bookkeeping.</para>
/// </summary>
/// <param name="TypeName">The enumeration this belongs to.</param>
/// <param name="MemberName">The name the program wrote.</param>
/// <param name="Ordinal">The number behind the name, as <c>ToInteger()</c> reports it.</param>
public readonly record struct EnumValue(string TypeName, string MemberName, long Ordinal)
{
    /// <summary>The member's name, which is what printing one should show.</summary>
    public override string ToString() => MemberName;
}
