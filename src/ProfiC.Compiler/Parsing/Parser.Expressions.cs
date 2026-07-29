using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Parsing;

public sealed partial class Parser
{
    /// <summary>
    /// <para>Parses an expression by precedence climbing.</para>
    /// <para>The loop stops at the first token whose left binding power is below the current
    /// minimum, which is also what makes an expression terminate cleanly where a construct's
    /// body begins — no closing token is needed for that to work.</para>
    /// </summary>
    private Expression ParseExpression(int minimumBindingPower = 0)
    {
        Expression left = ParsePrefix();

        while (!AtEnd)
        {
            // Postfix binds tightest: a call, an index, or a member access.
            if (Operators.PostfixBindingPower(Kind) is { } postfix
                && postfix >= minimumBindingPower)
            {
                Expression? extended = TryParsePostfix(left);

                if (extended is null)
                {
                    break;
                }

                left = extended;
                continue;
            }

            // "is" and "as" sit at relational precedence but take a type on the right, so
            // they cannot go through the ordinary infix path.
            if (Check(TokenType.Is) || Check(TokenType.As))
            {
                (int isLeft, _) = Operators.InfixBindingPower(Kind)!.Value;

                if (isLeft < minimumBindingPower)
                {
                    break;
                }

                bool isTest = Check(TokenType.Is);
                Advance();

                TypeSyntax target = ParseType();
                int from = left.Span.Start.Offset;

                left = isTest
                    ? new TypeTestExpr(SpanTo(from), left, target)
                    : new TypeCastExpr(SpanTo(from), left, target);

                continue;
            }

            if (Operators.InfixBindingPower(Kind) is not { } power
                || power.Left < minimumBindingPower)
            {
                break;
            }

            BinaryOperator op = Operators.InfixFrom(Kind)!.Value;
            Advance();

            Expression right = ParseExpression(power.Right);
            left = new BinaryExpr(Span(left.Span.Start.Offset, right), left, op, right);
        }

        return left;
    }

    /// <summary>A prefix operator, or a primary expression.</summary>
    private Expression ParsePrefix()
    {
        if (Operators.PrefixBindingPower(Kind) is { } power)
        {
            Token op = Advance();
            UnaryOperator kind = Operators.PrefixFrom(op.Type)!.Value;
            Expression operand = ParseExpression(power);

            return new UnaryExpr(Span(op.Span.Start.Offset, operand), kind, operand);
        }

        return ParsePrimary();
    }

    /// <summary>
    /// Extends an expression with a call, an index, or a member access. Returns null when the
    /// token turns out not to continue the expression after all.
    /// </summary>
    private Expression? TryParsePostfix(Expression receiver)
    {
        int startOffset = receiver.Span.Start.Offset;

        if (Match(TokenType.LeftParen))
        {
            List<Expression> arguments = ParseArguments();
            Expect(TokenType.RightParen);
            return new CallExpr(SpanTo(startOffset), receiver, arguments);
        }

        if (Match(TokenType.LeftBracket))
        {
            Expression index = ParseExpression();
            Expect(TokenType.RightBracket);
            return new IndexExpr(SpanTo(startOffset), receiver, index);
        }

        if (Match(TokenType.Dot))
        {
            string member = ExpectIdentifier();
            return new MemberExpr(SpanTo(startOffset), receiver, member);
        }

        return null;
    }

    private List<Expression> ParseArguments()
    {
        List<Expression> arguments = [];

        if (Check(TokenType.RightParen))
        {
            return arguments;
        }

        do
        {
            arguments.Add(ParseExpression());
        }
        while (Match(TokenType.Comma));

        return arguments;
    }

    /// <summary>The leaves of an expression, plus the bracketed forms.</summary>
    private Expression ParsePrimary()
    {
        Token token = Current;

        if (LiteralExpr.KindFrom(Kind) is { } literal)
        {
            Advance();
            return new LiteralExpr(token.Span, literal, token.Lexeme);
        }

        switch (Kind)
        {
            case TokenType.Identifier:
                Advance();
                return new IdentifierExpr(token.Span, token.Lexeme);

            case TokenType.This:
                Advance();
                return new ReceiverExpr(token.Span, ReceiverKind.This);

            case TokenType.Base:
                Advance();
                return new ReceiverExpr(token.Span, ReceiverKind.Base);

            case TokenType.New:
                return ParseNew();

            case TokenType.LeftBrace:
                return ParseCollection();

            case TokenType.Function:
                return ParseBlockLambda();

            case TokenType.If:
                return ParseIfExpression();

            case TokenType.LeftParen:
                return LooksLikeArrowLambda() ? ParseArrowLambda() : ParseParenthesized();
        }

        if (TryReportDecrement())
        {
            return new MissingExpr(EmptySpanHere());
        }

        _diagnostics.Report(DiagnosticDescriptors.ExpectedExpression, token.Span, Describe(token));
        return new MissingExpr(EmptySpanHere());
    }

