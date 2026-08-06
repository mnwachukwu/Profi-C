using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Services;

/// <summary>
/// <para>What every name in a file is, so an editor can color it for what it means rather than
/// for what it looks like.</para>
/// <para><b>The question a grammar cannot answer.</b> Highlighting is regex over a line, so it
/// can see that <c>total</c> is an identifier and never that it is the parameter declared six
/// lines up. It colors a parameter where it is declared and gives up where it is used — which is
/// the half a reader spends their time looking at. The compiler settled all of it while checking;
/// this only writes the answers down where an editor can read them.</para>
/// <para>Every span here is one the parser recorded as a name. Nothing works out where a name
/// sits by measuring from either end of what encloses it, for the same reason renaming does
/// not.</para>
/// </summary>
public static class SemanticTokens
{
    /// <summary>
    /// <para>The kinds of thing a name can be, in the order the protocol will refer to them
    /// by.</para>
    /// <para>These are the protocol's own names rather than Profi-C's. A reader's theme already
    /// has an opinion about what a <c>parameter</c> looks like, and a language that invented its
    /// own vocabulary here would arrive uncolored under every theme but its own.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "namespace",
        "class",
        "struct",
        "enum",
        "enumMember",
        "type",
        "function",
        "method",
        "property",
        "parameter",
        "variable",
    ];

    /// <summary>
    /// <para>What can be true of a name as well as what it is.</para>
    /// <para>Sent as a bit set, so a name may carry several. These are the protocol's standard
    /// ones, which most themes already render — <c>readonly</c> in particular, which is how a
    /// <c>constant</c> arrives looking like one without anybody choosing a color.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Traits =
    [
        "declaration",
        "readonly",
        "static",
        "abstract",
        "defaultLibrary",
    ];

    private const int Declaration = 1 << 0;

    private const int ReadOnly = 1 << 1;

    private const int Static = 1 << 2;

    private const int Abstract = 1 << 3;

    private const int DefaultLibrary = 1 << 4;

    /// <summary>Where each kind sits in <see cref="Kinds"/>, for the few that are named twice.</summary>
    private const int Namespace = 0;

    private const int Function = 6;

    private const int Method = 7;

    private const int Property = 8;

    private const int Parameter = 9;

    private const int Variable = 10;

    /// <summary>
    /// <para>Every name in the file, encoded the way the protocol asks for it.</para>
    /// <para>Five numbers each, and each row is measured from the row before it rather than from
    /// the start of the file — so a token near the end of a long file is described by two small
    /// numbers instead of two large ones. That is the whole reason for the format, and the whole
    /// difficulty of it: a list in the wrong order does not fail, it silently colors the wrong
    /// characters.</para>
    /// </summary>
    public static JsonObject Of(CompilationUnit unit, SemanticModel model, SourceText source)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(source);

        List<(int Line, int Column, int Length, int Kind, int Traits)> found = [];

        // What is being called. Gathered first because the question is asked of a node about its
        // parent, and a node does not carry one.
        HashSet<SyntaxNode> called = [];

        foreach (SyntaxNode node in Everything(unit))
        {
            if (node is CallExpr call)
            {
                called.Add(call.Callee);
            }
        }

        foreach (SyntaxNode node in Everything(unit))
        {
            // A dotted type name is several names: everything before the last says which
            // namespace to look in, and only the last is the type. Colored as one, the part that
            // says where it came from reads as part of the name.
            if (node is NamedTypeSyntax { IsQualified: true } qualified
                && qualified.PartSpans.Count == qualified.Parts.Count)
            {
                foreach (SourceSpan part in qualified.PartSpans.Take(qualified.Parts.Count - 1))
                {
                    found.Add((
                        part.Start.Line - 1, part.Start.Column - 1, part.Length, Namespace, 0));
                }
            }

            if (!node.HasName || node.NameSpan.Length == 0)
            {
                continue;
            }

            Symbol? symbol = model.GetSymbol(node);

            // Two things carry no symbol and are named all the same. A member the language
            // provides is a catalog entry, and a type written as a type records what it names
            // rather than a name that resolved — so 'WriteLine' and the 'Circle' in
            // 'Shapes.Flat.Circle' were the two most-written names in a file with no color on
            // them at all.
            if (KindOf(symbol, model, node) is not { } kind)
            {
                continue;
            }

            // A name being invoked reads as a function, whatever is holding it. A local of a
            // delegate type is a local everywhere else and a call here, and which of the two it
            // looks like should follow what is being done with it rather than what it was
            // declared as — the parentheses are the thing a reader is looking at.
            if (kind is Variable or Parameter or Property && called.Contains(node))
            {
                kind = symbol is FieldSymbol ? Method : Function;
            }

            SourceSpan where = node.NameSpan;

            found.Add((
                where.Start.Line - 1,
                where.Start.Column - 1,
                where.Length,
                kind,
                symbol is null
                    ? (model.GetBuiltIn(node) is not null ? DefaultLibrary : 0)
                    : TraitsOf(symbol, node)));
        }

        // Sorted because the encoding is a walk: each row says how far it is from the one before,
        // so an unsorted list produces negative distances and an editor colors from nowhere.
        found.Sort((left, right) =>
            left.Line != right.Line
                ? left.Line.CompareTo(right.Line)
                : left.Column.CompareTo(right.Column));

        JsonArray data = [];

        int lastLine = 0;
        int lastColumn = 0;

        foreach ((int line, int column, int length, int kind, int traits) in found)
        {
            data.Add(line - lastLine);
            data.Add(line == lastLine ? column - lastColumn : column);
            data.Add(length);
            data.Add(kind);
            data.Add(traits);

            lastLine = line;
            lastColumn = column;
        }

        return new JsonObject { ["data"] = data };
    }

    /// <summary>
    /// <para>Which kind of name this is, as an index into <see cref="Kinds"/>, or null for a
    /// symbol that is not a name a reader wrote.</para>
    /// <para>A function declared on a type is a <c>method</c> and one declared in a body is a
    /// <c>function</c>, which is the distinction the protocol draws and the one a theme colors
    /// differently.</para>
    /// </summary>
    /// <summary>
    /// Which kind of name this is, from whichever of the three things the compiler recorded about
    /// it: the symbol it resolved to, the catalog entry it named, or the type it wrote.
    /// </summary>
    private static int? KindOf(Symbol? symbol, SemanticModel model, SyntaxNode node)
    {
        if (symbol is not null)
        {
            return KindOf(symbol);
        }

        if (model.GetBuiltIn(node) is { } provided)
        {
            return BuiltIns.Find(provided) is { IsValue: true } ? Property : Method;
        }

        // Only a type that was declared somewhere, by a program or by the catalog. A primitive is
        // written as a reserved word — 'integer', not 'Integer' — so it is a keyword on the line
        // and the grammar has already colored it as one. A semantic token wins where both apply,
        // so naming it here would repaint a keyword as a type name and leave 'function' beside it
        // looking like something else entirely.
        return node is TypeSyntax && model.GetType(node) is DeclaredTypeSymbol named
            ? KindOf(named)
            : null;
    }

    private static int? KindOf(Symbol symbol) => symbol switch
    {
        NamespaceSymbol => 0,
        ModelSymbol => 1,
        StructureSymbol => 2,
        EnumerationSymbol => 3,
        EnumMemberSymbol => 4,
        TypeSymbol => 5,

        // A constructor is the one member that does not read as a member. It is written with the
        // type's own name, and 'new Circle()' and 'function Circle(...)' are the same word — so a
        // reader looking at either is looking at the type, and coloring the two differently says
        // they are different things.
        FunctionSymbol { IsConstructor: true, DeclaringType: { } owner } => KindOf(owner),

        FunctionSymbol function => function.DeclaringType is null ? Function : Method,
        FieldSymbol => Property,
        ParameterSymbol => Parameter,
        LocalSymbol => Variable,
        _ => null,
    };

    /// <summary>
    /// <para>What else is true of this name here.</para>
    /// <para><c>declaration</c> is about the place rather than the symbol — the same local is a
    /// declaration once and a use everywhere else — which is why the node is asked as well.</para>
    /// </summary>
    private static int TraitsOf(Symbol symbol, SyntaxNode node)
    {
        int traits = symbol.Declaration is { } declared && ReferenceEquals(declared, node)
            ? Declaration
            : 0;

        // Nothing declared it, so no program did: a name the language provides. What that means
        // to a reader is that it cannot be gone to, changed, or found in this program.
        if (symbol.Declaration is null && symbol is TypeSymbol)
        {
            traits |= DefaultLibrary;
        }

        switch (symbol)
        {
            case LocalSymbol { IsConstant: true }:
            case LocalSymbol { IsLoopVariable: true }:
                traits |= ReadOnly;
                break;

            case FieldSymbol field:
                traits |= field.IsConstant ? ReadOnly : 0;
                traits |= field.IsShared ? Static : 0;
                break;

            case FunctionSymbol function:
                traits |= function.IsShared ? Static : 0;
                traits |= function.IsAbstract ? Abstract : 0;
                break;

            case ModelSymbol model:
                traits |= model.IsShared ? Static : 0;
                traits |= model.IsAbstract ? Abstract : 0;
                break;
        }

        return traits;
    }

    private static IEnumerable<SyntaxNode> Everything(SyntaxNode node)
    {
        yield return node;

        foreach (SyntaxNode child in node.Children)
        {
            foreach (SyntaxNode inside in Everything(child))
            {
                yield return inside;
            }
        }
    }
}
