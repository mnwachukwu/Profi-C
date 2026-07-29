using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

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

        while (Check(TokenType.Using))
        {
            usings.Add(ParseUsing());
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

        return new CompilationUnit(SpanFrom(start), usings, declarations, _source);
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

        TypeSyntax type = ParseType();

        // "integer function Name(...)" — what was read is the return type.
        if (AtFunctionDeclaration())
        {
            Advance();
            return ParseFunctionRest(start, modifiers, type);
        }

        string name = ExpectIdentifier();
        Expression? initializer = Match(TokenType.Equal) ? ParseExpression() : null;
        Expect(TokenType.Semicolon);

        return new FieldDecl(SpanFrom(start), modifiers, type, name, initializer);
    }

    /// <summary>Reads a function from its name onward, the <c>function</c> word consumed.</summary>
    private FunctionDecl ParseFunctionRest(
        Token start,
        DeclarationModifiers modifiers,
        TypeSyntax? returnType)
    {
        string name = ExpectIdentifier();

        Expect(TokenType.LeftParen);
        List<ParameterDecl> parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        List<Statement> body = ParseBody(TokenType.Function);
        ExpectEnd(TokenType.Function, "function", start);

        return new FunctionDecl(SpanFrom(start), modifiers, returnType, name, parameters, body);
    }

    private Declaration ParseModel(DeclarationModifiers modifiers, Token start)
    {
        Advance();
        string name = ExpectIdentifier();

        string? baseTypeName = Match(TokenType.Extends) ? ExpectIdentifier() : null;

        List<Declaration> members = ParseMembers();
        ExpectEnd(TokenType.Model, "model", start);

        return new ModelDecl(SpanFrom(start), modifiers, name, baseTypeName, members);
    }

    private Declaration ParseStructure(DeclarationModifiers modifiers, Token start)
    {
        Advance();
        string name = ExpectIdentifier();

        List<Declaration> members = ParseMembers();
        ExpectEnd(TokenType.Structure, "structure", start);

        return new StructureDecl(SpanFrom(start), modifiers, name, members);
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
        string name = ExpectIdentifier();

        List<EnumMemberDecl> members = [];

        while (!AtEnd && !Check(TokenType.End) && !ShouldStop)
        {
            int before = _position;
            Token memberStart = Current;
            string memberName = ExpectIdentifier();

            Expression? value = Match(TokenType.Equal) ? ParseExpression() : null;
            members.Add(new EnumMemberDecl(SpanFrom(memberStart), memberName, value));

            if (!Match(TokenType.Comma))
            {
                break;
            }

            EnsureProgress(before);
        }

        ExpectEnd(TokenType.Enumeration, "enumeration", start);

        return new EnumerationDecl(SpanFrom(start), modifiers, name, members);
    }
}
