using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Parsing;

public sealed partial class Parser
{
    /// <summary>
    /// A whole file: <c>using</c> directives, then declarations and nothing else. There are no
    /// top-level statements, functions, or variables.
    /// </summary>
    private CompilationUnit ParseCompilationUnit()
    {
        using DiagnosticBag.FileScope reporting = _diagnostics.InFile(_source);

        Token start = Current;
        List<UsingDirective> usings = [];
        List<ImportDirective> imports = [];

        // Both open a file and may be interleaved, since one says which files are compiled and
        // the other which names are reachable, and neither order changes what either means.
        while (Check(TokenType.Using) || Check(TokenType.Import))
        {
            if (Check(TokenType.Using))
            {
                usings.Add(ParseUsing());
            }
            else
            {
                imports.Add(ParseImport());
            }
        }

        List<Declaration> declarations = [];

        while (!AtEnd && !ShouldStop)
        {
            int before = _position;
            Declaration? declaration = ParseTopLevelDeclaration();

            if (declaration is not null)
            {
                declarations.Add(declaration);
            }

            EnsureProgress(before);
        }

        return new CompilationUnit(
            SpanFrom(start), usings, imports, declarations, _source, _documentation, _comments);
    }

    /// <summary>
    /// <c>import "path";</c> — one file, named by a string so that a path may hold anything a
    /// file name can, which an identifier could not.
    /// </summary>
    private ImportDirective ParseImport()
    {
        Token start = Advance();
        Token path = Expect(TokenType.StringLiteral, "a quoted path");

        Expect(TokenType.Semicolon);

        // The lexeme is the exact source slice, quotes and all, so the path is the inside.
        string written = path.Lexeme.Length >= 2 ? path.Lexeme[1..^1] : string.Empty;

        return new ImportDirective(SpanFrom(start), written);
    }

    private UsingDirective ParseUsing()
    {
        Token start = Advance();
        QualifiedName name = ParseQualifiedName();
        Expect(TokenType.Semicolon);

        return new UsingDirective(SpanFrom(start), name);
    }

    private QualifiedName ParseQualifiedName()
    {
        Token start = Current;
        List<string> parts = [ExpectIdentifier()];

        while (Match(TokenType.Dot))
        {
            parts.Add(ExpectIdentifier());
        }

        return new QualifiedName(SpanFrom(start), parts);
    }

    private Declaration? ParseTopLevelDeclaration()
    {
        if (Check(TokenType.Namespace))
        {
            return ParseNamespace();
        }

        // A directive reaching here is one written below a namespace, since the only other
        // place they are read is the prologue. Consumed rather than merely refused, so that
        // the rest of the line is not then reported as several more mistakes.
        if (Check(TokenType.Using) || Check(TokenType.Import))
        {
            _diagnostics.Report(
                DiagnosticDescriptors.DirectiveInsideNamespace,
                Current.Span,
                Current.Type == TokenType.Using ? "using" : "import");

            if (Current.Type == TokenType.Using)
            {
                ParseUsing();
            }
            else
            {
                ParseImport();
            }

            return null;
        }

        Token start = Current;
        DeclarationModifiers modifiers = ParseModifiers();

        if (Check(TokenType.Model) || Check(TokenType.Structure) || Check(TokenType.Enumeration))
        {
            return ParseMember(modifiers, start);
        }

        _diagnostics.Report(
            DiagnosticDescriptors.ExpectedDeclaration,
            Current.Span,
            Describe(Current));

        RecoverToNextMember();
        return null;
    }

    /// <summary>
    /// A namespace in either form. The file-scoped form takes everything that follows it and
    /// has no closer; the block form ends with <c>end namespace</c>.
    /// </summary>
    private Declaration ParseNamespace()
    {
        Token start = Advance();
        QualifiedName name = ParseQualifiedName();

        if (Match(TokenType.Semicolon))
        {
            List<Declaration> fileScoped = [];

            while (!AtEnd && !ShouldStop)
            {
                int before = _position;
                Declaration? declaration = ParseTopLevelDeclaration();

                if (declaration is not null)
                {
                    fileScoped.Add(declaration);
                }

                EnsureProgress(before);
            }

            return new NamespaceDecl(SpanFrom(start), name, fileScoped, isFileScoped: true);
        }

        List<Declaration> declarations = [];

        while (!AtEnd && !Check(TokenType.End) && !ShouldStop)
        {
            int before = _position;
            Declaration? declaration = ParseTopLevelDeclaration();

            if (declaration is not null)
            {
                declarations.Add(declaration);
            }

            EnsureProgress(before);
        }

        ExpectEnd(TokenType.Namespace, "namespace", start);

        return new NamespaceDecl(SpanFrom(start), name, declarations, isFileScoped: false);
    }

    /// <summary>Reads any run of modifier words, in any order.</summary>
    private DeclarationModifiers ParseModifiers()
    {
        DeclarationModifiers modifiers = DeclarationModifiers.None;

        while (DeclarationModifiersExtensions.FromToken(Kind) is { } modifier)
        {
            modifiers |= modifier;
            Advance();
        }

        return modifiers;
    }

