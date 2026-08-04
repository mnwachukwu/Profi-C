using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>What a <c>string</c> answers, which is nearly all one call into
/// <see cref="ProfiCText"/> — the very methods the interpreter calls.</para>
/// <para><b>Almost none of these is the framework method of the same name.</b> Profi-C compares
/// ordinally where .NET compares by culture, counts positions in 64 bits where .NET counts in 32,
/// treats an empty argument as matching trivially where .NET raises, and capitalizes a first
/// letter without lowering the rest. Emitting a call to <c>System.String</c> would be right often
/// enough to look right, and each of those differences is a place two engines could quietly part
/// company.</para>
/// <para>Two exceptions, both because there is nothing for the language to decide: <c>Count</c> is
/// the CLR's own length widened, and <c>Substring</c> — named for what C# calls it — is the
/// runtime's, since it refuses out of range in the language's words rather than the framework's.
/// </para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>One member of a string: the subject, the arguments, then the call.</para>
    /// <para>Every position narrows on the way in and every count widens on the way out, the same
    /// boundary a set has — Profi-C counts with <c>integer</c>, and the CLR addresses text with
    /// <c>int</c>.</para>
    /// </summary>
    /// <param name="member">The member as written, whose receiver is the string.</param>
    /// <param name="arguments">
    /// What the call passed, empty for a member read rather than called — <c>Count</c> is a value
    /// and arrives with none.
    /// </param>
    /// <param name="id">Which member the checker settled on.</param>
    private void EmitStringMember(
        MemberExpr member,
        IReadOnlyList<Expression> arguments,
        BuiltInId id)
    {
        // A string member is called on a value, so the string goes down first. A Parse is called
        // on a type's own name, which holds nothing there is a value for — the text it reads is
        // the argument, and pushing anything for the name would leave what nothing takes off.
        if (!CilBuiltIns.IsParsedThroughATypeName(id))
        {
            EmitExpression(member.Receiver);
        }

        switch (id)
        {
            // The one member with no runtime method behind it, because there is nothing about a
            // string's length this language decides differently.
            case BuiltInId.StringCount:
                _il.Emit(OpCodes.Callvirt, StringLength);
                _il.Emit(OpCodes.Conv_I8);
                return;

            // Everything else is the subject, then whatever was written after it, then one call.
            // No position is narrowed on the way: a Profi-C integer already is what these take,
            // unlike a set, which is addressed in 32 bits and converts at every boundary.
            case BuiltInId.StringContains:
            case BuiltInId.StringIndexOf:
            case BuiltInId.StringSubstring:
            case BuiltInId.StringSubsetFrom:
            case BuiltInId.StringSubsetBetween:
            case BuiltInId.StringInsert:
            case BuiltInId.StringInsertAt:
            case BuiltInId.StringRemove:
            case BuiltInId.StringRemoveAt:
            case BuiltInId.StringToCharacters:
            case BuiltInId.StringTrim:
            case BuiltInId.StringTrimText:
            case BuiltInId.StringTrimSet:
            case BuiltInId.StringTrimStart:
            case BuiltInId.StringTrimStartText:
            case BuiltInId.StringTrimStartSet:
            case BuiltInId.StringTrimEnd:
            case BuiltInId.StringTrimEndText:
            case BuiltInId.StringTrimEndSet:
            case BuiltInId.StringSplit:
            case BuiltInId.StringReplace:
            case BuiltInId.StringToUpper:
            case BuiltInId.StringToLower:
            case BuiltInId.StringCapitalize:
            case BuiltInId.StringToInteger:
            case BuiltInId.StringToReal:
            case BuiltInId.StringToFloat:
            case BuiltInId.StringToBoolean:
            case BuiltInId.StringToCharacter:
            case BuiltInId.StringToFraction:

            // The same readings reached through a type's own name. They arrive here rather than
            // beside the type's constants because what stands behind them is the same runtime
            // method the string members call — one implementation, so the two spellings cannot
            // come to disagree. Nothing was pushed for the name, so the loop below reads the
            // text out of the argument exactly as it reads every other argument.
            case BuiltInId.IntegerParse:
            case BuiltInId.RealParse:
            case BuiltInId.FloatParse:
            case BuiltInId.BooleanParse:
            case BuiltInId.CharacterParse:
            case BuiltInId.FractionParse:
                foreach (Expression argument in arguments)
                {
                    EmitExpression(argument);
                }

                _il.Emit(OpCodes.Call, TextMethod(id));
                return;

            default:
                throw Unhandled($"the string member '{id}'");
        }
    }

    /// <summary>
    /// <para>The runtime method behind one member.</para>
    /// <para>Named here rather than at each case because several members share a name and are
    /// told apart by what they take — <c>Trim</c> has three forms and <c>Subset</c> two — so
    /// choosing the overload is one question answered in one place.</para>
    /// </summary>
    private static MethodInfo TextMethod(BuiltInId id) => id switch
    {
        BuiltInId.StringContains => Text(nameof(ProfiCText.Contains), typeof(string), typeof(string)),
        BuiltInId.StringIndexOf => Text(nameof(ProfiCText.IndexOf), typeof(string), typeof(string)),

        BuiltInId.StringSubstring =>
            Text(nameof(ProfiCText.Substring), typeof(string), typeof(long), typeof(long)),
        BuiltInId.StringSubsetFrom => Text(nameof(ProfiCText.Subset), typeof(string), typeof(long)),
        BuiltInId.StringSubsetBetween =>
            Text(nameof(ProfiCText.Subset), typeof(string), typeof(long), typeof(long)),

        BuiltInId.StringInsert => Text(nameof(ProfiCText.Insert), typeof(string), typeof(string)),
        BuiltInId.StringInsertAt =>
            Text(nameof(ProfiCText.InsertAt), typeof(string), typeof(long), typeof(string)),
        BuiltInId.StringRemove => Text(nameof(ProfiCText.Remove), typeof(string), typeof(string)),
        BuiltInId.StringRemoveAt => Text(nameof(ProfiCText.RemoveAt), typeof(string), typeof(long)),

        BuiltInId.StringToCharacters => Text(nameof(ProfiCText.ToCharacters), typeof(string)),

        BuiltInId.StringTrim => Text(nameof(ProfiCText.Trim), typeof(string)),
        BuiltInId.StringTrimText => Text(nameof(ProfiCText.Trim), typeof(string), typeof(string)),
        BuiltInId.StringTrimSet => Text(nameof(ProfiCText.Trim), typeof(string), typeof(IProfiCSet)),

        BuiltInId.StringTrimStart => Text(nameof(ProfiCText.TrimStart), typeof(string)),
        BuiltInId.StringTrimStartText =>
            Text(nameof(ProfiCText.TrimStart), typeof(string), typeof(string)),
        BuiltInId.StringTrimStartSet =>
            Text(nameof(ProfiCText.TrimStart), typeof(string), typeof(IProfiCSet)),

        BuiltInId.StringTrimEnd => Text(nameof(ProfiCText.TrimEnd), typeof(string)),
        BuiltInId.StringTrimEndText =>
            Text(nameof(ProfiCText.TrimEnd), typeof(string), typeof(string)),
        BuiltInId.StringTrimEndSet =>
            Text(nameof(ProfiCText.TrimEnd), typeof(string), typeof(IProfiCSet)),

        BuiltInId.StringSplit => Text(nameof(ProfiCText.Split), typeof(string), typeof(string)),
        BuiltInId.StringReplace =>
            Text(nameof(ProfiCText.Replace), typeof(string), typeof(string), typeof(string)),

        BuiltInId.StringToUpper => Text(nameof(ProfiCText.ToUpper), typeof(string)),
        BuiltInId.StringToLower => Text(nameof(ProfiCText.ToLower), typeof(string)),
        BuiltInId.StringCapitalize => Text(nameof(ProfiCText.Capitalize), typeof(string)),

        BuiltInId.StringToInteger or BuiltInId.IntegerParse =>
            Text(nameof(ProfiCText.ToInteger), typeof(string)),
        BuiltInId.StringToReal or BuiltInId.RealParse =>
            Text(nameof(ProfiCText.ToReal), typeof(string)),
        BuiltInId.StringToFloat or BuiltInId.FloatParse =>
            Text(nameof(ProfiCText.ToFloat), typeof(string)),
        BuiltInId.StringToBoolean or BuiltInId.BooleanParse =>
            Text(nameof(ProfiCText.ToBoolean), typeof(string)),
        BuiltInId.StringToCharacter or BuiltInId.CharacterParse =>
            Text(nameof(ProfiCText.ToCharacter), typeof(string)),
        BuiltInId.StringToFraction or BuiltInId.FractionParse =>
            Text(nameof(ProfiCText.ToFraction), typeof(string)),

        _ => throw new InvalidOperationException($"No runtime method stands behind '{id}'."),
    };

    private static MethodInfo Text(string name, params Type[] taking) =>
        typeof(ProfiCText).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    private static readonly MethodInfo StringLength =
        typeof(string).GetProperty(nameof(string.Length))!.GetMethod!;

    /// <summary>
    /// The two crossings between a string and its characters, which the language performs without
    /// either being written. Neither loses anything, which is why both are implicit.
    /// </summary>
    private static readonly MethodInfo TextToCharacters =
        Text(nameof(ProfiCText.ToCharacters), typeof(string));

    private static readonly MethodInfo TextFromCharacters =
        Text(nameof(ProfiCText.FromCharacters), typeof(IProfiCSet));

    /// <summary>A character by position, which is what indexing a string means.</summary>
    private static readonly MethodInfo TextAt =
        Text(nameof(ProfiCText.At), typeof(string), typeof(long));
}
