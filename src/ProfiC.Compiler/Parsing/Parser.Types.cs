using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Parsing;

public sealed partial class Parser
{
    /// <summary>
    /// <para>True if a token can begin a type.</para>
    /// <para><c>function</c> is in the list although it is not one: it is what a reader writes
    /// reaching for a function type, and letting it begin one here is what buys a single
    /// message saying so instead of a cascade about a type that never arrived.</para>
    /// </summary>
    private static bool StartsType(TokenType type) => type switch
    {
        TokenType.Integer or TokenType.Real or TokenType.Float or TokenType.Character or TokenType.Boolean
            or TokenType.String or TokenType.Fraction or TokenType.Identifier
            or TokenType.Delegate or TokenType.Function => true,
        _ => false,
    };

    /// <summary>
    /// <para>Parses a type, applying suffixes left to right.</para>
    /// <para>Order matters and the two arrangements differ: <c>Node?[]</c> builds a set
    /// wrapping an optional, while <c>Node[]?</c> builds an optional wrapping a set. Reading
    /// the suffixes in the order written produces that naturally.</para>
    /// <para>A delegate type may be preceded by its result, so a name followed by
    /// <c>delegate</c> turns out to have been a result rather than the whole thing — and since
    /// a result may itself be a delegate type, that repeats. <c>integer delegate(integer)
    /// delegate(integer)</c> is a function yielding a function, and the loop below is the only
    /// reason it can be written at all.</para>
    /// </summary>
    private TypeSyntax ParseType()
    {
        Token start = Current;

        if (!StartsType(Kind))
        {
            _diagnostics.Report(DiagnosticDescriptors.ExpectedType, Current.Span, Describe(Current));
            return new MissingType(EmptySpanHere());
        }

        TypeSyntax type = AtDelegateType()
            ? ParseDelegateType(start, result: null)
            : ApplySuffixes(ParseBaseType(), start);

        // Each turn takes what has been read so far as the result of the next. One word tells
        // the two apart, so no lookahead is needed and nesting has no depth limit.
        while (AtDelegateType())
        {
            type = ApplySuffixes(ParseDelegateType(start, type), start);
        }

        return ApplySuffixes(type, start);
    }

    /// <summary>
    /// <para>True where a delegate type begins.</para>
    /// <para>Written <c>function</c> it is the mistake this reads as the type meant, so that
    /// the declaration around it is checked normally and the one thing wrong with it is said
    /// once. That is also why no lookahead is needed: <c>delegate</c> only ever writes a type,
    /// where <c>function</c> also declares one and also makes a lambda.</para>
    /// </summary>
    private bool AtDelegateType() =>
        Check(TokenType.Delegate)
        || (Check(TokenType.Function) && !CheckNext(TokenType.Identifier));

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

        // "Function(string)" is the shape someone writes reaching for a delegate type, having
        // met Function as the root and taken it for the way one is spelled. It is read as the
        // type they meant, so the declaration around it goes on to be checked normally and
        // the one thing wrong with it is said once.
        if (name == "Function" && Check(TokenType.LeftParen))
        {
            _diagnostics.Report(DiagnosticDescriptors.FunctionTypeIsLowercase, token.Span);
            return new FunctionTypeSyntax(SpanFrom(token), returnType: null, ParseTypeList());
        }

        // A dotted name says where to look before saying what to look for. Only a name may be
        // qualified, since everything else a type can be is built out of one.
        List<string> parts = [name];

        while (Check(TokenType.Dot))
        {
            Advance();
            parts.Add(ExpectIdentifier());
        }

        return new NamedTypeSyntax(SpanFrom(token), parts);
    }

    /// <summary>
    /// Reads <c>delegate ( types )</c>, given whatever result preceded it.
    /// </summary>
    private FunctionTypeSyntax ParseDelegateType(Token start, TypeSyntax? result)
    {
        if (Check(TokenType.Function))
        {
            _diagnostics.Report(DiagnosticDescriptors.FunctionTypeIsDelegate, Current.Span);
        }

        Advance();

        // Read before the span is taken: the span runs to the last token consumed, and an
        // argument written inline would be measured before the parameters were read.
        List<TypeSyntax> parameters = ParseTypeList();

        return new FunctionTypeSyntax(SpanFrom(start), result, parameters);
    }

    /// <summary>The parenthesized types a function type takes, which may be none.</summary>
    private List<TypeSyntax> ParseTypeList()
    {
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

        return parameters;
    }
}
