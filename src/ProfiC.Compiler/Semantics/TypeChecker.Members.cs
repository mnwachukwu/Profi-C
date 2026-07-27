using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    /// <summary>
    /// <para>Works out what a member access denotes.</para>
    /// <para>Two shapes reach here: a member of a value, and a member of a type. The second is
    /// how a global member is reached, since a bare name never finds one.</para>
    /// </summary>
    private TypeSymbol CheckMember(MemberExpr member)
    {
        // A type name on the left means a global member, so the receiver is not a value.
        if (member.Receiver is IdentifierExpr identifier
            && _model.GetSymbol(identifier) is DeclaredTypeSymbol declaredType)
        {
            _model.BindType(identifier, declaredType);
            return CheckStaticMember(member, declaredType);
        }

        TypeSymbol receiver = CheckExpression(member.Receiver);

        if (receiver.IsError)
        {
            return ErrorType.Instance;
        }

        // Members the language provides come first, so that a set answers Count() and an
        // optional answers HasValue() without either being declared anywhere.
        if (FindBuiltIn(member.Receiver, receiver, member.MemberName) is { } builtIn)
        {
            _model.BindType(member, TypeOfBuiltIn(builtIn));
            return TypeOfBuiltIn(builtIn);
        }

        if (receiver is DeclaredTypeSymbol declared)
        {
            IReadOnlyList<Symbol> found = declared is ModelSymbol model
                ? model.LookupIncludingBase(member.MemberName)
                : declared.Lookup(member.MemberName);

            if (found.Count > 0)
            {
                return BindMember(member, found);
            }
        }

        Report(DiagnosticDescriptors.MemberNotFound, member, receiver.WithArticleCapitalized(), member.MemberName);
        return ErrorType.Instance;
    }

    /// <summary>A member reached through a type name: a global member, or an enumeration's.</summary>
    private TypeSymbol CheckStaticMember(MemberExpr member, DeclaredTypeSymbol type)
    {
        if (BuiltInMembers.Find(type, member.MemberName) is { } builtIn)
        {
            return TypeOfBuiltIn(builtIn);
        }

        IReadOnlyList<Symbol> found = type is ModelSymbol model
            ? model.LookupIncludingBase(member.MemberName)
            : type.Lookup(member.MemberName);

        if (found.Count > 0)
        {
            return BindMember(member, found);
        }

        Report(DiagnosticDescriptors.MemberNotFound, member, type.WithArticleCapitalized(), member.MemberName);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Records which member an access refers to and gives back its type. Where several share
    /// a name they are overloads, and the call settles which; the type here is the first,
    /// which a call replaces once it knows its arguments.
    /// </summary>
    private TypeSymbol BindMember(MemberExpr member, IReadOnlyList<Symbol> candidates)
    {
        Symbol first = candidates[0];
        _model.Bind(member, first);

        return first switch
        {
            FieldSymbol field => field.Type,
            EnumMemberSymbol enumMember => enumMember.Owner,
            FunctionSymbol function => function.AsType(),
            DeclaredTypeSymbol nested => nested,
            _ => ErrorType.Instance,
        };
    }

    private static TypeSymbol TypeOfBuiltIn(BuiltInMember member) =>
        member.ReturnType ?? PrimitiveType.Nothing;

    /// <summary>
    /// Finds a built-in member on a receiver, falling back to the type it was declared with.
    /// Narrowing makes a guarded optional read as its underlying type, but the optional's own
    /// members must stay reachable so that writing <c>n.Value()</c> anyway still works.
    /// </summary>
    private BuiltInMember? FindBuiltIn(Expression receiverExpression, TypeSymbol receiver, string name)
        => FindAllBuiltIn(receiverExpression, receiver, name).FirstOrDefault();

    private IReadOnlyList<BuiltInMember> FindAllBuiltIn(
        Expression receiverExpression,
        TypeSymbol receiver,
        string name)
    {
        IReadOnlyList<BuiltInMember> found = BuiltInMembers.FindAll(receiver, name);

        if (found.Count > 0)
        {
            return found;
        }

        if (UnnarrowedTypeOf(receiverExpression) is { } declared
            && !ReferenceEquals(declared, receiver))
        {
            return BuiltInMembers.FindAll(declared, name);
        }

        return [];
    }

    /// <summary>
    /// True when a built-in version could take these arguments. A null parameter type accepts
    /// anything, which is how a member that takes a value of any kind is described.
    /// </summary>
    private static bool Accepts(BuiltInMember member, List<TypeSymbol> arguments)
    {
        if (member.ParameterTypes.Count != arguments.Count)
        {
            return false;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (member.ParameterTypes[i] is { } expected
                && !Conversions.IsAssignable(arguments[i], expected))
            {
                return false;
            }
        }

        return true;
    }

    // ---- Calls --------------------------------------------------------------------------------

    /// <summary>
    /// <para>Checks a call, choosing among overloads where a name has several.</para>
    /// <para>An exact match wins outright. Otherwise every version that could accept the
    /// arguments is considered, and more than one surviving is an error rather than a guess:
    /// picking silently would make which version runs depend on rules nobody remembers.</para>
    /// </summary>
    private TypeSymbol CheckCall(CallExpr call)
    {
        List<TypeSymbol> arguments = [.. call.Arguments.Select(CheckExpression)];

        // A member call is the common shape, and needs the receiver to pick the overload.
        if (call.Callee is MemberExpr member)
        {
            return CheckMemberCall(call, member, arguments);
        }

        // "base(...)" chains to a parent constructor. It looks like a call on a value but is
        // not one: "base" alone names no value that could be invoked.
        if (call.Callee is ReceiverExpr { Receiver: ReceiverKind.Base })
        {
            return CheckBaseConstructorCall(call, arguments);
        }

        TypeSymbol callee = CheckExpression(call.Callee);

        if (callee.IsError)
        {
            return ErrorType.Instance;
        }

        if (callee is FunctionType functionType)
        {
            CheckArgumentsAgainst(call, "this function", functionType.ParameterTypes, arguments);
            return functionType.ReturnType ?? PrimitiveType.Nothing;
        }

        // Calling a type name constructs nothing; "new" does that.
        Report(DiagnosticDescriptors.NotCallable, call, callee.WithArticleCapitalized());
        return ErrorType.Instance;
    }

    /// <summary>
    /// Checks <c>base(...)</c>, which runs a parent's constructor rather than calling
    /// anything. The parent's constructors are its functions named for it.
    /// </summary>
    private TypeSymbol CheckBaseConstructorCall(CallExpr call, List<TypeSymbol> arguments)
    {
        if (_currentType is not ModelSymbol { BaseType: { } parent })
        {
            // Already reported by the resolver, which knows whether a parent exists.
            return PrimitiveType.Nothing;
        }

        List<FunctionSymbol> constructors =
            [.. parent.Lookup(parent.Name).OfType<FunctionSymbol>().Where(f => f.IsConstructor)];

        if (constructors.Count == 0)
        {
            // A parent with no constructor takes no arguments, so only an empty call fits.
            if (arguments.Count > 0)
            {
                Report(DiagnosticDescriptors.WrongArgumentCount, call, parent.Name, 0, arguments.Count);
            }

            return PrimitiveType.Nothing;
        }

        if (ResolveOverload(call, parent.Name, constructors, arguments) is { } chosen)
        {
            _model.Bind(call, chosen);
        }

        return PrimitiveType.Nothing;
    }

    private TypeSymbol CheckMemberCall(
        CallExpr call,
        MemberExpr member,
        List<TypeSymbol> arguments)
    {
        // A type name on the left reaches a global member.
        bool onType = member.Receiver is IdentifierExpr identifier
                      && _model.GetSymbol(identifier) is DeclaredTypeSymbol;

        TypeSymbol receiver = onType
            ? (TypeSymbol)_model.GetSymbol((IdentifierExpr)member.Receiver)!
            : CheckExpression(member.Receiver);

        if (receiver.IsError)
        {
            return ErrorType.Instance;
        }

        IReadOnlyList<BuiltInMember> builtIns =
            FindAllBuiltIn(member.Receiver, receiver, member.MemberName);

        if (builtIns.Count > 0)
        {
            // Pick by argument type, not merely by count. It matters for an optional's
            // "Or": given another optional the chain stays optional, and given a plain value
            // it ends with a definite one, and the two differ only in what they accept.
            BuiltInMember chosenBuiltIn =
                builtIns.FirstOrDefault(m => Accepts(m, arguments))
                ?? builtIns.FirstOrDefault(m => m.ParameterTypes.Count == arguments.Count)
                ?? builtIns[0];

            CheckArgumentsAgainst(call, member.MemberName, chosenBuiltIn.ParameterTypes, arguments);
            TypeSymbol result = TypeOfBuiltIn(chosenBuiltIn);
            _model.BindType(member, result);
            return result;
        }

        if (receiver is not DeclaredTypeSymbol declared)
        {
            Report(DiagnosticDescriptors.MemberNotFound, member, receiver.WithArticleCapitalized(), member.MemberName);
            return ErrorType.Instance;
        }

        IReadOnlyList<Symbol> candidates = declared is ModelSymbol model
            ? model.LookupIncludingBase(member.MemberName)
            : declared.Lookup(member.MemberName);

        List<FunctionSymbol> functions = [.. candidates.OfType<FunctionSymbol>()];

        if (functions.Count == 0)
        {
            Report(DiagnosticDescriptors.MemberNotFound, member, receiver.WithArticleCapitalized(), member.MemberName);
            return ErrorType.Instance;
        }

        FunctionSymbol? chosen = ResolveOverload(call, member.MemberName, functions, arguments);

        if (chosen is null)
        {
            return ErrorType.Instance;
        }

        _model.Bind(member, chosen);
        _model.Bind(call, chosen);

        return chosen.ReturnType ?? PrimitiveType.Nothing;
    }

    private FunctionSymbol? ResolveOverload(
        CallExpr call,
        string name,
        List<FunctionSymbol> candidates,
        List<TypeSymbol> arguments)
    {
        List<FunctionSymbol> byArity =
            [.. candidates.Where(f => f.Parameters.Count == arguments.Count)];

        if (byArity.Count == 0)
        {
            Report(
                DiagnosticDescriptors.WrongArgumentCount,
                call,
                name,
                candidates[0].Parameters.Count,
                arguments.Count);

            return null;
        }

        if (byArity.Count == 1)
        {
            CheckArgumentsAgainst(
                call, name, [.. byArity[0].Parameters.Select(p => (TypeSymbol?)p.Type)], arguments);

            return byArity[0];
        }

        // An exact match settles it outright, without weighing conversions against each other.
        List<FunctionSymbol> exact =
            [.. byArity.Where(f => f.Parameters
                .Select((p, i) => Conversions.Classify(arguments[i], p.Type))
                .All(k => k == ConversionKind.Identity))];

        if (exact.Count == 1)
        {
            return exact[0];
        }

        List<FunctionSymbol> applicable =
            [.. byArity.Where(f => f.Parameters
                .Select((p, i) => Conversions.IsAssignable(arguments[i], p.Type))
                .All(ok => ok))];

        switch (applicable.Count)
        {
            case 0:
                Report(DiagnosticDescriptors.NoMatchingOverload, call, name);
                return null;

            case 1:
                return applicable[0];

            default:
                // Two versions reachable only by conversion is a tie, and a tie is reported
                // rather than broken.
                Report(DiagnosticDescriptors.AmbiguousOverload, call, name);
                return null;
        }
    }

    /// <summary>
    /// Checks arguments against a fixed parameter list. A null parameter type accepts
    /// anything, which is how a member that takes a value of any kind is described.
    /// </summary>
    private void CheckArgumentsAgainst(
        CallExpr call,
        string name,
        IReadOnlyList<TypeSymbol?> parameters,
        List<TypeSymbol> arguments)
    {
        if (parameters.Count != arguments.Count)
        {
            Report(
                DiagnosticDescriptors.WrongArgumentCount,
                call,
                name,
                parameters.Count,
                arguments.Count);

            return;
        }

        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] is { } expected)
            {
                RequireAssignable(arguments[i], expected, call.Arguments[i]);
            }
        }
    }
}
