using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests.Parsing;

/// <summary>Every statement and declaration form, and the shapes that are easy to get wrong.</summary>
[TestFixture]
public sealed class StatementParsingTests : ParserTestBase
{
    [Test]
    public void AnonymousBlockParses()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    begin
                        let x = 1;
                    end
            """);

        Assert.That(statements[0], Is.TypeOf<BlockStmt>());
        Assert.That(((BlockStmt)statements[0]).Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void InferredAndTypedDeclarationsAreDistinguished()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    let a = 1;
                    integer b = 2;
                    integer c;
                    constant integer D = 3;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(((VarDeclStmt)statements[0]).IsInferred, Is.True);
            Assert.That(((VarDeclStmt)statements[1]).IsInferred, Is.False);
            Assert.That(((VarDeclStmt)statements[2]).Initializer, Is.Null);
            Assert.That(((VarDeclStmt)statements[3]).IsConstant, Is.True);
        });
    }

    /// <summary>
    /// The chain closes once. Reading "else if" as a nested statement would need three
    /// closers for a three-way branch, which is the pile of closers this design removes.
    /// </summary>
    [Test]
    public void ElseIfChainIsOneConstructClosingOnce()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    if a
                        yield;
                    else if b
                        yield;
                    else if c
                        yield;
                    else
                        yield;
                    end if
            """);

        IfStmt statement = (IfStmt)statements[0];

        Assert.Multiple(() =>
        {
            Assert.That(statements, Has.Count.EqualTo(1), "the whole chain is one statement");
            Assert.That(statement.ElseIfClauses, Has.Count.EqualTo(2));
            Assert.That(statement.ElseBody, Is.Not.Null);
        });
    }

    [Test]
    public void AnIfWithNoElseHasANullElseBody()
    {
        IfStmt statement = (IfStmt)ParseStatements(
            """
                    if a
                        yield;
                    end if
            """)[0];

        Assert.That(statement.ElseBody, Is.Null, "no else is distinct from an empty else");
    }

    [TestCase("to", true)]
    [TestCase("until", false)]
    public void RangeLoopRecordsWhetherItsBoundIsInclusive(string keyword, bool inclusive)
    {
        ForStmt statement = (ForStmt)ParseStatements(
            $"""
                    for i = 1 {keyword} 10
                        yield;
                    end for
            """)[0];

        Assert.That(statement.IsInclusive, Is.EqualTo(inclusive));
    }

    /// <summary>
    /// A range loop's counter carries no type, because counting is done with integers and
    /// there was never a second option to record. A reader arriving from C# writes one anyway,
    /// so it earns a diagnostic that names the fix rather than a bare "expected '='".
    /// </summary>
    [TestCase("integer")]
    [TestCase("real")]
    [TestCase("string")]
    public void WritingATypeOnTheCounterIsRejectedAndOnlyOnce(string type)
    {
        (_, DiagnosticBag diagnostics) = ParseRaw(
            $$"""
            global model Program
                function Main()
                    for {{type}} i = 1 to 10
                        yield;
                    end for
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0111" }));
            Assert.That(diagnostics.Single().Message, Does.Contain($"Remove the '{type}'"));
        });
    }

    [Test]
    public void RangeLoopStepIsOptional()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    for i = 1 to 10
                        yield;
                    end for
                    for i = 10 until 0 step -1
                        yield;
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(((ForStmt)statements[0]).Step, Is.Null);
            Assert.That(((ForStmt)statements[1]).Step, Is.Not.Null);
        });
    }

    [Test]
    public void BothLoopFormsCloseWithEndFor()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    for i = 1 to 10
                        yield;
                    end for
                    for each item in items
                        yield;
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(statements[0], Is.TypeOf<ForStmt>());
            Assert.That(statements[1], Is.TypeOf<ForEachStmt>());
            Assert.That(((ForEachStmt)statements[1]).VariableName, Is.EqualTo("item"));
        });
    }

    /// <summary>
    /// Several labels may stack before one body. That is how two values are handled alike in
    /// a language where no case falls through.
    /// </summary>
    [Test]
    public void StackedCaseLabelsShareOneBody()
    {
        SwitchStmt statement = (SwitchStmt)ParseStatements(
            """
                    switch code
                        case 1:
                            yield;
                        case 2:
                        case 3:
                            yield;
                        default:
                            yield;
                    end switch
            """)[0];

        Assert.Multiple(() =>
        {
            Assert.That(statement.Cases, Has.Count.EqualTo(2));
            Assert.That(statement.Cases[0].Labels, Has.Count.EqualTo(1));
            Assert.That(statement.Cases[1].Labels, Has.Count.EqualTo(2));
            Assert.That(statement.DefaultBody, Is.Not.Null);
        });
    }

    [Test]
    public void TryCatchFinallyParses()
    {
        TryStmt statement = (TryStmt)ParseStatements(
            """
                    try
                        throw new ArgumentException();
                    catch ArgumentException problem
                        yield;
                    catch Exception other
                        yield;
                    finally
                        yield;
                    end try
            """)[0];

        Assert.Multiple(() =>
        {
            Assert.That(statement.Catches, Has.Count.EqualTo(2));
            Assert.That(statement.Catches[0].VariableName, Is.EqualTo("problem"));
            Assert.That(statement.FinallyBody, Is.Not.Null);
        });
    }

    [Test]
    public void YieldMayCarryAValueOrNot()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    yield;
                    yield 1;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(((YieldStmt)statements[0]).Value, Is.Null);
            Assert.That(((YieldStmt)statements[1]).Value, Is.Not.Null);
        });
    }

    [Test]
    public void AssignmentTargetsMayBeComplex()
    {
        IReadOnlyList<Statement> statements = ParseStatements(
            """
                    x = 1;
                    a[i] = 2;
                    this.field = 3;
            """);

        Assert.That(statements.Select(s => s.NodeKind),
                    Is.All.EqualTo(nameof(AssignmentStmt)));
    }

    [Test]
    public void ALocalFunctionParses()
    {
        LocalDeclStmt statement = (LocalDeclStmt)ParseStatements(
            """
                    integer function Doubled()
                        yield 2;
                    end function
            """)[0];

        Assert.That(statement.Declaration, Is.TypeOf<FunctionDecl>());
    }

    // ---- Types ---------------------------------------------------------------------------

    [Test]
    public void TypeSuffixesNestInTheOrderWritten()
    {
        TypeSyntax setOfOptionals = ParseType("Node?[]");
        TypeSyntax optionalSet = ParseType("Node[]?");

        Assert.Multiple(() =>
        {
            Assert.That(setOfOptionals, Is.TypeOf<SetTypeSyntax>());
            Assert.That(((SetTypeSyntax)setOfOptionals).ElementType,
                        Is.TypeOf<OptionalTypeSyntax>());

            Assert.That(optionalSet, Is.TypeOf<OptionalTypeSyntax>());
            Assert.That(((OptionalTypeSyntax)optionalSet).UnderlyingType,
                        Is.TypeOf<SetTypeSyntax>());
        });
    }

    [Test]
    public void FunctionTypesParseWithAndWithoutAReturnType()
    {
        TypeSyntax withReturn = ParseType("integer delegate(integer, integer)");
        TypeSyntax withoutReturn = ParseType("delegate(string)");

        Assert.Multiple(() =>
        {
            Assert.That(((FunctionTypeSyntax)withReturn).ReturnType, Is.Not.Null);
            Assert.That(((FunctionTypeSyntax)withReturn).ParameterTypes, Has.Count.EqualTo(2));
            Assert.That(((FunctionTypeSyntax)withoutReturn).ReturnType, Is.Null);
        });
    }

    [Test]
    public void FunctionTypesTakeSuffixes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParseType("integer delegate(integer)?"), Is.TypeOf<OptionalTypeSyntax>());
            Assert.That(ParseType("delegate(string)[]"), Is.TypeOf<SetTypeSyntax>());
        });
    }

    /// <summary>
    /// The word does three jobs, and reading a declaration as a type is the mistake that
    /// broke the first parse of the sample corpus.
    /// </summary>
    [Test]
    public void FunctionIsToldApartAsDeclarationTypeAndLambda()
    {
        CompilationUnit unit = ParseUnit(
            """
            global model Program
                string function Describe()
                    yield "a";
                end function

                delegate(string)[] handlers = {};

                function Main()
                    integer delegate(integer, integer) add = (integer a, integer b) yield a + b;
                end function
            end model
            """);

        ModelDecl model = (ModelDecl)unit.Declarations[0];

        Assert.Multiple(() =>
        {
            Assert.That(model.Members[0], Is.TypeOf<FunctionDecl>(), "return type then declaration");
            Assert.That(model.Members[1], Is.TypeOf<FieldDecl>(), "a field of function-set type");
            Assert.That(((FieldDecl)model.Members[1]).Type, Is.TypeOf<SetTypeSyntax>());
            Assert.That(model.Members[2], Is.TypeOf<FunctionDecl>());
        });
    }

    // ---- Declarations --------------------------------------------------------------------

    [Test]
    public void BothNamespaceFormsParse()
    {
        CompilationUnit fileScoped = ParseUnit("namespace A.B;\nglobal model P\nend model");
        CompilationUnit block = ParseUnit("namespace A.B\n    global model P\n    end model\nend namespace");

        Assert.Multiple(() =>
        {
            Assert.That(((NamespaceDecl)fileScoped.Declarations[0]).IsFileScoped, Is.True);
            Assert.That(((NamespaceDecl)fileScoped.Declarations[0]).Name.Text, Is.EqualTo("A.B"));
            Assert.That(((NamespaceDecl)block.Declarations[0]).IsFileScoped, Is.False);
        });
    }

    [Test]
    public void ModifiersAreCollectedInAnyOrder()
    {
        CompilationUnit unit = ParseUnit(
            """
            public sealed model Square extends Shape
                public override real function Area()
                    yield 1.0;
                end function
            end model
            """);

        ModelDecl model = (ModelDecl)unit.Declarations[0];
        FunctionDecl area = (FunctionDecl)model.Members[0];

        Assert.Multiple(() =>
        {
            Assert.That(model.Modifiers.Has(DeclarationModifiers.Public), Is.True);
            Assert.That(model.Modifiers.Has(DeclarationModifiers.Sealed), Is.True);
            Assert.That(model.BaseTypeName, Is.EqualTo("Shape"));
            Assert.That(area.Modifiers.Has(DeclarationModifiers.Override), Is.True);
        });
    }

    [Test]
    public void EnumerationMembersMayCarryExplicitValues()
    {
        CompilationUnit unit = ParseUnit(
            """
            public enumeration Color
                Red,
                Green = 10,
                Blue,
            end enumeration
            """);

        EnumerationDecl enumeration = (EnumerationDecl)unit.Declarations[0];

        Assert.Multiple(() =>
        {
            Assert.That(enumeration.Members, Has.Count.EqualTo(3));
            Assert.That(enumeration.Members[0].Value, Is.Null);
            Assert.That(enumeration.Members[1].Value, Is.Not.Null);
        });
    }

    [Test]
    public void TypesNestInsideAModel()
    {
        CompilationUnit unit = ParseUnit(
            """
            global model Program
                model Nested
                end model

                structure Pair
                end structure

                enumeration Color
                    Red,
                end enumeration
            end model
            """);

        ModelDecl enclosing = (ModelDecl)unit.Declarations[0];

        Assert.Multiple(() =>
        {
            Assert.That(enclosing.Members[0], Is.TypeOf<ModelDecl>());
            Assert.That(enclosing.Members[1], Is.TypeOf<StructureDecl>());
            Assert.That(enclosing.Members[2], Is.TypeOf<EnumerationDecl>());
        });
    }

    /// <summary>
    /// A type introduced by a statement would force name resolution to interleave collecting
    /// types with binding bodies, rather than doing each once. C# has no local classes for
    /// much the same reason.
    /// </summary>
    [TestCase("model", "        model Inner\n        end model")]
    [TestCase("structure", "        structure Inner\n        end structure")]
    [TestCase("enumeration", "        enumeration Inner\n            Red,\n        end enumeration")]
    public void TypesCannotBeDeclaredInsideAFunction(string what, string declaration)
    {
        (_, ProfiC.Compiler.Diagnostics.DiagnosticBag diagnostics) = ParseRaw(
            $$"""
            global model Program
                function Main()
            {{declaration}}
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0110" }));
            Assert.That(diagnostics.Single().Message, Does.Contain(what));
            Assert.That(diagnostics.Single().Message, Does.Contain("Move it out"));
        });
    }

    [Test]
    public void FunctionsMayStillBeDeclaredInsideAFunction()
    {
        // Only types are barred. A nested function introduces no type name, and functions
        // are already ordered like locals, so it causes none of the trouble.
        CompilationUnit unit = ParseUnit(
            """
            global model Program
                function Main()
                    integer function Helper()
                        yield 1;
                    end function
                end function
            end model
            """);

        FunctionDecl main = (FunctionDecl)((ModelDecl)unit.Declarations[0]).Members[0];

        Assert.That(main.Body[0], Is.TypeOf<LocalDeclStmt>());
    }
}
