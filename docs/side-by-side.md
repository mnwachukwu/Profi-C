# Profi-C and C#: A Side by Side Comparison

Every construct written both ways: Profi-C first, the nearest C# underneath.

For the condensed reference and the prose comparison see
[language-summary.md](language-summary.md); for the normative definition,
[language-spec.md](language-spec.md); for the surface syntax as productions,
[grammar.ebnf](grammar.ebnf). Every diagnostic named here is listed in the specification's
[diagnostics appendix](language-spec.md#appendix-a-diagnostics).

**These are fragments, not programs.** Each carries the least context needed to show the shape,
so most would not compile as written — a model with no enclosing file, a statement with no
function around it. Where a whole program is wanted, [the samples](../samples) are all runnable
and recorded.

**The C# is the nearest equivalent rather than the best C#.** Where an idiomatic C# author would
reach for something Profi-C has no counterpart to, the comparison shows the same job being done
rather than the same style — and where that costs C# something real, it is said.

**The last three sections keep score.** A comparison that only listed the ways one language is
tidier would be advertising, in either direction.
[§9](#9-where-profi-c-does-it-better) covers what both have and Profi-C does better;
[§10](#10-where-c-does-it-better) covers what both have and C# does better; and
[§11](#11-c-with-no-profi-c-equivalent) covers what C# has and Profi-C has no form for at all.
That last one is the longest, which is the honest shape of a young language against one
twenty-four years old — and the reason it is last rather than first is that a reader who stops
before it has still read the trades.

## Contents

- [1. Declaring a type](#1-declaring-a-type)
- [2. Members](#2-members)
- [3. Functions and function values](#3-functions-and-function-values)
- [4. Loops](#4-loops)
- [5. Choosing](#5-choosing)
- [6. Optionals, sets, and fractions](#6-optionals-sets-and-fractions)
- [7. When something goes wrong](#7-when-something-goes-wrong)
- [8. Files, names, and text](#8-files-names-and-text)
- [9. Where Profi-C does it better](#9-where-profi-c-does-it-better)
- [10. Where C# does it better](#10-where-c-does-it-better)
- [11. C# with no Profi-C equivalent](#11-c-with-no-profi-c-equivalent)
  - [11.1 Shape and abstraction](#111-shape-and-abstraction)
  - [11.2 Data](#112-data)
  - [11.3 Control and sequence](#113-control-and-sequence)
  - [11.4 Reaching other code](#114-reaching-other-code)
  - [11.5 What is scheduled, and what is not](#115-what-is-scheduled-and-what-is-not)
- [12. The same code, a different answer](#12-the-same-code-a-different-answer)

---

## 1. Declaring a type

### A model, and one extending it

**Profi-C**

```
model Shape
    protected string Name;

    public function Shape(string name)
        this.Name = name;
    end function
end model

model Circle extends Shape
    public function Circle()
        base("circle");
    end function
end model
```

**C#**

```csharp
class Shape
{
    protected string Name;

    public Shape(string name)
    {
        this.Name = name;
    }
}

class Circle : Shape
{
    public Circle() : base("circle") { }
}
```

`extends` rather than `:`, and there is no interface list after it — see
[§10.1](#111-shape-and-abstraction). A type with no visibility word is `internal`, exactly as in
C#.

### A shared model, which is where a program starts

**Profi-C**

```
shared model Program
    function Main()
        Console.WriteLine("Hello, World!");
    end function
end model
```

**C#**

```csharp
static class Program
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");
    }
}
```

A `shared model`'s members are shared already, so writing `shared function Main()` adds nothing.
`Main` takes no arguments and yields nothing.

C# can drop the ceremony entirely with top-level statements — `Console.WriteLine("Hello");` in a
file by itself is a whole program. Profi-C cannot, and for a first lesson that is a real cost.

### A structure, which is a value

**Profi-C**

```
structure Point
    public integer X;
    public integer Y;

    public function Point(integer x, integer y)
        this.X = x;
        this.Y = y;
    end function
end structure
```

**C#**

```csharp
struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}
```

Structures compare by their fields and copy on assignment. What they cannot do is convert to
`Model`, because that would be boxing.

### An enumeration

**Profi-C**

```
enumeration Color
    Red,
    Green = 10,
    Blue
end enumeration
```

**C#**

```csharp
enum Color
{
    Red,
    Green = 10,
    Blue,
}
```

The word is spelled out. A `switch` leaving a member unhandled is a warning, where C# says
nothing.

---

## 2. Members

### Fields, and reaching them

**Profi-C**

```
model Account
    integer balance;
    public string Owner;

    public function Deposit(integer amount)
        this.balance = this.balance + amount;
    end function
end model
```

**C#**

```csharp
class Account
{
    int balance;
    public string Owner;

    public void Deposit(int amount)
    {
        this.balance += amount;
    }
}
```

**`this.` is not optional.** A bare name reaches locals and parameters only, so every line
touching object state says so. There is no `private` keyword — private is what writing nothing
gets you.

The `+=` on the C# side is not stylistic: Profi-C has no compound assignment, so accumulation is
always written out. See [§9](#10-where-c-does-it-better).

### Abstract, virtual, override

**Profi-C**

```
abstract model Shape
    public abstract real function Area();

    public virtual string function Describe()
        yield "a shape";
    end function
end model

model Square extends Shape
    public override real function Area()
        yield this.side * this.side;
    end function
end model
```

**C#**

```csharp
abstract class Shape
{
    public abstract double Area();

    public virtual string Describe() => "a shape";
}

class Square : Shape
{
    public override double Area() => this.side * this.side;
}
```

An abstract function ends at its semicolon and may not carry a body. `virtual` beside `abstract`
is an opinion, since abstract already implies it.

### A constant

**Profi-C**

```
shared model Program
    constant integer PassingScore = 60;

    function Main()
        constant real Half = 0.5;
    end function
end model
```

**C#**

```csharp
static class Program
{
    const int PassingScore = 60;

    static void Main()
    {
        const double Half = 0.5;
    }
}
```

`constant` needs an explicit type and a value known while compiling, and is permitted only where
an unchanging binding really means an unchanging value.

---

## 3. Functions and function values

### Declaring one, and giving a result

**Profi-C**

```
integer function Add(integer a, integer b)
    yield a + b;
end function

function Announce(string text)
    Console.WriteLine(text);
end function
```

**C#**

```csharp
int Add(int a, int b)
{
    return a + b;
}

void Announce(string text)
{
    Console.WriteLine(text);
}
```

**`yield` means return.** This is the single most dangerous word for a C# reader, since C# uses
`yield return` for iterators; here it has nothing to do with them. A function yielding nothing
writes no type at all rather than `void`.

### A function declared among statements

**Profi-C**

```
function Report()
    integer by = 4;

    integer function Raised(integer n)
        yield n + by;
    end function

    Console.WriteLine(Raised(2));
end function
```

**C#**

```csharp
void Report()
{
    int by = 4;

    int Raised(int n) => n + by;

    Console.WriteLine(Raised(2));
}
```

A local function is in scope for the whole run it sits in, not from its own line onward, so a
call may be written above it and two may call each other. What it names still comes into being
in order, so calling one before a local it reads is `PC0405`.

### A function as a value

**Profi-C**

```
integer delegate(integer, integer) add = (a, b) yield a + b;

delegate(string) announce = function(text)
    Console.WriteLine(text);
end function;

Console.WriteLine(add(2, 3));
```

**C#**

```csharp
Func<int, int, int> add = (a, b) => a + b;

Action<string> announce = text =>
{
    Console.WriteLine(text);
};

Console.WriteLine(add(2, 3));
```

**`delegate` writes the type; `function` writes the thing.** One word, one job — where C# needs
`Func<>` for something yielding a value and `Action<>` for something not, Profi-C writes the
result type in front or leaves it off. `Function` is the root every function type descends from,
and says a value is a function without saying what shape.

---

## 4. Loops

### Counting

**Profi-C**

```
loop for i = 1 to 10
    Console.WriteLine(i);
end loop

loop for i = 10 to 1 stepby -1
    Console.WriteLine(i);
end loop
```

**C#**

```csharp
for (int i = 1; i <= 10; i++)
{
    Console.WriteLine(i);
}

for (int i = 10; i >= 1; i--)
{
    Console.WriteLine(i);
}
```

`to` includes its bound and `until` excludes it, which is the distinction C# leaves to
remembering whether `<` or `<=` was written. The counter carries no type, and cannot be assigned
to inside the body. **The bound and the step are read again on every turn**, exactly as a C-style
header is.

C#'s three-clause header does more: any initializer, any condition, any increment, and several
of each. Profi-C's counts, and anything else is a different loop.

### Walking

**Profi-C**

```
loop each grade in grades
    Console.WriteLine(grade);
end loop
```

**C#**

```csharp
foreach (int grade in grades)
{
    Console.WriteLine(grade);
}
```

A `loop each` reads its sequence's length once, when the loop begins, so changing that sequence
inside its own loop is refused (`PC0243`) rather than left to mean something subtle. C# permits
the same code and throws partway through instead.

C#'s walks anything implementing `IEnumerable`, including things computed lazily as they are
read. Profi-C's walks a set.

### Asking before, asking after, and not asking

**Profi-C**

```
loop while count < 10
    count = count + 1;
end loop

loop
    guess = Console.Read();
until guess == secret

loop
    if Done()
        break;
    end if
end loop
```

**C#**

```csharp
while (count < 10)
{
    count++;
}

do
{
    guess = Console.ReadLine();
} while (guess != secret);

while (true)
{
    if (Done()) break;
}
```

Three things to notice. **`until` is the opposite sense of `while`** — it names the condition to
stop on, so the C# `while (guess != secret)` becomes `until guess == secret`. **It is also the
only construct `end` does not close**, because the word carrying the condition is doing the
closing. And **a loop with no condition is written with none**, rather than as a `while` whose
condition is a constant standing in for a statement the writer could simply make; one that
nothing inside can break, yield, or throw out of is `PC0406`.

---

## 5. Choosing

### Branching

**Profi-C**

```
if score >= 90
    Console.WriteLine("excellent");
else if score >= 60
    Console.WriteLine("pass");
else
    Console.WriteLine("fail");
end if
```

**C#**

```csharp
if (score >= 90)
{
    Console.WriteLine("excellent");
}
else if (score >= 60)
{
    Console.WriteLine("pass");
}
else
{
    Console.WriteLine("fail");
}
```

No parentheses, and the whole chain closes once. A condition must be a `boolean` — there is no
truthiness to learn.

### Choosing a value

**Profi-C**

```
string verdict = if score >= 60 then "pass" else "fail";
```

**C#**

```csharp
string verdict = score >= 60 ? "pass" : "fail";
```

The ternary is absent because `a ? b : c` has no reading-aloud form. The `else` is required — an
`if` expression with no `else` has nothing to yield when the condition fails.

### Switching

**Profi-C**

```
switch grade
    case 'A':
    case 'B':
        Console.WriteLine("well done");
    case 'C':
        Console.WriteLine("passed");
    default:
        Console.WriteLine("see me");
end switch
```

**C#**

```csharp
switch (grade)
{
    case 'A':
    case 'B':
        Console.WriteLine("well done");
        break;
    case 'C':
        Console.WriteLine("passed");
        break;
    default:
        Console.WriteLine("see me");
        break;
}
```

**There is no fallthrough, so there is no `break` to forget.** Labels stack to handle two values
alike, which is the only thing fallthrough was reliably used for. A `switch` over an enumeration
that omits a member is a warning.

C# switches on far more: type patterns, property patterns, ranges, `when` guards, and a switch
*expression* yielding a value. Profi-C switches on a constant. See
[§9](#10-where-c-does-it-better).

---

## 6. Optionals, sets, and fractions

### A value that may be absent

**Profi-C**

```
string? nickname = Find(id);

if nickname.HasValue()
    Console.WriteLine(nickname.Value());
end if

Console.WriteLine(nickname.Or("stranger"));
```

**C#**

```csharp
string? nickname = Find(id);

if (nickname is not null)
{
    Console.WriteLine(nickname);
}

Console.WriteLine(nickname ?? "stranger");
```

**There is no `null`.** A `string` always holds text and a `string?` is the type that may not, so
reading one the compiler cannot prove is present does not compile. `HasValue()` narrows the
optional for the rest of the guarded block.

This is the one place Profi-C is straightforwardly stricter rather than merely different: C#'s
nullable reference types are warnings over a runtime that still permits null, so the guarantee is
advisory. Profi-C has no null to permit.

### Sets, which are the one collection

**Profi-C**

```
integer[] scores = {90, 85, 77};
scores.Insert(60);

Console.WriteLine(scores[0]);
Console.WriteLine(scores.Count);
```

**C#**

```csharp
List<int> scores = new() { 90, 85, 77 };
scores.Add(60);

Console.WriteLine(scores[0]);
Console.WriteLine(scores.Count);
```

One collection type, ordered and growable, with no array/list distinction to learn.

That is also the loss: C# has arrays, `List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `Queue<T>`,
`Stack<T>` and the interfaces behind them, and a program that wants a key-to-value lookup has
one. Profi-C has a set and no way to write another. See
[§10.2](#112-data).

### A set of sets

**Profi-C**

```
integer[][] grid = {{1, 2, 3},
                    {4, 5, 6}};

Console.WriteLine(grid[1][2]);
```

**C#**

```csharp
int[][] grid = { new[] { 1, 2, 3 },
                 new[] { 4, 5, 6 } };

Console.WriteLine(grid[1][2]);
```

`[]` means "a set of", and what it is said about may be a set already — nothing was added for
this. Rows are sets in their own right and may differ in length. C# also has a rectangular form,
`int[,]`, indexed `grid[row, column]` and fixed in shape; Profi-C does not, and it is on the v2
list. See [matrices.pc](../samples/matrices.pc).

### Exact fractions

**Profi-C**

```
fraction third = 1|3;

Console.WriteLine(third + third + third);
```

**C#**

```csharp
// No equivalent. The nearest is decimal, which is
// still base ten and still cannot hold a third.
decimal third = 1m / 3m;

Console.WriteLine(third + third + third);
```

The Profi-C line prints exactly `1|1`. The C# prints `0.9999999999999999999999999999`. A fraction
is a numerator and a denominator kept reduced, and arithmetic on it is exact — one of the few
places Profi-C has something C# has no form for at all.

---

## 7. When something goes wrong

### Catching

**Profi-C**

```
try
    Console.WriteLine(numbers[10]);
catch IndexOutOfRangeException problem
    Console.WriteLine(problem.Message());
catch Exception problem
    Console.WriteLine("something else");
finally
    Console.WriteLine("always runs");
end try
```

**C#**

```csharp
try
{
    Console.WriteLine(numbers[10]);
}
catch (IndexOutOfRangeException problem)
{
    Console.WriteLine(problem.Message);
}
catch (Exception problem)
{
    Console.WriteLine("something else");
}
finally
{
    Console.WriteLine("always runs");
}
```

The exception names are .NET's on purpose, so what a reader learns here carries over. What they
*say* is written for this language. And `catch Exception` takes less than C#'s: it takes what the
program caused, never a failure in the implementation.

C# has exception filters — `catch (IOException e) when (e.HResult == 32)` — which decide without
catching, so an unmatched exception keeps its original stack. Profi-C has no equivalent.

### Declaring and throwing one

**Profi-C**

```
model InsufficientFunds extends Exception
    public function InsufficientFunds(string message)
        base(message);
    end function
end model

throw new InsufficientFunds("short by 150");
```

**C#**

```csharp
class InsufficientFunds : Exception
{
    public InsufficientFunds(string message) : base(message) { }
}

throw new InsufficientFunds("short by 150");
```

There is no bare `throw` to re-raise: the caught exception is a value with a name, so it is
thrown the way anything is. That costs something — C#'s bare `throw` preserves the original stack
trace where `throw problem;` in C# resets it. Profi-C sidesteps the trap by not having the pair,
rather than by solving it.

---

## 8. Files, names, and text

### Namespaces and reaching another file

**Profi-C**

```
namespace Store.Pricing;

import "models/Product.pc";

using Store.Models;
```

**C#**

```csharp
namespace Store.Pricing;

// No import — the project file lists what compiles.

using Store.Models;
```

`namespace` also takes a block form. `import` is the piece C# has no counterpart to: it names a
file to compile alongside this one, where C# leaves that to the project. `using Standard;` is an
opinion, since Standard is already in scope.

C# has more here: `using static`, aliases (`using Json = System.Text.Json;`), and global usings
that apply across a project. Profi-C has the one form.

### Text

**Profi-C**

```
string greeting = "Hello, {{name}} — you are {{age}}";

string exact = """
    Held as written: a \ and a " and a {{ mean nothing here.
    """;
```

**C#**

```csharp
string greeting = $"Hello, {name} — you are {age}";

string exact = """
    Held as written: a \ and a " mean nothing here.
    """;
```

Double braces rather than single, because a single brace is common in text and doubling it is
rarer than escaping it. A run of three or more quotation marks holds text exactly as written, and
the closing run's indentation comes off every line.

C#'s interpolation carries alignment and format specifiers inline — `{total,10:C2}` right-aligns
a currency in ten columns. Profi-C writes that as a `Format` call.

### Comments and documentation

**Profi-C**

```
# a line comment

##
    a block comment, spanning
    as many lines as needed
##

##
    @summary: What this does.
    @score: what it takes.
    @yields: what it gives back.
##
integer function Doubled(integer score)
    yield score * 2;
end function
```

**C#**

```csharp
// a line comment

/*
    a block comment
*/

/// <summary>What this does.</summary>
/// <param name="score">what it takes.</param>
/// <returns>what it gives back.</returns>
int Doubled(int score) => score * 2;
```

Documentation is labels rather than markup — no angle brackets to balance. **The compiler holds a
doc to what it documents**: naming a parameter that is not there, or a `@yields:` on a function
yielding nothing, is reported. A missing doc never is.

What C#'s markup buys is a toolchain: `<see cref="Other"/>` is a checked link, and the whole
comment compiles to an XML file that IDEs and doc generators read. Profi-C's labels are read by
its own compiler and nothing else, so far.

---

## 9. Where Profi-C does it better

Both languages have these. Profi-C's version is better, and in several rows the difference is
between a mistake the compiler refuses and one it lets you find at run time.

| | Profi-C | C# | Why it is better |
|---|---|---|---|
| **A value that may be absent** | `string?`, and reading one unproven does not compile | `string?`, and reading one unproven is a warning | There is no `null` to permit. C#'s nullable reference types are analysis over a runtime that still allows it, so the guarantee is advice; here it is the type system |
| **Arithmetic that will not fit** | checked always — `OverflowException`, naming the bound | wraps silently unless `checked` is written | The default is the safe one. A C# program that overflows carries on with a plausible wrong number, and nothing says so |
| **Changing a set mid-walk** | refused while compiling (`PC0243`), naming the member that would change it | `InvalidOperationException`, partway through, at run time | Moved from run time to build time, which is the language's whole premise. The same rule reaches a set held under a second name at run time, so nothing slips through |
| **Comparing two values** | `==` is deep and structural, cycle-safe, for nothing | reference identity unless you write `Equals`/`GetHashCode` or reach for a `record` | Two models holding equal contents are equal, without generating anything. A cyclic graph compares without looping forever |
| **A `switch` missing an enumeration member** | a warning naming what was left out | silence | The commonest way a `switch` rots is a member added later. C# says nothing until something behaves oddly |
| **Falling through a case** | impossible; labels stack instead | `break` required on every case, or it will not compile — except where it silently may | Nothing to forget. C#'s rule has an exception for empty labels, which is exactly the case a reader misreads |
| **Closing a block** | `end if`, `end loop`, `end model` — the compiler checks the word | one `}` closes whatever is open | A misplaced `end` is reported where it is written, naming both what was expected and what was found, rather than as a cascade at the end of the file |
| **Loops** | one `loop` opener, five forms, `end loop` closing all but one | `for`, `foreach`, `while`, `do` — four keywords, and `do`'s condition sits past the closing brace | One thing to learn and one shape to recognize |
| **A field against a local** | `this.` is required, so every line touching state says so | a bare name may be either | A reader never has to look elsewhere to know what a name reaches |
| **Reusing a name** | refused if a scope around it is using the name, lambdas included | permitted in a lambda, and permitted for a field | Reading a name is never a search for which one is meant |
| **A field left unset** | an error before the constructor ends (`PC0402`) | a warning for non-nullable references (`CS8618`), and only where nullable analysis is switched on | An error rather than a warning, and on by default rather than opted into |
| **Exact fractions** | `1\|3 + 1\|3 + 1\|3` is exactly `1\|1` | no form at any price — `decimal` is still base ten | The one place Profi-C has a type C# cannot express. A third is a third |

Two of these — checked arithmetic and no `null` — are cases where C# knows the better default and
could not take it without breaking every program ever written. Starting later is worth something,
and this is most of what it is worth.

## 10. Where C# does it better

Both languages have these. C#'s version is better, and the Profi-C choice was made knowing it.

| | Profi-C | C# | The trade |
|---|---|---|---|
| **Accumulating** | `total = total + n;` | `total += n;` | Absent so that a beginner reads one form of assignment rather than eleven. It costs every counter and every accumulator an extra reading of the name |
| **Counting up** | `n = n + 1;` | `n++;` | Same reasoning, and `++` carries the pre/post distinction that is a classic first-year trap. The cost is that the commonest statement in programming is the long one |
| **Reading a member** | `scores.Count` | `scores.Count` | The same. A program cannot declare a property, but the library provides them, and a member that is a value is read rather than called |
| **Matching a shape** | `if x is Dog` then `x as Dog` | `if (x is Dog d)` | No pattern variables, so a test and a cast are written separately. C#'s form cannot get them out of step |
| **Choosing on a value** | `switch` over constants | switch expressions, type and property patterns, `when` guards | Profi-C's switch is a jump table with better defaults. C#'s is a small pattern language, and for anything past equality it is far less code |
| **Returning two things** | a `structure` declared for it | `(int, string)` tuple, deconstructed at the call | Naming the pair is often the better design. When it is not, C# costs a line and Profi-C costs a type |
| **Numbers** | `integer` (64-bit), `real`, `float` (64-bit), `fraction` | `byte` through `ulong`, `float`, `double`, `decimal`, `BigInteger` | One type per idea rather than one per width: no width to choose and no unsigned surprises. `real` is C#'s `decimal` and is what a decimal point means, so money is exact by default rather than by remembering a suffix; `float` is C#'s `double`, named so that binary floating point is asked for. What is given up is wider-than-64 arithmetic and the narrow types a program that has to fit a wire format needs |
| **Ending a scope** | `finally` | `using` on an `IDisposable` | Explicit and visible, versus a construct that guarantees it. A file left unclosed is a bug Profi-C cannot make impossible |
| **Documentation** | `@summary:` labels | `///` XML with checked `cref` links | Nothing to balance and nothing to escape, against a format the whole .NET toolchain already reads |

---

## 11. C# with no Profi-C equivalent

Not "worse" — absent. There is no way to write these, and a program that needs one needs a
different approach.

### 11.1 Shape and abstraction

| C# | What it does | Profi-C |
|---|---|---|
| `interface IShape { double Area(); }` | Gives unrelated types a shared static shape | Only `extends`. A type's shape comes from what it inherits, so two types share one by sharing a parent |
| `class Box<T>` | A type parameterized by another | Nothing user-facing. `integer[]` and `Node?` are parameterized, but only the compiler may write such a type |
| `public int Count { get; set; }` | A member that reads as a value and runs as code | Functions only |
| `this[string key]` | An indexer on a user type | `[ ]` works on sets and strings, and nothing else |
| `static abstract` members, generic math | Constraining a type parameter to arithmetic | Follows generics |
| `partial class` | One type split across files | A type is one declaration |
| `record Point(int X, int Y)` | Value equality, `with`, deconstruction, all generated | Structures already compare by value; the rest is absent |

### 11.2 Data

| C# | What it does | Profi-C |
|---|---|---|
| `Dictionary<K, V>` | Key-to-value lookup | Absent, and the most-missed of these. On the v2 list |
| `HashSet<T>`, `Queue<T>`, `Stack<T>` | Containers with their own guarantees | One set type, ordered and growable |
| `int[,]` | A rectangle, fixed in shape | A set of sets, whose rows may differ in length. On the v2 list |
| `(int, string)` | An anonymous pair | Declare a structure |
| `new { Name = n }` | An anonymous type | Declare a structure |
| `arr[^1]`, `arr[1..3]` | Index from the end, and ranges | `Subset(from, to)` and arithmetic |
| `decimal` | Base-ten money arithmetic | `fraction` is exact but not base ten; `real` is neither |
| `IEnumerable<T>` and LINQ | Query, project, and filter as expressions | Loops |

### 11.3 Control and sequence

| C# | What it does | Profi-C |
|---|---|---|
| `async` / `await` | Work that waits without blocking | Absent entirely. There is no concurrency story |
| `yield return` | A sequence produced as it is read | A function builds a set and hands it back |
| `goto`, `goto case` | Jump to a label | Absent, and not missed |
| `checked` / `unchecked` blocks | Choose overflow behavior per block | Integer arithmetic is always checked |
| `ref` / `out` / `in` parameters | Pass a variable rather than its value | Parameters are by value; a model is a reference already |
| `catch (E e) when (...)` | Decide without catching | A `catch` takes or does not |
| `lock`, `volatile`, `Interlocked` | Coordinate threads | Follows concurrency |

### 11.4 Reaching other code

| C# | What it does | Profi-C |
|---|---|---|
| `static int Twice(this int n)` | Add a member to a type you do not own | Absent |
| `public static Money operator +(...)` | Give a user type an operator | Operators work on built-in types only |
| `event EventHandler Changed` | Multicast subscription | A function value holds one function |
| `[Obsolete]` and reflection | Metadata read at run time | Absent |
| `unsafe`, `Span<T>`, `stackalloc` | Memory without a safety net | Absent, deliberately |
| A NuGet package | Any library at all | The standard library, and nothing else yet |

### 11.5 What is scheduled, and what is not

Three of the absences above are **deferred rather than rejected**: generics, interfaces, and
properties, together with a key-to-value type and rectangular sets. They are the prerequisites
for binding directly to .NET, which is what would turn the last row of
[§10.4](#114-reaching-other-code) from "nothing" into "everything".

The rest are **decisions**. No `null`, no ternary, no fallthrough, no compound assignment, no
`++`, no truthiness, no `unsafe`: each was weighed against a beginner reading a line and getting
it right, and each cost something a working developer would miss. That trade is the language's
whole premise — see [§2 of the summary](language-summary.md#2-reserved-models) and the
[README](../README.md#what-it-is-for).

---

## 12. The same code, a different answer

Every other section here compares two ways of writing a thing. This one is the short list of
places where **the same source is legal in both languages and means something different**. None
of them is reported, because each is a correct program — just not the one a C# reader would
predict.

| Written | Profi-C | C# |
|---|---|---|
| a `break` ending a `switch` case, inside a loop | leaves the **loop** | leaves the switch |
| `2 ^ 3` | `8` — `^` raises to a power | `1` — `^` is exclusive-or |
| `a == b` on two models holding equal fields | `true` — equality is deep and structural | `false` — reference identity, unless `Equals` was written |
| arithmetic that overflows | `OverflowException`, naming the bound | wraps silently, unless `checked` was written |

The first row is the one to watch, because writing it is a C# habit rather than a decision. A
case here cannot fall through, so a `break` has nothing to end and keeps the meaning it has
everywhere else in the language — which means a habitual one at the end of a case quietly ends
the loop instead. Nothing warns about it: it is a legal statement, and the program that meant it
is as ordinary as the program that did not.

A `break` with no loop around it at all is a different matter, and that one **is** refused —
there is nothing it could mean.
