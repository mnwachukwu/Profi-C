using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>A whole source file: its <c>using</c> directives, then its declarations.</para>
/// <para>A file contains declarations and nothing else — no top-level statements, functions,
/// or variables.</para>
/// </summary>
public sealed class CompilationUnit(
    SourceSpan span,
    IReadOnlyList<UsingDirective> usings,
    IReadOnlyList<ImportDirective> imports,
    IReadOnlyList<Declaration> declarations,
    SourceText source) : SyntaxNode(span)
{
    public IReadOnlyList<UsingDirective> Usings { get; } = usings;

    /// <summary>
    /// The files this one names to be compiled with it. Read before compiling begins, since
    /// they decide what there is to compile.
    /// </summary>
    public IReadOnlyList<ImportDirective> Imports { get; } = imports;

    public IReadOnlyList<Declaration> Declarations { get; } = declarations;

    /// <summary>The file this tree was parsed from, carried for diagnostics.</summary>
    public SourceText Source { get; } = source;

    public override IEnumerable<SyntaxNode> Children =>
        Usings.Concat<SyntaxNode>(Imports).Concat(Declarations);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitCompilationUnit(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitCompilationUnit(this);
}

/// <summary>A <c>using</c> directive. In v1 these name Profi-C namespaces, never CLR ones.</summary>
public sealed class UsingDirective(SourceSpan span, QualifiedName name) : Declaration(span)
{
    public QualifiedName Name { get; } = name;

    public override IEnumerable<SyntaxNode> Children => [Name];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitUsingDirective(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitUsingDirective(this);
}

/// <summary>
/// <para>An <c>import</c>, naming one file to compile alongside this one.</para>
/// <para>Composition rather than visibility: an import decides which files are compiled and
/// nothing about which names are reachable. Making names reachable is what <c>using</c> does.
/// </para>
/// </summary>
public sealed class ImportDirective(SourceSpan span, string path) : Declaration(span)
{
    /// <summary>The path as written, before it is resolved against the importing file.</summary>
    public string Path { get; } = path;

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitImportDirective(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitImportDirective(this);
}

/// <summary>A dotted name, as in <c>Standard.Text</c>.</summary>
public sealed class QualifiedName(SourceSpan span, IReadOnlyList<string> parts) : SyntaxNode(span)
{
    public IReadOnlyList<string> Parts { get; } = parts;

    /// <summary>The name as written.</summary>
    public string Text => string.Join('.', Parts);

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitQualifiedName(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitQualifiedName(this);
}

/// <summary>
/// A namespace declaration in either form. The file-scoped form has no closer and takes
/// everything that follows it; the block form closes with <c>end namespace</c>.
/// </summary>
public sealed class NamespaceDecl(
    SourceSpan span,
    QualifiedName name,
    IReadOnlyList<Declaration> declarations,
    bool isFileScoped) : Declaration(span)
{
    public QualifiedName Name { get; } = name;

    public IReadOnlyList<Declaration> Declarations { get; } = declarations;

    /// <summary>True for the <c>namespace X;</c> form, which has no <c>end namespace</c>.</summary>
    public bool IsFileScoped { get; } = isFileScoped;

    public override IEnumerable<SyntaxNode> Children => new SyntaxNode[] { Name }.Concat(Declarations);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitNamespaceDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitNamespaceDecl(this);
}

/// <summary>
/// A model declaration. <c>BaseTypeName</c> is null when nothing was written after
/// <c>extends</c>, which means the model extends <c>Model</c> implicitly.
/// </summary>
public sealed class ModelDecl(
    SourceSpan span,
    DeclarationModifiers modifiers,
    string name,
    string? baseTypeName,
    IReadOnlyList<Declaration> members) : Declaration(span)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    public string Name { get; } = name;

    public string? BaseTypeName { get; } = baseTypeName;

    public IReadOnlyList<Declaration> Members { get; } = members;

    public override IEnumerable<SyntaxNode> Children => Members;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitModelDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitModelDecl(this);
}

/// <summary>
/// A structure declaration. Structures have no <c>extends</c> clause: a value type cannot
/// inherit without slicing.
/// </summary>
public sealed class StructureDecl(
    SourceSpan span,
    DeclarationModifiers modifiers,
    string name,
    IReadOnlyList<Declaration> members) : Declaration(span)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    public string Name { get; } = name;

    public IReadOnlyList<Declaration> Members { get; } = members;

    public override IEnumerable<SyntaxNode> Children => Members;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitStructureDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitStructureDecl(this);
}

/// <summary>An enumeration declaration. Members are integer-backed.</summary>
public sealed class EnumerationDecl(
    SourceSpan span,
    DeclarationModifiers modifiers,
    string name,
    IReadOnlyList<EnumMemberDecl> members) : Declaration(span)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    public string Name { get; } = name;

    public IReadOnlyList<EnumMemberDecl> Members { get; } = members;

    public override IEnumerable<SyntaxNode> Children => Members;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitEnumerationDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitEnumerationDecl(this);
}

/// <summary>
/// One member of an enumeration. A null value means the ordinal follows from position,
/// continuing from the previous explicit value.
/// </summary>
public sealed class EnumMemberDecl(SourceSpan span, string name, Expression? value)
    : Declaration(span)
{
    public string Name { get; } = name;

    public Expression? Value { get; } = value;

    public override IEnumerable<SyntaxNode> Children => NonNull(Value);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitEnumMemberDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitEnumMemberDecl(this);
}

/// <summary>
/// A field. The <c>constant</c> modifier lives in <see cref="Modifiers"/>; that a constant
/// requires an initializer is a semantic check rather than a parse failure, which gives a
/// better message.
/// </summary>
public sealed class FieldDecl(
    SourceSpan span,
    DeclarationModifiers modifiers,
    TypeSyntax type,
    string name,
    Expression? initializer) : Declaration(span)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    public TypeSyntax Type { get; } = type;

    public string Name { get; } = name;

    public Expression? Initializer { get; } = initializer;

    public override IEnumerable<SyntaxNode> Children => NonNull(Type, Initializer);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitFieldDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitFieldDecl(this);
}

