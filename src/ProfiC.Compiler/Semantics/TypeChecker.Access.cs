using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    /// <summary>
    /// <para>Rejects a member reached from further away than it reaches.</para>
    /// <para>Four levels, and each is a question about where the access is written rather than
    /// about the member: private asks which type, protected asks which line of descent,
    /// internal asks which project, and public asks nothing.</para>
    /// <para>Reported once, where the member is named. The access still produces the member's
    /// type, so a program that reaches too far is told that and not also told every consequence
    /// of the answer it would have got.</para>
    /// </summary>
    private void RequireVisible(SyntaxNode where, Symbol member, string name)
    {
        if (Reaches(member))
        {
            return;
        }

        DeclaredTypeSymbol owner = member.DeclaringType!;
        Visibility visibility = VisibilityOf(member);

        Report(
            DiagnosticDescriptors.MemberIsNotVisible,
            where,
            name,
            visibility.Spell(),
            visibility == Visibility.Internal ? ProjectName(owner.Project) : owner.Name,
            Widening(visibility));
    }

    /// <summary>Whether the type being checked can reach a member from where it stands.</summary>
    private bool Reaches(Symbol member)
    {
        // A member the language provides, a local, or anything with no declaring type is not
        // something a program restricted, so there is nothing here to enforce.
        if (member.DeclaringType is not { } owner)
        {
            return true;
        }

        Visibility visibility = VisibilityOf(member);

        if (visibility == Visibility.Public)
        {
            return true;
        }

        // Outside every type there is no vantage point to judge from. The checker only walks
        // members, so this is a guard rather than a case a program can reach.
        if (_currentType is not { } from)
        {
            return false;
        }

        return visibility switch
        {
            Visibility.Private => ReferenceEquals(from, owner),
            Visibility.Protected => ReferenceEquals(from, owner) || Extends(from, owner),
            Visibility.Internal => string.Equals(from.Project, owner.Project, StringComparison.Ordinal),
            _ => true,
        };
    }

    /// <summary>Whether one type inherits from another, which is what <c>protected</c> asks.</summary>
    private static bool Extends(DeclaredTypeSymbol from, DeclaredTypeSymbol owner) =>
        from is ModelSymbol model
        && owner is ModelSymbol target
        && model.SelfAndAncestors().Any(ancestor => ReferenceEquals(ancestor, target));

    private static Visibility VisibilityOf(Symbol member) => member switch
    {
        FieldSymbol field => field.Visibility,
        FunctionSymbol function => function.Visibility,

        // A type declared inside another carries a visibility like any other member.
        DeclaredTypeSymbol { Container: DeclaredTypeSymbol } nested => nested.Visibility,

        // An enumeration member is reached as far as the enumeration holding it, since it
        // carries no visibility of its own.
        _ => Visibility.Public,
    };

    /// <summary>
    /// The word that would widen a member enough to be reached from where it was named. Shared
    /// with the resolver, which asks the same question of a type declared inside another.
    /// </summary>
    internal static string WideningFor(Visibility visibility) => Widening(visibility);

    /// <summary>The word that would let this member be reached from where it was named.</summary>
    private static string Widening(Visibility visibility) => visibility switch
    {
        Visibility.Private => "Mark it 'public' to allow that, or 'internal' to allow it "
                              + "anywhere in this project.",
        Visibility.Protected => "Mark it 'public' to allow that, or 'internal' to allow it "
                                + "anywhere in this project.",
        Visibility.Internal => "Mark it 'public' if another project is meant to use it.",
        _ => string.Empty,
    };

    /// <summary>
    /// What to call a project in a message. A compilation nobody divided has one project with
    /// no name, and calling it "this project" reads better than quoting an empty string.
    /// </summary>
    internal static string ProjectName(string project) =>
        project.Length == 0 ? "this project" : project;
}
