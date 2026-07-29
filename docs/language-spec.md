# The Profi-C Language Specification

**Version 0.1.0 (draft). This document is incomplete by design.**

Sections are written as each part of the language is implemented and covered by tests, so
that the specification never describes more than the compiler does.

Until a section is written, [language-summary.md](language-summary.md) is the best available
description of that area.

| Section | State |
|---|---|
| 0. Overview | Written |
| 1. Lexical structure | Written |
| 2. Tokens and reserved words | Written |
| 3. Types | Not yet written |
| 4. Declarations | Not yet written |
| 5. Expressions | Not yet written |
| 6. Statements | Not yet written |
| 7. Models, structures, enumerations | Not yet written |
| 8. Optionals | Not yet written |
| 9. Functions and closures | Not yet written |
| 10. Exceptions | Not yet written |
| 11. The standard library | Not yet written |
| 12. Execution and entry point | Partly written: what a compilation is made of |

---

## 0. Overview

### 0.1 Identity

| | |
|---|---|
| Name | Profi-C |
| Source file extension | `.pc` |
| Target | CIL, on `net10.0` |
| Implementation language | C# |

### 0.2 Purpose

Profi-C is a **teaching language**. Its goal is to make programming concepts legible to a
beginner while staying faithful to the patterns a C# developer uses daily, so that what a
student learns transfers rather than has to be unlearned.

This purpose is normative, not decorative. Several design decisions were reversed once it was
stated explicitly, and it is the tiebreaker wherever two designs are otherwise comparable.

### 0.3 Design principles

**Pedagogy beats ergonomics.** Where the two conflict, the language chooses the form that
teaches better, even when it is more to type.

**Compile-time errors beat runtime crashes.** Definite assignment, strict optional access, and
qualified block closers all exist to move failures earlier. A program that compiles should
fail in fewer ways.

**Explicit beats implicit.** `this.` is required on member access. A constant must name its
type. A bare identifier reaches only locals and parameters.

**Nothing the language defines is abbreviated.** `boolean`, not `bool`. `enumeration`, not
`enum`. `function`, not `func`. A student should be able to read a keyword aloud and know
what it means without a glossary.

**What the language borrows keeps its source spelling.** The library surface mirrors .NET:
`Math.Sqrt` stays `Math.Sqrt` rather than becoming `Mathematics.SquareRoot`. Profi-C is
expected to gain real .NET imports eventually, and renaming now would create two spellings
for one function.

**Every construct says what it closes.** `end if`, `end while`, `end model`. The compiler
verifies the qualifier and reports a mismatch by name, so a beginner who loses track of
nesting is told exactly where.

### 0.4 Relationship to C#

Profi-C compiles to CIL and runs on the CLR, so it shares C#'s runtime, garbage collector,
and calling convention. It matches C# on single inheritance, explicit `virtual`/`override`,
exceptions, overloading, reference semantics for classes, private-by-default members, and
truncating integer division.

The differences that matter most to a C# reader:

- **`yield` means return.** This is the single most dangerous difference, since C# uses
  `yield return` for iterators. In Profi-C it is an ordinary return statement.
- **There is no `null`.** Optionals replace it, and access is strict.
- **`==` is deep by default** on models and sets, comparing structurally with cycle-safe
  bisimulation. `Reference.Equals(a, b)` spells out C#'s default behavior.
- **`this.` is mandatory**, not conventional.
- **Assignment is a statement**, so `if x = 5` is a syntax error rather than a warning.

[language-summary.md](language-summary.md) carries the full comparison table.

### 0.5 Conformance and terminology

An **implementation** is anything that reads Profi-C source: the compiler in this repository,
the interpreter beside it, an editor's language server, or something written by someone else
entirely. A **conforming** implementation is one that obeys every rule stated here.

This document both explains the language and states its rules, and the words below are how
the two are told apart. They carry the meanings given in RFC 2119, and appear only where a
rule is being stated:

| Word | Meaning |
|---|---|
| **must**, **must not**, **required**, **shall** | Absolute. An implementation that does otherwise does not conform. |
| **should** | A recommendation. An implementation may do otherwise, having weighed the consequences. |
| **may** | Optional. Either choice conforms. |

Every other sentence is explanation and requires nothing. Where explanation and rule appear
to disagree, the rule governs and the explanation is at fault.