/// <summary>
/// <para>A function. A null return type is a function that yields nothing.</para>
/// <para>A constructor is a function whose name matches its enclosing type and which
/// declares no return type; recognizing that is the resolver's job, so nothing here
/// distinguishes the two.</para>
/// </summary>
public sealed class FunctionDecl(
    SourceSpan span,
    DeclarationModifiers modifiers,
    TypeSyntax? returnType,
    string name,
    IReadOnlyList<ParameterDecl> parameters,
    IReadOnlyList<Statement>? body) : Declaration(span)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    public TypeSyntax? ReturnType { get; } = returnType;

    public string Name { get; } = name;

    public IReadOnlyList<ParameterDecl> Parameters { get; } = parameters;

    /// <summary>
    /// <para>The statements between the signature and <c>end function</c>, or null where the
    /// declaration ended at a semicolon and there is no body at all.</para>
    /// <para>Null and empty are different answers. An empty body is a function that does
    /// nothing; no body is a function whose descendants supply one, which only an
    /// <c>abstract</c> function may be.</para>
    /// </summary>
    public IReadOnlyList<Statement>? Body { get; } = body;

    /// <summary>True when the declaration ended at a semicolon, leaving the body to a descendant.</summary>
    public bool IsBodiless => Body is null;

    public override IEnumerable<SyntaxNode> Children =>
        NonNull(ReturnType).Concat(Parameters).Concat(Body ?? []);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitFunctionDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitFunctionDecl(this);
}

/// <summary>One parameter of a function or lambda.</summary>
public sealed class ParameterDecl(SourceSpan span, TypeSyntax? type, string name)
    : Declaration(span)
{
    /// <summary>
    /// The written type, or null for a lambda parameter left for the surrounding code to
    /// settle. A declared function's parameters always carry one.
    /// </summary>
    public TypeSyntax? Type { get; } = type;

    public string Name { get; } = name;

    public override IEnumerable<SyntaxNode> Children => NonNull(Type);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitParameterDecl(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitParameterDecl(this);
}
