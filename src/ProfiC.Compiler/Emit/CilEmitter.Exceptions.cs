using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para><c>throw</c>, <c>try</c>, and the models a program builds on <c>Exception</c>.</para>
/// <para><b>Profi-C's exceptions are .NET exceptions</b>, which is what makes this small. A model
/// extending <c>Exception</c> becomes a class deriving from <c>System.Exception</c>, so a throw is
/// one instruction, a catch clause is a CIL handler, and matching a thrown value against the type
/// a clause names is the runtime's own work rather than a comparison the emitter writes.</para>
/// <para>The interpreter cannot do it that way — a model it holds is an <c>Instance</c> rather
/// than an object of that type, so it wraps one on the way out and matches up the chain of symbols
/// by hand. Two mechanisms, and they are meant to be indistinguishable: which clause takes a
/// thrown value has to be the same question wherever the program runs, and the corpus is what
/// holds them to it.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// A <c>throw</c>, which is the value and then the instruction. What may be thrown is settled
    /// by <c>PC0331</c> long before, so nothing here asks whether the value is an exception.
    /// </summary>
    private void EmitThrow(ThrowStmt raised)
    {
        EmitExpression(raised.Exception);
        _il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// <para><c>try</c>, its <c>catch</c> clauses, and its <c>finally</c>.</para>
    /// <para>Each clause becomes a handler for the CLR type its name denotes, and the runtime
    /// picks the first that matches — which is the order they were written, and the same rule the
    /// interpreter follows by walking the list.</para>
    /// <para>The thrown value arrives on the stack at the top of a handler and nowhere else, so it
    /// is stored into the clause's own local straight away. A clause that never names it still
    /// stores it: the stack has to be emptied either way, and a name a program declined to use is
    /// not a name that costs anything.</para>
    /// </summary>
    private void EmitTry(TryStmt guarded)
    {
        _il.BeginExceptionBlock();
        _protection++;

        EmitStatements(guarded.Body);

        foreach (CatchClause clause in guarded.Catches)
        {
            _il.BeginCatchBlock(CaughtBy(clause));

            EmitCaughtValue(clause);
            EmitStatements(clause.Body);
        }

        if (guarded.FinallyBody is not null)
        {
            _il.BeginFinallyBlock();
            EmitStatements(guarded.FinallyBody);
        }

        _protection--;
        _il.EndExceptionBlock();
    }

    /// <summary>
    /// <para>Takes the thrown value off the stack and into the name the clause bound it to.</para>
    /// <para>A local of the clause's own, declared here rather than by the ordinary local pass —
    /// a <c>catch</c> variable is introduced by the clause and never declared as a statement, so
    /// nothing else would ever make a slot for it.</para>
    /// </summary>
    private void EmitCaughtValue(CatchClause clause)
    {
        if (_model.GetSymbol(clause) is not LocalSymbol bound)
        {
            _il.Emit(OpCodes.Pop);
            return;
        }

        LocalBuilder slot = _il.DeclareLocal(TypeOf(bound.Type!, clause.VariableName));

        _locals[bound] = slot;
        _il.Emit(OpCodes.Stloc, slot);
    }

    /// <summary>
    /// <para>The CLR type a <c>catch</c> clause takes.</para>
    /// <para>A model the program declared answers for itself, since it derives from
    /// <c>System.Exception</c> and the runtime matches it up its own chain. A name the language
    /// provides is looked up in the catalog both engines share, which is what makes
    /// <c>catch DivideByZeroException</c> mean the same thing in either.</para>
    /// </summary>
    private Type CaughtBy(CatchClause clause) =>
        _model.GetType(clause.ExceptionType) is { } named
            ? TypeOf(named, $"the type '{clause.ExceptionType}' catches")
            : throw Unhandled("a catch clause naming a type that resolved to nothing");

    /// <summary>
    /// <para>An exception's message, which is <c>System.Exception</c>'s own.</para>
    /// <para>A declared model reaches the same property, inheriting it from the parent its
    /// <c>base(...)</c> handed the message to — so there is nothing for a program to carry and
    /// nothing for the emitter to store.</para>
    /// </summary>
    private void EmitExceptionMessage(MemberExpr member)
    {
        EmitExpression(member.Receiver);
        _il.Emit(OpCodes.Callvirt, ExceptionMessage);
    }

    private static readonly MethodInfo ExceptionMessage =
        typeof(Exception).GetProperty(nameof(Exception.Message))!.GetMethod!;
}
