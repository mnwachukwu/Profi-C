using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Ast;

/// <summary>
/// <para>Helpers for building syntax trees by hand.</para>
/// <para>Nothing parses yet, so every tree in these tests is constructed directly. That is
/// the point: it exercises the hierarchy independently of the parser, so a failure here is
/// unambiguously the tree's fault rather than the parser's.</para>
/// </summary>
public abstract class AstTestBase
{
    /// <summary>A distinct span per call, so tests can tell nodes apart by position.</summary>
    private static int _nextOffset;

    protected static SourceSpan NextSpan(int length = 1)
    {
        int offset = Interlocked.Increment(ref _nextOffset);
        return new SourceSpan(new SourcePosition(1, offset, offset), length);
    }

    protected static SourceSpan SpanAt(int line, int column, int offset = 0, int length = 1) =>
        new(new SourcePosition(line, column, offset), length);

    // ---- Shorthand constructors ---------------------------------------------------------

    protected static NamedTypeSyntax Named(string name) => new(NextSpan(), name);

    protected static SetTypeSyntax SetOf(TypeSyntax element) => new(NextSpan(), element);

    protected static OptionalTypeSyntax OptionalOf(TypeSyntax inner) => new(NextSpan(), inner);

    protected static LiteralExpr Int(string text) =>
        new(NextSpan(), LiteralKind.Integer, text);

    protected static LiteralExpr Str(string text) =>
        new(NextSpan(), LiteralKind.String, text);

    protected static IdentifierExpr Id(string name) => new(NextSpan(), name);

    protected static BinaryExpr Binary(Expression left, BinaryOperator op, Expression right) =>
        new(NextSpan(), left, op, right);

    protected static ParameterDecl Param(string type, string name) =>
        new(NextSpan(), Named(type), name);

    protected static FunctionDecl Function(
        string name,
        DeclarationModifiers modifiers = DeclarationModifiers.None,
        TypeSyntax? returnType = null,
        IReadOnlyList<ParameterDecl>? parameters = null,
        IReadOnlyList<Statement>? body = null) =>
        new(NextSpan(), modifiers, returnType, name, parameters ?? [], body ?? []);

    protected static ModelDecl Model(
        string name,
        DeclarationModifiers modifiers = DeclarationModifiers.None,
        string? baseTypeName = null,
        IReadOnlyList<Declaration>? members = null) =>
        new(NextSpan(), modifiers, name, baseTypeName, members ?? []);

    protected static CompilationUnit Unit(params Declaration[] declarations) =>
        new(NextSpan(), [], declarations, new SourceText(string.Empty, "<test>"));
}