A **diagnostic** is a message a conforming implementation produces about a source program.
Diagnostics carry an identifier of the form `PC` followed by four digits, a severity, and a
source span. Two severities exist: **error**, which prevents compilation, and **warning**,
which does not.

Identifiers are stable from v1 onward: one that has been published keeps its meaning, and one
that is withdrawn is not reissued. Before v1 they may be renumbered freely, since nothing
depends on them yet.

### 0.6 Versioning

This document describes **v1**. Features named as deferred are not part of v1 and a
conforming v1 implementation must reject them:

- Generics, interfaces, and properties (v2)
- `out` and `ref` parameters, indexers, `params`, operator overloading, and extension
  methods (v3)
- `async`, `await`, and `Task` (v4)
- Direct binding to arbitrary .NET types (v5)

Deferred is not rejected. Each is expected to arrive as an additive change.

**Boxing is refused rather than deferred.** A value type inherits `Model`'s members but never
converts to `Model`, in this or any later version. See section 3.

---

## 1. Lexical structure

### 1.1 Source files

A source file is Unicode text. The conventional extension is `.pc`.

A **line terminator** is a carriage return followed by a line feed, or a line feed alone.
Each counts as one terminator. A carriage return not followed by a line feed is whitespace
but does not begin a new line. A file with no terminator in it has one line.

Positions reported in diagnostics are one-based in both line and column. A tab advances the
column by one.

### 1.2 Whitespace

Space, tab, carriage return, line feed, and any other Unicode whitespace character separate
tokens and are otherwise insignificant. Profi-C is not layout-sensitive: indentation carries
no meaning, and no construct is terminated by a line break.

### 1.3 Comments

Comments are delimited by words rather than by symbols.

The word `comment` begins a **line comment**, which runs to the next line terminator.

```
comment this runs to the end of the line
```

The word `comment` followed by `begin`, **on the same line**, opens a **block comment**,
which ends at the next occurrence of `end` followed by `comment`.

```
comment begin
    as many lines as needed
end comment
```

Both delimiters are matched only as whole words, so an identifier such as `commentary` is
not a comment. Reaching the end of the file inside a block comment is an error, reported at
the opener.

Three consequences follow from comments being scanned as words:

- If `begin` does not appear on the same line as the `comment` that precedes it, the
  construct is a line comment and `begin` is code.
- Neither `end` nor `comment` alone closes a block comment. Only the two in sequence do.
- The scanner does not read string literals while skipping a comment, so the closing pair
  closes the block wherever it occurs, including inside quotation marks. **A block comment
  cannot contain its own closing phrase.**

Comments produce no tokens.

### 1.4 Identifiers

An identifier begins with a **Unicode letter or an underscore**, and continues with letters,
decimal digits, and underscores. So `count`, `_count`, `max_score`, `item2`, and `café` are
all identifiers.

Identifiers are case-sensitive, so `Model` and `model` are different words — and since one of
them is reserved, they are different *kinds* of word.

An identifier may not be one of the reserved words in section 2, nor `comment`. Adjacency to
an underscore is enough to make a word ordinary rather than reserved: `model_` and
`comment_text` are identifiers.

This follows C#, minus its rarer allowances. Combining marks and format characters are not
identifier characters, `\u` escapes may not be written inside a name, and there is no
verbatim `@name` form for using a reserved word as an identifier.

### 1.5 Literals

**Integer literals** are one or more decimal digits. Leading zeros are permitted and
insignificant.

**Real literals** are digits, a full stop, then digits. Digits are required on *both* sides:
`1.0` is a real, while `1.` is an integer followed by a full stop, which is what makes member
access on a number possible.

**Fraction literals** are digits, a vertical bar, then digits, and denote an exact rational:
`22|7`, `1|3`. Digits are required on both sides here too, so `a|b` is not a fraction.

**Character literals** are a single character between apostrophes: `'A'`. Exactly one
character is required, so both `''` and `'ab'` are errors.

**String literals** are a sequence of characters between quotation marks: `"text"`. A string
literal may not span a line terminator; an unterminated one is reported at its opening quote.

**Boolean literals** are the reserved words `true` and `false`.

### 1.6 Escape sequences

Character and string literals may contain these escapes, and no others:

