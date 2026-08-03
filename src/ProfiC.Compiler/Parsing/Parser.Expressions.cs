using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

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

            if (Operators.InfixBindingPower(Kind) is not { } power)
            {
                break;
            }

            // Which level "bitwise" is on depends on the word after it, which the table cannot
            // see. Settled before the level is compared, or "bitwise and" would be turned away
            // at the loosest of the three and never reach its own.
            if (Check(TokenType.Bitwise))
            {
                power = Operators.BitwisePower(Peek().Type);
            }

            if (power.Left < minimumBindingPower)
            {
                break;
            }

            BinaryOperator op;

            // "bitwise" says which of two words follows, so the operator is two tokens. It is
            // the only one, and only because "and" and "or" already mean something on their
            // own; every other bitwise word stands alone.
            if (Check(TokenType.Bitwise))
            {
                Advance();
                op = ReadWhichBitwiseOperation();
            }
            else
            {
                op = Operators.InfixFrom(Kind)!.Value;
                Advance();
            }

            Expression right = ParseExpression(power.Right);

            left = new BinaryExpr(Span(left.Span.Start.Offset, right), left, op, right);
        }

        return left;
    }

    /// <summary>
    /// <para>Reads the word after <c>bitwise</c>, which says which operation was meant.</para>
    /// <para>Only <c>and</c> and <c>or</c> may follow it, and only because those two already
    /// mean something on their own — <c>xor</c> claims nothing else, so it needs no qualifier.
    /// Anything else here is named rather than left to scan as a word out of place.</para>
    /// </summary>
    private BinaryOperator ReadWhichBitwiseOperation()
    {
        if (Match(TokenType.And))
        {
            return BinaryOperator.BitwiseAnd;
        }

        if (Match(TokenType.Or))
        {
            return BinaryOperator.BitwiseOr;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.BitwiseNeedsAndOrOr, Current.Span, Describe(Current));

        // The wrong word is taken with the message rather than left to be read as the start of
        // the right-hand side, where "1 bitwise not 2" would draw a second complaint about
        // "not" and an integer. A word is what was written in place of one.
        if (Kind.IsKeyword() || Check(TokenType.Identifier))
        {
            Advance();
        }

        // Carrying on as one of the two keeps the rest of the expression readable; which one
        // it is cannot matter, since the program is already refused.
        return BinaryOperator.BitwiseAnd;
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
            string member = ExpectIdentifier(out SourceSpan named);

            return new MemberExpr(SpanTo(startOffset), receiver, member)
            {
                NameSpan = named,
            };
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

    /// <summary>
    /// <para>A string with holes in it, from its opening quote to its closing one.</para>
    /// <para>The scanner has already decided where the text ends and each hole begins, so
    /// there is nothing to re-scan here: what lies between the braces arrived as ordinary
    /// tokens and is parsed by the ordinary expression parser, which is what makes any
    /// expression legal in a hole without the grammar saying so twice.</para>
    /// <para>Texts and holes alternate, and a run between two adjacent holes is an empty text
    /// rather than a missing one, so the two lists stay in step.</para>
    /// </summary>
    private Expression ParseInterpolatedString()
    {
        Token start = Current;
        Advance();

        List<string> texts = [];
        List<InterpolationPart> holes = [];
        string pending = string.Empty;

        while (Kind is not TokenType.InterpolatedStringEnd and not TokenType.EndOfFile)
        {
            if (Kind == TokenType.InterpolatedStringText)
            {
                pending += Current.Lexeme;
                Advance();
                continue;
            }

            if (Kind != TokenType.InterpolationStart)
            {
                // Nothing else can appear here: the scanner emits text, a hole, or the end.
                // Standing a token in keeps the loop moving rather than spinning on one.
                Advance();
                continue;
            }

            Token opener = Current;
            Advance();

            texts.Add(pending);
            pending = string.Empty;

            Expression value = Kind is TokenType.InterpolationEnd or TokenType.InterpolationFormat
                ? new MissingExpr(EmptySpanHere())
                : ParseExpression();

            string? format = null;

            if (Kind == TokenType.InterpolationFormat)
            {
                format = Current.Lexeme[1..];
                Advance();
            }

            holes.Add(new InterpolationPart(SpanFrom(opener), value, format));

            if (Kind == TokenType.InterpolationEnd)
            {
                Advance();
            }
        }

        texts.Add(pending);

        if (Kind == TokenType.InterpolatedStringEnd)
        {
            Advance();
        }

        return new InterpolatedStringExpr(SpanFrom(start), texts, holes);
    }

    /// <summary>The leaves of an expression, plus the bracketed forms.</summary>
    private Expression ParsePrimary()
    {
        Token token = Current;

        if (Kind == TokenType.InterpolatedStringStart)
        {
            return ParseInterpolatedString();
        }

        if (LiteralExpr.KindFrom(Kind) is { } literal)
        {
            Advance();
            return new LiteralExpr(token.Span, literal, token.Lexeme);
        }

        switch (Kind)
        {
            case TokenType.Identifier:
                Advance();
                // The span and the name are the same characters here, but recording it says so
                // rather than leaving it to be inferred, which is what an edit needs.
                return new IdentifierExpr(token.Span, token.Name) { NameSpan = token.Span };

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
                return LooksLikeInlineLambda() ? ParseInlineLambda() : ParseParenthesized();
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
        List<string> parts = [ExpectIdentifier()];

        // Qualified the same way a type written anywhere else is, since this is one.
        while (Check(TokenType.Dot))
        {
            Advance();
            parts.Add(ExpectIdentifier());
        }

        string typeName = string.Join('.', parts);

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
        List<ParameterDecl> parameters = ParseParameterList(allowInferredTypes: true);
        Expect(TokenType.RightParen);

        List<Statement> body = ParseBody(TokenType.Function);
        ExpectEnd(TokenType.Function, "function", start);

        return LambdaExpr.Block(SpanFrom(start), parameters, body);
    }

    /// <summary>The <c>(…) yield expression</c> lambda.</summary>
    private Expression ParseInlineLambda()
    {
        Token start = Advance();
        List<ParameterDecl> parameters = ParseParameterList(allowInferredTypes: true);

        Expect(TokenType.RightParen);
        Expect(TokenType.Yield);

        Expression body = ParseExpression();

        return LambdaExpr.Inline(SpanFrom(start), parameters, body);
    }

    /// <summary>
    /// <para>Decides whether a <c>(</c> opens a lambda's parameter list or a parenthesized
    /// expression.</para>
    /// <para>The two readings are disjoint — nothing parses as both — but the decision cannot
    /// be made from one token, since the parentheses may nest arbitrarily. Skipping to the
    /// matching <c>)</c> and looking for <c>yield</c> settles it with a token scan rather than
    /// a speculative parse, so nothing has to be parsed twice or thrown away.</para>
    /// </summary>
    private bool LooksLikeInlineLambda()
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
                        return _tokens[_position + offset + 1].Type == TokenType.Yield;
                    }

                    break;

                case TokenType.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// <para>The parameters between a pair of parentheses.</para>
    /// <para><paramref name="allowInferredTypes"/> is set only for a lambda, where a parameter
    /// may be a bare name and take its type from whatever the lambda is being written into. A
    /// declared function has nothing to take a type from, so it always requires one.</para>
    /// </summary>
    private List<ParameterDecl> ParseParameterList(bool allowInferredTypes = false)
    {
        List<ParameterDecl> parameters = [];

        if (Check(TokenType.RightParen))
        {
            return parameters;
        }

        do
        {
            Token start = Current;

            // A bare name is a parameter with no type only when nothing follows it, since
            // "integer n" and "n" differ solely in what comes after the first identifier.
            if (allowInferredTypes && Check(TokenType.Identifier) && NextEndsAParameter())
            {
                Token bare = Advance();

                parameters.Add(new ParameterDecl(SpanFrom(start), null, bare.Name)
                {
                    NameSpan = bare.Span,
                });

                continue;
            }

            TypeSyntax type = ParseType();
            string name = ExpectIdentifier(out SourceSpan named);

            parameters.Add(new ParameterDecl(SpanFrom(start), type, name)
            {
                NameSpan = named,
            });
        }
        while (Match(TokenType.Comma));

        return parameters;
    }

    private bool NextEndsAParameter() =>
        _tokens[_position + 1].Type is TokenType.Comma or TokenType.RightParen;

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
