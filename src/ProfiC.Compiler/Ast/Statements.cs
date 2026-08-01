using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// An anonymous scope, written <c>begin</c> … <c>end</c>. This is the only construct
/// <c>begin</c> introduces: it is never a body opener.
/// </summary>
/// <summary>
/// <para>A loop marked as walking a sequence, so the sequence can refuse to be changed while
/// it runs.</para>
/// <para>Built by lowering and never by the parser. A <c>for each</c> becomes an index loop
/// over a held count, and by the time it does nothing left in the tree says a walk is
/// happening — this is what says it. Most changes to a walked sequence are refused while
/// compiling (`PC0243`); this is what catches the rest, where the set was reached under
/// another name or handed to a function.</para>
/// </summary>
public sealed class WalkStmt(SourceSpan span, Expression sequence, Statement body)
    : Statement(span)
{
    /// <summary>The sequence being walked, as a name lowering can evaluate cheaply.</summary>
    public Expression Sequence { get; } = sequence;

    public Statement Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children => [Sequence, Body];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitWalkStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitWalkStmt(this);
}

public sealed class BlockStmt(SourceSpan span, IReadOnlyList<Statement> statements)
    : Statement(span)
{
    public IReadOnlyList<Statement> Statements { get; } = statements;

    public override IEnumerable<SyntaxNode> Children => Statements;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitBlockStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitBlockStmt(this);
}

/// <summary>
/// <para>A local variable. A null <see cref="Type"/> means the <c>let</c> form, whose type
/// is inferred and whose initializer is therefore required.</para>
/// <para><c>constant</c> may not decorate a <c>let</c>, so a constant always has a type
/// written out.</para>
/// </summary>
public sealed class VarDeclStmt(
    SourceSpan span,
    TypeSyntax? type,
    string name,
    Expression? initializer,
    bool isConstant) : Statement(span)
{
    public TypeSyntax? Type { get; } = type;

    public string Name { get; } = name;

    public Expression? Initializer { get; } = initializer;

    public bool IsConstant { get; } = isConstant;

    /// <summary>True for the <c>let</c> form, where the type comes from the initializer.</summary>
    public bool IsInferred => Type is null;

    public override IEnumerable<SyntaxNode> Children => NonNull(Type, Initializer);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitVarDeclStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitVarDeclStmt(this);
}

/// <summary>
/// A declaration appearing among statements. Functions, models, and structures may all be
/// declared inside a function body, where they capture the enclosing locals — which is why
/// the resolver cannot collect types in a separate pass over declarations alone.
/// </summary>
public sealed class LocalDeclStmt(SourceSpan span, Declaration declaration) : Statement(span)
{
    public Declaration Declaration { get; } = declaration;

    public override IEnumerable<SyntaxNode> Children => [Declaration];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitLocalDeclStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitLocalDeclStmt(this);
}

/// <summary>
/// <para>An <c>if</c> and its whole chain, closing once with <c>end if</c>.</para>
/// <para><c>else if</c> is part of this node rather than a nested <c>if</c>. That is what
/// makes a three-way branch close with one <c>end if</c> instead of three, and it removes
/// the dangling-else problem entirely.</para>
/// </summary>
public sealed class IfStmt(
    SourceSpan span,
    Expression condition,
    IReadOnlyList<Statement> thenBody,
    IReadOnlyList<ElseIfClause> elseIfClauses,
    IReadOnlyList<Statement>? elseBody) : Statement(span)
{
    public Expression Condition { get; } = condition;

    public IReadOnlyList<Statement> ThenBody { get; } = thenBody;

    public IReadOnlyList<ElseIfClause> ElseIfClauses { get; } = elseIfClauses;

    /// <summary>Null when no <c>else</c> was written, which is distinct from an empty one.</summary>
    public IReadOnlyList<Statement>? ElseBody { get; } = elseBody;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Condition }
            .Concat(ThenBody)
            .Concat(ElseIfClauses)
            .Concat(ElseBody ?? []);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitIfStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitIfStmt(this);
}

