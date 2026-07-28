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

        for integer i = 1 to limit
            total = total + i;
        end for

        yield total;
    end function

    global integer function CountDown()
        integer total = 0;

        for integer i = 10 until 0 step -1
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

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/ProfiC.Cli -- run samples/hello.pfc
```

```bash
dotnet run --project src/ProfiC.Cli -- check samples/tour.pfc
```

`profi-c` also prints each stage of compilation, which is most of why it exists:

```bash
dotnet run --project src/ProfiC.Cli -- tokens samples/hello.pfc
```

```bash
dotnet run --project src/ProfiC.Cli -- ast samples/tour.pfc
```

```bash
dotnet run --project src/ProfiC.Cli -- lower samples/hello.pfc
```

The build produces two identical executables, `profi-c` and the shorter `pfc`. Put either on
your PATH and the commands above become:

```bash
pfc run samples/fizzbuzz.pfc
```

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
| [shapes.pfc](samples/shapes.pfc) | Inheritance, `virtual`/`override`, and dispatch on the runtime type |
| [bank.pfc](samples/bank.pfc) | Exceptions, including one the program declares — and when to yield an optional instead |
| [tour.pfc](samples/tour.pfc) | Every construct in the grammar, once. Not a program; it checks but does not run |

`literals.pfc`, `operators.pfc`, and `comments.pfc` exercise the scanner and likewise declare
no entry point.

Each runnable sample's output is recorded under `tests/ProfiC.Tests/TestData/Running/` and
asserted on every build, so a sample that starts printing the wrong answer fails the suite.

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
```

## License

MIT. See [LICENSE](LICENSE).
