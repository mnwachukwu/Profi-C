# Profi-C

A teaching language that compiles to CIL and runs on .NET.

The name is a nod to Profisee, the company I work for, which is pronounced "prophecy" — so
Profi-C reads the same way out loud. It is also a pun, because I put "C" in it. Har, har.

## What it is for

Profi-C exists to make programming concepts legible to a beginner while staying faithful to
the patterns a C# developer uses daily, so that what a student learns **transfers** rather
than has to be unlearned.

That goal is load-bearing. Where ergonomics and pedagogy conflict, pedagogy wins:
compile-time errors are preferred to runtime crashes, and explicitness is preferred to
convenience. A few consequences you can see immediately:

- **Every block says what it closes.** `end if`, `end while`, `end model` — and the compiler
  verifies the qualifier, so writing `end while` to close an `if` is an error that names both.
- **There is no `null`.** Optionals written with a trailing `?` replace it, and reading one
  the compiler cannot prove present is a compile error rather than a crash.
- **Nothing is abbreviated.** `boolean`, not `bool`. `enumeration`, not `enum`. `function`,
  not `func`. You should be able to read a keyword aloud and know what it means.
- **`and`, `or`, `not`** instead of `&&`, `||`, `!`.
- **Fractions are exact.** `22|7` is a rational literal, so `1|3 + 1|6` is exactly `1|2` —
  no floating-point drift.
- **Comments are words.** `comment` opens a line comment; `comment begin` opens a block
  closed by `end comment`.

Profi-C is deliberately full-featured rather than minimal. It has single inheritance with
virtual dispatch, structures with value semantics, exceptions, optionals, exact rational
arithmetic, first-class functions with closures, and compile-time definite assignment.

## A taste

```
comment begin
    Profi-C at a glance.
end comment

namespace Examples;

global model Program

    constant integer PassingScore = 60;

    function Main()
        integer[] grades = {100, 95, 72, 40};

        for each grade in grades
            Console.WriteLine(Program.Describe(grade));
        end for
    end function

    string function Describe(integer score)
        yield if score >= Program.PassingScore then "pass" else "fail";
    end function

end model
```

Three things a C# reader should notice. **`yield` means return** — it has nothing to do with
iterators. **`if ... then ... else` is an expression**, filling the role of the ternary, which
Profi-C does not have because `a ? b : c` has no reading-aloud form. And conditions take no
parentheses, because nothing needs them once bodies have no opener.

## More examples

### Hello, World!

A source file contains model declarations and nothing else. The entry point is a
`global model` named `Program` holding a function called `Main`.

```
global model Program
    function Main()
        Console.WriteLine("Hello, World!");
    end function
end model
```

`global model` is Profi-C's spelling of C#'s `static class`: it cannot be instantiated or
extended, which is exactly what a running program wants — there is no such thing as an
instance of it.

A `global model` has global members; in C#, a `static class` must mark each of its members
`static`. So `function Main()` and `global function Main()` mean the same thing here — write
the word if you like it, but it adds nothing.

### Models and inheritance

Members are private by default; `public` and `protected` opt out. `this.` is mandatory —
a bare identifier reaches only locals and parameters, which keeps the distinction between a
local and object state visible rather than hidden.

```
model Shape
    protected string Name;

    public function Shape(string name)
        this.Name = name;
    end function

    public virtual real function Area()
        yield 0.0;
    end function
end model

model Circle extends Shape
    real radius;

    public function Circle(real r)
        base("circle");
        this.radius = r;
    end function

    public override real function Area()
        yield 3.14159 * this.radius * this.radius;
    end function
end model
```

### Optionals instead of null

An optional is written with a trailing `?`. You cannot read one without proving it is there.

```
model Greeter
    global function Greet(string? nickname)
        comment Or supplies a fallback, and does not evaluate it unless it is needed
        Console.WriteLine(nickname.Or("Hello, stranger"));

        comment HasValue narrows the optional inside the guarded block
        if nickname.HasValue()
            Console.WriteLine(nickname.Value());
        end if
    end function
end model
```

### Loops

There is no three-clause `for`. It carried the worst teaching problem in the language: an
increment clause written before the body but executed after it.

```
model Counting
    global integer function SumTo(integer limit)
        integer total = 0;

        for i = 1 to limit
            total = total + i;
        end for

        yield total;
    end function

    global integer function CountDown()
        integer total = 0;

        for i = 10 until 0 step -1
            total = total + i;
        end for

        yield total;
    end function

    global integer function CountLetters(string word)
        integer seen = 0;

        for each letter in word
            seen = seen + 1;
        end for

        yield seen;
    end function
end model
```

`to` is inclusive and `until` is exclusive. The loop variable is fresh on every iteration and
read-only inside the body, which removes the classic closure-capture trap.

## Status

**Profi-C runs.** Programs execute on a tree-walking interpreter; the CIL emitter is next.

| Stage | State |
|---|---|
| Lexer | Complete |
| Parser | Complete |
| Resolver | Complete |
| Type checker | Complete |
| Definite assignment, optional narrowing | Complete |
| Lowering | Complete |
| Interpreter | Complete |
| CIL emitter | Not started |

