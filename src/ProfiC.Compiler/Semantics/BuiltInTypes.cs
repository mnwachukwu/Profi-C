using ProfiC.Compiler.Ast;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>The one symbol standing for each type the language owns.</para>
/// <para>Two types are the same type when they are the same object, so a member whose
/// signature names <c>DateTime</c> and a variable declared as <c>DateTime</c> have to reach
/// the very same symbol. Making one per compilation would leave the catalog holding a
/// different <c>DateTime</c> from the resolver's, and a value of one would not fit the
/// other.</para>
/// <para>Sharing them is safe because none of them changes after it is built: the language
/// owns no members a program can add to, and the only thing written here is the parent, which
/// is the same answer every time it is asked.</para>
/// </summary>
public static class BuiltInTypes
{
    /// <summary>The name of the namespace every type the language owns belongs to.</summary>
    public const string StandardName = "Standard";

    private static readonly Dictionary<string, ModelSymbol> Known = new(StringComparer.Ordinal);

    /// <summary>
    /// <para>The namespace holding every type the language provides.</para>
    /// <para>Shared for the same reason the symbols in it are: what it holds never varies, so
    /// one of them serves every compilation, and a <c>Standard.DateTime</c> named in one place
    /// is the same type as a <c>DateTime</c> named in another.</para>
    /// <para>Its parent is null rather than any compilation's global namespace, which is what
    /// makes sharing it safe — a parent would differ per compilation and tie it to one.</para>
    /// <para>Built on first use rather than with this class, because what belongs in it is
    /// read from the catalog and the catalog names types from here: settling either one while
    /// the other is still being built would read a list that does not exist yet.</para>
    /// </summary>
    public static NamespaceSymbol Standard => Lazy.Value;

    private static readonly Lazy<NamespaceSymbol> Lazy = new(BuildStandard);

    private static NamespaceSymbol BuildStandard()
    {
        NamespaceSymbol standard = new(StandardName, parent: null);

        foreach (string name in BuiltIns.AllTypeNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            ModelSymbol model = Of(name);
            model.Container = standard;
            standard.Types[name] = model;
        }

        return standard;
    }

    /// <summary>
    /// The symbol for a built-in type, made the first time it is asked for. Anything the
    /// language raises descends from <c>Exception</c>; everything else descends from
    /// <c>Model</c>, which is what the word means and is the root itself.
    /// </summary>
    public static ModelSymbol Of(string name)
    {
        lock (Known)
        {
            if (Known.TryGetValue(name, out ModelSymbol? existing))
            {
                return existing;
            }

            ModelSymbol model = new(name, DeclarationModifiers.Public);

            // Recorded before the parent is worked out, so that asking for Model or Exception
            // from inside that work finds this entry rather than starting again.
            Known[name] = model;

            if (name == "Model")
            {
                return model;
            }

            model.BaseType = Runtime.BuiltInExceptions.Names.Contains(name) && name != "Exception"
                ? Of("Exception")
                : Of("Model");

            return model;
        }
    }
}