| Escape | Meaning |
|---|---|
| `\n` | line feed |
| `\t` | tab |
| `\r` | carriage return |
| `\0` | null |
| `\\` | backslash |
| `\"` | quotation mark |
| `\'` | apostrophe |
| `\u####` | the character with the given four-digit hexadecimal code |

An escape outside this set is an error, as is a `\u` not followed by four hexadecimal digits.
An escape counts as one character for the purpose of the character-literal rule above.

The set matches C#, so an escape a student learns here works unchanged there.

---

## 2. Tokens and reserved words

### 2.1 Reserved words

Profi-C has **54** reserved words. None may be used as an identifier.

```
abstract     and          as           base         begin        boolean
break        case         catch        character    constant     continue
default      each         else         end          enumeration  extends
false        finally      for          fraction     function     global
if           in           integer      is           let          model
namespace    new          not          or           override     protected
public       real         sealed       step         string       structure
switch       then         this         throw        to           true
try          until        using        virtual      while        yield
```

`comment` is reserved in addition to these, but never produces a token: it is recognized
before tokenizing and what follows it is skipped.

Words a C# author might expect to be reserved and which are **not**: `private`, `static`,
`null`, `void`, `return`, `class`, `interface`, `enum`, `struct`, `var`, `do`, `foreach`,
`const`, `bool`, `int`. Members are private by default, so `public` and `protected` opt out
rather than `private` opting in; `global` fills the role of `static`; there is no `null`; and
nothing the language defines is abbreviated.

### 2.2 Operators and punctuation

```
+   -   *   /   %   ^
==  !=  <   >   <=  >=
=   =>  ?   :   |
(   )   {   }   [   ]
,   ;   .
```

Scanning is longest-match, so `<=` is one token rather than `<` followed by `=`, and `=>` is
one token rather than `=` followed by `>`. No operator is longer than two characters.

`?` is the optional type suffix. `:` ends a `case` label. `=>` introduces an expression
lambda. `|` separates the parts of a fraction literal. `^` raises to a power.

**`^` is exponentiation, not exclusive-or.** Profi-C has no bitwise operators. In C# the same
symbol is a bitwise operation, where `10 ^ 2` evaluates to 8, so the meaning does not carry
across.

The boolean operators are the reserved words `and`, `or`, and `not`, not symbols.

There is **no** ternary conditional, no compound assignment (`+=` and its family), and no
increment or decrement (`++`, `--`). The role of the ternary is filled by the
`if ... then ... else` expression; the others are written out in full.

Two of these absences are diagnosed at different stages.
`++` and the compound assignments have no possible reading in Profi-C — there is no unary
`+`, and `=` can never follow an arithmetic operator — so they are rejected while scanning.
`--` is different: unary `-` *does* exist, so `x--1` is a well-formed subtraction of negative
one and **must remain valid however it is spaced**. Distinguishing that from an intended
decrement requires knowing whether an operand follows, which is a grammatical question, so
`--` scans as two `-` tokens and any diagnostic about it comes from the parser.

### 2.3 End of file

Every token stream ends with a single end-of-file token. It carries no text and occupies a
zero-width position just past the final character.

### 2.4 Diagnostics from this stage

| Identifier | Reported when |
|---|---|
| `PC0001` | A character appears that begins no token |
| `PC0002` | A string literal is not closed before a line terminator or the end of the file |
| `PC0003` | A character literal is not closed |
| `PC0004` | A character literal does not hold exactly one character |
| `PC0005` | A block comment is not closed before the end of the file |
| `PC0006` | A character sequence is used that is an operator in C# and has no reading in Profi-C |
| `PC0007` | An escape sequence is not recognized |
| `PC0008` | A Unicode escape is not followed by four hexadecimal digits |

Scanning never stops at the first error. Each of these has a defined recovery, so a file
containing several mistakes reports all of them in one pass and still yields a usable token
stream.

## 3. Types

*Not yet written.* Will cover the base types, the `[]` set and `?` optional suffixes, the
value and reference split, conversions, and definite assignment.

**`Model` is the root of every type**, values included, so every type
inherits `ToString()` and `Equals()` from one place. Inheriting those members does not make a
value type *convertible* to `Model`. Assigning a structure or an enumeration to a
`Model`-typed variable is a compile error, and **this is permanent** — that conversion is
boxing, which the language does not have.

C# `ref struct` types have exactly this shape: in the `object` hierarchy, not boxable. The
guarantees are therefore permanent rather than provisional — no assignment allocates
invisibly, and two copies of one value can never compare unequal by reference.

