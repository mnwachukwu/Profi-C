# The Profi-C standard library

Everything a program can use without declaring it. Nothing here is imported, and nothing is
installed: these members exist in every file, and the models live in the `Standard` namespace,
which is in scope everywhere without being asked for.

This page is the map — every type, then every member, each linking to the section that explains
it. [Section 11 of the specification](../language-spec.md#11-the-standard-library) says what the
library is and why it is shaped this way.

## Every type

### Types you write down

A value can have one of these, so you can declare a variable of it.

| Type | What it is | Its members |
|---|---|---|
| `integer` | A whole number, 64 bits | [On a number](numbers.md#members-on-a-number) |
| `real` | A decimal number, counted in tens and exact about them | [On a number](numbers.md#members-on-a-number) |
| `float` | Binary floating point, as everywhere else | [On a number](numbers.md#members-on-a-number) |
| `fraction` | An exact ratio of two whole numbers | [On a number](numbers.md#members-on-a-number) |
| `boolean` | `true` or `false` | Only [what every value has](every-value.md#members) |
| `character` | One character | Only [what every value has](every-value.md#members) |
| `string` | Text, and it cannot be changed | [Text](text.md) |
| `T[]` | A row of values that keeps its order | [Sets](sets.md) |
| `T?` | A `T`, or nothing | [Optionals](optionals.md) |
| an enumeration | The members it declared, and no others | [Enumerations](every-value.md#enumerations) |
| `DateTime` | Which moment | [Dates and times](dates-and-times.md) |
| `Date` | Which day | [Dates and times](dates-and-times.md) |
| `Time` | What time of day | [Dates and times](dates-and-times.md) |
| `TimeSpan` | How long | [Dates and times](dates-and-times.md) |
| `Random` | A generator of chance you hold | [Random](random.md) |
| `Exception` | The root of everything that can be thrown | [Exceptions](exceptions.md) |
| `Model` | The root of every type | [Every value](every-value.md) |
| `Function` | The root of every `delegate` type, so a function can be held without naming its signature | Only [what every value has](every-value.md#members) |

The five that a program may build with `new` are `DateTime`, `Date`, `Time`, `TimeSpan` and
`Random`. `Model` and `Exception` are the only two a program may `extend`.

### Names you reach members through

There is no such thing as *a* `Math` or *a* `Console`. These are names, not types: a variable of
one could never be filled, and declaring it is reported.

| Name | What it holds | Its members |
|---|---|---|
| `Console` | Writing to the screen and reading a line back | [Console](input-output.md#console) |
| `File` | Reading and writing whole files | [File](input-output.md#reading) |
| `Directory` | The folders files sit in | [Directory](input-output.md#directory) |
| `Math` | Roots, logarithms, angles, rounding and sizing | [Math](math.md) |
| `Reference` | The one way to ask whether two names reach the same object | [Reference.Equals](every-value.md#referenceequals) |
| `Integer` | Where an `integer` runs out | [What each type knows](numbers.md#what-each-type-knows-about-itself) |
| `Real` | Where a `real` runs out | [What each type knows](numbers.md#what-each-type-knows-about-itself) |
| `Float` | Where a `float` runs out, and its three odd values | [What only a float has](numbers.md#what-only-a-float-has) |
| `String` | The name for an empty string | [String](text.md#string) |
| `Fraction` | Building a `fraction` from values | [Fraction](numbers.md#fraction) |

A reserved word cannot stand in front of a dot, which is why the facts about a primitive live
beside it under a capital: `integer.MaxValue` is not something the grammar can read, and
`Integer.MaxValue` is.

### Names for what went wrong

Every one descends from `Exception` and carries
[`Message`](exceptions.md#the-member-every-exception-carries). All but the last may be written
after `catch`; [what the language raises](exceptions.md#what-the-language-raises) says more about
each.

| Exception | Raised when |
|---|---|
| `Exception` | The root — a `catch` on it takes every one below |
| `DivideByZeroException` | Dividing by a zero the compiler could not see |
| `IndexOutOfRangeException` | An index outside the set or string it is used on |
| `EmptyOptionalException` | `Value()` on an optional that turned out empty |
| `SequenceChangedException` | A set changed while a `loop each` was walking it |
| `InvalidCastException` | An `as` to a type the value is not |
| `FormatException` | A pattern `Format` does not recognize |
| `ArgumentException` | A value a member cannot work with |
| `OverflowException` | A number grown too large to hold |
| `IOException` | Anything that goes wrong with a file except its absence |
| `RecursionTooDeepException` | Recursion with no base case — **and the one nothing catches**, since there is nothing useful to do about it. A `catch` naming it is reported (`PC0344`) |

## Every member, A–Z

One row per name. Where a name is on more than one type, the row says so — `Insert` is one idea
answered by both a string and a set, and reading them apart would hide that.

| Member | On | Yields | Where |
|---|---|---|---|
| `Abs(x)` | `Math` | the type given | [Comparing and sizing](math.md#comparing-and-sizing) |
| `Acos(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Acosh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Add(TimeSpan)` | `DateTime` · `TimeSpan` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddDays(n)` | `DateTime` · `Date` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddHours(real)` | `DateTime` · `Time` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddMinutes(real)` | `DateTime` · `Time` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddMonths(integer)` | `DateTime` · `Date` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddSeconds(real)` | `DateTime` | `DateTime` | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `AddYears(integer)` | `DateTime` · `Date` | the receiver's type | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `Append(path, text)` | `File` | nothing | [Writing](input-output.md#writing) |
| `Asin(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Asinh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Atan(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Atan2(y, x)` | `Math` | `real` | [Angles](math.md#angles) |
| `Atanh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Capitalize()` | `string` | `string` | [Case](text.md#case) |
| `Cbrt(real)` | `Math` | `real` | [Roots and powers](math.md#roots-and-powers) |
| `Ceiling(x)` | `Math` | `integer` | [Rounding](math.md#rounding) |
| `Changed(path)` | `File` | `DateTime?` | [Managing](input-output.md#managing) |
| `Clear()` | `T[]` | nothing | [Changing it](sets.md#changing-it) |
| `CompareTo(same type)` | `DateTime` · `Date` · `Time` · `TimeSpan` | `integer` | [Comparing](dates-and-times.md#comparing) |
| `Contains(what)` | `string` · `T[]` | `boolean` | [Text](text.md#asking-about-it) · [Sets](sets.md#asking-about-it) |
| `Copy(from, to)` | `File` | nothing | [Managing](input-output.md#managing) |
| `Cos(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Cosh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Count` | `string` · `T[]` | `integer` | [Text](text.md#asking-about-it) · [Sets](sets.md#asking-about-it) |
| `Create(...)` | `Fraction` | `fraction` | [Fraction](numbers.md#fraction) |
| `Current` | `Directory` | `string` | [Directory](input-output.md#directory) |
| `Date` | `DateTime` | `Date` | [Moving between the four](dates-and-times.md#moving-between-the-four) |
| `new Date(y, m, d)` | `Date` | `Date` | [Making one](dates-and-times.md#making-one) |
| `new DateTime(...)` | `DateTime` | `DateTime` | [Making one](dates-and-times.md#making-one) |
| `Day` | `DateTime` · `Date` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `DayOfWeek` | `DateTime` · `Date` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `DayOfYear` | `DateTime` · `Date` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Days` | `TimeSpan` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Delete(path)` | `File` · `Directory` | `boolean` | [Managing](input-output.md#managing) · [Directory](input-output.md#directory) |
| `Distinct()` | `T[]` | `T[]` | [Two sets read together](sets.md#two-sets-read-together) |
| `Duration()` | `TimeSpan` | `TimeSpan` | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `E` | `Math` | `real` | [Constants](math.md#constants) |
| `Empty` | `String` | `string` | [String](text.md#string) |
| `Equals(anything)` | every value | `boolean` | [Members](every-value.md#members) |
| `Except(other)` | `T[]` | `T[]` | [Two sets read together](sets.md#two-sets-read-together) |
| `Exists(path)` | `File` · `Directory` | `boolean` | [Managing](input-output.md#managing) · [Directory](input-output.md#directory) |
| `Factorial(integer)` | `Math` | `integer` | [Roots and powers](math.md#roots-and-powers) |
| `Files(path)` | `Directory` | `string[]?` | [Directory](input-output.md#directory) |
| `Floor(x)` | `Math` | `integer` | [Rounding](math.md#rounding) |
| `Folders(path)` | `Directory` | `string[]?` | [Directory](input-output.md#directory) |
| `Format(pattern)` | every number · `DateTime` · `Date` · `Time` · `TimeSpan` | `string` | [Writing a number out](numbers.md#writing-a-number-out) · [Writing one out](dates-and-times.md#writing-one-out-and-reading-one-back) |
| `FromDateTime(DateTime)` | `Date` · `Time` | the receiver's type | [Moving between the four](dates-and-times.md#moving-between-the-four) |
| `FromDays(real)` | `TimeSpan` | `TimeSpan` | [From an amount](dates-and-times.md#making-a-timespan-from-an-amount) |
| `FromHours(real)` | `TimeSpan` | `TimeSpan` | [From an amount](dates-and-times.md#making-a-timespan-from-an-amount) |
| `FromMinutes(real)` | `TimeSpan` | `TimeSpan` | [From an amount](dates-and-times.md#making-a-timespan-from-an-amount) |
| `FromSeconds(real)` | `TimeSpan` | `TimeSpan` | [From an amount](dates-and-times.md#making-a-timespan-from-an-amount) |
| `HasValue()` | `T?` | `boolean` | [Members](optionals.md#members) |
| `Hour` | `DateTime` · `Time` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Hours` | `TimeSpan` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `IndexOf(what)` | `string` · `T[]` | `integer` | [Text](text.md#asking-about-it) · [Sets](sets.md#asking-about-it) |
| `Infinity` | `Float` | `float` | [What only a float has](numbers.md#what-only-a-float-has) |
| `Insert(what)` | `string` · `T[]` | `string` · nothing | [Text](text.md#building-a-new-one) · [Sets](sets.md#changing-it) |
| `InsertAt(where, what)` | `string` · `T[]` | `string` · nothing | [Text](text.md#building-a-new-one) · [Sets](sets.md#changing-it) |
| `Intersect(other)` | `T[]` | `T[]` | [Two sets read together](sets.md#two-sets-read-together) |
| `Join(separator)` | `T[]` | `string` | [Joining](sets.md#joining) |
| `Log(x)` · `Log(x, base)` | `Math` | `real` | [Logarithms](math.md#logarithms) |
| `Log10(x)` | `Math` | `real` | [Logarithms](math.md#logarithms) |
| `Log2(x)` | `Math` | `real` | [Logarithms](math.md#logarithms) |
| `Max(a, b)` | `Math` | the type given | [Comparing and sizing](math.md#comparing-and-sizing) |
| `MaxValue` | `Integer` · `Real` · `Float` | that type | [What each type knows](numbers.md#what-each-type-knows-about-itself) |
| `Message()` | every exception | `string` | [The member every exception carries](exceptions.md#the-member-every-exception-carries) |
| `Min(a, b)` | `Math` | the type given | [Comparing and sizing](math.md#comparing-and-sizing) |
| `MinValue` | `Integer` · `Real` · `Float` | that type | [What each type knows](numbers.md#what-each-type-knows-about-itself) |
| `Minute` | `DateTime` · `Time` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Minutes` | `TimeSpan` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Month` | `DateTime` · `Date` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Move(from, to)` | `File` | nothing | [Managing](input-output.md#managing) |
| `Negate()` | `TimeSpan` | `TimeSpan` | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `NegativeInfinity` | `Float` | `float` | [What only a float has](numbers.md#what-only-a-float-has) |
| `Next(...)` | `Random` | `integer` | [Asking for a number](random.md#asking-for-a-number) |
| `NextDouble()` | `Random` | `real` | [Asking for a number](random.md#asking-for-a-number) |
| `NotANumber` | `Float` | `float` | [What only a float has](numbers.md#what-only-a-float-has) |
| `Now` | `DateTime` · `Time` | that type | [Reading the clock](dates-and-times.md#reading-the-clock) |
| `Or(fallback)` | `T?` | `T` · `T?` | [Or supplies a fallback](optionals.md#or-supplies-a-fallback) |
| `Parse(text)` | `DateTime` · `Date` · `Time` · `TimeSpan` | that type, optional | [Reading one back](dates-and-times.md#writing-one-out-and-reading-one-back) |
| `Pi` | `Math` | `real` | [Constants](math.md#constants) |
| `Pow(x, by)` | `Math` | `real` | [Roots and powers](math.md#roots-and-powers) |
| `new Random()` · `new Random(seed)` | `Random` | `Random` | [Making one](random.md#making-one) |
| `Read()` · `Read(path)` | `Console` · `File` | `string?` | [Console](input-output.md#console) · [Reading](input-output.md#reading) |
| `ReadLines(path)` | `File` | `string[]?` | [Reading](input-output.md#reading) |
| `Reciprocal()` | `fraction` | `fraction` | [On a number](numbers.md#members-on-a-number) |
| `Remove(what)` | `string` · `T[]` | `string` · `boolean` | [Text](text.md#building-a-new-one) · [Sets](sets.md#changing-it) |
| `RemoveAt(where)` | `string` · `T[]` | `string` · nothing | [Text](text.md#building-a-new-one) · [Sets](sets.md#changing-it) |
| `Replace(what, with)` | `string` | `string` | [Building a new one](text.md#building-a-new-one) |
| `Root(x, degree)` | `Math` | `real` | [Roots and powers](math.md#roots-and-powers) |
| `Round(x)` · `Round(x, places)` | `Math` | `integer` · the type given | [Rounding](math.md#rounding) |
| `Second` | `DateTime` · `Time` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Seconds` | `TimeSpan` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Size(path)` | `File` | `integer?` | [Managing](input-output.md#managing) |
| `Sin(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Sinh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Split(separator)` | `string` | `string[]` | [Splitting and joining](text.md#splitting-and-joining) |
| `Sqrt(real)` | `Math` | `real` | [Roots and powers](math.md#roots-and-powers) |
| `Subset(start)` · `Subset(start, end)` | `string` · `T[]` | `string` · `T[]` | [Text](text.md#taking-a-piece) · [Sets](sets.md#taking-a-run) |
| `Substring(start, length)` | `string` | `string` | [Taking a piece](text.md#taking-a-piece) |
| `Subtract(...)` | `DateTime` · `TimeSpan` | `DateTime` · `TimeSpan` | [Moving forward and back](dates-and-times.md#moving-forward-and-back) |
| `Tan(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Tanh(real)` | `Math` | `real` | [Angles](math.md#angles) |
| `Time` | `DateTime` | `Time` | [Moving between the four](dates-and-times.md#moving-between-the-four) |
| `new Time(h, m)` · `new Time(h, m, s)` | `Time` | `Time` | [Making one](dates-and-times.md#making-one) |
| `new TimeSpan(...)` | `TimeSpan` | `TimeSpan` | [Making one](dates-and-times.md#making-one) |
| `ToBoolean()` | `string` | `boolean?` | [Reading a value back out](text.md#reading-a-value-back-out) |
| `ToCharacters()` | `string` | `character[]` | [Splitting and joining](text.md#splitting-and-joining) |
| `ToDateTime(Time)` | `Date` | `DateTime` | [Moving between the four](dates-and-times.md#moving-between-the-four) |
| `ToFloat()` | `integer` · `real` · `fraction` | `float` | [On a number](numbers.md#members-on-a-number) |
| `ToFraction()` | `float` · `string` | `fraction` · `fraction?` | [On a number](numbers.md#members-on-a-number) · [Reading a value back out](text.md#reading-a-value-back-out) |
| `ToInteger()` | `string` · an enumeration | `integer?` · `integer` | [Reading a value back out](text.md#reading-a-value-back-out) · [Enumerations](every-value.md#enumerations) |
| `ToLower()` | `string` | `string` | [Case](text.md#case) |
| `ToReal()` | `float` · `fraction` · `string` | `real` · `real?` | [On a number](numbers.md#members-on-a-number) · [Reading a value back out](text.md#reading-a-value-back-out) |
| `ToString()` | every value | `string` | [Members](every-value.md#members) |
| `ToTimeSpan()` | `Time` | `TimeSpan` | [Moving between the four](dates-and-times.md#moving-between-the-four) |
| `ToUpper()` | `string` | `string` | [Case](text.md#case) |
| `Today` | `DateTime` · `Date` | that type | [Reading the clock](dates-and-times.md#reading-the-clock) |
| `TotalDays` · `TotalHours` · `TotalMinutes` · `TotalSeconds` | `TimeSpan` | `real` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Trim(...)` | `string` · `T?[]` | `string` · `T?[]` | [Trimming](text.md#trimming) · [Dropping the empties](sets.md#dropping-the-empties) |
| `TrimAll()` | `T?[]` | `T[]` | [Dropping the empties](sets.md#dropping-the-empties) |
| `TrimEnd(...)` | `string` · `T?[]` | `string` · `T?[]` | [Trimming](text.md#trimming) · [Dropping the empties](sets.md#dropping-the-empties) |
| `TrimStart(...)` | `string` · `T?[]` | `string` · `T?[]` | [Trimming](text.md#trimming) · [Dropping the empties](sets.md#dropping-the-empties) |
| `Union(other)` | `T[]` | `T[]` | [Two sets read together](sets.md#two-sets-read-together) |
| `Value()` | `T?` | `T` | [Members](optionals.md#members) |
| `Write(anything)` · `Write(path, text)` | `Console` · `File` | nothing | [Console](input-output.md#console) · [Writing](input-output.md#writing) |
| `WriteLine(anything)` | `Console` | nothing | [Console](input-output.md#console) |
| `WriteLines(path, lines)` | `File` | nothing | [Writing](input-output.md#writing) |
| `Year` | `DateTime` · `Date` | `integer` | [Reading the parts](dates-and-times.md#reading-the-parts) |
| `Zero` | `TimeSpan` | `TimeSpan` | [Reading the clock](dates-and-times.md#reading-the-clock) |

## The pages

| | What is on it |
|---|---|
| [Every value](every-value.md) | `ToString`, `Equals`, and `Reference.Equals` — what any value answers, whatever its type |
| [Text](text.md) | Everything a `string` answers: searching, cutting, trimming, changing case, and reading a value back out of text |
| [Sets](sets.md) | Everything a `T[]` answers: counting, adding, removing, taking a run, and the four ways to drop empties |
| [Optionals](optionals.md) | The three members of a `T?`, and which of them the compiler makes you use |
| [Numbers](numbers.md) | What an `integer`, `real`, `float` and `fraction` answer, what each knows about itself, and every conversion between them |
| [Math](math.md) | Roots, logarithms, angles, rounding and sizing |
| [Random](random.md) | Chance, held or drawn through the name |
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
