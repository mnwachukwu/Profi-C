using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Parsing;

public sealed partial class Parser
{
    /// <summary>
    /// <para>Reads a construct's body: statements until a closer.</para>
    /// <para>There is no opening token. The body ends at <c>end</c>, or at <c>else</c> when
    /// an <c>if</c> is being read, or at a <c>case</c> or <c>default</c> inside a switch.
    /// </para>
    /// </summary>
    private List<Statement> ParseBody(TokenType _, params TokenType[] alsoStopAt)
    {
        List<Statement> statements = [];

        while (!AtEnd && !Check(TokenType.End) && !alsoStopAt.Contains(Kind))
        {
            if (ShouldStop)
            {
                break;
            }

            int before = _position;
            Statement? statement = ParseStatement();

            if (statement is not null)
            {
                statements.Add(statement);
            }

            EnsureProgress(before);
        }

        return statements;
    }

    /// <summary>Reads one statement, or null when the parser had to discard one.</summary>
    private Statement? ParseStatement()
    {
        switch (Kind)
        {
            case TokenType.Begin: return ParseBlock();
            case TokenType.Let: return ParseLetDeclaration();
            case TokenType.If: return ParseIfStatement();
            case TokenType.While: return ParseWhile();
            case TokenType.For: return ParseFor();
            case TokenType.Switch: return ParseSwitch();
            case TokenType.Try: return ParseTry();
            case TokenType.Throw: return ParseThrow();
            case TokenType.Yield: return ParseYield();
            case TokenType.Break: return ParseBreak();
            case TokenType.Continue: return ParseContinue();

            case TokenType.Model:
            case TokenType.Structure:
            case TokenType.Enumeration:
                return RejectTypeInsideFunction();
        }

        // A declaration nested in a body: "constant integer x = 1;", "integer y;",
        // "integer function Helper()". A type followed by a name is a declaration; a type
        // followed by anything else is the start of an expression.
        if (Check(TokenType.Constant) || StartsLocalDeclaration())
        {
            return ParseTypedLocalDeclaration();
        }

        return ParseExpressionStatement();
    }

    /// <summary>
    /// Distinguishes a declaration from an expression at the start of a statement, by
    /// looking for a type followed by a name or by <c>function</c>.
    /// </summary>
    private bool StartsLocalDeclaration()
    {
        if (!StartsType(Kind))
        {
            return false;
        }

        // "function" is not special-cased here. It may begin a declaration's type, as in
        // "function(string)[] handlers = {};", and only what follows the type tells the two
        // apart. A bare lambda as a statement would discard a function value and is
        // meaningless, so nothing is lost by letting the probe decide.

        // Read the type on a throwaway parser so that a failed guess reports nothing and
        // leaves the real cursor untouched.
        DiagnosticBag scratch = new();
        Parser probe = new(_source, _tokens, scratch) { _position = _position };
        probe.ParseType();

        return probe.Check(TokenType.Identifier) || probe.AtFunctionDeclaration();
    }

    private Statement ParseBlock()
    {
        Token start = Advance();
        List<Statement> statements = ParseBody(TokenType.End);
        ExpectBareEnd(start);

        return new BlockStmt(SpanFrom(start), statements);
    }

    private Statement ParseLetDeclaration()
    {
        Token start = Advance();
        string name = ExpectIdentifier();

        Expect(TokenType.Equal);
        Expression initializer = ParseExpression();
        Expect(TokenType.Semicolon);

        return new VarDeclStmt(SpanFrom(start), null, name, initializer, isConstant: false);
    }

    private Statement ParseTypedLocalDeclaration()
    {
        Token start = Current;
        bool isConstant = Match(TokenType.Constant);

        TypeSyntax type = ParseType();

        // A local function, which captures the enclosing locals.
        if (AtFunctionDeclaration())
        {
            Advance();
            FunctionDecl local = ParseFunctionRest(start, DeclarationModifiers.None, type);
            return new LocalDeclStmt(local.Span, local);
        }

        string name = ExpectIdentifier();
        Expression? initializer = Match(TokenType.Equal) ? ParseExpression() : null;
        Expect(TokenType.Semicolon);

        return new VarDeclStmt(SpanFrom(start), type, name, initializer, isConstant);
    }

