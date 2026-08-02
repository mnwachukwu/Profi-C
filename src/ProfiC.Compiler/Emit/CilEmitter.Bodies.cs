using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The body of a function, as instructions.</para>
/// <para>CIL is a stack machine, so a post-order walk of an expression tree is already correct
/// code: operands push, the operator pops them and pushes its result. Precedence needed no
/// thought here because it is already the shape of the tree.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Where a <c>break</c> and a <c>continue</c> go, for the loop being emitted.</para>
    /// <para>A stack rather than a pair of fields, because loops nest and each one leaves to
    /// its own labels. The innermost is the one those words mean, which is what the top of a
    /// stack is.</para>
    /// </summary>
    private readonly Stack<(Label Break, Label Continue)> _loops = new();

    /// <summary>The slot each local occupies, for the function being emitted.</summary>
    private readonly Dictionary<LocalSymbol, LocalBuilder> _locals = [];

    /// <summary>
    /// Which argument each parameter is, for the function being emitted. A symbol does not
    /// carry its own position, and every function so far is shared — so there is no receiver in
    /// front of them and the first parameter is argument zero.
    /// </summary>
    private readonly Dictionary<ParameterSymbol, int> _parameters = [];

    private ILGenerator _il = null!;

    /// <summary>
    /// <para>Whether the function being emitted is called on a receiver.</para>
    /// <para>What it decides is where the parameters start. The CLR puts the receiver in
    /// argument zero, so an instance method's first written parameter is argument one — and
    /// getting this wrong reads every argument one place out, which is not a crash but a
    /// program quietly working on the wrong values.</para>
    /// </summary>
    private bool _hasReceiver;

    private void EmitBody(Declared declared)
    {
        if (_model.GetSymbol(declared.Function) is not FunctionSymbol function
            || declared.Function.Body is null)
        {
            return;
        }

        if (function.IsConstructor)
        {
            EmitConstructor(declared, function);
            return;
        }

        if (!_functions.TryGetValue(function, out MethodBuilder? method))
        {
            return;
        }

        Begin(method.GetILGenerator(), function, hasReceiver: !method.IsStatic);

        EmitStatements(declared.Function.Body);

        // A function that yields nothing has no yield to end it, and one that does has already
        // returned down every path — proved by PC0404 rather than assumed. Either way the
        // method needs a ret to be well formed, and an unreachable one costs a byte.
        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <para>A constructor: the base first, then the fields, then what was written.</para>
    /// <para>The order is the language's. A field written with a value holds it before any
    /// constructor body runs, so a constructor that reads one sees the initializer rather than
    /// a zero — and a constructor that assigns one overwrites it, which is what a reader of
    /// those two lines expects.</para>
    /// <para>The base constructor comes before either. The CLR requires it of every constructor
    /// and will not verify one without it.</para>
    /// </summary>
    private void EmitConstructor(Declared declared, FunctionSymbol function)
    {
        if (!_constructors.TryGetValue(function, out ConstructorBuilder? constructor)
            || _model.GetSymbol(declared.Owner) is not DeclaredTypeSymbol owner)
        {
            return;
        }

        Begin(constructor.GetILGenerator(), function, hasReceiver: true);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, ObjectConstructor);

        EmitFieldInitializers(owner);
        EmitStatements(declared.Function.Body!);

        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <para>Declares a constructor for a model that wrote none.</para>
    /// <para>Without one the type has no way to be made at all — the CLR supplies a default only
    /// for a class defined by a compiler that asked for one, and this emitter defines every
    /// member itself. A shared model is skipped: it has no instances to construct.</para>
    /// </summary>
    private void DefineDefaultConstructor(ModelDecl declaration)
    {
        if (declaration.Modifiers.HasFlag(DeclarationModifiers.Shared)
            || _model.GetSymbol(declaration) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type)
            || declaration.Members.OfType<FunctionDecl>().Any(IsConstructor))
        {
            return;
        }

        _defaultConstructors[owner] = type.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes);
    }

    /// <summary>Fills in that constructor: the base, then whatever the fields were written with.</summary>
    private void EmitDefaultConstructor(ModelDecl declaration)
    {
        if (_model.GetSymbol(declaration) is not DeclaredTypeSymbol owner
            || !_defaultConstructors.TryGetValue(owner, out ConstructorBuilder? constructor))
        {
            return;
        }

        Begin(constructor.GetILGenerator(), function: null, hasReceiver: true);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, ObjectConstructor);

        EmitFieldInitializers(owner);

        _il.Emit(OpCodes.Ret);
    }

    private bool IsConstructor(FunctionDecl declaration) =>
        _model.GetSymbol(declaration) is FunctionSymbol { IsConstructor: true };

    /// <summary>
    /// <para>Runs the initializers of a model's instance fields, into the instance being built.
    /// </para>
    /// <para>Instance only. A shared field belongs to the type rather than to any instance, so
    /// running its initializer here would reset it every time one was made — which shows up not
    /// as a crash but as a tally that counts to one however many times it is added to.</para>
    /// </summary>
    private void EmitFieldInitializers(DeclaredTypeSymbol owner)
    {
        foreach (FieldDecl declared in InitializersOf(owner, shared: false))
        {
            _il.Emit(OpCodes.Ldarg_0);
            EmitExpression(declared.Initializer!);
            _il.Emit(OpCodes.Stfld, _fields[(FieldSymbol)_model.GetSymbol(declared)!]);
        }
    }

    /// <summary>
    /// <para>Runs the initializers of a model's shared fields, once.</para>
    /// <para>In a type initializer, which the runtime calls once before the type is first used —
    /// so a shared field holds what it was written with whether an instance was ever made or
    /// not, and holds it however many are.</para>
    /// </summary>
    private void EmitSharedFieldInitializers(ModelDecl declaration)
    {
        if (_model.GetSymbol(declaration) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type))
        {
            return;
        }

        FieldDecl[] shared = [.. InitializersOf(owner, shared: true)];

        if (shared.Length == 0)
        {
            return;
        }

        Begin(type.DefineTypeInitializer().GetILGenerator(), function: null, hasReceiver: false);

        foreach (FieldDecl declared in shared)
        {
            EmitExpression(declared.Initializer!);
            _il.Emit(OpCodes.Stsfld, _fields[(FieldSymbol)_model.GetSymbol(declared)!]);
        }

        _il.Emit(OpCodes.Ret);
    }

    /// <summary>The fields of one kind that were written with a value, in declaration order.</summary>
    private IEnumerable<FieldDecl> InitializersOf(DeclaredTypeSymbol owner, bool shared) =>
        _initializers.TryGetValue(owner, out List<FieldDecl>? initialized)
            ? initialized.Where(d =>
                _model.GetSymbol(d) is FieldSymbol field
                && field.IsShared == shared
                && _fields.ContainsKey(field))
            : [];

    /// <summary>Starts a body: fresh locals, fresh loops, and the parameters where they will be.</summary>
    private void Begin(ILGenerator il, FunctionSymbol? function, bool hasReceiver)
    {
        _il = il;
        _locals.Clear();
        _loops.Clear();
        _parameters.Clear();
        _hasReceiver = hasReceiver;

        if (function is null)
        {
            return;
        }

        int first = hasReceiver ? 1 : 0;

        for (int i = 0; i < function.Parameters.Count; i++)
        {
            _parameters[function.Parameters[i]] = first + i;
        }
    }

    private static readonly System.Reflection.ConstructorInfo ObjectConstructor =
        typeof(object).GetConstructor(Type.EmptyTypes)!;

    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStmt block:
                EmitStatements(block.Statements);
                break;

            case VarDeclStmt declaration:
                EmitVarDecl(declaration);
                break;

            case AssignmentStmt assignment:
                EmitAssignment(assignment);
                break;

            case ExpressionStmt expression:
                EmitDiscarded(expression.Expression);
                break;

            case IfStmt branch:
                EmitIf(branch);
                break;

            case WhileStmt loop:
                EmitWhile(loop);
                break;

            case LoopUntilStmt loop:
                EmitLoopUntil(loop);
                break;

            case LoopForeverStmt loop:
                EmitLoopForever(loop);
                break;

            case ForStmt loop:
                EmitFor(loop);
                break;

            case BreakStmt:
                _il.Emit(OpCodes.Br, _loops.Peek().Break);
                break;

            case ContinueStmt:
                _il.Emit(OpCodes.Br, _loops.Peek().Continue);
                break;

            case YieldStmt yield:
                EmitYield(yield);
                break;

            default:
                throw Unhandled(statement.GetType().Name);
        }
    }

    private void EmitVarDecl(VarDeclStmt declaration)
    {
        LocalBuilder slot = Slot(declaration);

        if (declaration.Initializer is null)
        {
            return;
        }

        EmitExpression(declaration.Initializer);
        _il.Emit(OpCodes.Stloc, slot);
    }

    /// <summary>
    /// The slot a local lives in, made the first time the local is met. Declared with its name
    /// so that a decompiler and a debugger show what the program called it.
    /// </summary>
    private LocalBuilder Slot(VarDeclStmt declaration)
    {
        if (_model.GetSymbol(declaration) is not LocalSymbol local)
        {
            throw Unhandled($"the local '{declaration.Name}' resolved to nothing");
        }

        if (!_locals.TryGetValue(local, out LocalBuilder? slot))
        {
            slot = _il.DeclareLocal(TypeOf(local.Type, local.Name));
            slot.SetLocalSymInfo(local.Name);
            _locals[local] = slot;
        }

        return slot;
    }

    private void EmitAssignment(AssignmentStmt assignment)
    {
        switch (assignment.Target)
        {
            case IdentifierExpr name:
                EmitAssignToName(name, assignment.Value);
                break;

            case MemberExpr member:
                EmitAssignToField(member, assignment.Value);
                break;

            default:
                throw Unhandled($"assigning to {assignment.Target.GetType().Name}");
        }
    }

    private void EmitAssignToName(IdentifierExpr name, Expression value)
    {
        // A bare name may be a field of the model this call is running in, in which case the
        // receiver has to go on the stack before the value does.
        if (_model.GetSymbol(name) is FieldSymbol field)
        {
            EmitStoreField(field, receiver: null, value);
            return;
        }

        EmitExpression(value);

        switch (_model.GetSymbol(name))
        {
            case LocalSymbol local when _locals.TryGetValue(local, out LocalBuilder? slot):
                _il.Emit(OpCodes.Stloc, slot);
                break;

            case ParameterSymbol parameter when _parameters.TryGetValue(parameter, out int at):
                _il.Emit(OpCodes.Starg, at);
                break;

            default:
                throw Unhandled($"assigning to '{name.Name}'");
        }
    }

    private void EmitAssignToField(MemberExpr member, Expression value)
    {
        if (_model.GetSymbol(member) is not FieldSymbol field)
        {
            throw Unhandled($"assigning to '{member.MemberName}'");
        }

        EmitStoreField(field, member.Receiver, value);
    }

    /// <summary>
    /// Stores into a field. The receiver goes first and the value second, which is the order
    /// <c>stfld</c> pops them — and a shared field has no receiver at all.
    /// </summary>
    private void EmitStoreField(FieldSymbol field, Expression? receiver, Expression value)
    {
        if (!_fields.TryGetValue(field, out FieldBuilder? slot))
        {
            throw Unhandled($"the field '{field.Name}'");
        }

        if (!field.IsShared)
        {
            EmitReceiver(receiver);
        }

        EmitExpression(value);
        _il.Emit(field.IsShared ? OpCodes.Stsfld : OpCodes.Stfld, slot);
    }

    /// <summary>
    /// <para>Pushes the instance a member is reached through.</para>
    /// <para>Null where the member was named on its own, which inside an instance method means
    /// the receiver the call was made on — <c>this</c>, argument zero. The language requires
    /// <c>this.</c> to be written, so this is rare rather than the common path, but a field
    /// initializer reaches its own instance without writing one.</para>
    /// </summary>
    private void EmitReceiver(Expression? receiver)
    {
        if (receiver is null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            return;
        }

        EmitExpression(receiver);
    }

    /// <summary>
    /// <para>An <c>if</c>, emitted as a jump past the block when the condition does not hold.
    /// </para>
    /// <para>The condition is evaluated as written and the branch is the negated one, rather
    /// than negating the expression and branching on truth. Both are correct; this one emits
    /// the instructions a reader of the CIL would expect beside the source.</para>
    /// </summary>
    private void EmitIf(IfStmt branch)
    {
        Label after = _il.DefineLabel();

        // Each 'else if' is another test reached only when every one before it failed, so the
        // whole chain is one flat run of test-and-jump rather than a nest. Every arm that runs
        // jumps to the same end, which is what makes them alternatives.
        EmitArm(branch.Condition, branch.ThenBody, after);

        foreach (ElseIfClause clause in branch.ElseIfClauses)
        {
            EmitArm(clause.Condition, clause.Body, after);
        }

        if (branch.ElseBody is { } orElse)
        {
            EmitStatements(orElse);
        }

        _il.MarkLabel(after);
    }

    private void EmitArm(Expression condition, IReadOnlyList<Statement> body, Label after)
    {
        Label next = _il.DefineLabel();

        EmitExpression(condition);
        _il.Emit(OpCodes.Brfalse, next);

        EmitStatements(body);
        _il.Emit(OpCodes.Br, after);

        _il.MarkLabel(next);
    }

    /// <summary>
    /// A <c>loop while</c>: the test is at the top, so a condition false to begin with runs the
    /// body no times.
    /// </summary>
    private void EmitWhile(WhileStmt loop)
    {
        Label test = _il.DefineLabel();
        Label after = _il.DefineLabel();

        _il.MarkLabel(test);
        EmitExpression(loop.Condition);
        _il.Emit(OpCodes.Brfalse, after);

        _loops.Push((after, test));
        EmitStatements(loop.Body);
        _loops.Pop();

        _il.Emit(OpCodes.Br, test);
        _il.MarkLabel(after);
    }

    /// <summary>
    /// A <c>loop ... until</c>: the test is at the bottom, so the body always runs at least
    /// once — which is why definite assignment treats what it assigns as assigned afterwards.
    /// A <c>continue</c> goes to the test rather than to the top, since the test is what
    /// decides whether there is another turn.
    /// </summary>
    private void EmitLoopUntil(LoopUntilStmt loop)
    {
        Label top = _il.DefineLabel();
        Label test = _il.DefineLabel();
        Label after = _il.DefineLabel();

        _il.MarkLabel(top);

        _loops.Push((after, test));
        EmitStatements(loop.Body);
        _loops.Pop();

        _il.MarkLabel(test);
        EmitExpression(loop.Condition);
        _il.Emit(OpCodes.Brfalse, top);
        _il.MarkLabel(after);
    }

    /// <summary>
    /// A bare <c>loop</c>, which has no condition at all. Only a <c>break</c>, a <c>yield</c> or
    /// a <c>throw</c> leaves one, and a program where none of those can happen has already been
    /// told so by PC0406.
    /// </summary>
    private void EmitLoopForever(LoopForeverStmt loop)
    {
        Label top = _il.DefineLabel();
        Label after = _il.DefineLabel();

        _il.MarkLabel(top);

        _loops.Push((after, top));
        EmitStatements(loop.Body);
        _loops.Pop();

        _il.Emit(OpCodes.Br, top);
        _il.MarkLabel(after);
    }

    /// <summary>
    /// <para>A counted loop, with the header read again on every turn.</para>
    /// <para>Both the bound and the step are evaluated at the top of each turn rather than once
    /// before the loop, which is the language's rule and not an emitter's convenience: a loop
    /// counts as far as what its header says now.</para>
    /// <para><b>Which way it counts is decided while it runs, not while it is emitted.</b> The
    /// step decides the direction, the step is an expression, and an expression can be negative
    /// — or can change. So both comparisons are emitted and the sign chooses between them.
    /// Picking one at emit time compiles <c>loop for i = 10 to 1 stepby -3</c> into a loop that
    /// ends before its first turn, which is not a crash and not a diagnostic: it is a program
    /// that silently prints nothing.</para>
    /// </summary>
    private void EmitFor(ForStmt loop)
    {
        if (_model.GetSymbol(loop) is not LocalSymbol counter)
        {
            throw Unhandled($"the loop variable '{loop.VariableName}' resolved to nothing");
        }

        LocalBuilder current = _il.DeclareLocal(TypeOf(counter.Type, counter.Name));
        current.SetLocalSymInfo(counter.Name);
        _locals[counter] = current;

        LocalBuilder bound = _il.DeclareLocal(typeof(long));
        LocalBuilder step = _il.DeclareLocal(typeof(long));

        EmitExpression(loop.Start);
        _il.Emit(OpCodes.Stloc, current);

        Label top = _il.DefineLabel();
        Label descending = _il.DefineLabel();
        Label body = _il.DefineLabel();
        Label next = _il.DefineLabel();
        Label after = _il.DefineLabel();

        _il.MarkLabel(top);

        EmitExpression(loop.Bound);
        _il.Emit(OpCodes.Stloc, bound);

        if (loop.Step is null)
        {
            _il.Emit(OpCodes.Ldc_I8, 1L);
        }
        else
        {
            EmitExpression(loop.Step);
        }

        _il.Emit(OpCodes.Stloc, step);

        // A step of zero counts as ascending, and so loops forever. Deliberate, and the same
        // answer the interpreter gives: the program said to move by nothing.
        _il.Emit(OpCodes.Ldloc, step);
        _il.Emit(OpCodes.Ldc_I8, 0L);
        _il.Emit(OpCodes.Blt, descending);

        // Counting up: 'to' includes the bound, 'until' stops before it. The branch is the
        // negated test, since it leaves the loop.
        _il.Emit(OpCodes.Ldloc, current);
        _il.Emit(OpCodes.Ldloc, bound);
        _il.Emit(loop.IsInclusive ? OpCodes.Bgt : OpCodes.Bge, after);
        _il.Emit(OpCodes.Br, body);

        _il.MarkLabel(descending);
        _il.Emit(OpCodes.Ldloc, current);
        _il.Emit(OpCodes.Ldloc, bound);
        _il.Emit(loop.IsInclusive ? OpCodes.Blt : OpCodes.Ble, after);

        _il.MarkLabel(body);

        _loops.Push((after, next));
        EmitStatements(loop.Body);
        _loops.Pop();

        // Where 'continue' goes: on to the next turn, which means moving the counter. Skipping
        // this would leave the counter where it was and the loop would never end.
        _il.MarkLabel(next);
        _il.Emit(OpCodes.Ldloc, current);
        _il.Emit(OpCodes.Ldloc, step);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, current);
        _il.Emit(OpCodes.Br, top);

        _il.MarkLabel(after);
    }

    private void EmitYield(YieldStmt yield)
    {
        if (yield.Value is not null)
        {
            EmitExpression(yield.Value);
        }

        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// An expression run for what it does rather than for what it produces. CIL leaves a
    /// result on the stack whether or not anybody wants it, and a stack left un-emptied at a
    /// branch is not verifiable code — so anything a discarded call gives back is popped.
    /// </summary>
    private void EmitDiscarded(Expression expression)
    {
        EmitExpression(expression);

        if (_model.GetType(expression) is { } type
            && !ReferenceEquals(type, PrimitiveType.Void))
        {
            _il.Emit(OpCodes.Pop);
        }
    }

    private static InvalidOperationException Unhandled(string what) =>
        new($"the emitter met {what}, which the survey should have refused");
}
