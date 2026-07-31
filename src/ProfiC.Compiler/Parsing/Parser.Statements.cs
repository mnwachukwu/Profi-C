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

        // A token that begins neither a statement nor an expression. A body stops at its own
        // terminators before reaching here, so this is one written where nothing can follow it
        // — a stray 'else', or an 'end' closing something never opened. Naming what was wanted
        // beats reading it as the start of an expression and failing there instead.
        if (!CanBeginExpression(Kind))
        {
            _diagnostics.Report(
                DiagnosticDescriptors.ExpectedStatement, Current.Span, Describe(Current));

            Advance();
            return null;
        }

        return ParseExpressionStatement();
    }

    /// <summary>
    /// Whether a token could open an expression. <c>(</c> and <c>-</c> are included although a
    /// statement may not begin with either: they are turned away with their own explanation.
    /// </summary>
    private static bool CanBeginExpression(TokenType kind) =>
        kind.IsLiteral()
        || kind is TokenType.Identifier
                or TokenType.True or TokenType.False
                or TokenType.LeftParen or TokenType.LeftBrace
                or TokenType.Minus or TokenType.Not
                or TokenType.New or TokenType.This or TokenType.Base
                or TokenType.If or TokenType.Function;

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
        Parser probe = new(_source, _tokens, scratch, []) { _position = _position };
        probe.ParseType();

        // A reserved word after a type is a declaration whose name is a word already taken.
        // Committing to the declaration is what lets that be reported as the naming mistake it
        // is, rather than as a statement that could not start.
        return probe.Check(TokenType.Identifier)
               || probe.AtFunctionDeclaration()
               || probe.Kind.IsKeyword();
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

        // A local function that yields nothing, which opens with the word itself. Settled
        // before a type is read, because reading one first would take "function" for the name
        // of a type and report that instead. Only a name may follow the word here; "function("
        // is a delegate type, and falls through to be read as one.
        if (AtFunctionDeclaration())
        {
            Advance();

            FunctionDecl bare = ParseFunctionRest(start, DeclarationModifiers.None, null);
            return new LocalDeclStmt(bare.Span, bare);
        }

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

        // A range loop counts, and counting is done with integers, so the counter carries no
        // written type. Someone arriving from a language that wants one writes "for integer i",
        // which is caught here rather than left as a confusing "expected '='".
        if (Current.Type is TokenType.Integer or TokenType.Real or TokenType.Boolean
            or TokenType.Character or TokenType.String or TokenType.Fraction)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.RangeLoopTakesNoType, Current.Span, Current.Lexeme);
            Advance();
        }

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

        return new ForStmt(SpanFrom(start), name, from, bound, inclusive, step, body);
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

        // Nothing was read, so there is no statement to terminate. Asking for the semicolon
        // anyway reports the same token a second time, which reads as two separate mistakes.
        if (expression is MissingExpr)
        {
            Advance();
            return new ExpressionStmt(SpanFrom(start), expression);
        }

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

        // An inline lambda cannot begin a statement either, and reporting the paren rule for
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