Neither generics nor .NET interop require boxing to be added later. .NET generics are
reified, so a set of structures stores them inline; and a library wrapper may box internally
when calling a .NET method that takes `object`, but that is an implementation detail of the
call rather than a conversion the language admits.

## 4. Declarations

*Not yet written.* Will cover variables, `constant`, fields, functions, and visibility.

## 5. Expressions

*Not yet written.* Will cover the nine precedence levels, `is` and `as`, the
`if ... then ... else` expression, lambdas, and collection literals.

**`^` is the only arithmetic operator whose two sides are not the same
kind of thing.** Everywhere else the operands unify — adding an integer to a real makes both
real. An exponent instead counts how many times the base is multiplied, so it stands on its
own, and the result follows the base:

| Base | Exponent | Result | |
|---|---|---|---|
| `integer` | `integer` | `integer` | `2 ^ 10` is `1024`, not `1024` rendered as a real |
| `fraction` | `integer` | `fraction` | exact; a negative exponent inverts, so `(1\|2) ^ -3` is `8\|1` |
| anything else | | `real` | including any `fraction` exponent |

A whole exponent counts multiplications, so the base's own type survives it. Any other
exponent means a root — raising to `m/n` is the nth root of the mth power — and the root of a
rational is usually irrational, so the answer is a real. `9 ^ 1|2` is `3`, and `16 ^ 3|4`
is `8`.

This is the only place a `fraction` widens to a `real` without being asked. Elsewhere the two
never convert implicitly, because an exact answer is available and the program states which
form it wants; a root has no exact rational form to preserve.

An `integer` raised to a negative power has no whole answer. Where the exponent can be seen
while compiling this is an error; where it cannot, as with a variable, it throws at run time,
exactly as dividing by a variable that turns out to be zero does.

**A collection literal takes its element type from what is expected of it, where anything
expects one.** In a variable or field initializer, an assignment, or a
`yield`, each element is checked against the wanted element type and converts on its own — so
a set of shapes may be written as the several kinds of shape it holds, and
`integer?[] xs = {1, 2}` wraps each element. Where nothing says what is wanted, as in
`let xs = {a, b};`, the elements must already agree on one type.

This is the same principle as C# array initializers. The element type is never inferred from
a common ancestor: two unrelated models share only `Model`, which no value type converts to.

## 6. Statements

*Not yet written.* Will cover block structure and the qualified `end`, the two `for` forms,
`switch`, and `try`.

**Neither `for` form writes a type for the variable it binds.** A range loop counts, and counting is done with integers, so
`for i = 1 to 10` has no type to write and writing one is an error; a `for each` takes its
element's type from the sequence. Both are fixed by the construct rather than inferred from a
value, which is why neither needs `let`. A range loop's bounds and step must themselves be
integers.

**An expression statement may not begin with `(` or `-`.** A construct's body has no opening token, so a
condition ends at the first token that cannot continue an expression — and those two can,
which would otherwise let a condition swallow the first statement of its own body. The
restriction applies only to a bare expression statement; `(x as Dog).Value()` remains legal
as an assignment's right side, as an argument, after `yield`, and within a condition. See
[grammar.ebnf](grammar.ebnf) for the full reasoning.

## 7. Models, structures, and enumerations

*Not yet written.* Will cover inheritance, constructors, virtual dispatch, deep equality,
value-typed structures, and enumerations.

## 8. Optionals

*Not yet written.* Will cover `HasValue`, `Or`, and `Value`, and the narrowing rules that
make optional access strict.

**Once an optional has been narrowed, a member its underlying type declares wins over the
optional's own member of the same name.**

An optional has exactly three members — `HasValue`, `Or`, and `Value` — and a model may
declare a method of the same name. `Temperature.Value()` returning the degrees is one such.
`reading.Value()` on a `Temperature?` then has two possible readings: unwrap the optional, or
call the model's method.

Narrowing settles it. Inside the guard the receiver is a `Temperature`, so every member call
on it is the `Temperature`'s:

```
Temperature? reading = new Temperature(21.5);

comment No guard, so this is still an optional: Value() unwraps it.
let t = reading.Value();            comment t is a Temperature
Console.WriteLine(t.Describe());

if reading.HasValue()
    comment Narrowed, so this is a Temperature: Value() is the model's.
    let degrees = reading.Value();  comment degrees is a real
    Console.WriteLine(reading.Describe());
end if
```