    /// <summary>
    /// <para>Reports <c>i--</c> as an attempted decrement, which the language does not have.
    /// </para>
    /// <para>The scanner cannot make this call: <c>x--1</c> is a perfectly good subtraction of
    /// negative one, and telling the two apart needs to know whether an operand follows.
    /// Arriving here, with an operand required and absent, is that proof. The two signs must
    /// also be adjacent — <c>x - - ;</c> is equally wrong but deserves the plain
    /// "expected an expression" instead.</para>
    /// </summary>
    private bool TryReportDecrement()
    {
        if (_position < 2)
        {
            return false;
        }

        Token second = _tokens[_position - 1];
        Token first = _tokens[_position - 2];

        if (first.Type != TokenType.Minus
            || second.Type != TokenType.Minus
            || first.Span.EndOffset != second.Span.Start.Offset)
        {
            return false;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.NotAnOperator,
            new Text.SourceSpan(first.Span.Start, 2),
            "--",
            "Profi-C has no decrement operator. Write 'x = x - 1'.");

        return true;
    }

    private Expression ParseNew()
    {
        Token start = Advance();
        string typeName = ExpectIdentifier();

        Expect(TokenType.LeftParen);
        List<Expression> arguments = ParseArguments();
        Expect(TokenType.RightParen);

        return new NewExpr(SpanFrom(start), typeName, arguments);
    }

    private Expression ParseCollection()
    {
        Token start = Advance();
        List<Expression> elements = [];

        if (!Check(TokenType.RightBrace))
        {
            do
            {
                elements.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RightBrace);
        return new CollectionExpr(SpanFrom(start), elements);
    }

    private Expression ParseParenthesized()
    {
        Token start = Advance();
        Expression inner = ParseExpression();
        Expect(TokenType.RightParen);

        return new ParenthesizedExpr(SpanFrom(start), inner);
    }

    /// <summary>
    /// <para><c>if c then a else b</c>, the value-producing conditional.</para>
    /// <para>Told apart from a statement <c>if</c> by the caller, which only reaches here in
    /// expression position.</para>
    /// </summary>
    private Expression ParseIfExpression()
    {
        Token start = Advance();
        Expression condition = ParseExpression();

        Expect(TokenType.Then);
        Expression thenValue = ParseExpression();

        if (!Check(TokenType.Else))
        {
            // Reported here rather than through Expect, and the branch is not attempted: with
            // no 'else' there is nothing to read, and trying anyway reports the same token
            // twice — once for the word that is missing and once for the value that follows it.
            _diagnostics.Report(DiagnosticDescriptors.IfExpressionWithoutElse, SpanFrom(start));

            return new IfExpr(
                SpanFrom(start), condition, thenValue, new MissingExpr(EmptySpanHere()));
        }

        Advance();
        Expression elseValue = ParseExpression();

        return new IfExpr(SpanFrom(start), condition, thenValue, elseValue);
    }

    /// <summary>The <c>function(…) … end function</c> lambda.</summary>
    private Expression ParseBlockLambda()
    {
        Token start = Advance();

        Expect(TokenType.LeftParen);
        List<ParameterDecl> parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        List<Statement> body = ParseBody(TokenType.Function);
        ExpectEnd(TokenType.Function, "function", start);

        return LambdaExpr.Block(SpanFrom(start), parameters, body);
    }

    /// <summary>The <c>(…) =&gt; expression</c> lambda.</summary>
    private Expression ParseArrowLambda()
    {
        Token start = Advance();
        List<ParameterDecl> parameters = ParseParameterList();

        Expect(TokenType.RightParen);
        Expect(TokenType.Arrow);

        Expression body = ParseExpression();

        return LambdaExpr.Arrow(SpanFrom(start), parameters, body);
    }

    /// <summary>
    /// <para>Decides whether a <c>(</c> opens a lambda's parameter list or a parenthesized
    /// expression.</para>
    /// <para>The two readings are disjoint — nothing parses as both — but the decision cannot
    /// be made from one token, since the parentheses may nest arbitrarily. Skipping to the
    /// matching <c>)</c> and looking for <c>=&gt;</c> settles it with a token scan rather than
    /// a speculative parse, so nothing has to be parsed twice or thrown away.</para>
    /// </summary>
    private bool LooksLikeArrowLambda()
    {
        int depth = 0;

        for (int offset = 0; _position + offset < _tokens.Count; offset++)
        {
            switch (_tokens[_position + offset].Type)
            {
                case TokenType.LeftParen:
                    depth++;
                    break;

                case TokenType.RightParen:
                    depth--;

                    if (depth == 0)
                    {
                        return _tokens[_position + offset + 1].Type == TokenType.Arrow;
                    }

                    break;

                case TokenType.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    private List<ParameterDecl> ParseParameterList()
    {
        List<ParameterDecl> parameters = [];

        if (Check(TokenType.RightParen))
        {
            return parameters;
        }

        do
        {
            Token start = Current;
            TypeSyntax type = ParseType();
            string name = ExpectIdentifier();
            parameters.Add(new ParameterDecl(SpanFrom(start), type, name));
        }
        while (Match(TokenType.Comma));

        return parameters;
    }

    // ---- Span helpers -------------------------------------------------------------------

    /// <summary>A span from a start offset through the token last consumed.</summary>
    private Text.SourceSpan SpanTo(int startOffset)
    {
        Token last = _tokens[Math.Max(0, _position - 1)];
        return _source.SpanAt(startOffset, last.Span.EndOffset - startOffset);
    }

    /// <summary>A span from a start offset through the end of a node.</summary>
    private Text.SourceSpan Span(int startOffset, SyntaxNode end) =>
        _source.SpanAt(startOffset, end.Span.EndOffset - startOffset);
}