/// <summary>One <c>else if</c> arm of an <see cref="IfStmt"/>.</summary>
public sealed class ElseIfClause(
    SourceSpan span,
    Expression condition,
    IReadOnlyList<Statement> body) : SyntaxNode(span)
{
    public Expression Condition { get; } = condition;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Condition }.Concat(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitElseIfClause(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitElseIfClause(this);
}

/// <summary>A <c>while</c> loop.</summary>
public sealed class WhileStmt(
    SourceSpan span,
    Expression condition,
    IReadOnlyList<Statement> body) : Statement(span)
{
    public Expression Condition { get; } = condition;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Condition }.Concat(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitWhileStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitWhileStmt(this);
}

/// <summary>
/// <para>A <c>loop</c> with no condition anywhere, which runs until something leaves it.</para>
/// <para>Its own kind rather than a <c>while true</c> in disguise, because the two differ in
/// what can be proved about them. A condition that happens to be the literal <c>true</c> is
/// still a condition, and reading it would mean constant-folding to learn what the writer
/// already said. Here the absence of one *is* the statement, so the end of the loop is
/// unreachable unless the body breaks out — which is what lets a function end in one and still
/// satisfy the rule that every path yields.</para>
/// </summary>
public sealed class LoopForeverStmt(
    SourceSpan span,
    IReadOnlyList<Statement> body) : Statement(span)
{
    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children => Body;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitLoopForeverStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitLoopForeverStmt(this);
}

/// <summary>
/// <para>A <c>loop</c> whose condition is tested after the body, so the body always runs at
/// least once.</para>
/// <para>The condition is written where it is tested, at the bottom, which is what separates
/// this from a <c>while</c> that merely happens to be spelled differently. It is the one loop
/// closed by <c>until</c> rather than by <c>end loop</c>, because the word carrying the
/// condition is doing the closing.</para>
/// <para>The sense is "keep going until", so the loop ends when the condition holds — the
/// opposite of <see cref="WhileStmt"/>, and the same reading <c>until</c> has as a range
/// loop's exclusive bound.</para>
/// </summary>
public sealed class LoopUntilStmt(
    SourceSpan span,
    IReadOnlyList<Statement> body,
    Expression condition) : Statement(span)
{
    public IReadOnlyList<Statement> Body { get; } = body;

    public Expression Condition { get; } = condition;

    public override IEnumerable<SyntaxNode> Children =>
        Body.Cast<SyntaxNode>().Append(Condition);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitLoopUntilStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitLoopUntilStmt(this);
}

/// <summary>
/// <para>The range <c>for</c>. There is no three-clause form: it carried an increment written
/// before the body but executed after it, which is the worst teaching problem the language
/// had.</para>
/// <para>The bound is inclusive with <c>to</c> and exclusive with <c>until</c>. A null step
/// means one. The sign of the step is evaluated once on entry and selects the comparison, so
/// the emitter must branch at run time rather than choosing at compile time.</para>
/// </summary>
public sealed class ForStmt(
    SourceSpan span,
    string variableName,
    Expression start,
    Expression bound,
    bool isInclusive,
    Expression? step,
    IReadOnlyList<Statement> body) : Statement(span)
{
    /// <summary>
    /// <para>The counter's name. It has no written type: a range loop counts, and counting is
    /// done with integers.</para>
    /// <para>This is not inference — nothing is worked out from the bounds. The type is fixed
    /// by the construct, the way <c>for each</c> takes its element's type from the sequence.
    /// </para>
    /// </summary>
    public string VariableName { get; } = variableName;

    public Expression Start { get; } = start;

    public Expression Bound { get; } = bound;

    /// <summary>True for <c>to</c>, false for <c>until</c>.</summary>
    public bool IsInclusive { get; } = isInclusive;

    public Expression? Step { get; } = step;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children =>
        NonNull(Start, Bound, Step).Concat(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitForStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitForStmt(this);
}

/// <summary>
/// <c>for each</c> over a sequence. The bound variable has no written type, unlike the range
/// form.
/// </summary>
public sealed class ForEachStmt(
    SourceSpan span,
    string variableName,
    Expression sequence,
    IReadOnlyList<Statement> body) : Statement(span)
{
    public string VariableName { get; } = variableName;

    public Expression Sequence { get; } = sequence;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Sequence }.Concat(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitForEachStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitForEachStmt(this);
}

/// <summary>
/// A <c>switch</c>. There is no fallthrough and no <c>break</c> is required, which keeps
/// <c>break</c> meaning exactly one thing in the language: leaving a loop.
/// </summary>
public sealed class SwitchStmt(
    SourceSpan span,
    Expression subject,
    IReadOnlyList<CaseGroup> cases,
    IReadOnlyList<Statement>? defaultBody) : Statement(span)
{
    public Expression Subject { get; } = subject;

    public IReadOnlyList<CaseGroup> Cases { get; } = cases;

    /// <summary>Null when no <c>default</c> clause was written.</summary>
    public IReadOnlyList<Statement>? DefaultBody { get; } = defaultBody;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Subject }.Concat(Cases).Concat(DefaultBody ?? []);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitSwitchStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitSwitchStmt(this);
}

/// <summary>
/// One or more case labels sharing a body. Several labels may stack before a single body,
/// which is how two values are handled alike without fallthrough existing.
/// </summary>
public sealed class CaseGroup(
    SourceSpan span,
    IReadOnlyList<Expression> labels,
    IReadOnlyList<Statement> body) : SyntaxNode(span)
{
    public IReadOnlyList<Expression> Labels { get; } = labels;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children => Labels.Concat<SyntaxNode>(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitCaseGroup(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitCaseGroup(this);
}

/// <summary>A <c>try</c> with its catches and optional <c>finally</c>.</summary>
public sealed class TryStmt(
    SourceSpan span,
    IReadOnlyList<Statement> body,
    IReadOnlyList<CatchClause> catches,
    IReadOnlyList<Statement>? finallyBody) : Statement(span)
{
    public IReadOnlyList<Statement> Body { get; } = body;

    public IReadOnlyList<CatchClause> Catches { get; } = catches;

    /// <summary>Null when no <c>finally</c> was written.</summary>
    public IReadOnlyList<Statement>? FinallyBody { get; } = finallyBody;

    public override IEnumerable<SyntaxNode> Children =>
        Body.Concat<SyntaxNode>(Catches).Concat(FinallyBody ?? []);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitTryStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitTryStmt(this);
}

/// <summary>One <c>catch</c> arm, binding the caught value to a name.</summary>
public sealed class CatchClause(
    SourceSpan span,
    TypeSyntax exceptionType,
    string variableName,
    IReadOnlyList<Statement> body) : SyntaxNode(span)
{
    public TypeSyntax ExceptionType { get; } = exceptionType;

    public string VariableName { get; } = variableName;

    public IReadOnlyList<Statement> Body { get; } = body;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { ExceptionType }.Concat(Body);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitCatchClause(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitCatchClause(this);
}

/// <summary>A <c>throw</c>.</summary>
public sealed class ThrowStmt(SourceSpan span, Expression exception) : Statement(span)
{
    public Expression Exception { get; } = exception;

    public override IEnumerable<SyntaxNode> Children => [Exception];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitThrowStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitThrowStmt(this);
}

/// <summary>
/// A <c>yield</c>, which is this language's return statement and has nothing to do with
/// iterators. A null value returns from a function that yields nothing.
/// </summary>
public sealed class YieldStmt(SourceSpan span, Expression? value) : Statement(span)
{
    public Expression? Value { get; } = value;

    public override IEnumerable<SyntaxNode> Children => NonNull(Value);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitYieldStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitYieldStmt(this);
}

/// <summary>Leaves the innermost loop.</summary>
public sealed class BreakStmt(SourceSpan span) : Statement(span)
{
    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitBreakStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitBreakStmt(this);
}

/// <summary>Begins the innermost loop's next iteration.</summary>
public sealed class ContinueStmt(SourceSpan span) : Statement(span)
{
    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitContinueStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitContinueStmt(this);
}

/// <summary>
/// An expression evaluated for its effect. May not begin with <c>(</c> or <c>-</c>: since a
/// construct's body has no opening token, either would let a preceding condition swallow the
/// statement.
/// </summary>
public sealed class ExpressionStmt(SourceSpan span, Expression expression) : Statement(span)
{
    public Expression Expression { get; } = expression;

    public override IEnumerable<SyntaxNode> Children => [Expression];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitExpressionStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitExpressionStmt(this);
}

/// <summary>
/// <para>An assignment.</para>
/// <para>Assignment is a statement rather than an expression, so <c>if x = 5</c> is a syntax
/// error rather than a warning about an accidental assignment. The parser reaches this by
/// parsing a full expression and then finding <c>=</c>, which is also what makes a complex
/// target such as <c>a[i]</c> or <c>p.field</c> fall out without special cases.</para>
/// </summary>
public sealed class AssignmentStmt(SourceSpan span, Expression target, Expression value)
    : Statement(span)
{
    public Expression Target { get; } = target;

    public Expression Value { get; } = value;

    public override IEnumerable<SyntaxNode> Children => [Target, Value];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitAssignmentStmt(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitAssignmentStmt(this);
}
