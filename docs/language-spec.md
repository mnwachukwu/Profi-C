# The Profi-C Language Specification

**Version 0.1.0 (draft).**

Sections are written as each part of the language is implemented and covered by tests, so
that the specification never describes more than the compiler does.

### How this document relates to the compiler

Everything described here is implemented and runs, with two exceptions, each marked where it
appears:

- **Namespaces** parse but do not scope. A type declared inside one is reached without
  qualification, exactly as though the namespace were not written. See §12.3.
- **`using`** parses and is otherwise ignored, because there is nothing yet for it to make
  reachable. `import`, which does a different job, works. See §12.1.

Profi-C currently runs on a tree-walking interpreter (`pc run`). A CIL back end is planned
and does not exist; nothing in this document depends on which of the two executes a program,
and where a rule is enforced — while checking or while running — is stated wherever it
matters.

Three files sit beside this one and answer different questions. [grammar.ebnf](grammar.ebnf)
gives the surface syntax as a set of productions; it is written for people and is not read by
the compiler, which parses by hand-written recursive descent.
[language-summary.md](language-summary.md) is the short tour for someone arriving from C#.
This document is the normative one: where they disagree, this is right.

| Section | Covers |
|---|---|
| 0. Overview | Identity, purpose, design principles, conformance |
| 1. Lexical structure | Source files, comments, identifiers, literals, escapes |
| 2. Tokens and reserved words | The 55 words, the operators, end of file |
| 3. Types | Base types, suffixes, function types, values and references |
| 4. Declarations | Variables, constants, fields, functions, visibility |
| 5. Expressions | Precedence, `is` and `as`, the `if` expression, literals |
| 6. Statements | Blocks, the qualified `end`, both loops, `switch` |
| 7. Models, structures, enumerations | Inheritance, dispatch, value semantics, equality |
| 8. Optionals | `HasValue`, `Or`, `Value`, and narrowing |
| 9. Functions and closures | Function types, lambdas, capture |
| 10. Exceptions | `try`, `catch`, `finally`, `throw`, the built-in hierarchy |
| 11. The standard library | The built-in models and what they provide |
| 12. Execution and entry point | Compilations, `Program.Main`, namespaces |
| A. Diagnostics | Every identifier the compiler reports |

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

**A reserved word may be used as a name by writing `@` before it**: `@end`, `@base`, `@to`.
The mark is no part of the name — `@end` names `end` — and it is the only place an identifier
may begin with something other than a letter or an underscore.

This exists because several reserved words are ordinary things to call a variable. `end` pairs
with `start`, `base` pairs with `height`, `to` pairs with `from`, and `each` and `step` are
natural names for what a loop is given. Freeing them by renaming the keywords would cost the
vocabulary and still leave the rest taken, so the language keeps its words and hands one back
on request.

An `@` before a word that is not reserved does nothing, and is reported as such. An `@`
followed by no name at all is an error.

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

Profi-C has **55** reserved words. A name may take one back by writing `@` in front of it —
`@end`, `@step` — which is the only place a name may begin with something other than a letter.

```
abstract     and          as           base         begin        boolean
break        case         catch        character    constant     continue
default      each         else         end          enumeration  extends
false        finally      for          fraction     function     global
if           import       in           integer      is           let
model        namespace    new          not          or           override
protected    public       real         sealed       step         string
structure    switch       then         this         throw        to
true         try          until        using        virtual      while
yield
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
=   ?   :   |
(   )   {   }   [   ]
,   ;   .
```

Scanning is longest-match, so `<=` is one token rather than `<` followed by `=`. No operator
is longer than two characters.

`?` is the optional type suffix. `:` ends a `case` label. `|` separates the parts of a
fraction literal. `^` raises to a power.

**There is no `=>`.** A lambda's body follows `yield`, the word every other function uses to
say what it produces, so `(integer n) yield n + 1` is read with vocabulary already learned.
Writing `=>` or `->` reports `PC0006` and names the word to use instead.

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

### 2.4 Recovery