    /// <summary>
    /// <para>Reports a type declared inside a function, then parses and discards it so that
    /// the rest of the body still reads.</para>
    /// <para>Types live at namespace level or inside a model. Allowing one here would mean a
    /// statement could introduce a type, which forces name resolution to interleave
    /// collecting types with binding bodies instead of doing each once.</para>
    /// </summary>
    private Statement? RejectTypeInsideFunction()
    {
        string what = Kind switch
        {
            TokenType.Model => "model",
            TokenType.Structure => "structure",
            _ => "enumeration",
        };

        _diagnostics.Report(DiagnosticDescriptors.TypeInsideFunction, Current.Span, what);

        Token start = Current;
        _ = ParseMember(DeclarationModifiers.None, start);

        return null;
    }

    /// <summary>
    /// An <c>if</c> and its whole chain. The chain closes once, because <c>else if</c> belongs
    /// to this construct rather than nesting a second <c>if</c> inside the first.
    /// </summary>
    private Statement ParseIfStatement()
    {
        Token start = Advance();
        Expression condition = ParseExpression();
        List<Statement> thenBody = ParseBody(TokenType.If, TokenType.Else);

        List<ElseIfClause> elseIfClauses = [];
        List<Statement>? elseBody = null;

        while (Check(TokenType.Else))
        {
            Token elseToken = Advance();

            if (Match(TokenType.If))
            {
                Expression elseIfCondition = ParseExpression();
                List<Statement> body = ParseBody(TokenType.If, TokenType.Else);
                elseIfClauses.Add(new ElseIfClause(SpanFrom(elseToken), elseIfCondition, body));
                continue;
            }

            elseBody = ParseBody(TokenType.If);
            break;
        }

        ExpectEnd(TokenType.If, "if", start);

        return new IfStmt(SpanFrom(start), condition, thenBody, elseIfClauses, elseBody);
    }

    private Statement ParseWhile()
    {
        Token start = Advance();
        Expression condition = ParseExpression();
        List<Statement> body = ParseBody(TokenType.While);

        ExpectEnd(TokenType.While, "while", start);

        return new WhileStmt(SpanFrom(start), condition, body);
    }

    /// <summary>
    /// Both loop forms, told apart by the <c>each</c> that may follow <c>for</c>. Both close
    /// with <c>end for</c>.
    /// </summary>
    private Statement ParseFor()
    {
        Token start = Advance();

        if (Match(TokenType.Each))
        {
            string eachName = ExpectIdentifier();
            Expect(TokenType.In);

            Expression sequence = ParseExpression();
            List<Statement> eachBody = ParseBody(TokenType.For);

            ExpectEnd(TokenType.For, "for", start);

            return new ForEachStmt(SpanFrom(start), eachName, sequence, eachBody);
        }

        TypeSyntax type = ParseType();
        string name = ExpectIdentifier();

        Expect(TokenType.Equal);
        Expression from = ParseExpression();

        bool inclusive = true;

        if (Match(TokenType.To))
        {
            inclusive = true;
        }
        else if (Match(TokenType.Until))
        {
            inclusive = false;
        }
        else
        {
            Expect(TokenType.To, "'to' or 'until'");
        }

        Expression bound = ParseExpression();
        Expression? step = Match(TokenType.Step) ? ParseExpression() : null;

        List<Statement> body = ParseBody(TokenType.For);
        ExpectEnd(TokenType.For, "for", start);

        return new ForStmt(SpanFrom(start), type, name, from, bound, inclusive, step, body);
    }

    /// <summary>
    /// A <c>switch</c>. Several labels may stack before one body, which is how two values are
    /// handled alike in a language with no fallthrough.
    /// </summary>
    private Statement ParseSwitch()
    {
        Token start = Advance();
        Expression subject = ParseExpression();

        List<CaseGroup> cases = [];
        List<Statement>? defaultBody = null;

        while (Check(TokenType.Case))
        {
            Token caseStart = Current;
            List<Expression> labels = [];

            while (Match(TokenType.Case))
            {
                labels.Add(ParseExpression());
                Expect(TokenType.Colon);
            }

            List<Statement> body =
                ParseBody(TokenType.Switch, TokenType.Case, TokenType.Default);

            cases.Add(new CaseGroup(SpanFrom(caseStart), labels, body));
        }

        if (Match(TokenType.Default))
        {
            Expect(TokenType.Colon);
            defaultBody = ParseBody(TokenType.Switch);
        }

        ExpectEnd(TokenType.Switch, "switch", start);

        return new SwitchStmt(SpanFrom(start), subject, cases, defaultBody);
    }