    /// <summary>
    /// Dispatches on what follows the modifiers: a type declaration, a function, or a field.
    /// </summary>
    private Declaration ParseMember(DeclarationModifiers modifiers, Token start)
    {
        switch (Kind)
        {
            case TokenType.Model: return ParseModel(modifiers, start);
            case TokenType.Structure: return ParseStructure(modifiers, start);
            case TokenType.Enumeration: return ParseEnumeration(modifiers, start);
        }

        // "function Name(...)" with nothing before it yields nothing.
        if (AtFunctionDeclaration())
        {
            Advance();
            return ParseFunctionRest(start, modifiers, returnType: null);
        }

        if (Check(TokenType.Let))
        {
            return ParseFieldWrittenWithLet(modifiers, start);
        }

        TypeSyntax type = ParseType();

        // "integer function Name(...)" — what was read is the return type.
        if (AtFunctionDeclaration())
        {
            Advance();
            return ParseFunctionRest(start, modifiers, type);
        }

        string name = ExpectIdentifier(out SourceSpan named);
        Expression? initializer = Match(TokenType.Equal) ? ParseExpression() : null;
        Expect(TokenType.Semicolon);

        return new FieldDecl(SpanFrom(start), modifiers, type, name, initializer)
        {
            NameSpan = named,
        };
    }

    /// <summary>
    /// <para>Reads a field someone wrote with <c>let</c>, reports the one thing wrong with it,
    /// and carries on as though the type had been written.</para>
    /// <para>Standing a type in is what keeps this to one message. Left to the type parser,
    /// <c>let</c> is not a type, then the name after it is a second error, then the name is
    /// looked up as a type and is not one — four reports for a line with a single mistake in
    /// it, none of them naming the rule.</para>
    /// </summary>
    private Declaration ParseFieldWrittenWithLet(DeclarationModifiers modifiers, Token start)
    {
        Token let = Current;
        Advance();

        string name = ExpectIdentifier(out SourceSpan named);
        Expression? initializer = Match(TokenType.Equal) ? ParseExpression() : null;
        Expect(TokenType.Semicolon);

        // The suggestion names a type the reader has to choose, since what the initializer
        // works out to is a question for a pass that has not run yet.
        _diagnostics.Report(
            DiagnosticDescriptors.LetIsForLocals, let.Span, "integer", name);

        return new FieldDecl(
            SpanFrom(start),
            modifiers,
            new NamedTypeSyntax(let.Span, "integer"),
            name,
            initializer)
        {
            NameSpan = named,
        };
    }

    /// <summary>Reads a function from its name onward, the <c>function</c> word consumed.</summary>
    private FunctionDecl ParseFunctionRest(
        Token start,
        DeclarationModifiers modifiers,
        TypeSyntax? returnType)
    {
        string name = ExpectIdentifier(out SourceSpan named);

        Expect(TokenType.LeftParen);
        List<ParameterDecl> parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        // A semicolon in place of a body: the function is declared and left to a descendant.
        // Nothing else can follow a parameter list, so this needs no lookahead — and it closes
        // no block, which is why it does not want an 'end function' the way a body does.
        if (Match(TokenType.Semicolon))
        {
            return new FunctionDecl(
                SpanFrom(start), modifiers, returnType, name, parameters, body: null)
            {
                NameSpan = named,
            };
        }

        List<Statement> body = ParseBody(TokenType.Function);
        ExpectEnd(TokenType.Function, "function", start);

        return new FunctionDecl(SpanFrom(start), modifiers, returnType, name, parameters, body)
        {
            NameSpan = named,
        };
    }

    private Declaration ParseModel(DeclarationModifiers modifiers, Token start)
    {
        Advance();
        string name = ExpectIdentifier(out SourceSpan named);

        string? baseTypeName = Match(TokenType.Extends) ? ExpectIdentifier() : null;

        List<Declaration> members = ParseMembers();
        ExpectEnd(TokenType.Model, "model", start);

        return new ModelDecl(SpanFrom(start), modifiers, name, baseTypeName, members)
        {
            NameSpan = named,
        };
    }

    private Declaration ParseStructure(DeclarationModifiers modifiers, Token start)
    {
        Advance();
        string name = ExpectIdentifier(out SourceSpan named);

        List<Declaration> members = ParseMembers();
        ExpectEnd(TokenType.Structure, "structure", start);

        return new StructureDecl(SpanFrom(start), modifiers, name, members)
        {
            NameSpan = named,
        };
    }

    private List<Declaration> ParseMembers()
    {
        List<Declaration> members = [];

        while (!AtEnd && !Check(TokenType.End) && !ShouldStop)
        {
            int before = _position;
            Token memberStart = Current;
            DeclarationModifiers memberModifiers = ParseModifiers();

            if (Check(TokenType.End))
            {
                break;
            }

            members.Add(ParseMember(memberModifiers, memberStart));
            EnsureProgress(before);
        }

        return members;
    }

    private Declaration ParseEnumeration(DeclarationModifiers modifiers, Token start)
    {
        Advance();
        string name = ExpectIdentifier(out SourceSpan named);

        List<EnumMemberDecl> members = [];

        while (!AtEnd && !Check(TokenType.End) && !ShouldStop)
        {
            int before = _position;
            Token memberStart = Current;
            string memberName = ExpectIdentifier(out SourceSpan memberNamed);

            Expression? value = Match(TokenType.Equal) ? ParseExpression() : null;

            members.Add(new EnumMemberDecl(SpanFrom(memberStart), memberName, value)
            {
                NameSpan = memberNamed,
            });

            if (!Match(TokenType.Comma))
            {
                break;
            }

            EnsureProgress(before);
        }

        ExpectEnd(TokenType.Enumeration, "enumeration", start);

        return new EnumerationDecl(SpanFrom(start), modifiers, name, members)
        {
            NameSpan = named,
        };
    }
}