Anything the narrowed type does not declare still falls back to the optional's members, so
writing `HasValue()` on a narrowed optional keeps working. Only a name the underlying type
claims for itself is taken from the optional.

The question does not arise before narrowing. An optional exposes its own three members and
nothing else, so the underlying type's members are unreachable until presence is proven:

```
Temperature? t = new Temperature(21.5);

let unwrapped = t.Value();          comment the optional's Value; unwrapped is a Temperature
Console.WriteLine(unwrapped.Value());   comment now the model's; 21.5

let d = t.Describe();               comment PC0306: a Temperature? has no member 'Describe'
```

The two names are in scope together only after narrowing, which is where the rule above
applies.

## 9. Functions and closures

*Not yet written.* Will cover function types, overload resolution, capture, and name
resolution.

## 10. Exceptions

*Not yet written.* Will cover `try`, `catch`, `finally`, `throw`, and the built-in hierarchy.

## 11. The standard library

*Not yet written.* Will cover the built-in models and the curated .NET wrappers.

**`Fraction.Create(numerator, denominator)`** builds a `fraction` from
two integers. A fraction literal is two numerals fixed when the program is written, so this
is the only way to make one from values that exist only while it runs. The result is an
ordinary fraction — reduced, with its sign carried on the numerator. A denominator of zero is
rejected while compiling where it can be seen, exactly as `1 / 0` is, and throws
`DivideByZeroException` where it cannot.

Note the two spellings: `fraction` is the type and a reserved word; `Fraction` is the model
beside it, holding what a fraction needs that is not a member of one.

**`Console.Write` and `Console.WriteLine` accept a
value of any type**, and behave as in C#: only the second ends the line. Neither is an
overload set; both are compiler-known, and the compiler chooses how to render the value from
its static type. **`ToString()` is inherited from `Model` by every type, values included**,
and is `virtual`. Calling it on a value type does not box. Defaults: a structure prints field
by field, an enumeration prints its member name, a model prints its type name.

## 12. Execution and entry point

*Not yet written.* Will cover program structure and `Program.Main`.

**`Program` may be declared exactly once in a compilation, and must be
`global model Program` containing `Main`.** This differs from `Model`, `Exception`, `Console`,
and `Reference`, which may not be declared at all — every program must declare `Program`, but
may not declare a second one, and may not use the name for an ordinary model.

### 12.1 What a compilation is made of

A compilation is a set of source files. Declarations across all of them share one scope, so a
file may name a type another file declares, in either order, with nothing written to arrange
it. Which files form the set is settled before compiling begins, and there are two ways to
settle it.

**A source file names its folder.** Compiling `bookshelf/Program.pc` compiles it together with
every other `.pc` directly in `bookshelf`, except those that declare `Program`. A file that
declares `Program` is a program; a file that does not is shared code available to all of them.
A folder may therefore hold several programs, each seeing the same shared code and none seeing
the others. The rule does not descend into subfolders.

**A project file names files and folders.** A `.pcp` lists what a build is made of, across as
many folders as it names:

```
comment A storefront, spread across folders.

project Storefront
    source Program.pc
    source models
    source pricing
end project
```

A `source` naming a folder takes every `.pc` directly inside it and does not descend, so a
nested folder is named by its own `source` and what a project builds can be read off the file.
Paths are relative to the project file and are written with forward slashes on every platform.
A `comment` line, and any blank line, is ignored.

A project file is not Profi-C. It describes a build rather than a computation, nothing in it
is compiled, and its vocabulary is only `project`, `source`, `comment`, and `end project`.

Because `Program` may be declared once in a compilation, a project listing two files that each
declare one is rejected, naming the second.

### 12.2 A name belongs to one type

Two types may not share a name, whether they are written in one file or across several. The
second is rejected, and the message says where the first one is, since a reader looking at the
second cannot see it and may not have the other file open.

Nothing merges them. A type is declared in one place, and there is no implicit partial type: a
name appearing twice is far more often two people writing the same thing than one type
deliberately split, and a language that joined them silently would hide the first case
completely. Whether an explicit `partial` should exist is left open for a later version — it is
a question interoperating with .NET may eventually force, and one that should be answered
deliberately rather than fallen into.
