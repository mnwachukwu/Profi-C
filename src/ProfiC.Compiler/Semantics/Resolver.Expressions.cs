using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    private void BindExpression(Expression expression)
    {
        switch (expression)
        {
            case MissingExpr:
                // Nothing to bind, and nothing to report: the parser already said so.
                break;

            case IdentifierExpr identifier:
                BindIdentifier(identifier);
                break;

            case ReceiverExpr receiver:
                BindReceiver(receiver);
                break;

            case ParenthesizedExpr parenthesized:
                BindExpression(parenthesized.Inner);
                break;

            case InterpolatedStringExpr interpolated:
                // What sits in a hole is an ordinary expression written in an ordinary scope,
                // so the names in it are bound the same way as any others.
                foreach (InterpolationPart hole in interpolated.Holes)
                {
                    BindExpression(hole.Value);
                }

                break;

            case UnaryExpr unary:
                BindExpression(unary.Operand);
                break;

            case BinaryExpr binary:
                BindExpression(binary.Left);
                BindExpression(binary.Right);
                break;

            case TypeTestExpr test:
                BindExpression(test.Operand);
                ResolveType(test.TargetType);
                break;

            case TypeCastExpr cast:
                BindExpression(cast.Operand);
                ResolveType(cast.TargetType);
                break;

            case IfExpr conditional:
                BindExpression(conditional.Condition);
                BindExpression(conditional.ThenValue);
                BindExpression(conditional.ElseValue);
                break;

            case CollectionExpr collection:
                foreach (Expression element in collection.Elements)
                {
                    BindExpression(element);
                }

                break;

            case NewExpr construction:
                BindNew(construction);
                break;

            case CallExpr call:
                BindExpression(call.Callee);

                foreach (Expression argument in call.Arguments)
                {
                    BindExpression(argument);
                }

                break;

            case IndexExpr index:
                BindExpression(index.Receiver);
                BindExpression(index.Index);
                break;

            case MemberExpr member:
                BindMemberReceiver(member);
                break;

            case LambdaExpr lambda:
                BindLambda(lambda);
                break;
        }
    }

    /// <summary>
    /// <para>Binds what a member is read from.</para>
    /// <para><c>Shapes.Circle.Area()</c> and <c>account.Balance()</c> are the same shape, and
    /// only where the name leads decides which. So a run of plain names is offered to the type
    /// lookup first: if it reaches one, the whole run is that type and nothing in it was ever
    /// an expression. Otherwise the receiver is bound as a value, which is what it is.</para>
    /// <para>Tried longest-first, since <c>Shapes.Circle</c> naming a type has to beat
    /// <c>Shapes</c> naming one with a <c>Circle</c> member — the longer run is the more
    /// specific reading, and a type whose name is a namespace's is not otherwise separable
    /// from a namespace holding a type.</para>
    /// </summary>
    private void BindMemberReceiver(MemberExpr member)
    {
        if (NameSpine(member.Receiver) is { Count: > 1 } parts
            && LookupQualifiedType(parts) is { } type)
        {
            RequireVisibleType(member.Receiver, type);
            _model.Bind(member.Receiver, type);
            return;
        }

        BindExpression(member.Receiver);
    }

    /// <summary>
    /// The run of plain names an expression is, or null where it is anything else. Only names
    /// joined by dots can be a qualified type; a call or an index in the middle settles that
    /// what is being read is a value.
    /// </summary>
    private static List<string>? NameSpine(Expression expression)
    {
        List<string> parts = [];

        for (Expression current = expression; ;)
        {
            switch (current)
            {
                case IdentifierExpr identifier:
                    parts.Insert(0, identifier.Name);
                    return parts;

                case MemberExpr member:
                    parts.Insert(0, member.MemberName);
                    current = member.Receiver;
                    break;

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// <para>Binds a bare name, which reaches only locals and parameters.</para>
    /// <para>That restriction is what makes this two lines rather than a search through five
    /// levels of scope. It also means a name that happens to match a member is unambiguously
    /// a mistake with one fix, which is worth saying rather than reporting that the name does
    /// not exist.</para>
    /// </summary>
    private void BindIdentifier(IdentifierExpr identifier)
    {
        if (_scope.Lookup(identifier.Name) is { } local)
        {
            _model.Bind(identifier, local);
            return;
        }

        // A type name is legal here: it is how a shared member is reached, as in
        // "Program.Describe(x)".
        if (LookupType(identifier.Name) is { } type)
        {
            RequireVisibleType(identifier, type);
            _model.Bind(identifier, type);
            return;
        }

        if (ReportIfAmbiguous(identifier, identifier.Name))
        {
            return;
        }

        if (BuiltInTypeNames.Contains(identifier.Name))
        {
            _model.Bind(identifier, BuiltInModel(identifier.Name));
            return;
        }

        if (TryReportMissingReceiver(identifier))
        {
            return;
        }

        Report(DiagnosticDescriptors.NameNotFound, identifier, identifier.Name);
    }

    /// <summary>
    /// <para>Reports a bare name that would have matched a member of the enclosing type.</para>
    /// <para>This is the diagnostic that pays for requiring <c>this.</c> everywhere. Without
    /// it the message would be "not defined here", which is true and unhelpful.</para>
    /// </summary>
    private bool TryReportMissingReceiver(IdentifierExpr identifier)
    {
        if (_currentType is null)
        {
            return false;
        }

        IReadOnlyList<Symbol> members = _currentType is ModelSymbol model
            ? model.LookupIncludingBase(identifier.Name)
            : _currentType.Lookup(identifier.Name);

        if (members.Count == 0)
        {
            return false;
        }

        Symbol member = members[0];

        bool isShared = member switch
        {
            FieldSymbol field => field.IsShared,
            FunctionSymbol function => function.IsShared,
            _ => false,
        };

        // A shared member is reached through its type's name; an instance member through
        // "this". Naming the right one is the whole point of the message.
        string receiver = isShared ? _currentType.Name : "this";

        Report(
            DiagnosticDescriptors.MemberNeedsReceiver,
            identifier,
            identifier.Name,
            member.Kind,
            _currentType.Name,
            receiver);

        return true;
    }

    private void BindReceiver(ReceiverExpr receiver)
    {
        string word = receiver.Receiver.ToString().ToLowerInvariant();

        // "this" belongs to any declared type with instances, structures included; only
        // "base" needs a model, since only a model has a parent.
        if (_currentType is null || _inSharedMember)
        {
            Report(DiagnosticDescriptors.ThisOutsideModel, receiver, word);
            return;
        }

        // Nothing is built yet inside a field's starting value: the fields hold nothing until
        // their own initializers have run, and no constructor has started. A name reached
        // through 'this' here would answer with whatever it happened to hold, and which fields
        // had run would depend on the order they were written in.
        if (_initializingField is { } beingBuilt)
        {
            Report(
                DiagnosticDescriptors.ThisInFieldInitializer,
                receiver,
                word,
                _currentType.Name,
                beingBuilt);

            return;
        }

        if (receiver.Receiver == ReceiverKind.Base)
        {
            if (_currentModel?.BaseType is null)
            {
                Report(DiagnosticDescriptors.BaseWithoutParent, receiver, _currentType.Name);
                return;
            }

            _model.Bind(receiver, _currentModel.BaseType);
            _model.BindType(receiver, _currentModel.BaseType);
            return;
        }

        _model.Bind(receiver, _currentType);
        _model.BindType(receiver, _currentType);
    }

    private void BindNew(NewExpr construction)
    {
        foreach (Expression argument in construction.Arguments)
        {
            BindExpression(argument);
        }

        if (LookupQualifiedType(construction.TypeName.Split('.')) is { } type)
        {
            RequireVisibleType(construction, type);
            _model.Bind(construction, type);
            _model.BindType(construction, type);
            return;
        }

        if (ReportIfAmbiguous(construction, construction.TypeName))
        {
            return;
        }

        if (BuiltInTypeNames.Contains(construction.TypeName))
        {
            ModelSymbol builtIn = BuiltInModel(construction.TypeName);
            _model.Bind(construction, builtIn);
            _model.BindType(construction, builtIn);
            return;
        }

        Report(DiagnosticDescriptors.TypeNotFound, construction, construction.TypeName);
    }

    /// <summary>
    /// Binds a lambda. Its parameters live in a nested scope, so the body sees both them and
    /// the enclosing locals — which is what capture means.
    /// </summary>
    private void BindLambda(LambdaExpr lambda)
    {
        InScope(() =>
        {
            foreach (ParameterDecl parameter in lambda.Parameters)
            {
                // A parameter written as a bare name has no type yet. The type checker fills
                // it in from whatever the lambda is being written into, so it starts as the
                // error type — which is also the right answer if nothing ever supplies one.
                TypeSymbol type = parameter.Type is null
                    ? ErrorType.Instance
                    : ResolveType(parameter.Type);

                ParameterSymbol symbol = new(parameter.Name, type) { Declaration = parameter };

                Declare(symbol, parameter);
                _model.Bind(parameter, symbol);
            }

            if (lambda.ExpressionBody is not null)
            {
                BindExpression(lambda.ExpressionBody);
            }
            else if (lambda.Body is not null)
            {
                BindStatements(lambda.Body);
            }
        });
    }
}
