using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>What could come next, after a dot or on its own.</para>
/// <para><b>This is the first question that cannot be asked of the program as written.</b>
/// Everything else a server answers is about syntax that exists — a name, a call, a declaration.
/// A reader who has just typed <c>word.</c> has written something that is not Profi-C and never
/// will be: there is no member yet, so there is no member access, so there is nothing to ask the
/// model about.</para>
/// <para><b>The way through is to ask about a program that does parse.</b> A name is put where
/// the member will go, the text is compiled with it there, and the receiver of the member access
/// that appears is the thing whose members are wanted. The compiler answers about a real
/// program; nothing here reasons about half-written syntax, which is the part that would rot.
/// </para>
/// <para>A bare name needs no such trick, and could not use one: what is wanted is not the type
/// of something written but the set of names in force where the cursor is, which the resolver
/// wrote down as it went. So that half asks the compiled program directly, at whatever offset
/// the cursor sits — including on a line that is half typed, since the scope is a stretch of the
/// file rather than a piece of syntax that has to parse.</para>
/// <para>What is offered is what would resolve — the same catalog the checker reads, and the
/// members a type actually declares, with the ones a caller could not reach left out. A list
/// that suggests something the next keystroke would reject is worse than a shorter list.</para>
/// </summary>
public static class Completion
{
    /// <summary>
    /// <para>The name put where the member will go.</para>
    /// <para>Long and unlovely on purpose: it has to be something no program contains, since a
    /// file that already had one would have two and the wrong one might be found. It never
    /// reaches a reader — the tree it makes is thrown away once the receiver has been read off
    /// it.</para>
    /// </summary>
    private const string Placeholder = "__profi_c_completing__";

    /// <summary>
    /// <para>What can follow the dot before the cursor, or null where the cursor does not follow
    /// one.</para>
    /// <para>Null rather than an empty list, so a caller can tell "nothing goes here" from "this
    /// is not a place where members go" — an editor shows an empty list as "no suggestions" and
    /// says nothing at all for the second.</para>
    /// </summary>
    public static JsonArray? After(
        string path,
        SourceText source,
        int offset,
        SourceReader read,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!FollowsADot(source.Text, offset))
        {
            return null;
        }

        // A receiver whose type is in error is one the compiler could not work out — a name
        // nothing declares, most often. Every type answers ToString and Equals, so offering what
        // is known about it would offer those two and imply the name is fine.
        if (ReceiverType(path, source, offset, read, cancellation) is not { IsError: false } receiver)
        {
            return null;
        }

        JsonArray offered = [];

        foreach ((string name, string detail, int kind) in MembersOf(receiver))
        {
            offered.Add(new JsonObject
            {
                ["label"] = name,
                ["detail"] = detail,
                ["kind"] = kind,
            });
        }

