namespace ProfiC.Cli.Alias;

/// <summary>
/// <para><c>pfc</c>, the short name for <c>profi-c</c>.</para>
/// <para>A separate executable rather than a shell alias so that the short name works the same
/// way everywhere — in a script, in a build task, and on a machine whose shell nobody
/// configured. It adds no behaviour of its own; the command reports whichever name it was
/// invoked as.</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => Cli.Program.Run(args);
}