**The front end is finished.** Source becomes a syntax tree, every name resolves, every
expression has a type, nothing can be read before it holds a value, and an optional cannot be
read at all until presence is proven. All of it reports errors with positions and recovers
rather than stopping at the first mistake.

```bash
dotnet run --project src/ProfiC.Cli -- run samples/hello.pfc
```

The interpreter is not a stopgap. It runs the same lowered tree the emitter will, so once both
exist it stays on as the oracle: where the two disagree about what a program means, the
compiler has the bug.

## Writing and running a program

You need the .NET 10 SDK and a clone of this repository. There is no installer yet — one
arrives once the compiler emits assemblies. Until then you build the tool from source, which
takes one command.

### 1. Build the tool

```bash
dotnet publish src/ProfiC.Cli.Alias -p:PublishProfile=dist
```

That writes two identical executables into `dist/`: `profi-c`, and the shorter `pfc`. Either
name works everywhere below. (In Visual Studio, right-click **ProfiC.Cli.Alias → Publish** and
pick the `dist` profile — it does the same thing.)

### 2. Put `dist` on your PATH

For the current terminal only, from the repository root:

```powershell
$env:PATH = "$PWD\dist;$env:PATH"
```

```bash
export PATH="$PWD/dist:$PATH"
```

To make it permanent, add the same line to your PowerShell `$PROFILE` or your `~/.bashrc`.
Adding the folder to your account's PATH through the system dialog also works — but note that
an already-running terminal, editor, or file manager keeps the environment it started with, so
open a new window afterwards.

Prefer to skip PATH entirely? Every command below also works as
`dotnet run --project src/ProfiC.Cli -- run <file>`, straight from the repository root.

### 3. Write a program

This is the smallest legal Profi-C program. It compiles, runs, and does nothing:

```
global model Program
    function Main()
    end function
end model
```

A source file contains model declarations and nothing else — there is no top-level code. The
entry point is a function called `Main` inside a `global model` named `Program`, which is
Profi-C's spelling of C#'s `static class`. Members of a `global model` are already global, so
writing `global function Main()` is allowed but adds nothing.

Save something worth watching as `hello.pfc`, anywhere you like:

```
global model Program
    function Main()
        Console.WriteLine("Hello, World!");

        for i = 1 to 5
            Console.WriteLine(i + " squared is " + (i * i));
        end for
    end function
end model
```

### 4. Run it

```bash
pfc run hello.pfc
```

`run` checks the program and then executes it on the interpreter. Nothing runs until
everything checks, so a program that reaches execution has already been proved free of every
mistake the front end can see. No file is produced — the CIL emitter is what will change that.

To check without running:

```bash
pfc check hello.pfc
```

Errors come back all at once rather than one per run, with positions, in the format editors
already parse. A file with several mistakes in it reports like this:

```
scratch.pfc(4,27): error PFC0330: 'Count' is a function, so it has to be called: write 'Count()'.
scratch.pfc(7,27): error PFC0400: 'total' is used here before it has been given a value.
scratch.pfc(10,27): error PFC0303: '+' is not defined for an integer? and an integer.
```

Three mistakes, three messages, one run — and each caught by a different part of the compiler.

### Seeing the machinery

The remaining commands print each stage of compilation, which is most of why the tool exists:

```bash
pfc tokens hello.pfc
```

```bash
pfc ast hello.pfc
```

```bash
pfc lower samples/sorting.pfc
```

`lower` is the interesting one — it shows the simplified tree the interpreter actually walks,
with `for each` already rewritten into an index loop and every implicit conversion made
explicit.

### One thing to remember

`dist` holds a **copy** of the tool from when you published it. If you change the compiler's
own source, re-run step 1 or `pfc` will keep running the old build. While working on the
compiler itself, `dotnet run --project src/ProfiC.Cli -- run <file>` cannot go stale.

## Documentation

| Document | What it is |
|---|---|
| [docs/language-spec.md](docs/language-spec.md) | The normative specification. Grows section by section as each is implemented and tested |
| [docs/language-summary.md](docs/language-summary.md) | A condensed reference and a full **comparison to C#** |
| [docs/grammar.ebnf](docs/grammar.ebnf) | The formal grammar and the operator precedence table |

The specification is written section by section as each part of the language is implemented
and covered by tests, so it never describes more than the compiler actually does. Sections 1
and 2, the lexical rules and the token table, are complete. Until a later section lands, the
summary is the best description of that area.

## Building

Requires the .NET 10 SDK.

There is no `global.json`, deliberately. The build uses whichever SDK is newest on your
machine, so that a break caused by a new SDK shows up as a break rather than being hidden
behind a pin nobody remembers to revisit.

```bash
dotnet build
```

```bash
dotnet test
```

Much of the suite compares against recorded files — token streams, syntax trees, what a
program printed, and how a failing program failed. After a change that is meant to alter one
of those, re-record them and read the diff:

