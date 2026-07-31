using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    /// <summary>
    /// <para>Holds every function that redeclares one from a base model to its word.</para>
    /// <para>Runs after signatures are settled, since matching an override to what it overrides
    /// compares parameter types and those are not known until then.</para>
    /// <para>Three questions, and they are the same question asked from either side. Does a
    /// function marked <c>override</c> have something to override; was that something offered
    /// for overriding; and does a function that redeclares one say <c>override</c> at all. The
    /// third is what keeps the first two from being avoidable by silence.</para>
    /// </summary>
    private void CheckOverrides()
    {
        foreach (DeclaredTypeSymbol type in _allTypes)
        {
            // Every model, not only one that wrote "extends": each inherits ToString and
            // Equals from Model, so overriding is something even a model with no base does.
            if (type is not ModelSymbol model)
            {
                continue;
            }

            if (model.DeclaredIn is { } file)
            {
                using DiagnosticBag.FileScope reporting = _diagnostics.InFile(file);
                CheckOverridesOf(model);
            }
            else
            {
                CheckOverridesOf(model);
            }
        }
    }

    private void CheckOverridesOf(ModelSymbol model)
    {
        foreach (Symbol member in model.Members.Values.SelectMany(overloads => overloads))
        {
            // A constructor names its own type and is never inherited, so nothing above can be
            // the one it overrides.
            if (member is not FunctionSymbol { IsConstructor: false } function)
            {
                continue;
            }

            FunctionSymbol? overridden = FindOverridden(model, function);

            if (function.IsOverride)
            {
                CheckAgainstOverridden(model, function, overridden);
                continue;
            }

            // Written without the word. Nothing spells "hide the base one on purpose", so
            // whichever of the two this is, it is worth saying.
            if (overridden is not null)
            {
                ReportAt(
                    DiagnosticDescriptors.HidesBaseFunction,
                    function.Declaration,
                    function.Name,
                    overridden.DeclaringType!.Name);
            }
        }
    }

    /// <summary>
    /// Reports what is wrong with a function marked <c>override</c>: that it overrides nothing,
    /// that what it overrides was never offered, or that it yields something else.
    /// </summary>
    private void CheckAgainstOverridden(
        ModelSymbol model,
        FunctionSymbol function,
        FunctionSymbol? overridden)
    {
        if (overridden is null)
        {
            // A member every model inherits is overridable without anything declaring it, so
            // this is settled before concluding there was nothing to override.
            if (InheritedFromModel(function))
            {
                return;
            }

            ReportAt(
                DiagnosticDescriptors.NothingToOverride,
                function.Declaration,
                function.Name,
                model.BaseType?.Name ?? "Model");

            return;
        }

        string declaredBy = overridden.DeclaringType!.Name;

        // An override may itself be overridden, so the word passes down a chain without every
        // link having to repeat "virtual"; and an abstract function is offered by being
        // abstract, since a descendant is obliged to write it.
        if (!overridden.IsOverridable)
        {
            ReportAt(
                DiagnosticDescriptors.BaseIsNotVirtual,
                function.Declaration,
                function.Name,
                declaredBy);

            return;
        }

        if (!SameResult(function.ReturnType, overridden.ReturnType))
        {
            ReportAt(
                DiagnosticDescriptors.OverrideResultDiffers,
                function.Declaration,
                function.Name,
                Describe(function.ReturnType),
                declaredBy,
                Describe(overridden.ReturnType));
        }
    }

    /// <summary>
    /// <para>The function above this one that it redeclares, or null when there is none.</para>
    /// <para>Matched on name and parameter types, which is what makes a signature: a function
    /// differing in either is an overload rather than an override, and overloading across a
    /// base and a derived model is perfectly ordinary.</para>
    /// </summary>
    private static FunctionSymbol? FindOverridden(ModelSymbol model, FunctionSymbol function)
    {
        foreach (ModelSymbol ancestor in model.SelfAndAncestors().Skip(1))
        {
            foreach (Symbol candidate in ancestor.Lookup(function.Name))
            {
                if (candidate is FunctionSymbol { IsConstructor: false } above
                    && SameParameters(function, above))
                {
                    return above;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <para>Whether this redeclares a member every model inherits from <c>Model</c>.</para>
    /// <para>Read from the same catalog the checker finds them by, so that a member the
    /// language says every model has and a member the language lets one override cannot come
    /// to disagree.</para>
    /// </summary>
    private static bool InheritedFromModel(FunctionSymbol function) =>
        BuiltIns.OnEveryType().Any(inherited =>
            string.Equals(inherited.Name, function.Name, StringComparison.Ordinal)
            && inherited.ParameterTypes.Count == function.Parameters.Count);

    private static bool SameParameters(FunctionSymbol left, FunctionSymbol right)
    {
        if (left.Parameters.Count != right.Parameters.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Parameters.Count; i++)
        {
            TypeSymbol here = left.Parameters[i].Type;
            TypeSymbol there = right.Parameters[i].Type;

            // An error type matches anything. Whatever was wrong with it has been reported, and
            // treating it as a mismatch would add "this overrides nothing" on top of that.
            if (here.IsError || there.IsError)
            {
                continue;
            }

            if (!here.Equals(there))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two results are the same, counting yielding nothing as a result.</summary>
    private static bool SameResult(TypeSymbol? left, TypeSymbol? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.IsError || right.IsError || left.Equals(right);

    private static string Describe(TypeSymbol? type) => type is null ? "nothing" : type.Display;

    /// <summary>Reports against a declaration, or against nothing when there is none to point at.</summary>
    private void ReportAt(
        DiagnosticDescriptor descriptor,
        SyntaxNode? declaration,
        params object?[] args)
    {
        if (declaration is not null)
        {
            _diagnostics.Report(descriptor, declaration.Span, args);
        }
    }
}
