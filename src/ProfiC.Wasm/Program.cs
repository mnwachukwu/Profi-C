namespace ProfiC.Wasm;

/// <summary>
/// <para>The entry point a .NET WebAssembly build requires, which nothing calls.</para>
/// <para>This is a library wearing an executable's clothes: the browser starts the runtime and
/// then calls the exported functions itself, so there is no beginning to write. The build still
/// wants one, and an empty one is the honest version of that.</para>
/// </summary>
internal static class Program
{
    private static void Main()
    {
    }
}
