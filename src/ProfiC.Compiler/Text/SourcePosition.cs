namespace ProfiC.Compiler.Text;

/// <summary>
/// <para>A location in a source file: a one-based line and column, together with the
/// zero-based character offset they denote.</para>
/// <para>Line and column are one-based because diagnostics are read by people. The
/// Language Server Protocol's zero-based convention is a conversion applied at the
/// protocol boundary, not a representation carried through the compiler.</para>
/// </summary>
public readonly record struct SourcePosition(int Line, int Column, int Offset)
{
    /// <summary>A position that denotes nowhere, used where no source location applies.</summary>
    public static readonly SourcePosition None = new(0, 0, -1);

    public override string ToString() => $"({Line},{Column})";
}
