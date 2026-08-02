# The Profi-C standard library

Everything a program can use without declaring it. Nothing here is imported, and nothing is
installed: these members exist in every file, and the models live in the `Standard` namespace,
which is in scope everywhere without being asked for.

This is the reference. [Section 11 of the specification](../language-spec.md#11-the-standard-library)
says what the library is and why it is shaped this way; the pages here say what is in it.

## The pages

| | What is on it |
|---|---|
| [Every value](every-value.md) | `ToString`, `Equals`, and `Reference.Equals` — what any value answers, whatever its type |
| [Text](text.md) | Everything a `string` answers: searching, cutting, trimming, changing case, and reading a value back out of text |
| [Sets](sets.md) | Everything a `T[]` answers: counting, adding, removing, taking a run, and the four ways to drop empties |
| [Optionals](optionals.md) | The three members of a `T?`, and which of them the compiler makes you use |
| [Numbers](numbers.md) | `Math`, `Fraction`, `Random`, and what an `integer`, `real`, `fraction` and enumeration answer |
| [Dates and times](dates-and-times.md) | `DateTime`, `Date`, `Time` and `TimeSpan` — four types for four different questions |
| [Input and output](input-output.md) | `Console`, `File` and `Directory` |
| [Exceptions](exceptions.md) | `Message`, and every exception the language itself raises |

## How to read a signature

Every member is written the way the compiler understands it — what it takes, and what it gives
back:

```text
Substring(integer start, integer length) -> string
```

Names like `start` are for reading, not for writing: **Profi-C has no named arguments**, so what
matters is the order and the types. A member with `-> nothing` yields no value and cannot be used
where one is expected.

**A member written without parentheses is a value rather than something to call.** `Math.Pi` and
`landing.Year` are read; `word.ToUpper()` is called. Writing parentheses on a value is reported,
as is leaving them off a function — the two mistakes are each other's mirror.

## Two kinds of member

The distinction runs through the whole library, and it is the one to have straight:

**Reached through a value.** `word.ToUpper()`, `scores.Count`, `landing.Year`. These are found
by the *type* of what is on the left, so every `string` answers the same members and every set
answers the same members whatever it holds.

**Reached through a model's name.** `Math.Sqrt(2.0)`, `Console.WriteLine(x)`, `File.Read(path)`.
These belong to a model that has no instances — there is no such thing as *a* `Math` — so the
name on the left is the type itself.

**Five models are both.** `Random`, `DateTime`, `Date`, `Time` and `TimeSpan` each have members
reached through the name (`DateTime.Now`) and members reached through a value you are holding
(`landing.Year`) — and they are the five a program may construct with `new`. Each page says which
member is which.

## What the library does not have

**No generics and no interfaces.** The members here work on any element type because the
*compiler* knows them, not because the language can express "a member of a set of anything". That
is the same reason `Console.Write` accepts a value of any type while a program cannot write a
function that does.

**No properties a program can declare.** The library has them — `Math.Pi` and `landing.Year` are
values rather than calls — but they are the compiler's, and a model a program writes cannot
declare one. That is on the v2 list along with generics and interfaces.

**Nothing is `null`.** A member that may not have an answer yields an [optional](optionals.md) —
`File.Read` yields `string?`, `"x".ToInteger()` yields `integer?` — and the compiler will not let
you read one without proving it is there.

## Where the shapes come from

**The library keeps .NET's names and .NET's shapes wherever it can.** `Substring`, `IndexOf`,
`TrimStart`, `Math.Atan2`, `AddDays`, `CompareTo` and the format patterns are all the ones a
reader will type next in C#. Where Profi-C differs it is because the language does: `Insert` on a
string yields a new string because a Profi-C `string` cannot be changed, and a set's `Union`
appends rather than merging because a Profi-C set keeps its order.

Three members are the language's own rather than .NET's, and each page says so where it comes up:
`Capitalize`, `Fraction.Create`, and a set's `Distinct` reading of what a "set" is.
