using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    /// <summary>
    /// <para>The type a receiver names, or null where it is a value.</para>
    /// <para>Only a written name can name a type: an identifier, or a run of them joined by
    /// dots. <c>this</c> is bound to the type around it and is still a value rather than that
    /// type's name, which is the difference between reading a field and reaching a shared one.
    /// </para>
    /// </summary>
    private DeclaredTypeSymbol? TypeNamedBy(Expression receiver) =>
        receiver is IdentifierExpr or MemberExpr
            ? _model.GetSymbol(receiver) as DeclaredTypeSymbol
            : null;

    /// <summary>
    /// <para>Works out what a member access denotes.</para>
    /// <para>Two shapes reach here: a member of a value, and a member of a type. The second is
    /// how a shared member is reached, since a bare name never finds one.</para>
    /// </summary>
    private TypeSymbol CheckMember(MemberExpr member)
    {
        // A type name on the left means a shared member, so the receiver is not a value.
        // Asked of the receiver whatever shape it is: a qualified name is a run of member
        // accesses that the resolver already settled onto one type, and it names that type
        // exactly as a bare identifier does.
        if (TypeNamedBy(member.Receiver) is { } declaredType)
        {
            _model.BindType(member.Receiver, declaredType);
            return CheckStaticMember(member, declaredType);
        }

        TypeSymbol receiver = CheckExpression(member.Receiver);

        if (receiver.IsError)
        {
            return ErrorType.Instance;
        }

        // A call with no result has no members at all — not even ToString and Equals, which
        // every other type inherits from Model. There is no value for them to describe.
        if (ReferenceEquals(receiver, PrimitiveType.Void))
        {
            Report(DiagnosticDescriptors.ValueExpected, member.Receiver);
            return ErrorType.Instance;
        }

        // Members the language provides come first, so that a set answers Count() and an
        // optional answers HasValue() without either being declared anywhere.
        if (FindBuiltIn(member.Receiver, receiver, member.MemberName) is { } builtIn)
        {
            // Only an uncalled access reaches here; a call is handled by CheckMemberCall.
            return ReadUncalled(member, builtIn);
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

    /// <summary>
    /// <para>Reads a member of the language's own, named without being called.</para>
    /// <para>A value is what it is; a function named this way is missing its parentheses, and
    /// is reported rather than yielding the type it would have produced if called. Both give
    /// that type back either way, so one mistake does not become several.</para>
    /// </summary>
    private TypeSymbol ReadUncalled(MemberExpr member, BuiltInMember builtIn)
    {
        if (builtIn.IsValue)
        {
            RecordBuiltIn(member, builtIn);
        }
        else
        {
            Report(DiagnosticDescriptors.BuiltInMemberNeedsCall, member, member.MemberName);
        }

        TypeSymbol type = TypeOfBuiltIn(builtIn);
        _model.BindType(member, type);
        return type;
    }

    /// <summary>A member reached through a type name: a shared member, or an enumeration's.</summary>
    private TypeSymbol CheckStaticMember(MemberExpr member, DeclaredTypeSymbol type)
    {
        if (BuiltInMembers.Find(type, member.MemberName) is { } builtIn)
        {
            return ReadUncalled(member, builtIn);
        }

        IReadOnlyList<Symbol> found = type is ModelSymbol model
            ? model.LookupIncludingBase(member.MemberName)
            : type.Lookup(member.MemberName);

        if (found.Count > 0)
        {
            RequireShared(member, type, found[0]);
            return BindMember(member, found);
        }

        Report(DiagnosticDescriptors.MemberNotFound, member, type.WithArticleCapitalized(), member.MemberName);
        return ErrorType.Instance;
    }

    /// <summary>
    /// <para>Rejects an instance member reached through the name of its type.</para>
    /// <para>Without this the member binds, produces its declared type, and yields nothing at
    /// all when the program runs — the mistake stays quiet right up until the wrong answer
    /// appears. Enumeration members and nested types belong to the type itself, so they pass.
    /// </para>
    /// </summary>
    private void RequireShared(MemberExpr member, DeclaredTypeSymbol type, Symbol found)
    {
        bool needsInstance = found switch
        {
            FieldSymbol field => !field.IsShared,
            FunctionSymbol function => !function.IsShared && !function.IsConstructor,
            _ => false,
        };

        if (needsInstance)
        {
            Report(DiagnosticDescriptors.MemberNeedsInstance, member, member.MemberName, type.Name);
        }
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
        RequireVisible(member, first, member.MemberName);

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
        member.ReturnType ?? PrimitiveType.Void;

    /// <summary>
    /// Writes down which member the language provides was chosen, so the back end carries out
    /// that decision rather than making its own from the value in hand.
    /// </summary>
    private void RecordBuiltIn(SyntaxNode node, BuiltInMember chosen)
    {
        if (chosen.Id is { } id)
        {
            _model.BindBuiltIn(node, id);
        }
    }

    /// <summary>Whether a type declares a member of this name itself, inheritance included.</summary>
    private static bool DeclaresMember(TypeSymbol type, string name) => type switch
    {
        ModelSymbol model => model.LookupIncludingBase(name).Count > 0,
        DeclaredTypeSymbol declared => declared.Lookup(name).Count > 0,
        _ => false,
    };

    /// <summary>
    /// <para>Finds a built-in member on a receiver, falling back to the type it was declared
    /// with.</para>
    /// <para>Narrowing makes a guarded optional read as its underlying type, and the optional's
    /// own members stay reachable through that fallback, so writing <c>n.Value()</c> on one
    /// still unwraps it.</para>
    /// <para>The fallback stops at a member the narrowed type declares for itself. Inside the
    /// guard the receiver <em>is</em> that type, so a model declaring <c>Value</c> means its
    /// own — otherwise a name resolves to the optional's member while every other name on the
    /// same receiver resolves to the model's.</para>
    /// </summary>
    private BuiltInMember? FindBuiltIn(Expression receiverExpression, TypeSymbol receiver, string name)
        => FindAllBuiltIn(receiverExpression, receiver, name).FirstOrDefault();

    private IReadOnlyList<BuiltInMember> FindAllBuiltIn(
        Expression receiverExpression,
        TypeSymbol receiver,
        string name)
    {
        // A member the type declares for itself wins, which is what makes ToString and Equals
        // overridable: both are members every model inherits, and one inherited from Model that
        // could not be replaced would be a promise the language makes and does not keep.
        if (DeclaresMember(receiver, name))
        {
            return [];
        }

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
    private static bool Accepts(BuiltInMember member, List<TypeSymbol> arguments) =>
        Matches(member, arguments, exactly: false);

    /// <summary>
    /// <para>True when a built-in version takes these arguments with no conversion at all.</para>
    /// <para>Preferred over one that merely accepts them, for the same reason a declared
    /// function's overloads are: an integer widens to both a real and a fraction, so
    /// <c>Math.Abs(-3)</c> fits three versions and only one of them is what was written.
    /// Without this the order they happen to be listed in would decide.</para>
    /// </summary>
    private static bool AcceptsExactly(BuiltInMember member, List<TypeSymbol> arguments) =>
        Matches(member, arguments, exactly: true);

    private static bool Matches(BuiltInMember member, List<TypeSymbol> arguments, bool exactly)
    {
        if (member.ParameterTypes.Count != arguments.Count)
        {
            return false;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (member.ParameterTypes[i] is not { } expected)
            {
                continue;
            }

            bool fits = exactly
                ? Conversions.Classify(arguments[i], expected) == ConversionKind.Identity
                : Conversions.IsAssignable(arguments[i], expected);

            if (!fits)
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
        List<TypeSymbol> arguments = [.. call.Arguments.Select(CheckArgument)];

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
            return functionType.ReturnType ?? PrimitiveType.Void;
        }

        Report(
            DiagnosticDescriptors.NotCallable,
            call,
            callee.WithArticleCapitalized(),
            WhatToDoInstead(callee, TypeNamedBy(call.Callee)));

        return ErrorType.Instance;
    }

    /// <summary>
    /// <para>What to do about something written with parentheses after it that is not a
    /// function.</para>
    /// <para>There are three ways to arrive here and each has its own answer, so each gets it.
    /// An <b>optional holding a function</b> is the near miss: the function is right there, one
    /// check away, and the check is the same one the language asks for before reading anything
    /// out of an optional. A <b>type name</b> is almost always <c>new</c> written without the
    /// word — the reader has said which type they want and how to build it, and left out only
    /// the part that says build it. Anything else is a value that is not a function and never
    /// was, where the useful thing to say is what a call needs rather than what this is.</para>
    /// </summary>
    private static string WhatToDoInstead(TypeSymbol callee, DeclaredTypeSymbol? named)
    {
        if (callee is OptionalType { UnderlyingType: FunctionType })
        {
            return "Check it with 'HasValue()' first, or unwrap it with 'Value()'.";
        }

        return named is not null
            ? $"Write 'new {named.Name}(...)' to build one."
            : "Only a function can be called.";
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
            return PrimitiveType.Void;
        }

        List<FunctionSymbol> constructors =
            [.. parent.Lookup(parent.Name).OfType<FunctionSymbol>().Where(f => f.IsConstructor)];

        if (constructors.Count == 0)
        {
            // The built-in Exception declares nothing a program can see, but it does take the
            // message every exception carries. That one form is allowed through.
            if (BuiltInMembers.IsException(parent)
                && arguments is [{ } only]
                && Conversions.IsAssignable(only, PrimitiveType.String))
            {
                return PrimitiveType.Void;
            }

            // A parent with no constructor takes no arguments, so only an empty call fits.
            if (arguments.Count > 0)
            {
                Report(
                    DiagnosticDescriptors.WrongArgumentCount,
                    call,
                    parent.Name,
                    Wording.Count(0, "argument"),
                    arguments.Count);
            }

            return PrimitiveType.Void;
        }

        if (ResolveOverload(call, parent.Name, constructors, arguments) is { } chosen)
        {
            _model.Bind(call, chosen);
        }

        return PrimitiveType.Void;
    }

    private TypeSymbol CheckMemberCall(
        CallExpr call,
        MemberExpr member,
        List<TypeSymbol> arguments)
    {
        // A type name on the left reaches a shared member, however that name was written.
        DeclaredTypeSymbol? named = TypeNamedBy(member.Receiver);
        bool onType = named is not null;

        TypeSymbol receiver = named ?? CheckExpression(member.Receiver);

        if (receiver.IsError)
        {
            return ErrorType.Instance;
        }

        // As above: nothing can be called on the result of a call that produced none.
        if (!onType && ReferenceEquals(receiver, PrimitiveType.Void))
        {
            Report(DiagnosticDescriptors.ValueExpected, member.Receiver);
            return ErrorType.Instance;
        }

        IReadOnlyList<BuiltInMember> builtIns =
            FindAllBuiltIn(member.Receiver, receiver, member.MemberName);

        if (builtIns.Count > 0)
        {
            // Pick by argument type, not merely by count. It matters for an optional's
            // "Or": given another optional the chain stays optional, and given a plain value
            // it ends with a definite one, and the two differ only in what they accept.
            //
            // An exact match settles it outright, before any widening is weighed, so that a
            // number keeps the type it was written as rather than the first one it fits.
            BuiltInMember chosenBuiltIn =
                builtIns.FirstOrDefault(m => !m.IsValue && AcceptsExactly(m, arguments))
                ?? builtIns.FirstOrDefault(m => !m.IsValue && Accepts(m, arguments))
                ?? builtIns.FirstOrDefault(m => !m.IsValue && m.ParameterTypes.Count == arguments.Count)
                ?? builtIns[0];

            // A value written with parentheses is the mirror of a function written without
            // them, and is said the same way rather than being quietly called.
            if (chosenBuiltIn.IsValue)
            {
                Report(DiagnosticDescriptors.BuiltInMemberIsNotCalled, member, member.MemberName);

                TypeSymbol valueType = TypeOfBuiltIn(chosenBuiltIn);
                _model.BindType(member, valueType);
                return valueType;
            }

            RecordBuiltIn(member, chosenBuiltIn);
            CheckArgumentsAgainst(call, member.MemberName, chosenBuiltIn.ParameterTypes, arguments);

            // Only a literal written on the spot. A named constant that happens to be empty
            // was named deliberately, and saying anything about it would be arguing with the
            // name rather than with the code.
            if (chosenBuiltIn.Id is BuiltInId.ConsoleWriteLine
                && call.Arguments is [LiteralExpr { Kind: LiteralKind.String, Text: "\"\"" }])
            {
                Report(DiagnosticDescriptors.EmptyLineNeedsNoArgument, call.Arguments[0]);
            }

            // Asking whether two values are the same object, which is a question a value has no
            // answer to. Checked here rather than by the parameter types because the two this
            // takes are written as accepting anything — which is right for everything else that
            // does, and wrong for exactly this one.
            // Once, at the first one that is a value: both sides being values is one mistake
            // rather than two, and the same sentence twice reads as a compiler stuttering.
            if (chosenBuiltIn.Id is BuiltInId.ReferenceEquals
                && call.Arguments.FirstOrDefault(
                       a => _model.GetType(a) is { IsValueType: true, IsError: false }) is { } written)
            {
                Report(
                    DiagnosticDescriptors.ValuesHaveNoIdentity,
                    written,
                    _model.GetType(written)!.WithArticleCapitalized());
            }

            // A fraction with a denominator of zero is the same mistake as dividing by zero,
            // so it is caught in the same place when it can be seen while compiling.
            if (onType
                && receiver is ModelSymbol { Name: "Fraction" }
                && member.MemberName == "Create"
                && call.Arguments.Count == 2
                && ConstantFolder.IsZero(ConstantFolder.TryFold(call.Arguments[1], _model)))
            {
                Report(DiagnosticDescriptors.DivisionByZero, call.Arguments[1]);
            }

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

        List<FunctionSymbol> functions = [.. VersionsOf(declared, member.MemberName)];

        // A member holding a function is called through, once no function of that name answers.
        // Second rather than first only for tidiness: a type's members have distinct names, so
        // the two cannot both be there to choose between.
        if (functions.Count == 0)
        {
            return CheckCallThroughMember(call, member, declared, onType, candidates, arguments);
        }

        FunctionSymbol? chosen = ResolveOverload(call, member.MemberName, functions, arguments);

        if (chosen is null)
        {
            return ErrorType.Instance;
        }

        if (onType)
        {
            RequireShared(member, declared, chosen);
        }

        RequireVisible(member, chosen, member.MemberName);

        _model.Bind(member, chosen);
        _model.Bind(call, chosen);

        return chosen.ReturnType ?? PrimitiveType.Void;
    }

    /// <summary>
    /// <para>Every version of a name a type answers to, its ancestors included.</para>
    /// <para><b>Collected down the whole chain rather than stopped at the nearest model that
    /// declares the name.</b> Stopping there hides every version a parent wrote: adding
    /// <c>Which(string)</c> to a child took <c>Which(integer)</c> away from it, so a call that
    /// compiled before the child existed stopped compiling because of a function that has
    /// nothing to do with it.</para>
    /// <para>A version already found wins over one further up taking the same types, which is one
    /// rule doing two jobs. An override replaces what it overrides — and without that, an
    /// override and the virtual behind it would be two candidates fitting equally well, so every
    /// call to an overridden function would be reported as a tie.</para>
    /// </summary>
    private static IEnumerable<FunctionSymbol> VersionsOf(DeclaredTypeSymbol type, string name)
    {
        IEnumerable<DeclaredTypeSymbol> chain = type is ModelSymbol model
            ? model.SelfAndAncestors()
            : [type];

        List<FunctionSymbol> found = [];

        foreach (DeclaredTypeSymbol current in chain)
        {
            foreach (FunctionSymbol version in current.Lookup(name).OfType<FunctionSymbol>())
            {
                if (!found.Any(nearer => Conversions.SameParameters(nearer, version)))
                {
                    found.Add(version);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// <para>Calls what a member holds, where the member is a value of function type rather than
    /// a function.</para>
    /// <para><b>A function kept in a field is called like any other function.</b> Without this a
    /// name after a dot could only be called where it was declared with <c>function</c>, and the
    /// way round it was to copy the field into a local and call that — one extra line producing
    /// exactly the same call, so the rule charged for itself and protected nothing.</para>
    /// <para>The member's type is written down, which is what tells the back ends apart from an
    /// ordinary call: both reach the value through the member and invoke what they find, rather
    /// than choosing a body from the name.</para>
    /// <para>A member that is not a function value says so, rather than being reported missing.
    /// It is not missing — it can be read, assigned, and handed around — and saying otherwise
    /// sends a reader looking for a declaration that is right there.</para>
    /// </summary>
    private TypeSymbol CheckCallThroughMember(
        CallExpr call,
        MemberExpr member,
        DeclaredTypeSymbol declared,
        bool onType,
        IReadOnlyList<Symbol> candidates,
        List<TypeSymbol> arguments)
    {
        if (candidates.Count == 0)
        {
            Report(
                DiagnosticDescriptors.MemberNotFound,
                member,
                declared.WithArticleCapitalized(),
                member.MemberName);

            return ErrorType.Instance;
        }

        if (candidates[0] is not FieldSymbol { Type: FunctionType held } field)
        {
            TypeSymbol reached = BindMember(member, candidates);

            Report(
                DiagnosticDescriptors.NotCallable,
                call,
                reached.WithArticleCapitalized(),
                WhatToDoInstead(reached, named: null));

            return ErrorType.Instance;
        }

        if (onType)
        {
            RequireShared(member, declared, field);
        }

        RequireVisible(member, field, member.MemberName);

        _model.Bind(member, field);
        _model.BindType(member, held);

        CheckArgumentsAgainst(call, member.MemberName, held.ParameterTypes, arguments);

        return held.ReturnType ?? PrimitiveType.Void;
    }

    private FunctionSymbol? ResolveOverload(
        CallExpr call,
        string name,
        List<FunctionSymbol> candidates,
        List<TypeSymbol> arguments) =>
        ResolveOverload(call, call.Arguments, name, candidates, arguments);

    /// <summary>
    /// Chooses among overloads at any site that passes arguments, which is a call or a
    /// <c>new</c>. Both need the same rules, and a constructor left unresolved would let a
    /// value of any type reach a field of any other.
    /// </summary>
    private FunctionSymbol? ResolveOverload(
        SyntaxNode site,
        IReadOnlyList<Expression> written,
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
                site,
                name,
                Wording.Count(candidates[0].Parameters.Count, "argument"),
                arguments.Count);

            return null;
        }

        if (byArity.Count == 1)
        {
            CheckArgumentsAgainst(
                site,
                written,
                name,
                [.. byArity[0].Parameters.Select(p => (TypeSymbol?)p.Type)],
                arguments);

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
                Report(DiagnosticDescriptors.NoMatchingOverload, site, name);
                return null;

            case 1:
                return applicable[0];

            default:
                // Two versions reachable only by conversion is a tie, and a tie is reported
                // rather than broken.
                Report(DiagnosticDescriptors.AmbiguousOverload, site, name);
                return null;
        }
    }

    /// <summary>
    /// <para>Works out one argument's type, or holds it back if it has none yet.</para>
    /// <para>A lambda written with bare parameter names is the only argument that cannot be
    /// checked where it stands, since what it means depends on the parameter it is being
    /// passed to — and which parameter that is depends on which version of an overloaded name
    /// was chosen. It stands as the error type until the arguments are checked against a
    /// chosen signature, which also keeps it from steering the choice it is waiting on.</para>
    /// </summary>
    private TypeSymbol CheckArgument(Expression argument) =>
        NeedsATarget(argument) ? ErrorType.Instance : CheckExpression(argument);

    /// <summary>
    /// Checks arguments against a fixed parameter list. A null parameter type accepts
    /// anything, which is how a member that takes a value of any kind is described.
    /// </summary>
    private void CheckArgumentsAgainst(
        CallExpr call,
        string name,
        IReadOnlyList<TypeSymbol?> parameters,
        List<TypeSymbol> arguments) =>
        CheckArgumentsAgainst(call, call.Arguments, name, parameters, arguments);

    private void CheckArgumentsAgainst(
        SyntaxNode site,
        IReadOnlyList<Expression> written,
        string name,
        IReadOnlyList<TypeSymbol?> parameters,
        List<TypeSymbol> arguments)
    {
        if (parameters.Count != arguments.Count)
        {
            Report(
                DiagnosticDescriptors.WrongArgumentCount,
                site,
                name,
                Wording.Count(parameters.Count, "argument"),
                arguments.Count);

            return;
        }

        for (int i = 0; i < parameters.Count; i++)
        {
            // The lambdas held back in CheckCall are checked here, now that the parameter they
            // are being passed to says what their bare names stand for. A parameter that takes
            // a value of any kind says nothing, so the lambda is checked without a target and
            // reports the names it could not settle rather than passing silently.
            if (NeedsATarget(written[i]))
            {
                arguments[i] = parameters[i] is { } target
                    ? CheckExpressionAgainst(written[i], target)
                    : CheckExpression(written[i]);
            }
            else if (written[i] is LambdaExpr lambda
                     && parameters[i] is { } declared
                     && TargetFor(declared) is { } wanted)
            {
                // One whose types were all written needed no target and was checked on its own
                // terms above. The target is still worth reading against it, since it is what
                // makes those types unnecessary.
                MatchParametersToTarget(lambda, wanted);
            }

            // A parameter that takes a value of any kind still takes a value. Nothing is not a
            // kind of value, so a call that produced none is refused here rather than passed on
            // as though it had.
            if (ReferenceEquals(arguments[i], PrimitiveType.Void))
            {
                Report(DiagnosticDescriptors.ValueExpected, written[i]);
                continue;
            }

            if (parameters[i] is { } expected)
            {
                RequireAssignable(arguments[i], expected, written[i]);
            }
        }
    }
}
