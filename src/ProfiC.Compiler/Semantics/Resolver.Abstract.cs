using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    /// <summary>
    /// <para>What <c>abstract</c> obliges: a function left open, and a model that has to close
    /// it.</para>
    /// <para>An abstract function is a name and a signature with nothing behind them, so two
    /// things have to hold for it to mean anything. It may only sit on a model no one can
    /// construct, since an instance of that model would reach a function nobody wrote. And the
    /// obligation travels down the chain until it meets a model that <em>can</em> be
    /// constructed, which is the one that must discharge it.</para>
    /// <para>Runs after signatures are settled, since deciding whether a descendant wrote a
    /// function means comparing parameter types.</para>
    /// </summary>
    private void CheckAbstractFunctions()
    {
        foreach (DeclaredTypeSymbol type in _allTypes)
        {
            if (type is not ModelSymbol model)
            {
                continue;
            }

            if (model.DeclaredIn is { } file)
            {
                using DiagnosticBag.FileScope reporting = _diagnostics.InFile(file);
                CheckAbstractFunctionsOf(model);
            }
            else
            {
                CheckAbstractFunctionsOf(model);
            }
        }
    }

    private void CheckAbstractFunctionsOf(ModelSymbol model)
    {
        foreach (Symbol member in model.Members.Values.SelectMany(overloads => overloads))
        {
            if (member is FunctionSymbol { IsConstructor: false } function
                && function.Declaration is FunctionDecl declaration)
            {
                CheckShapeOf(model, function, declaration);
            }
        }

        RequireOpenFunctionsWritten(model);
    }

    /// <summary>
    /// Whether a function's body and its words agree, and whether the model around it can carry
    /// what the words promise.
    /// </summary>
    private void CheckShapeOf(ModelSymbol model, FunctionSymbol function, FunctionDecl declaration)
    {
        if (!function.IsAbstract)
        {
            // Nothing else may be left open: no descendant is obliged to fill it, so the
            // program would reach a function that was never written.
            if (declaration.IsBodiless)
            {
                ReportAt(DiagnosticDescriptors.BodyIsMissing, declaration, function.Name);
            }

            return;
        }

        if (!declaration.IsBodiless)
        {
            ReportAt(DiagnosticDescriptors.AbstractHasABody, declaration, function.Name);
        }

        if (!model.IsAbstract)
        {
            ReportAt(
                DiagnosticDescriptors.AbstractInConcreteModel,
                declaration,
                function.Name,
                model.Name);
        }

        if (function.IsVirtual)
        {
            ReportAt(DiagnosticDescriptors.AbstractIsAlreadyVirtual, declaration, function.Name);
        }
    }

    /// <summary>
    /// <para>A model that can be constructed writes every function left open above it.</para>
    /// <para>Reported once on the model and naming all of them, rather than once per function:
    /// the reader has one thing to do, which is to write them, and a list is what says how many.
    /// </para>
    /// </summary>
    private void RequireOpenFunctionsWritten(ModelSymbol model)
    {
        if (model.IsAbstract || model.Declaration is null)
        {
            return;
        }

        List<string> open = [];

        // Ancestors only. A model that is not abstract and declares an abstract function of its
        // own has already been told so, and saying it twice names one mistake as two.
        foreach (ModelSymbol ancestor in model.SelfAndAncestors().Skip(1))
        {
            foreach (Symbol member in ancestor.Members.Values.SelectMany(overloads => overloads))
            {
                if (member is FunctionSymbol { IsConstructor: false, IsAbstract: true } left
                    && !IsWrittenBetween(model, ancestor, left))
                {
                    open.Add($"'{left.Name}' from '{ancestor.Name}'");
                }
            }
        }

        if (open.Count > 0)
        {
            ReportAt(
                DiagnosticDescriptors.AbstractIsNotOverridden,
                model.Declaration,
                model.Name,
                string.Join(", ", open));
        }
    }

    /// <summary>
    /// Whether anything from this model up to — but not including — the one that left the
    /// function open has written it. A model in the middle discharges the obligation for
    /// everything below it, which is what lets a hierarchy fill a function once.
    /// </summary>
    private static bool IsWrittenBetween(
        ModelSymbol model,
        ModelSymbol declaring,
        FunctionSymbol open)
    {
        foreach (ModelSymbol between in model.SelfAndAncestors())
        {
            if (ReferenceEquals(between, declaring))
            {
                return false;
            }

            foreach (Symbol member in between.Lookup(open.Name))
            {
                if (member is FunctionSymbol { IsAbstract: false } written
                    && SameParameters(written, open))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