    private Statement ParseTry()
    {
        Token start = Advance();
        List<Statement> body = ParseBody(TokenType.Try, TokenType.Catch, TokenType.Finally);

        List<CatchClause> catches = [];

        while (Check(TokenType.Catch))
        {
            Token catchStart = Advance();
            TypeSyntax exceptionType = ParseType();
            string name = ExpectIdentifier();

            List<Statement> catchBody =
                ParseBody(TokenType.Try, TokenType.Catch, TokenType.Finally);

            catches.Add(new CatchClause(SpanFrom(catchStart), exceptionType, name, catchBody));
        }

        List<Statement>? finallyBody = null;

        if (Match(TokenType.Finally))
        {
            finallyBody = ParseBody(TokenType.Try);
        }

        ExpectEnd(TokenType.Try, "try", start);

        return new TryStmt(SpanFrom(start), body, catches, finallyBody);
    }

    private Statement ParseThrow()
    {
        Token start = Advance();
        Expression exception = ParseExpression();
        Expect(TokenType.Semicolon);

        return new ThrowStmt(SpanFrom(start), exception);
    }

    /// <summary>
    /// <c>yield</c>, which is this language's return statement. A bare one returns from a
    /// function that yields nothing.
    /// </summary>
    private Statement ParseYield()
    {
        Token start = Advance();
        Expression? value = Check(TokenType.Semicolon) ? null : ParseExpression();

        Expect(TokenType.Semicolon);

        return new YieldStmt(SpanFrom(start), value);
    }

    private Statement ParseBreak()
    {
        Token start = Advance();
        Expect(TokenType.Semicolon);
        return new BreakStmt(SpanFrom(start));
    }

    private Statement ParseContinue()
    {
        Token start = Advance();
        Expect(TokenType.Semicolon);
        return new ContinueStmt(SpanFrom(start));
    }

    /// <summary>
    /// <para>An expression statement, or an assignment.</para>
    /// <para>Assignment is reached by parsing a whole expression and then finding <c>=</c>,
    /// which is what makes a complex target such as <c>a[i]</c> or <c>p.field</c> work without
    /// any special case, and what keeps assignment a statement so <c>if x = 5</c> cannot be
    /// written at all.</para>
    /// </summary>
    private Statement? ParseExpressionStatement()
    {
        if (!RejectIllegalStatementStart())
        {
            return null;
        }

        Token start = Current;
        Expression expression = ParseExpression();

        if (Match(TokenType.Equal))
        {
            Expression value = ParseExpression();
            Expect(TokenType.Semicolon);

            if (!IsAssignable(expression))
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.AssignmentTargetNotAssignable,
                    expression.Span);
            }

            return new AssignmentStmt(SpanFrom(start), expression, value);
        }

        Expect(TokenType.Semicolon);
        return new ExpressionStmt(SpanFrom(start), expression);
    }

    /// <summary>
    /// <para>Rejects a statement beginning with <c>(</c> or <c>-</c>.</para>
    /// <para>A construct's body has no opening token, so a condition ends at the first token
    /// that cannot continue an expression. Both of these can continue one, so without this
    /// rule the condition of an enclosing <c>if</c> or <c>while</c> would swallow the first
    /// statement of its own body.</para>
    /// </summary>
    private bool RejectIllegalStatementStart()
    {
        if (!Check(TokenType.LeftParen) && !Check(TokenType.Minus))
        {
            return true;
        }

        // An arrow lambda cannot begin a statement either, and reporting the paren rule for
        // it is the more useful message.
        _diagnostics.Report(
            DiagnosticDescriptors.StatementCannotStartWith,
            Current.Span,
            Current.Lexeme);

        SkipRestOfStatement();
        return false;
    }

    /// <summary>Whether an expression may appear on the left of an assignment.</summary>
    private static bool IsAssignable(Expression expression) =>
        expression is IdentifierExpr or IndexExpr or MemberExpr;
}
