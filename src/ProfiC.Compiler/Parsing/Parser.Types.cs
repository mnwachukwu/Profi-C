using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Parsing;

public sealed partial class Parser
{
    /// <summary>True if a token can begin a type.</summary>
    private static bool StartsType(TokenType type) => type switch
    {
        TokenType.Integer or TokenType.Real or TokenType.Character or TokenType.Boolean
            or TokenType.String or TokenType.Fraction or TokenType.Identifier
            or TokenType.Function => true,
        _ => false,
    };

    /// <summary>
    /// <para>Parses a type, applying suffixes left to right.</para>
    /// <para>Order matters and the two arrangements differ: <c>Node?[]</c> builds a set
    /// wrapping an optional, while <c>Node[]?</c> builds an optional wrapping a set. Reading
    /// the suffixes in the order written produces that naturally.</para>
    /// <para>A function type may be preceded by its return type, so a name followed by
    /// <c>function</c> turns out to have been a return type rather than the whole thing.</para>
    /// </summary>
    private TypeSyntax ParseType()
    {
        Token start = Current;

        if (!StartsType(Kind))
        {
            _diagnostics.Report(DiagnosticDescriptors.ExpectedType, Current.Span, Describe(Current));
            return new MissingType(EmptySpanHere());
        }

        TypeSyntax type;

        if (AtFunctionType())
        {
            type = ParseFunctionType(start, returnType: null);
        }
        else
        {
            type = ApplySuffixes(ParseBaseType(), start);

            // "integer function(...)" — what was read is the return type, not the whole type.
            if (AtFunctionType())
            {
                type = ParseFunctionType(start, type);
            }
        }

        return ApplySuffixes(type, start);
    }

    /// <summary>
    /// <para>True when <c>function</c> here begins a function <em>type</em> rather than a
    /// function <em>declaration</em>.</para>
    /// <para>The word has three jobs: <c>function Name(</c> declares one, <c>function(</c> in
    /// type position describes one, and <c>function(</c> in expression position is a lambda.
    /// A single token of lookahead separates the first from the other two, and position
    /// separates those two from each other.</para>
    /// </summary>
    private bool AtFunctionType() =>
        Check(TokenType.Function) && !CheckNext(TokenType.Identifier);

    /// <summary>True when <c>function</c> here begins a function declaration.</summary>
    private bool AtFunctionDeclaration() =>
        Check(TokenType.Function) && CheckNext(TokenType.Identifier);

    /// <summary>Reads the <c>[]</c> and <c>?</c> suffixes that follow a type.</summary>
    private TypeSyntax ApplySuffixes(TypeSyntax type, Token start)
    {
        while (true)
        {
            if (Check(TokenType.LeftBracket) && CheckNext(TokenType.RightBracket))
            {
                Advance();
                Advance();
                type = new SetTypeSyntax(SpanFrom(start), type);
                continue;
            }

            if (Match(TokenType.Question))
            {
                type = new OptionalTypeSyntax(SpanFrom(start), type);
                continue;
            }

            return type;
        }
    }

    /// <summary>Reads a built-in type word or a name.</summary>
    private TypeSyntax ParseBaseType()
    {
        Token token = Advance();

        string name = token.Type switch
        {
            TokenType.Identifier => token.Name,
            _ => token.Type.Text() ?? token.Lexeme,
        };

        return new NamedTypeSyntax(token.Span, name);
    }

    /// <summary>
    /// Reads <c>function ( types )</c>, given whatever return type preceded it.
    /// </summary>
    private FunctionTypeSyntax ParseFunctionType(Token start, TypeSyntax? returnType)
    {
        Expect(TokenType.Function);
        Expect(TokenType.LeftParen);

        List<TypeSyntax> parameters = [];

        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(ParseType());
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RightParen);

        return new FunctionTypeSyntax(SpanFrom(start), returnType, parameters);
    }
}
