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
        if (ReceiverType(path, source, offset, read, cancellation) is not var (found, within)
            || found is not { IsError: false } receiver)
        {
            return null;
        }

        JsonArray offered = [];

        foreach ((string name, string detail, int kind, string said) in MembersOf(receiver, within))
        {
            JsonObject item = new()
            {
                ["label"] = name,
                ["detail"] = detail,
                ["kind"] = kind,
            };

            // What it is for, shown in the panel beside the list rather than in the row. The row
            // has the signature on it already, and a second line of prose per row would make a
            // list nobody can scan.
            if (said.Length > 0)
            {
                item["documentation"] = new JsonObject
                {
                    ["kind"] = "markdown",
                    ["value"] = said,
                };
            }

            offered.Add(item);
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
    private static (TypeSymbol Receiver, DeclaredTypeSymbol? Within)? ReceiverType(
        string path,
        SourceText source,
        int offset,
        SourceReader read,
        CancellationToken cancellation)
    {
        SourceText completing = new(
            source.Text[..offset] + Standing(source.Text, offset) + source.Text[offset..],
            source.FileName);

        if (Compiled(path, completing, read, cancellation) is not var (model, unit))
        {
            return null;
        }

        // The member access the placeholder made. Found by name rather than by position, since
        // inserting the text moved every offset after it.
        //
        // Held anywhere in the name rather than at its end: the cursor is not always past what is
        // typed. Putting it after the dot in 'Greeting.Words()' — which is what somebody editing
        // a line they already wrote does — makes the placeholder the start of the name and not
        // the finish of it.
        MemberExpr? access = Everything(unit)
            .OfType<MemberExpr>()
            .FirstOrDefault(m => m.MemberName.Contains(Placeholder, StringComparison.Ordinal));

        // Which type the cursor sits inside, which is what decides whether a private member is
        // reachable from here. Taken at the placeholder, since that is where the caret is.
        DeclaredTypeSymbol? within = model.NamesAt(unit.Source, offset)?.EnclosingType;

        if (access is null)
        {
            // Nothing carrying the placeholder means the file did not parse around it, which the
            // placeholder cannot fix: it makes the cursor's own line a statement and says nothing
            // about the one above. A name half typed on the line before is a parse error, and
            // recovery does not always reach past it — and that line is where the caret just was,
            // so this is the ordinary case rather than a rare one.
            return NamedBefore(path, source, offset, read, cancellation) is { } fallback
                ? (fallback, within)
                : null;
        }

        // A receiver that names a type holds no value, so what it is worth asking about is the
        // type it names rather than any type recorded for it. Asked the other way round, a model
        // reached through its own name offers nothing the moment the member beside it does not
        // resolve — which it never does, since the member is a name nobody declared.
        TypeSymbol? receiver =
            model.GetSymbol(access.Receiver) as TypeSymbol ?? model.GetType(access.Receiver);

        return receiver is null ? null : (receiver, within);
    }

    /// <summary>
    /// <para>The type of the plain name written before the dot, found without the tree.</para>
    /// <para><b>What the placeholder trick falls back to when the file will not parse around the
    /// cursor.</b> It asks a smaller question — not "what is the type of whatever expression
    /// precedes this dot" but "there is a single name here; what is it?" — and answers it from
    /// the names in force at that point, which are recorded against stretches of the file and so
    /// survive a line that is not a statement.</para>
    /// <para>A single name only. <c>counter.</c> and <c>Math.</c> are answered; <c>f(x).</c> is
    /// not, and does not need to be — an expression that involved is one somebody finished
    /// writing, and a finished line parses.</para>
    /// </summary>
    private static TypeSymbol? NamedBefore(
        string path, SourceText source, int offset, SourceReader read, CancellationToken cancellation)
    {
        string text = source.Text;

        int at = offset;

        while (at > 0 && IsNamePart(text[at - 1]))
        {
            at--;
        }

        if (at == 0 || text[at - 1] != '.')
        {
            return null;
        }

        int end = at - 1;
        int start = end;

        while (start > 0 && IsNamePart(text[start - 1]))
        {
            start--;
        }

        if (start == end)
        {
            return null;
        }

        string name = text[start..end];

        if (Compiled(path, source, read, cancellation) is not var (model, unit))
        {
            return null;
        }

        if (model.NamesAt(unit.Source, start) is not { } names)
        {
            return null;
        }

        return names.Visible()
            .Where(symbol => string.Equals(symbol.Name, name, StringComparison.Ordinal))
            .Select(Held)
            .FirstOrDefault(type => type is { IsError: false });
    }

    /// <summary>The type a name stands for: what it holds, or the type it names outright.</summary>
    private static TypeSymbol? Held(Symbol symbol) => symbol switch
    {
        LocalSymbol local => local.Type,
        ParameterSymbol parameter => parameter.Type,
        FieldSymbol field => field.Type,
        TypeSymbol type => type,
        _ => null,
    };

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// <para>The name to put where the member will go, and a semicolon after it where the line
    /// needs one.</para>
    /// <para><b>Inserting the name is not enough on its own, and the case it misses is the
    /// ordinary one.</b> <c>counter.</c> on a line by itself becomes
    /// <c>counter.__profi_c_completing__</c> — an expression with no semicolon, which is not a
    /// statement, so the parser recovers by discarding it and there is no member access left to
    /// read a receiver off. The trick works only when the line already parses, which is exactly
    /// when nobody needs it: written inside a call that is already closed, as a test fixture
    /// tends to be.</para>
    /// <para>Only where the rest of the line is blank. A dot typed in the middle of something —
    /// <c>Console.WriteLine(counter.)</c> — has its terminator already, and adding another would
    /// make a worse program than the one it was given.</para>
    /// </summary>
    private static string Standing(string text, int offset)
    {
        int at = offset;

        while (at < text.Length && text[at] is not ('\n' or '\r'))
        {
            if (!char.IsWhiteSpace(text[at]))
            {
                return Placeholder;
            }

            at++;
        }

        return Placeholder + ";";
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

        // Found by path, the way every other question here finds it. By reference it depends on
        // the text handed to the reader being the very object that comes back on the unit, which
        // holds when the reader is asked for exactly the path it was given and not otherwise —
        // and a file reached a second way is then a unit that looks unrelated to the one asked
        // about.
        CompilationUnit? unit = compilation.Units.FirstOrDefault(
            u => SourceDiscovery.PathComparer.Equals(
                Path.GetFullPath(u.Source.FileName), Path.GetFullPath(path)));

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
    private static IEnumerable<(string Name, string Detail, int Kind, string Summary)> MembersOf(
        TypeSymbol receiver, DeclaredTypeSymbol? within)
    {
        HashSet<string> already = new(StringComparer.Ordinal);

        foreach (BuiltInMember member in BuiltInMembers.On(receiver))
        {
            if (already.Add(member.Name))
            {
                yield return (
                    member.Name,
                    Describe(member),
                    member.IsValue ? Property : Method,
                    member.Id is { } id ? BuiltInDocs.Summary(id) : string.Empty);
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
                if (!Reachable(member, within))
                {
                    continue;
                }

                if (already.Add(member.Name))
                {
                    yield return (member.Name, Describe(member), KindOf(member), string.Empty);
                }
            }
        }
    }

    /// <summary>
    /// <para>Whether a member can be written from where the cursor is.</para>
    /// <para><b>A member written with no visibility at all is private</b>, which is most of them
    /// in most files. Left out on the grounds that a private member is reachable from nowhere,
    /// the list drops nearly everything in the model somebody is working inside — the one type
    /// whose members they ask for most.</para>
    /// <para>So where the cursor is decides it. Inside the type that declares a member, or inside
    /// one nested in it, a private member is exactly as reachable as any other.</para>
    /// <para>Protected and internal are still offered from anywhere, which is the coarse half
    /// left: being a little too generous is the kinder way to be wrong, since the compiler says
    /// so plainly if the suggestion is taken.</para>
    /// </summary>
    private static bool Reachable(Symbol member, DeclaredTypeSymbol? within)
    {
        Visibility visibility = member switch
        {
            FieldSymbol field => field.Modifiers.OfMember(),
            FunctionSymbol function => function.Modifiers.OfMember(),
            _ => Visibility.Public,
        };

        return visibility != Visibility.Private
            || (member.DeclaringType is { } owner && Inside(owner, within));
    }

    /// <summary>Whether the cursor sits in a type, or in one nested inside it.</summary>
    private static bool Inside(DeclaredTypeSymbol owner, DeclaredTypeSymbol? within)
    {
        for (Symbol? here = within; here is not null;)
        {
            if (ReferenceEquals(here, owner))
            {
                return true;
            }

            here = here is DeclaredTypeSymbol nested ? nested.Container : null;
        }

        return false;
    }

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
