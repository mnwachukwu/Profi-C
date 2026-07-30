using System.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>Renders a syntax tree as an indented outline.</para>
/// <para>Each line names the node kind, then whatever distinguishes that node from another of
/// the same kind: a name, an operator, a literal's text. Positions are omitted by default,
/// since including them makes the output churn on every whitespace edit; pass
/// <c>includePositions</c> when the position is what is being investigated.</para>
/// </summary>
public sealed class AstPrinter(bool includePositions = false)
{
    private readonly StringBuilder _builder = new();
    private int _depth;

    /// <summary>Renders a tree.</summary>
    public static string Print(SyntaxNode node, bool includePositions = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        AstPrinter printer = new(includePositions);
        printer.Write(node);
        return printer._builder.ToString();
    }

    private void Write(SyntaxNode node)
    {
        _builder.Append(' ', _depth * 2).Append(node.NodeKind);

        string detail = Describe(node);

        if (detail.Length > 0)
        {
            _builder.Append(' ').Append(detail);
        }

        if (includePositions)
        {
            _builder.Append(" @").Append(node.Line).Append(':').Append(node.Column);
        }

        _builder.Append('\n');

        _depth++;

        foreach (SyntaxNode child in node.Children)
        {
            Write(child);
        }

        _depth--;
    }

    /// <summary>
    /// The distinguishing detail for a node, or an empty string when the kind says
    /// everything. Written as a switch rather than a virtual member so that the tree stays
    /// free of presentation concerns.
    /// </summary>
    private static string Describe(SyntaxNode node) => node switch
    {
        QualifiedName n => Quote(n.Text),
        NamespaceDecl n => Quote(n.Name.Text) + (n.IsFileScoped ? " file-scoped" : string.Empty),
        ModelDecl n => Name(n.Name, n.Modifiers)
                       + (n.BaseTypeName is null ? string.Empty : $" extends {n.BaseTypeName}"),
        StructureDecl n => Name(n.Name, n.Modifiers),
        EnumerationDecl n => Name(n.Name, n.Modifiers),
        EnumMemberDecl n => Quote(n.Name),
        FieldDecl n => Name(n.Name, n.Modifiers),
        FunctionDecl n => Name(n.Name, n.Modifiers),
        ParameterDecl n => Quote(n.Name),

        NamedTypeSyntax n => Quote(n.Text),

        VarDeclStmt n => Quote(n.Name)
                         + (n.IsConstant ? " [constant]" : string.Empty)
                         + (n.IsInferred ? " [inferred]" : string.Empty),
        ForStmt n => Quote(n.VariableName) + (n.IsInclusive ? " to" : " until"),
        ForEachStmt n => Quote(n.VariableName),
        CatchClause n => Quote(n.VariableName),

        LiteralExpr n => $"{n.Text} [{n.Kind}]",

        // The text is quoted so that an empty run between two holes is visible rather than
        // reading as nothing at all.
        InterpolatedStringExpr n => string.Join(" ", n.Texts.Select(t => $"'{t}'")),
        InterpolationPart n => n.Format is null ? string.Empty : $"format '{n.Format}'",

        IdentifierExpr n => Quote(n.Name),
        UnaryExpr n => Quote(n.Operator.Spelling()),
        BinaryExpr n => Quote(n.Operator.Spelling()),
        MemberExpr n => Quote(n.MemberName),
        NewExpr n => Quote(n.TypeName),
        LambdaExpr n => n.IsExpressionBodied ? "inline" : "block",

        _ => string.Empty,
    };

    private static string Quote(string text) => $"'{text}'";

    private static string Name(string name, DeclarationModifiers modifiers)
    {
        string words = modifiers.ToDisplayString();
        return words.Length == 0 ? Quote(name) : $"{Quote(name)} [{words}]";
    }
}
