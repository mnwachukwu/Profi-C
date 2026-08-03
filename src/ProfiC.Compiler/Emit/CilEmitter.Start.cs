using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The method the assembly starts at, which is not the one the program wrote.</para>
/// <para><b>A program fails the same way wherever it runs.</b> Pointed straight at <c>Main</c>, an
/// assembly that throws falls to the CLR's own handler: it prints <c>Unhandled exception</c>, names
/// the type, and lists the frames beneath it. The interpreter prints the file, the word
/// <c>unhandled</c>, the type and the message — and a language whose two engines describe one
/// failure two ways has two behaviors to learn instead of one.</para>
/// <para>So the entry point is a wrapper that calls <c>Main</c> inside a <c>try</c> and hands
/// whatever escapes to the runtime both engines report through. It answers with an exit code for
/// the same reason: a program that stopped because it failed should say so to whatever ran it, and
/// <c>Main</c> in this language yields nothing to say it with.</para>
/// <para>A fault in the compiler is not described and is left to travel, which is the CLR printing
/// its frames — for one of ours that is the right outcome and the only trace of it.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// The wrapper, or null where there is no entry point to wrap — a library, whose PE header
    /// then records no start at all.
    /// </summary>
    private MethodBuilder? _start;

    /// <summary>Writes the wrapper, on the same type the program's own entry point lives on.</summary>
    private void DefineStart(IReadOnlyList<CompilationUnit> units)
    {
        if (_model.EntryPoint is not { } main
            || !_functions.TryGetValue(main, out MethodBuilder? entry)
            || entry.DeclaringType is not TypeBuilder type)
        {
            return;
        }

        _start = type.DefineMethod(
            "<start>",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);

        ILGenerator il = _start.GetILGenerator();

        // Zero unless something goes wrong, since a local starts there and the ordinary way out
        // of the block is straight past the handler.
        LocalBuilder code = il.DeclareLocal(typeof(int));
        LocalBuilder failure = il.DeclareLocal(typeof(Exception));
        Label described = il.DefineLabel();

        il.BeginExceptionBlock();

        il.Emit(OpCodes.Call, entry);

        il.BeginCatchBlock(typeof(Exception));

        il.Emit(OpCodes.Stloc, failure);
        il.Emit(OpCodes.Ldstr, LabelFor(units, main));
        il.Emit(OpCodes.Ldloc, failure);
        il.Emit(OpCodes.Call, ReportFailure);
        il.Emit(OpCodes.Brtrue, described);

        // Not the program's, so nothing was printed. 'rethrow' rather than 'throw' keeps where it
        // came from, which is the whole value of a fault that reached this far.
        il.Emit(OpCodes.Rethrow);

        il.MarkLabel(described);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, code);

        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, code);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <para>What to call the program when it fails: the file its entry point was written in.
    /// </para>
    /// <para>The same name the interpreter prints, so that one program failing says one thing.
    /// Settled while building rather than read at run time, since a program that was built and
    /// handed on has no source beside it to ask.</para>
    /// </summary>
    private string LabelFor(IReadOnlyList<CompilationUnit> units, FunctionSymbol main)
    {
        foreach (CompilationUnit unit in units)
        {
            if (Declares(unit, main))
            {
                return Path.GetFileName(unit.Source.FileName);
            }
        }

        // Unreachable while an entry point comes from a unit being compiled, and the first file
        // is the one the reader named — so it is the name they would recognize either way.
        return units.Count > 0 ? Path.GetFileName(units[0].Source.FileName) : "program";
    }

    /// <summary>
    /// Whether this part of the tree declares the function, asked of the symbol rather than of
    /// the name — two models may each write a <c>Main</c>, and only one of them is the start.
    /// </summary>
    private bool Declares(SyntaxNode node, FunctionSymbol main) =>
        (node is FunctionDecl declared && ReferenceEquals(_model.GetSymbol(declared), main))
        || node.Children.Any(child => Declares(child, main));

    private static readonly MethodInfo ReportFailure =
        typeof(ProfiCFailure).GetMethod(
            nameof(ProfiCFailure.Report), [typeof(string), typeof(Exception)])!;
}
