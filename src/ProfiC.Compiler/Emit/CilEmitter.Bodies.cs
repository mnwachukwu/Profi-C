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
    /// <para>Each remembers how deeply protected the loop itself was, which is what decides how
    /// a <c>break</c> written under a <c>try</c> gets out. See <see cref="LeaveLoop"/>.</para>
    /// </summary>
    private readonly Stack<(Label Break, Label Continue, int Protection)> _loops = new();

    /// <summary>
    /// <para>How many <c>try</c> blocks enclose the instruction being written.</para>
    /// <para>The CLR will not let an ordinary branch cross out of a protected region — the whole
    /// point of one is that leaving it runs the <c>finally</c> — so a jump that crosses one has to
    /// be a <c>leave</c>. That includes a <c>break</c> for a loop written outside the <c>try</c>,
    /// which reads like an ordinary jump and is not one.</para>
    /// </summary>
    private int _protection;

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
        // left down every path — proved by PC0404 rather than assumed. Either way the method
        // needs an end to be well formed, and an unreachable one costs a few bytes.
        End();
    }

    /// <summary>
    /// <para>A constructor: this model's own fields, then the parent, then what was written.
    /// </para>
    /// <para>The order is the language's, and it is C#'s. A field written with a value holds it
    /// before any constructor body runs, so a constructor that reads one sees the initializer
    /// rather than a zero — and a constructor that assigns one overwrites it, which is what a
    /// reader of those two lines expects. Running only <em>this</em> model's initializers here is
    /// what makes the chain come out right: the parent's constructor runs the parent's, so every
    /// starting value in the whole chain has run by the time any constructor body does.</para>
    /// <para>The parent is reached before the body, which the CLR requires of every constructor
    /// and will not verify one without. <c>PC0248</c> is what makes that possible to honor while
    /// still running <c>base(...)</c> where it was written: it can only have been written
    /// first.</para>
    /// </summary>
    private void EmitConstructor(Declared declared, FunctionSymbol function)
    {
        if (!_constructors.TryGetValue(function, out ConstructorBuilder? constructor)
            || _model.GetSymbol(declared.Owner.Node) is not DeclaredTypeSymbol owner)
        {
            return;
        }

        Begin(constructor.GetILGenerator(), function, hasReceiver: true);

        IReadOnlyList<Statement> body = declared.Function.Body!;
        CallExpr? chaining = BaseCallOpening(body);

        EmitFieldInitializers(owner);
        EmitBaseConstructorCall(owner, chaining);

        // The chaining call has been emitted already; emitting the statement again would build
        // the parent twice.
        EmitStatements(chaining is null ? body : [.. body.Skip(1)]);

        End();
    }

    /// <summary>
    /// The <c>base(...)</c> a constructor opens with, or null where it wrote none. Only the
    /// first statement is looked at, since <c>PC0248</c> is what put it there.
    /// </summary>
    private static CallExpr? BaseCallOpening(IReadOnlyList<Statement> body) =>
        body is [ExpressionStmt { Expression: CallExpr { Callee: ReceiverExpr
        {
            Receiver: ReceiverKind.Base,
        } } opening }, ..]
            ? opening
            : null;

    /// <summary>
    /// <para>Reaches the parent's constructor, whether or not one was written.</para>
    /// <para>A constructor that wrote no <c>base(...)</c> still has a parent to build, and
    /// <c>PC0250</c> has already established that the parent can be built with nothing — so the
    /// call is to whichever constructor takes no arguments. Where the parent is not a model this
    /// program declared, the chain ends at <c>System.Object</c>, which is what Profi-C's
    /// <c>Model</c> is.</para>
    /// </summary>
    private void EmitBaseConstructorCall(DeclaredTypeSymbol owner, CallExpr? written)
    {
        _il.Emit(OpCodes.Ldarg_0);

        if (written is not null)
        {
            foreach (Expression argument in written.Arguments)
            {
                EmitValueInto(argument);
            }
        }

        _il.Emit(OpCodes.Call, ParentConstructorFor(owner, written));
    }

    /// <summary>The parent constructor to chain to, taking nothing where nothing was written.</summary>
    private System.Reflection.ConstructorInfo ParentConstructorFor(
        DeclaredTypeSymbol owner,
        CallExpr? written)
    {
        // What the checker settled the call to, which is the overload the arguments picked.
        if (written is not null
            && _model.GetSymbol(written) is FunctionSymbol chosen
            && _constructors.TryGetValue(chosen, out ConstructorBuilder? built))
        {
            return built;
        }

        if (owner is not ModelSymbol { BaseType: { } parent })
        {
            return ObjectConstructor;
        }

        // A parent the language provides — an exception — is a type in the runtime, so its
        // constructor is looked up rather than built. Which one is decided by how many arguments
        // were written, since every exception takes a message or nothing.
        if (!_types.ContainsKey(parent))
        {
            return CilTypes.OfBuiltInModel(parent) is { } provided
                ? BuiltInConstructor(provided, written?.Arguments.Count ?? 0)
                : ObjectConstructor;
        }

        FunctionSymbol? takingNothing = parent.Lookup(parent.Name)
            .OfType<FunctionSymbol>()
            .FirstOrDefault(c => c.IsConstructor && c.Parameters.Count == 0);

        if (takingNothing is not null && _constructors.TryGetValue(takingNothing, out ConstructorBuilder? declared))
        {
            return declared;
        }

        // The one made for a parent that wrote none, which is the ordinary case for a model
        // whose child does all the work.
        return _defaultConstructors.TryGetValue(parent, out ConstructorBuilder? supplied)
            ? supplied
            : throw Unhandled($"a constructor on '{parent.Name}' to build it from '{owner.Name}'");
    }

    /// <summary>
    /// <para>Declares a constructor for a model that wrote none.</para>
    /// <para>Without one the type has no way to be made at all — the CLR supplies a default only
    /// for a class defined by a compiler that asked for one, and this emitter defines every
    /// member itself. A shared model is skipped: it has no instances to construct.</para>
    /// </summary>
    private void DefineDefaultConstructor(Shaped declaration)
    {
        if (declaration.Modifiers.HasFlag(DeclarationModifiers.Shared)
            || _model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
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

    /// <summary>Fills in that constructor: this model's fields, then the parent.</summary>
    private void EmitDefaultConstructor(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || !_defaultConstructors.TryGetValue(owner, out ConstructorBuilder? constructor))
        {
            return;
        }

        Begin(constructor.GetILGenerator(), function: null, hasReceiver: true);

        EmitFieldInitializers(owner);
        EmitBaseConstructorCall(owner, written: null);

        End();
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
            EmitValueInto(declared.Initializer!);
            _il.Emit(OpCodes.Stfld, _fields[(FieldSymbol)_model.GetSymbol(declared)!]);
        }
    }

    /// <summary>
    /// <para>Runs the initializers of a model's shared fields, once.</para>
    /// <para>In a type initializer, which the runtime calls once before the type is first used —
    /// so a shared field holds what it was written with whether an instance was ever made or
    /// not, and holds it however many are.</para>
    /// </summary>
    private void EmitSharedFieldInitializers(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
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
            EmitValueInto(declared.Initializer!);
            _il.Emit(OpCodes.Stsfld, _fields[(FieldSymbol)_model.GetSymbol(declared)!]);
        }

        End();
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
    /// <summary>
    /// <para>Where every path out of the body meets, and what it carries.</para>
    /// <para><b>One exit per method, always.</b> A <c>yield</c> cannot be a <c>ret</c> where it
    /// sits inside a protected region — the CLR refuses the whole method — and a <c>loop each</c>
    /// puts one around its body to unmark the set on the way out. So a yield stores its value and
    /// leaves, and the single <c>ret</c> at the bottom is the only one there is.</para>
    /// <para>Cheaper than deciding: working out whether a particular yield is inside a region
    /// would mean tracking that everywhere, and this costs a local nobody reads in the methods
    /// that did not need it.</para>
    /// </summary>
    private Label _exit;

    private LocalBuilder? _result;

    private void Begin(ILGenerator il, FunctionSymbol? function, bool hasReceiver)
    {
        _il = il;
        _locals.Clear();
        _loops.Clear();
        _parameters.Clear();
        _hasReceiver = hasReceiver;
        _exit = il.DefineLabel();
        _result = null;

        if (function is null)
        {
            return;
        }

        if (function.ReturnType is not null && !function.IsConstructor)
        {
            _result = il.DeclareLocal(TypeOf(function.ReturnType, function.Name));
        }

        int first = hasReceiver ? 1 : 0;

        for (int i = 0; i < function.Parameters.Count; i++)
        {
            _parameters[function.Parameters[i]] = first + i;
        }
    }

    private static readonly System.Reflection.ConstructorInfo ObjectConstructor =
        typeof(object).GetConstructor(Type.EmptyTypes)!;

    /// <summary>
    /// <para>The constructor of a type the language provides, chosen by how many arguments were
    /// written.</para>
    /// <para>An exception takes a message or nothing, which is the whole of the choice — every
    /// name in the catalog offers both, and the one-argument form is the one worth writing.</para>
    /// </summary>
    private static System.Reflection.ConstructorInfo BuiltInConstructor(Type provided, int taking) =>
        provided.GetConstructor(taking == 0 ? Type.EmptyTypes : [typeof(string)])
        ?? throw Unhandled($"a constructor on '{provided.Name}' taking {taking}");

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

            case SwitchStmt chosen:
                EmitSwitch(chosen);
                break;

            case WhileStmt loop:
                EmitWhile(loop);
                break;

            case WalkStmt walk:
                EmitWalk(walk);
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
                LeaveLoop(_loops.Peek().Break);
                break;

            case ContinueStmt:
                LeaveLoop(_loops.Peek().Continue);
                break;

            case ThrowStmt raised:
                EmitThrow(raised);
                break;

            case TryStmt guarded:
                EmitTry(guarded);
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

        EmitValueInto(declaration.Initializer);
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

            case IndexExpr index:
                EmitAssignToIndex(index, assignment.Value);
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

        EmitValueInto(value);

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

        EmitValueInto(value);
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
    /// <para>A <c>switch</c>: every test first, then the groups they jump to.</para>
    /// <para><b>Tests and bodies are separated, which an <c>if</c> chain does not need to do.</b>
    /// Several labels may share one group, so a group is a place two or more tests jump to rather
    /// than something that follows its own test — and putting the bodies after every test is what
    /// lets each of them be one destination.</para>
    /// <para>Compared with the same sequence <c>==</c> uses, which is what makes a label mean what
    /// a reader thinks it means: <c>case "deal"</c> compares the text rather than asking whether
    /// two strings are the same object.</para>
    /// <para><b>Nothing falls through</b>, so each group ends by jumping past the rest. That is
    /// also why <c>break</c> is untouched here: a switch is not something control has to be got
    /// out of, so the word still means the loop around it — see <see cref="_loops"/>, which this
    /// does not push to.</para>
    /// <para>The subject is evaluated once, into a slot, since a label being tested against a call
    /// must not run the call again.</para>
    /// </summary>
    private void EmitSwitch(SwitchStmt chosen)
    {
        LocalBuilder subject = _il.DeclareLocal(
            TypeOf(
                _model.GetType(chosen.Subject)
                ?? throw Unhandled("a switch on something with no type"),
                "the subject of a switch"));

        EmitExpression(chosen.Subject);
        _il.Emit(OpCodes.Stloc, subject);

        Label after = _il.DefineLabel();
        Label[] groups = [.. chosen.Cases.Select(_ => _il.DefineLabel())];

        // Whether the comparison is the runtime's deep walk, which takes two objects and so
        // needs a value boxed on the way in. Asked rather than assumed: what a label may be is
        // the checker's to say, and the two have to agree about which comparison is emitted.
        bool deeply = IsComparedDeeply(chosen.Subject);

        for (int at = 0; at < chosen.Cases.Count; at++)
        {
            foreach (Expression label in chosen.Cases[at].Labels)
            {
                _il.Emit(OpCodes.Ldloc, subject);

                if (deeply && subject.LocalType.IsValueType)
                {
                    _il.Emit(OpCodes.Box, subject.LocalType);
                }

                if (deeply)
                {
                    EmitAsObject(label);
                }
                else
                {
                    EmitExpression(label);
                }

                EmitEqualityOf(chosen.Subject, label, wanted: true);
                _il.Emit(OpCodes.Brtrue, groups[at]);
            }
        }

        // Reached when no label matched, whether or not a 'default' was written — a switch that
        // matches nothing and has no default does nothing at all.
        if (chosen.DefaultBody is { } otherwise)
        {
            EmitStatements(otherwise);
        }

        _il.Emit(OpCodes.Br, after);

        for (int at = 0; at < chosen.Cases.Count; at++)
        {
            _il.MarkLabel(groups[at]);
            EmitStatements(chosen.Cases[at].Body);
            _il.Emit(OpCodes.Br, after);
        }

        _il.MarkLabel(after);
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

        _loops.Push((after, test, _protection));
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

        _loops.Push((after, test, _protection));
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

        _loops.Push((after, top, _protection));
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

        _loops.Push((after, next, _protection));
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

    /// <summary>
    /// <para>Goes where a <c>break</c> or a <c>continue</c> means to go, by whichever branch the
    /// place it is written allows.</para>
    /// <para>An ordinary <c>br</c> where the loop and the word are equally protected, which is
    /// nearly always. A <c>leave</c> where the word sits under a <c>try</c> the loop does not: the
    /// CLR refuses a plain branch out of a protected region, since leaving one has to run its
    /// <c>finally</c>, and <c>leave</c> is the instruction that says so.</para>
    /// </summary>
    private void LeaveLoop(Label target) =>
        _il.Emit(_protection > _loops.Peek().Protection ? OpCodes.Leave : OpCodes.Br, target);

    /// <summary>
    /// Leaves rather than returns, so that a yield written inside a <c>loop each</c> is legal:
    /// the walk wraps its body to unmark the set, and a <c>ret</c> inside a protected region is
    /// not something the CLR will run at all.
    /// </summary>
    private void EmitYield(YieldStmt yield)
    {
        if (yield.Value is not null)
        {
            EmitValueInto(yield.Value);
            _il.Emit(OpCodes.Stloc, _result!);
        }

        _il.Emit(OpCodes.Leave, _exit);
    }

    /// <summary>
    /// Closes a body: the one place every path arrives at, and the one <c>ret</c>. Where the
    /// function yields a value the slot holds it, and where it yields none there is nothing to
    /// load.
    /// </summary>
    private void End()
    {
        _il.MarkLabel(_exit);

        if (_result is not null)
        {
            _il.Emit(OpCodes.Ldloc, _result);
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
        new($"the emitter met {what}, which it has no sequence for");
}