        return offered;
    }

    /// <summary>
    /// <para>What could be written where the cursor is, when nothing precedes it.</para>
    /// <para>Locals and parameters from the chain of scopes in force there, then every type a
    /// bare name reaches — which is how a shared member is called, since <c>Math.Abs</c> begins
    /// with a type name. The resolver wrote both down while it worked; nothing here decides again
    /// what is in scope.</para>
    /// <para><c>this</c> and <c>base</c> are offered where they mean something, which in this
    /// language is more useful than it sounds: every field access is written through one of them,
    /// so a reader reaching for a field types <c>t</c> first.</para>
    /// <para>Null where the cursor is somewhere no name can be written — between declarations,
    /// or in a file with nothing in it yet.</para>
    /// </summary>
    public static JsonArray? Bare(
        string path,
        SourceText source,
        int offset,
        SourceReader read,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (FollowsADot(source.Text, offset))
        {
            return null;
        }

        if (Compiled(path, source, read, cancellation) is not var (model, unit))
        {
            return null;
        }

        // Counted against the file that was compiled, whose text is this one's. The offset the
        // editor gave is an offset into it, unshifted, since nothing was inserted.
        if (model.NamesAt(unit.Source, offset) is not { } names)
        {
            return null;
        }

        JsonArray offered = [];

        foreach (Symbol symbol in names.Visible())
        {
            // A declaration whose name has not been typed yet still becomes a symbol, since the
            // parser recovers rather than stopping. Offering it would put a blank row in the list
            // — and a file being written has one of these in it most of the time.
            if (symbol.Name.Length == 0)
            {
                continue;
            }

            // Declared further down the file than the cursor. A local does not exist until its
            // declaration runs, so offering one would suggest a name the compiler refuses — and
            // a function does exist, since a local one may be called before it is declared.
            if (symbol is not FunctionSymbol
                && symbol.Declaration is { } declared
                && declared.Span.Start.Offset > offset)
            {
                continue;
            }

            offered.Add(new JsonObject
            {
                ["label"] = symbol.Name,
                ["detail"] = Describe(symbol),
                ["kind"] = KindOf(symbol),
            });
        }

        foreach (string keyword in Receivers(names))
        {
            offered.Add(new JsonObject
            {
                ["label"] = keyword,
                ["detail"] = names.EnclosingType?.Name ?? string.Empty,
                ["kind"] = Keyword,
            });
        }

        return offered;
    }

    /// <summary>
    /// The receivers a bare name can be written as here: <c>this</c> inside an instance member,
    /// and <c>base</c> as well where the model has something to inherit from.
    /// </summary>
    private static IEnumerable<string> Receivers(NameScope names)
    {
        if (names.EnclosingType is null || names.InSharedMember)
        {
            yield break;
        }

        yield return "this";

        if (names.EnclosingType is ModelSymbol { BaseType: not null })
        {
            yield return "base";
        }
    }

    /// <summary>
    /// <para>Whether the cursor sits after a dot, past however much of a member name is
    /// typed.</para>
    /// <para>A reader asks for this list twice: once on typing the dot, and again on every letter
    /// after it as the editor narrows what it shows. Both are the same question about the same
    /// receiver.</para>
    /// <para>A number is not a member access. <c>1.5</c> has a dot in it and the character after
    /// it is a digit, so the run skipped back over must be a name rather than any run at
    /// all.</para>
    /// </summary>
    private static bool FollowsADot(string text, int offset)
    {
        if (offset < 1 || offset > text.Length)
        {
            return false;
        }

        int at = offset;

        while (at > 0 && (char.IsLetterOrDigit(text[at - 1]) || text[at - 1] == '_'))
        {
            at--;
        }

        // A run that begins with a digit is a number rather than a name, so what is in front of
        // it is a decimal point.
        if (at < offset && char.IsDigit(text[at]))
        {
            return false;
        }

        return at > 0 && text[at - 1] == '.';
    }

    /// <summary>
    /// <para>The type of what is in front of the dot, worked out by compiling a program that
    /// parses.</para>
    /// <para>The placeholder is inserted rather than replacing what is typed, so
    /// <c>word.Cou</c> becomes <c>word.Cou__profi_c_completing__</c> — one name, in the one place
    /// a member goes. Removing the typed letters would work equally well and would move every
    /// offset after the cursor, which is a second thing to get right for nothing.</para>
    /// <para>Compiled as the whole program rather than the file alone: the receiver may be a type
    /// declared next door, and a compilation of one file would type it as nothing.</para>
    /// </summary>
    private static TypeSymbol? ReceiverType(
        string path,
        SourceText source,
        int offset,
        SourceReader read,
        CancellationToken cancellation)
    {
        SourceText completing = new(
            source.Text[..offset] + Placeholder + source.Text[offset..], source.FileName);

        if (Compiled(path, completing, read, cancellation) is not var (model, unit))
        {
            return null;
        }

        // The member access the placeholder made. Found by name rather than by position, since
        // inserting the text moved every offset after it.
        MemberExpr? access = Everything(unit)
            .OfType<MemberExpr>()
            .FirstOrDefault(m => m.MemberName.EndsWith(Placeholder, StringComparison.Ordinal));

        return access is null ? null : model.GetType(access.Receiver);
    }

    /// <summary>
    /// <para>The whole program around a file, checked, with the given text standing in for what
    /// is on disk.</para>
    /// <para>The whole program rather than the one file: a name in scope may be a type declared
    /// next door, and a compilation of one file would not have it.</para>
    /// <para>Checked and not merely resolved, because what is offered says types — a local's, and
    /// what a function yields — and those are the checker's answers.</para>
    /// </summary>
    private static (SemanticModel Model, CompilationUnit Unit)? Compiled(
        string path, SourceText text, SourceReader read, CancellationToken cancellation)
    {
        DiagnosticBag aside = new();

        SourceReader instead = asked =>
            SourceDiscovery.PathComparer.Equals(Path.GetFullPath(asked), Path.GetFullPath(path))
                ? text
                : read(asked);

        if (SourceDiscovery.Gather(path, aside, instead) is not { } compilation)
        {
            return null;
        }

        CompilationUnit? unit = compilation.Units.FirstOrDefault(
            u => ReferenceEquals(u.Source, text));

        if (unit is null)
        {
            return null;
        }

        SemanticModel model = Resolver.Resolve(
            compilation.Units,
            aside,
            projects: compilation.Projects,
            entryPoint: compilation.EntryPoint,
            cancellation: cancellation);

        TypeChecker.Check(compilation.Units, model, aside, cancellation);

        return (model, unit);
    }

    /// <summary>
    /// <para>Everything a program could write after a dot on this type.</para>
    /// <para>Both halves of what a type answers: the members the language provides, which no
    /// program declares, and the members a program declared — including the ones it inherited,
    /// since a reader calling <c>shape.Area()</c> does not care which model wrote it.</para>
    /// </summary>
    private static IEnumerable<(string Name, string Detail, int Kind)> MembersOf(TypeSymbol receiver)
    {
        HashSet<string> already = new(StringComparer.Ordinal);

        foreach (BuiltInMember member in BuiltInMembers.On(receiver))
        {
            if (already.Add(member.Name))
            {
                yield return (member.Name, Describe(member), member.IsValue ? Property : Method);
            }
        }

        if (receiver is not DeclaredTypeSymbol declared)
        {
            yield break;
        }

        IEnumerable<DeclaredTypeSymbol> selfAndAncestors = declared is ModelSymbol model
            ? model.SelfAndAncestors()
            : [declared];

        foreach (DeclaredTypeSymbol type in selfAndAncestors)
        {
            foreach (Symbol member in type.Members.Values.SelectMany(m => m))
            {
                // Reachable only from inside, so offering it would suggest a line the next
                // keystroke refuses. A shorter list is the better one.
                if (!Reachable(member))
                {
                    continue;
                }

                if (already.Add(member.Name))
                {
                    yield return (member.Name, Describe(member), KindOf(member));
                }
            }
        }
    }

    /// <summary>
    /// <para>Whether a member could be written from somewhere else at all.</para>
    /// <para>Only the coarse half of the question: a private member is reachable from nowhere but
    /// its own type, so it never belongs on this list, while a protected or internal one depends
    /// on where the cursor is. Answering that properly means asking the checker, which is worth
    /// doing when there is somewhere to ask from — and offering a little too much is the kinder
    /// way to be wrong, since the reader is told plainly if they take it.</para>
    /// </summary>
    private static bool Reachable(Symbol member) => member switch
    {
        FieldSymbol field => field.Modifiers.OfMember() != Visibility.Private,
        FunctionSymbol function => function.Modifiers.OfMember() != Visibility.Private,
        _ => true,
    };

    /// <summary>What is shown beside a name: enough to choose between two of them.</summary>
    private static string Describe(BuiltInMember member) =>
        member.IsValue
            ? member.ReturnType?.ToString() ?? string.Empty
            : $"({string.Join(", ", member.ParameterTypes.Select(p => p?.ToString() ?? "anything"))})"
              + $" {member.ReturnType?.ToString() ?? "nothing"}";

    private static string Describe(Symbol member) => member switch
    {
        FieldSymbol field => field.Type.ToString() ?? string.Empty,
        FunctionSymbol function =>
            $"({string.Join(", ", function.Parameters.Select(p => $"{p.Type} {p.Name}"))})"
            + $" {function.ReturnType?.ToString() ?? "nothing"}",
        EnumMemberSymbol member2 => member2.Owner.Name,
        LocalSymbol local => local.Type.ToString() ?? string.Empty,
        ParameterSymbol parameter => parameter.Type.ToString() ?? string.Empty,
        DeclaredTypeSymbol type => type.Kind,
        _ => string.Empty,
    };

    /// <summary>The protocol's <c>CompletionItemKind</c> for the few kinds a member can be.</summary>
    private const int Method = 2;

    private const int Field = 5;

    private const int Property = 10;

    private const int EnumMember = 20;

    private const int Variable = 6;

    private const int Class = 7;

    private const int Enum = 13;

    private const int Keyword = 14;

    private static int KindOf(Symbol member) => member switch
    {
        FunctionSymbol => Method,
        FieldSymbol => Field,
        EnumMemberSymbol => EnumMember,
        EnumerationSymbol => Enum,
        DeclaredTypeSymbol or ModelSymbol => Class,
        LocalSymbol or ParameterSymbol => Variable,
        _ => Property,
    };

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