```bash
PROFIC_UPDATE_GOLDEN=1 dotnet test
```

To build and run Profi-C programs rather than the compiler itself, see
[Writing and running a program](#writing-and-running-a-program) above.

## Samples

Every one of these runs. Each is a complete program, and each is there to show one thing.

| Sample | What it is for |
|---|---|
| [hello.pfc](samples/hello.pfc) | The smallest legal program |
| [fizzbuzz.pfc](samples/fizzbuzz.pfc) | Range loops and an if/else-if chain |
| [fibonacci.pfc](samples/fibonacci.pfc) | The same sequence written recursively and iteratively, side by side |
| [primes.pfc](samples/primes.pfc) | The Sieve of Eratosthenes; sets used as a workspace |
| [sorting.pfc](samples/sorting.pfc) | Insertion sort, and why `and` short-circuiting matters |
| [binary-search.pfc](samples/binary-search.pfc) | **Optionals.** Yields `integer?` rather than a `-1` nobody checks |
| [fractions.pfc](samples/fractions.pfc) | **Exact rationals.** `1\|3 + 1\|3 + 1\|3` is exactly 1; the same sum in `real` is not |
| [runtime-fractions.pfc](samples/runtime-fractions.pfc) | Building fractions from values with `Fraction.Create`, when literals will not do |
| [standard-library.pfc](samples/standard-library.pfc) | Everything the language provides without declaring anything |
| [shapes.pfc](samples/shapes.pfc) | Inheritance, `virtual`/`override`, and dispatch on the runtime type |
| [bank.pfc](samples/bank.pfc) | Exceptions, including one the program declares — and when to yield an optional instead |
| [tour.pfc](samples/tour.pfc) | Every construct in the grammar, once. Not a program; it checks but does not run |

`literals.pfc`, `operators.pfc`, and `comments.pfc` exercise the scanner and likewise declare
no entry point.

Each runnable sample's output is recorded under `tests/ProfiC.Tests/TestData/Running/` and
asserted on every build, so a sample that starts printing the wrong answer fails the suite.

### Samples that fail on purpose

`samples/negatives/` is the other half. A language is defined as much by what it turns away as
by what it accepts, so these are held to the same standard: each file gathers a group of
related mistakes and explains the lesson in its comments, and a reader sees the mistake and
the message side by side.

Programs the compiler rejects:

| Sample | Mistakes |
|---|---|
| [csharp-habits.pfc](samples/negatives/compile/csharp-habits.pfc) | `&&`, `\|\|`, `!`, `+=`, `++`, `**`, and a typed range counter |
| [optionals.pfc](samples/negatives/compile/optionals.pfc) | Using an optional without proving it holds something |
| [definite-assignment.pfc](samples/negatives/compile/definite-assignment.pfc) | Reading a variable before it has a value; a constant with none |
| [types.pfc](samples/negatives/compile/types.pfc) | Types that do not mix, and a division by a literal zero |
| [members.pfc](samples/negatives/compile/members.pfc) | A function used as a property, an instance member reached through its type, a call that yields nothing |
| [blocks.pfc](samples/negatives/compile/blocks.pfc) | An `end` that closes the wrong construct |

Programs that compile and then fail, because the answer depends on a value the compiler cannot
see:

| Sample | Failure |
|---|---|
| [divide-by-zero.pfc](samples/negatives/runtime/divide-by-zero.pfc) | `DivideByZeroException` — a divisor that arrived in a variable |
| [index-out-of-range.pfc](samples/negatives/runtime/index-out-of-range.pfc) | `IndexOutOfRangeException` — one index past the end |
| [empty-optional.pfc](samples/negatives/runtime/empty-optional.pfc) | `EmptyOptionalException` — `Value()` on an optional that turned out empty |
| [overflow.pfc](samples/negatives/runtime/overflow.pfc) | `OverflowException` — a factorial too large for an integer |
| [runaway-recursion.pfc](samples/negatives/runtime/runaway-recursion.pfc) | Recursion with no base case |
| [uncaught-exception.pfc](samples/negatives/runtime/uncaught-exception.pfc) | A declared exception no catch clause matches |

How each one fails is recorded under `tests/ProfiC.Tests/TestData/Negatives/` and asserted on
every build, holding the wording as well as the outcome. A compile sample must be rejected
with the diagnostics recorded for it; a runtime sample must compile with no diagnostics at all
and then fail with the message recorded for it — one that stops compiling is testing something
else and belongs under `compile/` instead.

## Repository layout

```
src/
  ProfiC.Compiler/     lexer, parser, semantic analysis, lowering, CIL emitter
  ProfiC.Runtime/      the value types a program uses: fraction, set, deep equality
  ProfiC.Interpreter/  runs the lowered tree
  ProfiC.Cli/          the profi-c command
  ProfiC.Cli.Alias/    pfc, the short name for the same command
tests/
  ProfiC.Tests/
docs/
samples/               .pfc programs
  negatives/           programs that are wrong on purpose
```

## License

MIT. See [LICENSE](LICENSE).
