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
    private static readonly Dictionary<string, ModelSymbol> Known = new(StringComparer.Ordinal);

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