Scanning never stops at the first error. Each lexical diagnostic — `PC0001` through `PC0010`,
listed in [Appendix A](#appendix-a-diagnostics) — has a defined recovery, so a file containing
several mistakes reports all of them in one pass and still yields a usable token stream.

Two recoveries are worth naming, because they change what the parser then sees.

**An unterminated string ends at the newline, not at the end of the file.** A missing close
quote almost always means a missing quote on that line, and scanning to the end of the file
would turn one real error into a hundred spurious ones. A partial token is emitted so the
parser keeps its footing.

**A C# operator with no reading here stands in for the Profi-C one.** `x += 1` scans as
`x = 1` and reports `PC0006`; `=>` before a lambda's body scans as `yield`. The statement
keeps its shape, so the parser reports nothing further and the one message carries the
rewrite.

## 3. Types

### 3.1 The base types

Six types are built in and spelled as reserved words.

| Type | Holds | Literals |
|---|---|---|
| `integer` | A whole number, 64 bits, signed | `0`, `42`, `-7` |
| `real` | A floating-point number, 64 bits | `3.14`, `0.5` |
| `fraction` | An exact rational | `1\|3`, `22\|7` |
| `character` | One Unicode character | `'A'`, `'\n'` |
| `string` | Text, immutable | `"hello"` |
| `boolean` | `true` or `false` | `true`, `false` |

`fraction` is the one with no counterpart in C#. It is a numerator and a denominator held
separately and kept reduced, so `1|3 + 1|3 + 1|3` is exactly `1|1` where the same sum in
`real` is not 1. Arithmetic on fractions is exact; arithmetic on reals is not, and the
language does not pretend otherwise by converting between them quietly.

### 3.2 The two suffixes

Any type takes two suffixes, and they nest:

```
integer[]     a set of integers
integer?      an integer that may be absent
Node?[]       a set of optionals
Node[]?       an optional set
```

`Node?[]` and `Node[]?` are different types and both are legal. Suffixes read left to right,
so the one written last is the outermost.

A **set** is Profi-C's one collection. It is ordered, indexed from zero, grows as you insert,
and holds one type. There is no array/list distinction to learn: `integer[] scores = {};`
then `scores.Insert(60);`. §11 lists its members.

An **optional** is how a value may be absent. There is no `null`; a `Node` always holds a
node, and `Node?` is the type that may not. §8 gives the rules.

### 3.3 Function types

A function type is written the way a declaration is — the result, then `function`, then what
it takes:

```
integer function(integer)             takes an integer, yields an integer
integer function(integer, integer)    takes two, yields one
function(string)                      takes a string, yields nothing
string function()                     takes nothing, yields a string
integer function(integer)?            an optional one
function(string)[]                    a set of them
```

Omitting the result means the function yields nothing. A type that yields nothing and a type
whose result is some "void type" are not two ideas here — there is only the first, and
nothing names the second.

**`Function` is the root of them all.** Every function type descends from it, so a function
may be held without its signature being named:

```
Function held = (integer n) yield n + 1;
Function[] all = { held, Program.Twice, (string s) yield Console.WriteLine(s) };
```

The set is what it is for: a set holds one type, so without a root there is no way to keep
functions of different shapes together. `Function` sits between `Model` and each concrete
signature — a `Function` is a `Model`, and nothing that is not a function reaches it.

It says nothing about what the parameters hold, so a lambda written into one has nothing to
take a type from and writes its own (§9.2). It cannot be called, since calling needs a
signature, and it cannot be extended: a child of it would be a function without being any
particular function.

### 3.4 Values and references

`integer`, `real`, `fraction`, `character`, `boolean`, every structure, and every enumeration
are **value types**: assigning one copies it. Every model, every set, and `string` are
**reference types**: assigning one copies the reference.

`string` is a reference type whose value never changes, so the distinction is not observable
for it — every operation that appears to modify a string returns a new one.

A structure holding a model copies the reference, not the model, so two copies of the
structure see the same model. This is the one place the split is worth stopping over, and it
is why §4.2's `constant` does not yet accept a structure that can reach a model.

### 3.5 Conversions

A conversion is **automatic** where no information is lost and no surprise is possible,
**written out** where a reader should see it happen, and **absent** otherwise.

| From | To | | |
|---|---|---|---|
| `integer` | `fraction` | automatic | |
| `integer` | `real` | automatic | |
| `fraction` | `real` | written out | `f.ToReal()` |
| `real` | `fraction` | written out | `r.ToFraction()` |
| `real` or `fraction` | `integer` | written out | `Math.Floor(x)`, `Math.Ceiling(x)`, `Math.Round(x)` |
| `character` | `integer` | none | |
| an enumeration | `integer` | written out | `member.ToInteger()` |
| `integer` | an enumeration | written out | `n as Suit` |
| any `T` | `T?` | automatic | |
| `T?` | `T` | none — see §8 | |
| `T?` | `U?` | wherever `T` reaches `U` | absence carried across |
| a model | any ancestor of it | automatic | |
| a model | any descendant of it | written out | `shape as Square`, yielding `Square?` |
| a value type | `Model` | none | |
| `string` | `character[]` | automatic, copying | |
| `character[]` | `string` | automatic, copying | |
| `T[]` | `U[]` | only where `T` and `U` are the same type | |

Note that the two spellings do different jobs. **`as` follows inheritance**, so it takes a
model to a descendant and yields an optional, because the value may not be one. Naming a
value type after `as` is rejected by `PC0335`: value types have no inheritance for a cast to
follow, so the question is not one about identity at all. **A conversion between two value
types is a member instead** — `ToReal`, `ToFraction`, `ToInteger` — which cannot fail and so
yields a plain value rather than an optional. An enumeration is the exception in the other
direction: an integer names one of its members, so `n as Suit` is a genuine question with a
"no" answer, and it yields `Suit?`.

Three of these are worth the reasoning.

**Between `fraction` and `real`, neither direction is automatic.** One third as a real is
0.33333333333333331, and one tenth as a fraction is 3602879701896397 over 36028797018963968.
Both are surprising, so the program says which it wants.

**A character is not a small integer.** Treating `'A'` as 65 is a C habit that teaches the
wrong thing about what a character is.

**A value type never converts to `Model`.** That conversion is boxing, which the language does
not have — so `Model m = 1;` is rejected rather than quietly allocating. Every type still
*inherits* `Model`'s members, including `ToString` and `Equals`; inheriting them and
converting to it are different things.

**A set of squares is not a set of shapes.** If it were, a circle could be inserted into it
through the wider name, and the squares would no longer all be squares.

**An optional travels wherever the value it holds would.** A `string?` fits a `character[]?`,
a `Square?` fits a `Shape?`, and an `integer?` fits a `real?` — each staying absent if that is
what it was:

```
string? word = "abc";
character[]? letters = word;      converts, and an absent word stays absent
```

This softens nothing. Nothing is unwrapped: what comes out is still an optional, and getting
a plain value out of one still means proving it holds something (§8). The rule is about which
optional a value can be, not about whether it has to be checked.

### 3.6 Type identity

Two written types are the same type when they are built the same way from the same parts.
This matters because set, optional, and function types are constructed on demand rather than
declared, so two mentions of `integer[]` are two objects that must compare equal.

Two **declared** types are never the same type, however alike they look. Two structures with
identical fields in identical order are two types, and a value of one does not fit the other:
a `Point` and a `Size` both holding two integers mean different things, and the compiler
keeps them apart.

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

### 4.1 Variables

A local is declared with its type, or with `let` and a value to take the type from:

```
integer count = 0;
integer count;              declared now, assigned before it is read
let name = "Ada";           the value on the right says what it holds
```

`let` requires an initializer, because there is nothing else for it to learn from. A written
type does not: a variable may be declared and assigned later, and §4.5 says what the compiler
demands in return.

A `let` is not a different kind of variable. It is the same variable with its type worked out
rather than written, and it may be assigned again afterwards.

### 4.2 Constants

`constant` marks a binding that never changes:

```
constant integer maxScore = 100;
global constant real Pi = 3.14159;
```

A constant requires an explicit type — `let` and `constant` do not combine — and an
initializer the compiler can fold (`PC0320`, `PC0321`). Assigning to one afterwards is
`PC0205`.

The permitted types are `integer`, `real`, `character`, `boolean`, `fraction`, `string`,
enumerations, and structures whose fields cannot reach a model or a set. Every one of those
is a type where an unchanging binding already means an unchanging value, so `constant` is
deep on everything it accepts rather than shallow. Models and sets are rejected by `PC0322`
for exactly that reason: `config.node.value = 5` would otherwise change something through a
constant.

### 4.3 Fields

A field is declared inside a model or structure, and is **private unless it says otherwise**:

```
model Account
    integer balance;                    private
    public string owner;                readable and writable from anywhere
    protected integer limit;            and by anything extending Account
    global integer opened;              one per program, not one per account
end model
```

There is no `private` keyword, because private is what you get by writing nothing. `public`
and `protected` opt out of it. `global` is what other languages call `static`: the member
belongs to the type rather than to an instance.

### 4.4 Functions

A function writes its result type before the word `function`, or omits it to yield nothing:

```
integer function Twice(integer n)
    yield n * 2;
end function

function Announce(string what)
    Console.WriteLine(what);
end function
```

Every parameter carries a type. A constructor is a function named for its type and takes no
result:

```
model Account
    public function Account(integer opening)
        this.balance = opening;
    end function
end model
```

Modifiers are `public`, `protected`, `global`, and `virtual` or `override`. §7.2 covers the
last two. **A function that declares a result must reach a `yield` on every path** — `PC0404`
— so a function cannot promise an integer and fall off the end without one. A constructor
must leave every field assigned (`PC0402`).

Functions may be declared among statements, capturing the locals around them. Types may not:
a type introduced by a statement would tie name resolution to statement order, and forward
references contradict that.

### 4.5 Definite assignment

**A variable must be assigned before it is read**, and the compiler proves it rather than
zeroing anything. Two diagnostics say it, because the two cases read differently: `PC0400`
where no path assigns it, and `PC0401` where only some do.

```
integer n;
Console.WriteLine(n);       PC0400: n was never assigned

integer m;
if ready
    m = 1;
end if
Console.WriteLine(m);       PC0401: only one path assigns m
```

The proof runs forward through the program and joins at every merge point, so a variable is
assigned after an `if` only when *both* branches assign it. This is the same analysis C# and
Java run, and it is here for the same reason: an uninitialized read is a bug that a default
value hides rather than prevents.

The same pass reports code nothing can reach, as a warning (`PC0403`).

## 5. Expressions

### 5.1 Precedence

Expressions are parsed by precedence climbing against one table, so adding an operator is
adding a row rather than adding a production. Ten levels, loosest first:

| | Operator | Position | Associativity |
|---|---|---|---|
| 1 | `or` | infix | left |
| 2 | `and` | infix | left |
| 3 | `not` | prefix | right |
| 4 | `==` `!=` | infix | left |
| 5 | `<` `>` `<=` `>=` `is` `as` | infix | left |
| 6 | `+` `-` | infix | left |
| 7 | `*` `/` `%` | infix | left |
| 8 | `-` | prefix | right |
| 9 | `^` | infix | **right** |
| 10 | `(` call, `[` index, `.` member | postfix | left |

Two placements differ from C and are deliberate.

**`not` is looser than comparison**, as in Python, so `not a == b` groups as `not (a == b)`.
The C reading, `(not a) == b`, is almost always a mistake.

**`^` binds tighter than the leading minus**, so `-2 ^ 2` is `-(2 ^ 2)` and `2 ^ 3 ^ 2` is
`2 ^ (3 ^ 2)` — both as written by hand. It is the language's only right-associative infix
operator.

### 5.2 Operators

`+` `-` `*` `/` `%` are arithmetic. `/` on two integers truncates toward zero, so `7 / 2` is
`3` and `-7 / 2` is `-3`. Dividing by a literal zero is rejected while compiling (`PC0324`);
dividing by a variable that turns out to be zero throws `DivideByZeroException`.

`+` also joins strings, and converts the other side when one side is a string.

`and` and `or` are the words; `&&` and `||` report `PC0006` and name the spelling to use.
Both short-circuit. `not` is the word for `!`.

**There is no compound assignment, no increment, and no decrement.** `x += 1`, `x++`, and
`x--` are each reported by name with the rewrite. There is no ternary either — `if ... then
... else` is an expression and does that job.

`^` raises to a power, and is **not** exclusive-or; Profi-C has no bitwise operators. In C#
the same symbol is bitwise, where `10 ^ 2` is 8, so the meaning does not carry across.

### 5.3 Raising to a power

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

### 5.4 Collection literals

A set is written between braces: `{1, 2, 3}`, and `{}` for an empty one.

**A collection literal takes its element type from what is expected of it, where anything
expects one.** In a variable or field initializer, an assignment, or a
`yield`, each element is checked against the wanted element type and converts on its own — so
a set of shapes may be written as the several kinds of shape it holds, and
`integer?[] xs = {1, 2}` wraps each element. Where nothing says what is wanted, as in
`let xs = {a, b};`, the elements must already agree on one type.

This is the same principle as C# array initializers. The element type is never inferred from
a common ancestor: two unrelated models share only `Model`, which no value type converts to.

`let xs = {};` is rejected (`PC0313`): an empty literal with nothing to take a type from
names no type at all.

### 5.5 `is` and `as`

`x is T` asks whether a value is a `T`, and yields `boolean`. `x as T` converts if it can and
yields `T?` — an optional, because the answer may be no.

```
if shape is Square
    Console.WriteLine("square");
end if

Square? maybe = shape as Square;
```

Both take a type name on the right rather than an expression, and both sit at relational
precedence, matching C#.

Where the answer is fixed while compiling, the compiler says so rather than letting the test
run: `PC0334` for a test that is always true, `PC0327` for one that never can be, and
`PC0335` for a cast naming a value type, which has no inheritance for a cast to follow.

### 5.6 The `if` expression

`if ... then ... else` is an expression and produces a value:

```
let label = if score > 50 then "pass" else "fail";
```

The `else` is required — an expression must produce something on every path — and both
branches must agree on a type. This is what Profi-C has instead of a ternary operator, and it
is spelled with the same three words the statement uses.

### 5.7 Other primary expressions

`this` is the current instance; `base` reaches the parent's members. `new T(...)` constructs.
A lambda is written as §9 describes. Parentheses group.

**Assignment is a statement, not an expression.** `if x = 5` cannot be written at all, which
removes the whole family of bugs where `=` was typed for `==`.

## 6. Statements

### 6.1 Blocks and the qualified `end`

**Every construct closes with `end` and the word that opened it** — `end if`, `end while`,
`end for`, `end function`, `end model`. The parser records what opened and rejects a
mismatched closer by naming both (`PC0104`), so a misplaced `end` is caught where it is
written rather than at the end of the file.

**A construct's body has no opening token.** `begin` opens a block, and a block is always an
anonymous scope rather than any construct's body:

```
begin
    integer scratch = 1;
end
```

**Conditions take no parentheses.** `if ready` and `while count < 10`, not `if (ready)`.

### 6.2 Choosing

```
if ready
    Console.WriteLine("go");
else if waiting
    Console.WriteLine("soon");
else
    Console.WriteLine("no");
end if
```

There is no dangling-`else` problem: `else if` belongs to the `if` rather than nesting, and
the whole chain closes with one `end if`.

`switch` compares a value against constant labels. Labels may be grouped, and a group falls
through to its statements only — there is no fall-through from one group to the next, so no
`break` is needed to prevent it:

```
switch suit
    case Suit.Hearts:
    case Suit.Diamonds:
        Console.WriteLine("red");
    case Suit.Spades:
    case Suit.Clubs:
        Console.WriteLine("black");
    default:
        Console.WriteLine("none");
end switch
```

A label must be a constant (`PC0325`) and no two may be the same (`PC0326`). The value being
switched on must be one a case can name (`PC0315`).

**A `switch` over an enumeration that leaves members out and writes no `default` is a
warning** (`PC0337`), naming the ones with no case. This is what makes adding a member to an
enumeration safe: every switch that has to change says so, at the place it has to change,
rather than the new member falling quietly through all of them.

Writing a `default` silences it, because a default handles the rest and saying so is the
point of writing one. Members are compared by the value each carries rather than by name, so
two members naming one value are handled together.

### 6.3 Looping

Two `for` forms and a `while`:

```
for i = 1 to 10          counts 1 through 10
for i = 1 until 10       counts 1 through 9
for i = 10 to 1 step -1  counts down
for each item in items   takes each element in turn
while count < 10         while the condition holds
```

`to` includes its bound and `until` excludes it, which is the distinction other languages
leave to remembering whether `<` or `<=` was written.

**Neither `for` form writes a type for the variable it binds.** A range loop counts, and counting is done with integers, so
`for i = 1 to 10` has no type to write and writing one is an error; a `for each` takes its
element's type from the sequence. Both are fixed by the construct rather than inferred from a
value, which is why neither needs `let`. A range loop's bounds and step must themselves be
integers (`PC0317`), and its counter cannot be assigned to inside the loop (`PC0206`).

**A loop variable is fresh on every turn.** A function made inside a loop closes over that
turn's variable, so three functions made in three turns report three values. This is the trap
that catches people in languages where the variable is shared and every function reports the
last value.

`break` leaves the innermost loop and `continue` goes to its next turn. Neither may appear
outside one.

### 6.4 Other statements

`yield` produces a function's result and ends it. Written bare, it just ends the function,
which is what a function yielding nothing does. There is no `return`: producing a value and
ending are the same act, and one word says it.

`throw` raises an exception, and `try` handles one — §10 covers both.

An **expression statement** is a call or an assignment followed by `;`. Assignment is a
statement rather than an expression, so `if x = 5` cannot be written.

**An expression statement may not begin with `(` or `-`.** A construct's body has no opening token, so a
condition ends at the first token that cannot continue an expression — and those two can,
which would otherwise let a condition swallow the first statement of its own body. The
restriction applies only to a bare expression statement; `(x as Dog).Value()` remains legal
as an assignment's right side, as an argument, after `yield`, and within a condition. See
[grammar.ebnf](grammar.ebnf) for the full reasoning.

## 7. Models, structures, and enumerations

Three ways to declare a type, and the choice between them is what the type *is* rather than
how it is used.

### 7.1 Models

A **model** is a reference type with single inheritance. It is what other languages call a
class.

```
model Shape
    protected string name;

    public function Shape(string name)
        this.name = name;
    end function

    public virtual real function Area()
        yield 0.0;
    end function
end model

sealed model Square extends Shape
    integer side;

    public function Square(integer side)
        base("square");
        this.side = side;
    end function

    public override real function Area()
        yield this.side * this.side;
    end function
end model
```

`extends` names the parent, and there is one. `base(...)` runs the parent's constructor and
`base.Member()` reaches its members. `sealed` forbids extending; `abstract` forbids
constructing and permits a member with no body.

**`this.` is required to reach an instance member.** `name` and `this.name` are not two ways
to write one thing — the first is a local and the second is a field, and the difference is
visible in every line that touches either. This costs five characters and removes the
question of which one a bare name means.

### 7.2 Virtual dispatch

A member is dispatched on the runtime type only where it says so. `virtual` permits
overriding, `override` does it, and both words are required — an override that omits
`override` is rejected rather than silently hiding the parent's member.

```
Shape shape = new Square(3);
Console.WriteLine(shape.Area());     9, from Square
```

### 7.3 Structures

A **structure** is a value type. Assigning one copies it:

```
structure Point
    public integer x;
    public integer y;
end structure

Point a = new Point(1, 2);
Point b = a;
b.x = 99;                    a.x is still 1
```

A structure may not extend anything and nothing may extend it; value types have no
inheritance. It may hold fields, functions, and a constructor. It may not contain itself,
directly or through another structure, since a value that contained itself would have no
size.

A structure holding a model copies the *reference*. Two copies of the structure then see one
model, which is the case §3.4 flags and the reason `constant` does not accept such a
structure.

### 7.4 Equality

`==` on two models compares them **field by field, all the way down** — not by reference.
Two separately built accounts with the same owner and balance are equal.

```
Reference.Equals(a, b)       true only if a and b are the same object
a == b                       true if they hold the same values
```

The comparison handles cycles: a model reachable from itself does not send it into a loop.
Two values are equal when nothing distinguishes them, which is what a reader means by the
word.

Two structures are equal when their fields are, in the same way. **Two different declared
types are never equal**, however alike their fields — a `Point` and a `Size` both holding two
integers are two types, and asking whether one equals the other is a question about values of
different types.

### 7.5 Enumerations

An **enumeration** is a value type naming a fixed set of members:

```
enumeration Suit
    Hearts, Diamonds, Clubs, Spades
end enumeration

enumeration Status
    Active = 1,
    Closed = 2
end enumeration
```

Members take consecutive ordinals from zero unless written. A member is reached through the
type — `Suit.Hearts` — never bare.

An enumeration converts to `integer` with `ToInteger()`, and an integer converts back with
`n as Suit`, which yields `Suit?` because the integer may name no member.

### 7.6 Nesting

A model, structure, or enumeration may be declared inside a model or structure. A nested type
holds no reference to the type it sits inside; it is a type declared in that scope, not an
inner instance. Types may not be declared inside a function body — see §4.4.

## 8. Optionals

**There is no `null`.** A `Node` always holds a node. `Node?` is the type that may not, and
it is a different type, so absence appears in the signature rather than lurking behind every
reference.

### 8.1 The three members

```
integer? found = Program.Search(items, 7);

found.HasValue()      boolean: is there a value?
found.Or(0)           the value, or the given fallback if there is none
found.Value()         the value, throwing EmptyOptionalException if there is none
```

Any value converts to an optional of its own type automatically, so `integer? n = 5;` is
written directly. **The reverse is never automatic** — that strictness is the whole point.
Reading a `T?` where a `T` is wanted is rejected by `PC0329`, whose message names all three
ways out rather than only reporting the mismatch.

`Or` is the one to reach for. `Value` is for when absence is genuinely impossible and you are
willing to say so.

### 8.2 Narrowing

Inside a guard that has proved presence, the optional reads as its underlying type:

```
if found.HasValue()
    Console.WriteLine(found + 1);      found is an integer here
end if
```

The compiler tracks this the same way it tracks definite assignment — forward, joining at
merge points. It also follows the negative case, so an early exit narrows the rest of the
function:

```
if not found.HasValue()
    yield 0;
end if

Console.WriteLine(found + 1);          narrowed for everything after
```

### 8.3 Narrowing settles which member is meant

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

**A function is a value.** It has a type (§3.3), and it can be stored in a variable, held in
a set, passed to another function, and handed back from one.

A function that already has a name is already a value and needs no lambda around it:

```
integer function(integer) tripled = Program.Triple;

Counter counter = new Counter(10);
integer function() advance = counter.Next;
```

A member reached through an instance is that member *bound to that instance*, so calling it
later still knows which one it belongs to.

**A lambda closes over the variables around it.** The function handed back below remembers
`by`, which belonged to the call that made it:

```
integer function(integer) function AdderOf(integer by)
    yield (n) yield n + by;
end function
```

**A loop variable is fresh on every turn**, so a function made inside a loop closes over that
turn's variable rather than a shared one. Three functions made in three turns report three
values, which is the opposite of what the same code does in languages where the variable is
shared.

Overloads are chosen by argument count first, then by exact match, then by what the arguments
can convert to. Two versions reachable only by conversion is a tie, and a tie is reported
(`PC0310`) rather than broken by a rule nobody remembers.

### 9.1 Writing a function as a value

A function value is written in one of two forms, and both say what they produce with `yield`:

```
integer function(integer) increment = (integer a) yield a + 1;

integer function(integer, integer) larger = function(integer a, integer b)
    if a > b
        yield a;
    else
        yield b;
    end if
end function;
```

The first form has one expression for a body. The second is a whole body, closed the way a
declared function is, and may hold as many statements as it needs.

### 9.2 Where a lambda's parameter types go

A lambda's parameter is a bare name where the surrounding code already says what it holds:

```
integer function(integer) increment = (a) yield a + 1;
```

Four things say it, and between them they cover every place a lambda can be written: a
declared type, the element type of a set being built, the parameter of the function being
called, and the result of the function doing the yielding.

```
integer function(integer)[] steps = { (n) yield n + 1 };     comment element type
Console.WriteLine(Program.Apply(numbers, (n) yield n * 2));  comment parameter
yield (n) yield n + by;                                      comment result
```

An optional function type is a target like any other, since the lambda is wrapped on the way
in and what it has to be is the type underneath.

**Where the type is already said, writing it again is reported.** `PC0115` is a warning: the
program says one thing and says it twice, which is the same argument `PC0111` makes about a
range loop's counter.

**Where nothing says it, leaving it out is reported.** `PC0336` names the parameter that has
no type.

The two rules meet with no gap and no overlap, which leaves exactly one place a lambda writes
its own types — a `let`, where nothing on the left says anything:

```
let halve = (integer n) yield n / 2;              comment nothing else says it, so this does
integer function(integer) double = (n) yield n * 2;   comment the declared type says it
```

So a lambda always has exactly one spelling that says nothing twice and leaves nothing unsaid.

Mixing the two forms in one list needs no rule of its own. Each written type is reported for
the same reason it would be alone, and taking the advice leaves a list written one way.
Nothing ever suggests writing the other types out, so the two rules never point in opposite
directions.

A declared function's parameters always carry a type, because a declaration has nothing to
take one from.

## 10. Exceptions

### 10.1 The built-in hierarchy

Eight exception types, and every one descends from `Exception`:

| | Raised when |
|---|---|
| `Exception` | The root. A `catch` naming it takes them all |
| `DivideByZeroException` | Dividing by a value that turned out to be zero |
| `IndexOutOfRangeException` | Indexing a set or string outside it |
| `EmptyOptionalException` | `Value()` on an optional holding nothing |
| `InvalidCastException` | A conversion that could not be made |
| `FormatException` | Text that could not be read as what was wanted |
| `ArgumentException` | An argument a function will not accept |
| `OverflowException` | A result too large for the type to hold |

Every one carries a `Message()`. A name the language can raise is a name a program can catch:
the two come from one list, so nothing can be thrown that cannot be named.

### 10.2 Throwing and catching

```
try
    Console.WriteLine(numbers[10]);
catch IndexOutOfRangeException problem
    Console.WriteLine("out of range: " + problem.Message());
catch Exception problem
    Console.WriteLine("something else");
finally
    Console.WriteLine("always runs");
end try
```

A `catch` names a type and binds a name to the exception. Clauses are tried in order, so the
narrower ones come first. `finally` runs whether or not anything was thrown, and whether or
not it was caught.

`throw` raises one:

```
throw new ArgumentException("balance cannot be negative");
```

**A throw ends a path**, so a function whose every path either yields or throws satisfies
§4.5's rule without a yield after the throw.

### 10.3 Declaring an exception

A program may extend any of them:

```
model InsufficientFunds extends Exception
    public function InsufficientFunds(string message)
        base(message);
    end function
end model
```

Extending is not redeclaring: the eight names above cannot be declared, but they can be
extended, and a declared exception is caught by a `catch` naming any of its ancestors.

**There are no checked exceptions.** A function does not declare what it may throw, and
nothing forces a caller to handle it. That choice follows the same reasoning as the rest of
the language: the alternative teaches people to write `catch` clauses that swallow.

## 11. The standard library

The library is small and is reached without importing anything. Ten names belong to the
language and no program may declare one: `Model`, `Function`, `Exception`, `Console`,
`Reference`, `Math`, `Fraction`, `Random`, `DateTime`, and `Program`. Of these only
`Exception` may be extended; `Program` must be declared, exactly once (§12).

`Model` and `Function` are the two roots (§3.3, §3.4) rather than things to call.

`Random` and `DateTime` are named but carry no members yet. They are reserved so that adding
them later cannot collide with a program that used the name.

### 11.1 On every type

Inherited from `Model` by every type, values included. Calling one on a value does not box.

| | |
|---|---|
| `ToString()` | `string`. Virtual; a structure prints field by field, an enumeration prints its member name, a model prints its type name |
| `Equals(other)` | `boolean`. The deep comparison `==` uses |

### 11.2 On a set

| | |
|---|---|
| `Count()` | `integer` |
| `Insert(value)` | adds at the end |
| `InsertAt(index, value)` | adds at a position |
| `Remove(value)` | `boolean`; removes the first match |
| `RemoveAt(index)` | removes by position |
| `Contains(value)` | `boolean` |
| `IndexOf(value)` | `integer`; -1 if absent |
| `Clear()` | empties it |
| `Subset(start)` | a copy of everything from `start` on |
| `Subset(start, end)` | a copy of the run from `start` up to but not including `end` |

`Subset`'s end is **exclusive**, the reading `until` has, which is what makes
`xs.Subset(0, n)` and `xs.Subset(n)` add back up to the whole set.

**Nothing that yields a set changes one.** `Subset` and the four in §3.2b hand back a new set;
`Insert`, `InsertAt`, `RemoveAt` and `Clear` change the set and yield nothing, and `Remove`
yields only whether it found something. So the two groups are told apart by their result, and
a set you were given is never quietly the set someone else is holding.

The copy is **shallow**, which is the depth the rest of the language uses: assigning a model
copies the reference (§3.4), so a set copied out holds the very same models.

### 3.2b Only on a set of optionals

Four more appear when the element type is an optional, since only there is there anything
empty to drop:

| | |
|---|---|
| `TrimStart()`, `TrimEnd()`, `Trim()` | `T?[]`; drops empties from one or both ends |
| `TrimAll()` | **`T[]`**; drops every empty, anywhere |

`TrimAll` is the one that changes the type. Removing every empty leaves a set where nothing
can be absent, so it yields the underlying type and the caller stops having to unwrap. The
other three take from the ends only, so an empty in the middle survives and the type has to
keep saying so.

The narrower type is safe because the set is a **new one**. `TrimAll` promises that nothing in
*what it hands back* is absent, and since the original is untouched and separate, nothing can
put an empty into it afterwards. Had it filtered in place, the promise would have been about a
set someone else was still holding, and the type would have been a lie waiting to happen.

### 11.3 On a string

| | |
|---|---|
| `Count()` | `integer` |
| `Contains(text)` | `boolean` |
| `IndexOf(text)` | `integer` |
| `Substring(start, length)` | `string` |
| `Insert(text)`, `InsertAt(index, text)` | `string` |
| `Remove(text)`, `RemoveAt(index)` | `string` |
| `ToCharacters()` | `character[]` |
| `Subset(start)`, `Subset(start, end)` | `string`; the run, with the end exclusive |
| `Trim()`, `TrimStart()`, `TrimEnd()` | `string`; whitespace goes |
| `Trim(text)`, and the same for the other two | `string`; any of that string's characters go |
| `Trim(characters)`, and the same for the other two | `string`; any character in the set goes |

A string never changes, so each of these returns a new one.

The three trims each take nothing, a string, or a set of characters. The set form is the one
to reach for when the characters were worked out rather than typed.

**`Substring` and `Subset` both cut a run out, and differ in their second argument**:
`Substring(start, length)` takes how many, `Subset(start, end)` takes where to stop. Whichever
number you have to hand is the one to write. Both give back a `string`, because a run of a
string is a string — the same rule `Subset` follows on a set, where a run of one is a set.

### 11.4 On a value of a particular type

| | |
|---|---|
| `optional.HasValue()`, `Or(fallback)`, `Value()` | §8 |
| `fraction.ToReal()` | `real` |
| `real.ToFraction()` | `fraction` |
| `enumeration.ToInteger()` | `integer` |
| `exception.Message()` | `string` |

### 11.5 The standard models

| | |
|---|---|
| `Console.Write(value)` | writes, no newline |
| `Console.WriteLine(value)` | writes and ends the line |
| `Console.Read()` | `string?`; absent at end of input |
| `Reference.Equals(a, b)` | `boolean`; identity, which is what `==` deliberately is not |
| `Math.Pi`, `Math.E` | `real`. **Values, so written without `()`** |
| `Math.Sqrt(x)`, `Math.Cbrt(x)` | `real` from a `real` |
| `Math.Root(x, degree)` | `real`; the roots with no name of their own |
| `Math.Pow(base, exponent)` | `real`; `^` is the operator form |
| `Math.Log(x)` | `real`. **The NATURAL logarithm** — see below |
| `Math.Log(x, base)`, `Math.Log10(x)`, `Math.Log2(x)` | `real` |
| `Math.Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan` | `real` from a `real`, in radians |
| `Math.Atan2(y, x)` | `real`; takes the two sides, so it knows the quadrant |
| `Math.Abs(x)` | the type it was given — `integer`, `real`, or `fraction` |
| `Math.Min(a, b)`, `Math.Max(a, b)` | the type they were given |
| `Math.Floor(x)`, `Math.Ceiling(x)`, `Math.Round(x)` | `integer`, from a `real` or a `fraction` |
| `Math.Factorial(n)` | `integer`; overflows past 20 |
| `Fraction.Create(numerator, denominator)` | `fraction` |
| `Fraction.Create(whole)` | `fraction`; a whole number over one |

**`Math.Log` of one number is the natural logarithm** — log to base `e`, what mathematicians
write as `ln`. That is what C#, Java and C all mean by the name, and Profi-C means it too, so
a program moved between them gives the same answer. For base ten, write `Math.Log10(x)`, or
`Math.Log(x, 10)`. This is the one place in the library where the obvious reading of a name
is not the right one, and it is spelled this way because the alternative — agreeing with the
guess and disagreeing with every other language — is worse.

**A root, a power and a logarithm leave the rationals**, so all of them answer in reals
whatever they were given: the square root of a fraction is usually irrational. Everything else
has a version for each number the language has, because an answer that arrives as a `real`
cannot be counted with and a `fraction` that widens to one stops being exact.

### 11.5a How far a real answer can be trusted

`Sqrt` is required by IEEE 754 to be correctly rounded, so it gives the same answer on every
machine. **The rest of the transcendental members are not, and may differ in the last bit
between one machine and another.** That is true of C, C#, Java and Python alike: each defers
to the arithmetic library the platform ships, and those libraries are permitted to disagree by
a fraction of an ulp.

Two guarantees are made against that:

**A root of an exact power is exact.** `Math.Cbrt(27.0)` is `3`, and `Math.Root(32.0, 5.0)` is
`2`, on every machine. Where raising the nearest whole number by the degree gives the value
back exactly, that whole number *is* a root of it, so it is used — which is a better answer as
well as the same one everywhere.

**Nothing else is corrected.** `Math.Cbrt(28.0)` is left as the library worked it out. A
program that needs a real answer to be identical across machines should round it to as many
places as it means to claim, which is what saying "to four places" amounts to.

**`Math.Pi` and `Math.E` are values, not functions.** Writing `Math.Pi()` is reported
(`PC0338`), as is naming a function without calling it (`PC0330`) — the two diagnostics are a
pair, so whichever a reader guesses, the compiler says which it is.

**Rounding lands on a whole number**, so each of the three yields an `integer` and can be used
as a count, an index, or a bound. Between them they are the three honest ways from a `real` to
an `integer`, which is why no single `ToInteger` exists: it would have to pick one of the three
silently, and which one is the question being asked.

A half goes **away from zero** — `Math.Round(2.5)` is `3` — the rule taught in school, rather
than .NET's default of rounding to the even neighbor.

`Console.Write` and `Console.WriteLine` take a value of **any** type, and behave as in C#:
only the second ends the line. Neither is an overload set; both are known to the compiler,
which chooses how to render the value from its static type.

**`Fraction.Create(numerator, denominator)`** builds a `fraction` from
two integers. A fraction literal is two numerals fixed when the program is written, so this
is the only way to make one from values that exist only while it runs. The result is an
ordinary fraction — reduced, with its sign carried on the numerator. A denominator of zero is
rejected while compiling where it can be seen, exactly as `1 / 0` is, and throws
`DivideByZeroException` where it cannot.

Given one integer, `Fraction.Create(n)` reads it as a whole number over one. An integer
already widens to a fraction wherever one is wanted, so this earns its place only where
nothing says a fraction is wanted: `let f = 3;` holds an integer, and
`let f = Fraction.Create(3);` holds `3|1`.

Note the two spellings: `fraction` is the type and a reserved word; `Fraction` is the model
beside it, holding what a fraction needs that is not a member of one.

### 11.6 How a value prints

A set prints its elements between braces, separated by a comma and a space, and a structure
prints its fields **in the order they were declared**.

**Where values sit beside one another, a character and a string are quoted as each is written
in source.** Without it a delimiter cannot be told from the same characters inside a value:
`{a, b}` would be what a set of one string holding a comma printed, and what a set of two
strings printed. So `{"a, b"}` and `{"a", "b"}` are distinct, and `"a, b".ToCharacters()`
prints `{'a', ',', ' ', 'b'}`.

A value printed on its own is not quoted, since nothing sits beside it to be confused with:
`Console.WriteLine("plain")` prints `plain`. A quote inside quotes is left as it is, which
reads a little oddly and is much the smaller of the two problems.

## 12. Execution and entry point

A program is run with `pc run`, which checks it and executes it:

```
pc run hello.pc                 one file, with the shared code beside it
pc run bookshelf/Program.pc     the same rule across a folder — see §12.1
pc run app.pcp                  a project file listing what to build
pc check app.pcp                check without running
pc tokens hello.pc              the token stream
pc ast hello.pc                 the tree
```

Every command takes a **file**, never a folder — a folder is reached by naming a file in it,
which §12.1 explains. The extension may be omitted: `pc run hello` finds `hello.pc`, and asks
for the extension only where both a `.pc` and a `.pcp` of that name exist.

**`Program` may be declared exactly once in a compilation, and must be
`global model Program` containing `Main`.** This differs from `Model`, `Exception`, `Console`,
and `Reference`, which may not be declared at all — every program must declare `Program`, but
may not declare a second one, and may not use the name for an ordinary model.

**`Main` declares no result, or an integer.** The integer is the program's exit code, which is
what whatever ran it reads to learn whether it succeeded.

The restriction is not a choice about what is useful. Every other function's result has a
purpose because a caller uses it; `Main`'s caller is the operating system, and that boundary
carries exactly one small integer. A result of any other type would be computed and then
dropped, so the program would appear to report something it never reported.

Giving another type a meaning would mean inventing one. A `string` result that printed itself
would be an implicit write that happens in one function and nowhere else. A `boolean` result
would duplicate the integer, inverted, since a shell reads zero as success. A result carrying a
failure is already served: an uncaught exception prints and exits non-zero, which is what a
program that cannot finish should do.

A `Main` that yields nothing exits zero. So does one that declares an integer and is left to
run off its end — except that it cannot be, since a function declaring a result must reach one
on every path.

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

**A file names another file with `import`.**

```
import "shared/Tally.pc";
```

An import brings exactly one file, and whatever that file imports in turn — an imported file
must be able to compile, and it cannot if what *it* names is left out. It does not apply the
folder rule at the destination, so no file arrives unnamed by someone.

The path is read relative to the file that wrote the import, and is written with forward
slashes on every platform. A path from the root of a disk is permitted but **warned about**:
it resolves only on the machine that wrote it.

**A file reached more than one way is compiled once.** Importing what the folder rule already
found, or what another file already imported, says nothing — it is one file, not two. A
duplicate is two *different* files declaring the same type, which is reported where the second
declaration is.

`import` and `using` do different jobs and neither does the other's. **An import decides which
files are compiled and affects no name; a using decides which names are reachable unqualified
and brings in no file.** Which to reach for follows the scale of what is wanted: one file, an
import; a group of related types, a namespace; a whole build across folders, a project.

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

### 12.3 Namespaces, as they stand

A namespace is written in either of two forms:

```
namespace Shapes;              file-scoped: everything after it belongs to Shapes

namespace Shapes               block: closes with "end namespace"
    model Circle
    end model
end namespace
```

**Neither form scopes anything today.** Both parse, and a `using` parses, but a type declared
inside a namespace is reached by its bare name exactly as though the namespace were not
written, and a `using` is read and ignored. Nothing is rejected that should be, and nothing is
accepted that will later be rejected on the strength of the namespace alone.

What is settled about the design and not yet built: namespaces nest; a file may hold one
file-scoped namespace or several block ones, but not both forms at once; `using` and `import`
directives come above any namespace; and a nested namespace whose name repeats an enclosing
one is legal but warned about, since `Shapes.Shapes.Circle` is more often a mistake than an
intent.


---

## Appendix A. Diagnostics

Every identifier the compiler reports. Identifiers are stable: once assigned, one is never
reused for a different rule, so a link or a note written today keeps its meaning.

Warnings do not block compilation; everything else does.

### PC0000 to PC0099

| Identifier | | Reported when |
|---|---|---|
| `PC0001` | error | Unrecognized character |
| `PC0002` | error | Unterminated string literal |
| `PC0003` | error | Unterminated character literal |
| `PC0004` | error | Malformed character literal |
| `PC0005` | error | Unterminated block comment |
| `PC0006` | error | Not an operator in Profi-C |
| `PC0007` | error | Unrecognized escape sequence |
| `PC0008` | error | Malformed Unicode escape sequence |
| `PC0009` | warning | This name needs no '@' |
| `PC0010` | error | Nothing to escape |

### PC0100 to PC0199

| Identifier | | Reported when |
|---|---|---|
| `PC0100` | error | Unexpected token |
| `PC0101` | error | Expected an expression |
| `PC0102` | error | Expected a type |
| `PC0103` | error | Expected a name |
| `PC0104` | error | Mismatched block closer |
| `PC0105` | error | Unterminated construct |
| `PC0106` | error | Statement cannot start here |
| `PC0107` | error | Expected a statement |
| `PC0108` | error | Expected a declaration |
| `PC0109` | error | Cannot assign to this expression |
| `PC0110` | error | Type declared inside a function |
| `PC0111` | warning | A range loop's counter has no written type |
| `PC0112` | error | An if expression has no 'else' |
| `PC0113` | error | Too many problems |
| `PC0114` | error | This word is reserved |
| `PC0115` | warning | This parameter's type is already known |

### PC0200 to PC0299

| Identifier | | Reported when |
|---|---|---|
| `PC0200` | error | Name not found |
| `PC0201` | error | Type not found |
| `PC0202` | error | Name already declared |
| `PC0203` | error | Reserved type name |
| `PC0204` | error | Member access needs a receiver |
| `PC0205` | error | Cannot assign to a constant |
| `PC0206` | error | Cannot assign to a loop variable |
| `PC0207` | error | Circular inheritance |
| `PC0208` | error | Cannot extend a sealed model |
| `PC0209` | error | Cannot extend this type |
| `PC0210` | error | Sealed and abstract together |
| `PC0211` | error | Instance member on a global model |
| `PC0212` | error | No entry point |
| `PC0213` | error | Program must be a global model |
| `PC0214` | error | '{0}' used outside a model |
| `PC0215` | error | No parent to reach |
| `PC0216` | error | Cannot extend a built-in type |
| `PC0217` | error | Type already declared |
| `PC0218` | error | Main declares no result or an integer |

### PC0300 to PC0399

| Identifier | | Reported when |
|---|---|---|
| `PC0300` | error | Cannot convert |
| `PC0301` | error | Conversion must be written out |
| `PC0302` | error | Condition must be a boolean |
| `PC0303` | error | Operator not defined for these types |
| `PC0304` | error | Operator not defined for this type |
| `PC0305` | error | Branches of an if expression have different types |
| `PC0306` | error | Member not found |
| `PC0307` | error | Not something that can be called |
| `PC0308` | error | Wrong number of arguments |
| `PC0309` | error | No overload matches |
| `PC0310` | error | Ambiguous call |
| `PC0311` | error | Not something that can be indexed |
| `PC0312` | error | Index must be an integer |
| `PC0313` | error | Cannot infer the type of an empty set |
| `PC0314` | error | Set elements have different types |
| `PC0315` | error | Cannot switch on this type |
| `PC0316` | error | Cannot iterate this type |
| `PC0317` | error | Range loop needs integers |
| `PC0318` | error | This function yields nothing |
| `PC0319` | error | Missing value to yield |
| `PC0320` | error | Constant needs a value |
| `PC0321` | error | Constant value must be known while compiling |
| `PC0322` | error | This type cannot be constant |
| `PC0323` | error | Nothing to infer from |
| `PC0324` | error | Division by zero |
| `PC0325` | error | Case label must be a constant |
| `PC0326` | error | Duplicate case label |
| `PC0327` | warning | This test is always false |
| `PC0328` | error | Cannot be instantiated |
| `PC0329` | error | Optional must be unwrapped first |
| `PC0330` | error | This member is a function |
| `PC0331` | error | Member needs an instance |
| `PC0332` | error | This produces no value |
| `PC0333` | error | Negative exponent on an integer |
| `PC0334` | warning | This test is always true |
| `PC0335` | error | Cannot cast to a value type |
| `PC0336` | error | Parameter needs a type |
| `PC0337` | warning | Not every member is handled |
| `PC0338` | error | This member is a value |

### PC0400 to PC0499

| Identifier | | Reported when |
|---|---|---|
| `PC0400` | error | Used before it is given a value |
| `PC0401` | error | Not given a value on every path |
| `PC0402` | error | Field not given a value |
| `PC0403` | warning | Unreachable code |
| `PC0404` | error | Not every path yields a value |

### PC0600 to PC0699

| Identifier | | Reported when |
|---|---|---|
| `PC0600` | error | Project file not found |
| `PC0601` | error | Project has no header |
| `PC0602` | error | Project has no name |
| `PC0603` | error | Project is not closed |
| `PC0604` | error | Unrecognized project entry |
| `PC0605` | error | Source with no path |
| `PC0606` | error | Source not found |
| `PC0607` | error | Source is not Profi-C |
| `PC0608` | error | Source listed more than once |
| `PC0609` | error | Folder holds no source |
| `PC0610` | error | Project builds nothing |
| `PC0611` | error | Imported file not found |
| `PC0612` | error | Import is not Profi-C |
| `PC0613` | warning | Import names an absolute path |

