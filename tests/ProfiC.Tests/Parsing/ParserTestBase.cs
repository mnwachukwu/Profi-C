using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Parsing;

/// <summary>Helpers for parsing snippets and inspecting the resulting trees.</summary>
public abstract class ParserTestBase
{
    /// <summary>Parses a whole file, asserting it produced no diagnostics.</summary>
    protected static CompilationUnit ParseUnit(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);

        Assert.That(
            diagnostics.Sorted().Select(d => $"({d.Span.Start.Line},{d.Span.Start.Column}) {d.Id}: {d.Message}"),
            Is.Empty,
            "expected the snippet to parse cleanly");

        return unit;
    }

    /// <summary>Parses and returns both the tree and whatever was reported.</summary>
    protected static (CompilationUnit Unit, DiagnosticBag Diagnostics) ParseRaw(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        return (unit, diagnostics);
    }

    /// <summary>Wraps statements in the smallest legal program and returns them.</summary>
    protected static IReadOnlyList<Statement> ParseStatements(string body)
    {
        CompilationUnit unit = ParseUnit($$"""
            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """);

        ModelDecl model = (ModelDecl)unit.Declarations[0];
        return ((FunctionDecl)model.Members[0]).Body!;
    }

    /// <summary>Parses a single expression by way of a variable initializer.</summary>
    protected static Expression ParseExpression(string expression)
    {
        IReadOnlyList<Statement> statements = ParseStatements($"        let value = {expression};");
        return ((VarDeclStmt)statements[0]).Initializer!;
    }

    /// <summary>Parses one expression and returns both it and whatever was reported.</summary>
    protected static (Expression Expression, DiagnosticBag Diagnostics)
        ParseExpressionWithDiagnostics(string expression)
    {
        (CompilationUnit unit, DiagnosticBag diagnostics) = ParseRaw($$"""
            shared model Program
                function Main()
                    let value = {{expression}};
                end function
            end model
            """);

        ModelDecl model = (ModelDecl)unit.Declarations[0];
        Statement statement = ((FunctionDecl)model.Members[0]).Body![0];

        return (((VarDeclStmt)statement).Initializer!, diagnostics);
    }

    /// <summary>Parses a single type by way of a variable declaration.</summary>
    protected static TypeSyntax ParseType(string type)
    {
        IReadOnlyList<Statement> statements = ParseStatements($"        {type} value;");
        return ((VarDeclStmt)statements[0]).Type!;
    }

    /// <summary>Renders a tree, normalizing line endings so assertions are portable.</summary>
    protected static string Print(SyntaxNode node) =>
        AstPrinter.Print(node).ReplaceLineEndings("\n");

    /// <summary>The diagnostic identifiers reported, in source order.</summary>
    protected static string[] IdsOf(DiagnosticBag bag) => [.. bag.Sorted().Select(d => d.Id)];
}
