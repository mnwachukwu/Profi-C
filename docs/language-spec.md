# The Profi-C Language Specification

**Version 0.1.0 (draft).**

Sections are written as each part of the language is implemented and covered by tests, so
that the specification never describes more than the compiler does.

### How this document relates to the compiler

Everything described here is implemented and runs.

Profi-C currently runs on a tree-walking interpreter (`pc run`). A CIL back end is planned
and does not exist; nothing in this document depends on which of the two executes a program,
and where a rule is enforced — while checking or while running — is stated wherever it
matters.

Three files sit beside this one and answer different questions. [grammar.ebnf](grammar.ebnf)
gives the surface syntax as a set of productions; it is written for people and is not read by
the compiler, which parses by hand-written recursive descent.
[language-summary.md](language-summary.md) is the short tour for someone arriving from C#.
This document is the normative one: where they disagree, this is right.

---

## Contents

- [0. Overview](#0-overview) — identity, purpose, design principles, conformance
  - [0.1 Identity](#01-identity)
  - [0.2 Purpose](#02-purpose)
  - [0.3 Design principles](#03-design-principles)
  - [0.4 Relationship to C#](#04-relationship-to-c)
  - [0.5 Conformance and terminology](#05-conformance-and-terminology)
  - [0.6 Versioning](#06-versioning)
- [1. Lexical structure](#1-lexical-structure) — source files, comments, identifiers, literals, escapes
  - [1.1 Source files](#11-source-files)
  - [1.2 Whitespace](#12-whitespace)
  - [1.3 Comments](#13-comments)
  - [1.4 Identifiers](#14-identifiers)
  - [1.5 Literals](#15-literals)
  - [1.5a Interpolated strings](#15a-interpolated-strings)
  - [1.6 Escape sequences](#16-escape-sequences)
- [2. Tokens and reserved words](#2-tokens-and-reserved-words) — the 63 words, the operators, end of file
  - [2.1 Reserved words](#21-reserved-words)
  - [2.2 Operators and punctuation](#22-operators-and-punctuation)
  - [2.3 End of file](#23-end-of-file)
  - [2.4 Recovery](#24-recovery)
- [3. Types](#3-types) — base types, suffixes, function types, values and references
  - [3.1 The base types](#31-the-base-types)
  - [3.1a Converting between numbers](#31a-converting-between-numbers)
  - [3.2 The two suffixes](#32-the-two-suffixes)
  - [3.2a Sets of sets](#32a-sets-of-sets)
  - [3.3 Function types](#33-function-types)
  - [3.4 Values and references](#34-values-and-references)
  - [3.5 Conversions](#35-conversions)
  - [3.6 Type identity](#36-type-identity)
- [4. Declarations](#4-declarations) — variables, constants, fields, functions, visibility
  - [4.1 Variables](#41-variables)
  - [4.2 Constants](#42-constants)
  - [4.3 Fields](#43-fields)
  - [4.4 Functions](#44-functions)
  - [4.5 Definite assignment](#45-definite-assignment)
  - [4.6 Visibility](#46-visibility)
- [5. Expressions](#5-expressions) — precedence, `is` and `as`, the `if` expression, literals
  - [5.1 Precedence](#51-precedence)
  - [5.2 Operators](#52-operators)
  - [5.3 Raising to a power](#53-raising-to-a-power)
  - [5.4 Collection literals](#54-collection-literals)
  - [5.5 `is` and `as`](#55-is-and-as)
  - [5.6 The `if` expression](#56-the-if-expression)
  - [5.7 Other primary expressions](#57-other-primary-expressions)
- [6. Statements](#6-statements) — blocks, the qualified `end`, both loops, `switch`
  - [6.1 Blocks and the qualified `end`](#61-blocks-and-the-qualified-end)
  - [6.2 Choosing](#62-choosing)
  - [6.3 Looping](#63-looping)
  - [6.4 Other statements](#64-other-statements)
- [7. Models, structures, and enumerations](#7-models-structures-and-enumerations) — inheritance, dispatch, value semantics, equality
  - [7.1 Models](#71-models)
  - [7.2 Virtual dispatch](#72-virtual-dispatch)
  - [7.3 Structures](#73-structures)
  - [7.4 Equality](#74-equality)
  - [7.5 Enumerations](#75-enumerations)
  - [7.6 Nesting](#76-nesting)
- [8. Optionals](#8-optionals) — `HasValue`, `Or`, `Value`, and narrowing
  - [8.1 The three members](#81-the-three-members)
  - [8.2 Narrowing](#82-narrowing)
  - [8.3 Narrowing settles which member is meant](#83-narrowing-settles-which-member-is-meant)
- [9. Functions and closures](#9-functions-and-closures) — function types, lambdas, capture
  - [9.1 Writing a function as a value](#91-writing-a-function-as-a-value)
  - [9.2 Where a lambda's parameter types go](#92-where-a-lambdas-parameter-types-go)
- [10. Exceptions](#10-exceptions) — `try`, `catch`, `finally`, `throw`, the built-in hierarchy
  - [10.1 The built-in hierarchy](#101-the-built-in-hierarchy)
  - [10.1a What a `catch` does not take](#101a-what-a-catch-does-not-take)
  - [10.1b Calling too deeply](#101b-calling-too-deeply)
  - [10.2 Throwing and catching](#102-throwing-and-catching)
  - [10.3 Declaring an exception](#103-declaring-an-exception)
- [11. The standard library](#11-the-standard-library) — the built-in models and what they provide
  - [11.1 The reference](#111-the-reference)
  - [11.2 Two rules the reference relies on](#112-two-rules-the-reference-relies-on)
  - [11.3 How a value prints](#113-how-a-value-prints)
- [12. Execution and entry point](#12-execution-and-entry-point) — compilations, which `Program` starts, namespaces
  - [12.1 What a compilation is made of](#121-what-a-compilation-is-made-of)
  - [12.2 A name belongs to one type](#122-a-name-belongs-to-one-type)
  - [12.3 Namespaces](#123-namespaces)
- [Appendix A. Diagnostics](#appendix-a-diagnostics) — every identifier the compiler reports
  - [PC0000 to PC0099](#pc0000-to-pc0099)
  - [PC0100 to PC0199](#pc0100-to-pc0199)
  - [PC0200 to PC0299](#pc0200-to-pc0299)
  - [PC0300 to PC0399](#pc0300-to-pc0399)
  - [PC0400 to PC0499](#pc0400-to-pc0499)
  - [PC0500 to PC0599](#pc0500-to-pc0599)
  - [PC0600 to PC0699](#pc0600-to-pc0699)
  - [PC9000 and up](#pc9000-and-up)

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
`Math.Sqrt` is `Math.Sqrt`, not `Mathematics.SquareRoot`. Anything Profi-C defines is spelled
out; anything it borrows is written the way its source writes it, so a name a reader already
knows is the name they type.

**Every construct says what it closes.** `end if`, `end loop`, `end model`. The compiler
verifies the qualifier and reports a mismatch by name, so a beginner who loses track of
nesting is told exactly where.

**Nothing depends on where the reader lives.** A program produces the same characters on every
machine, whatever the operating system has been told about language or region. Numbers are
rendered with a full stop for the decimal point and no digit grouping; a moment is written
year first, `2026-07-29`, and a time in twenty-four hours. None of this is the machine's
default — every rendering names the invariant form deliberately.

This matters more for a teaching language than for most. A student comparing output with a
classmate or a book should be comparing the program, not the two computers' idea of how a date
is written; and `07/08/2026` means two different days depending on who is reading it, which is
not something anyone should have to think about while learning what a loop is.

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

[language-summary.md](language-summary.md) sets these out in prose, and
[side-by-side.md](side-by-side.md) carries the full comparison, every construct written both ways.

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
source span. Three severities exist, and what separates them is how much is known about what
the program means. An **error** is reported where the meaning is genuinely unpredictable, and
is the only severity that prevents compilation. A **warning** is reported where the meaning is
clear and unlikely to be what was intended. An **opinion** is reported where the meaning is
clear, intended, and correct, and the language would still write it differently; every opinion
says that some written token has no effect.

Identifiers are stable from v1 onward: one that has been published keeps its meaning, and one
that is withdrawn is not reissued. Before v1 they may be renumbered freely, since nothing
depends on them yet.

**A warning or an opinion may be silenced. An error may not**, ever and by any means, because
the mechanism that quiets a compiler must not be able to make it lie. Nothing about silencing
one stops compilation either: a directive that cannot work is reported and the program builds,
since a feature reached for to make the compiler quieter would be worse than useless if getting
it slightly wrong made the compiler fatal.

A **line comment** carries the directive, in one of three forms. The word after `ignore` is
never absent:

```
# ignore warning
# ignore opinion
# ignore PC0340
```

Each covers the next line carrying code below it, passing over blank lines and further
comments, and covers a diagnostic whose span begins on that line. So a directive above a
`switch` covers `PC0337` however far the switch runs. Each also takes `in file`, which widens
it to every line of the file it is written in, wherever in that file it sits:

```
# ignore opinion in file
```

A project file widens it once more, over every file the project builds
([§12.1](#121-what-a-compilation-is-made-of)):

```text
project Library
    source Program.pc
    ignore opinion
end project
```

**Prose beginning with the word `ignore` is prose.** `# ignore the sign for now` is a remark a
reader writes, and a language that turned it into a diagnostic would be worse than one that
occasionally passes over a typo. A directive is therefore recognized only once `warning`,
`opinion`, or something shaped like an identifier follows; anything else is a comment and draws
nothing. Words after the target are prose too, so a directive may say why it is there. A `##`
block is always prose: a directive nobody sees is not one.

Naming an identifier asserts that a particular diagnostic is there, so one that reaches nothing
reporting it is itself reported (`PC0024`). Naming a severity claims nothing, and stays silent
where there is nothing to silence. An identifier no diagnostic carries is `PC0022`; one naming
a diagnostic that stops compilation is `PC0023`, which exists so that a reader who silences an
error and meets it anyway is told why rather than concluding the mechanism is broken.

**A comment may document a declaration**, and one that does opens with `@summary:`. Both comment
forms carry documentation, since a one-line summary is worth writing on one line:

```
##
    @summary: One person's money, and the rules about taking it out.
##
model Account

    # @summary: Whose account this is.
    string owner;

end model
```

Documentation sits above what it documents, and what it may document is a type, a member of
one, or an enumeration's member. Position alone never makes a comment documentation: a block
above a declaration is an ordinary remark unless it says otherwise, for the same reason a
comment beginning with the word `ignore` is not a directive.

**Every part is a labeled line**, opening with `@`. `@summary:` carries the first part,
`@remarks:` a fuller explanation, and `@yields:` and `@throws:` describe what a function gives
back and what it can raise. Any other name documents the parameter of that name. A line with no
label continues the one above it, and a blank line between them is a paragraph break rather than
an ending — so one label may run to several paragraphs.

The mark is what distinguishes a label from prose that happens to begin with a word and a colon,
which wrapped text frequently does. No rule about where a line sits does that job: one that
reads a label only at the start of a paragraph refuses labels written on consecutive lines, and
one that also accepts a line following a label refuses the label after a wrapped one.

A conforming implementation holds documentation to what it documents. A comment above something
that cannot carry one is `PC0244`; a parameter documented but not taken is `PC0245`; `@yields:`
on a function yielding nothing is `PC0246`; a label written twice is `PC0247`. **A missing doc
is never reported**, since requiring one everywhere is how documentation becomes a tax rather
than a help.

**`@throws:` is carried and not checked.** What a function can raise is not something v1 works
out — there are no checked exceptions and no inference of what a body may throw — so an
implementation has nothing to compare the claim against. A check that guessed would be worse
than none, and one that demanded the label would demand it everywhere. The text is kept and
shown, and its accuracy is the author's. This is a limit of v1 rather than a design principle:
should what a function raises ever become something the language tracks, the claim becomes
checkable and this stops being true.

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

A comment is **marked, not named**. `#` runs to the end of the line:

```
# this runs to the end of the line
let count = 1;      # and may end a line of code
```

`##` opens a **block comment**, which ends at the next `##` — **and takes the rest of that
line with it**:

```
##
    as many lines as needed
##
```

The body is indented under the marks by convention rather than by rule — the compiler reads a
block the same either way. It is worth writing because an editor folds by indentation with
nothing told to it, so a long comment collapses out of the way.

That closing rule settles two things at once.

**Nesting is not an idea that can go wrong, because it is not an idea.** The first `##` after
the opener closes the block, whatever was written between. A comment discussing comment syntax
cannot half-close itself and spill its remainder into the program as code.

**A comment is a line of its own, or the end of a line — never the middle of one.** Since the
closer takes the rest of its line, nothing can follow one and still be code. This is a
judgement about reading rather than a limitation: a comment interrupting an expression breaks
the sentence the expression is trying to be.

A run of marks is a heading rather than an error, since the extra ones are simply comment
text:

```
#############################
#  Everything below is ...   #
#############################
```

A single `#` cannot close a block; only a pair does. A `#` inside a string literal is text, as
the scanner reaches a comment only where a token could begin. Reaching the end of the file
inside a block comment is an error, reported at the opener.

Comments produce no tokens.

### 1.4 Identifiers

An identifier begins with a **Unicode letter or an underscore**, and continues with letters,
decimal digits, and underscores. So `count`, `_count`, `max_score`, `item2`, and `café` are
all identifiers.

**A digit cannot begin one**, and a name written against a number is reported as the single
mistake it is (`PC0021`) rather than read as a number beside a name: nothing in the language
puts two values side by side, so `1each` and `40var` have no reading in which they are two
things.

Identifiers are case-sensitive, so `Model` and `model` are different words — and since one of
them is reserved, they are different *kinds* of word.

An identifier may not be one of the reserved words in section 2. Adjacency to an underscore is
enough to make a word ordinary rather than reserved: `model_` and `models` are identifiers.

**A reserved word may be used as a name by writing `@` before it**: `@end`, `@base`, `@to`.
The mark is no part of the name — `@end` names `end` — and it is the only place an identifier
may begin with something other than a letter or an underscore.

This exists because several reserved words are ordinary things to call a variable. `end` pairs
with `start`, `base` pairs with `height`, `to` pairs with `from`, and `each` is a natural name
for what a loop is given. Renaming every such keyword would cost the vocabulary and still leave
the rest taken, so the language keeps its words and hands one back on request.

Where a keyword is only ever written in one position, a compound word costs nothing and the
plain word is worth more free than reserved. `stepby` is that case, as `shiftleft` and
`shiftright` are: it can appear nowhere but after a range loop's bound, so lengthening it takes
away no clarity and gives `step` back as a name. `each` is not that case, despite only
following `for` — a `foreach` opener would want `end foreach` to close it, and renaming one word
to keep `end` honest would reach every loop in every program.

An `@` before a word that is not reserved does nothing, and is reported as such. An `@`
followed by no name at all is an error.

This follows C#, minus its rarer allowances. Combining marks and format characters are not
identifier characters, `\u` escapes may not be written inside a name, and there is no
verbatim `@name` form for using a reserved word as an identifier.

### 1.5 Literals

**Integer literals** are one or more decimal digits. Leading zeros are permitted and
insignificant.

A `0x` or `0b` prefix writes the same whole number in **hexadecimal or binary**: `0xFF` and
`0b1111_1111` are both 255. Case is not significant in either the prefix or the digits, and a
digit outside the base is reported (`PC0018`) rather than ending the number early. There is no
prefix for a real, since a base names how digits are written and a point is not one of them.

The pairing is with `Format`, which already prints those bases: `n.Format("X")` gives `FF` and
`n.Format("B")` gives `11111111`, and `0xFF` and `0b11111111` are how they are read back.

**Real literals** are digits, a full stop, then digits. Digits are required on *both* sides:
`1.0` is a real, while `1.` is an integer followed by a full stop, which is what makes member
access on a number possible.

A real may also carry an **exponent** — `e` or `E`, an optional sign, then digits — with or
without a point before it. `1.5e3` is 1500.0 and `2e-3` is 0.002. An exponent names a scale
rather than a count, so a literal carrying one is a real whether or not a point was written:
`1e3` is `1000.0`, and `1000` is how the integer is asked for.

**An underscore may separate digits**, in any literal and any base, and means nothing to the
value: `1_000_000`, `0xFF_FF`, `3.141_592`, `1_500|1_000`. It has to sit between digits, since
grouping is all it does, so `1_` is reported (`PC0020`) — and a name may still begin with one,
which is why `_1` is a name and not a number.

**Fraction literals** are digits, a vertical bar, then digits, and denote an exact rational:
`22|7`, `1|3`. Digits are required on both sides here too, so `a|b` is not a fraction.

**Character literals** are a single character between apostrophes: `'A'`. Exactly one
character is required, so both `''` and `'ab'` are errors.

**String literals** are a sequence of characters between quotation marks: `"text"`. A string
literal may not span a line terminator; an unterminated one is reported at its opening quote.

**Boolean literals** are the reserved words `true` and `false`.

**Block string literals** are a sequence of characters between runs of quotation marks:
`"""text"""`. Nothing inside is read: no escape is recognized, no interpolation is looked for,
and a lone quotation mark is a quotation mark. A block may span line terminators.

Because nothing inside is read, this is also the language's verbatim form, and no separate one
exists. Where a block spans lines, the indentation of the closing quotes is removed from every
line, and the line terminators next to each run of quotes are dropped — so a block may sit at
the indentation of the code around it without carrying that indentation into what it holds.
Written on one line, it is exactly what lies between the quotes.

**The delimiter is three quotation marks or more, and the closing run must be the same length
as the opening one.** Any shorter run inside is text, which is what lets a block hold quotes at
all:

```text
"""say "hi" now"""            →  say "hi" now
""""holds """ here""""        →  holds """ here
```

So a block that has to hold three quotes is opened and closed with four, one holding four with
five, and there is no sequence of characters a block string cannot express.

Two lengths that do not match are reported:

- **A run longer than the delimiter** ends the block, with its last quotes closing and the rest
  held: `"""He said "hi""""` is `He said "hi"`. That is almost always what was meant, so it is a
  warning (`PC0015`) rather than an error.
- **A closing run shorter than the delimiter** is text by the rule above, so the block does not
  end there and runs on to consume the rest of the file. The run that was meant as the closer is
  reported (`PC0016`), rather than the opening quotes pages earlier.

A block whose text ends in a quotation mark has no single-line form, since that quote sits
against the closing run and lengthens it whatever the delimiter's length. Putting the closing
run on its own line separates them:

```text
"""
say "hi"
"""
```

### 1.5a Interpolated strings

A string literal may hold expressions, written between **doubled braces**:

```
integer apples = 3;
integer pears = 4;

Console.WriteLine("{{apples}} apples and {{pears}} pears is {{apples + pears}} fruit");
```

**A single brace is ordinary text.** Only a pair opens a hole, so `"a set is {1, 2}"` needs
nothing done to it. This is why the braces are doubled rather than the literal being marked
with a prefix: the cost is paid only where interpolation is used, instead of by every string
that happens to contain a brace. To write a literal pair, escape the first: `"\{{"`.

**A hole holds any expression**, including a call, a conditional, or a string that interpolates
in turn. The scanner counts braces opened inside a hole, so `"{{ {1, 2}.Count }}"` closes at
the right pair.

**A colon says how to write the value.** What follows it is a pattern rather than code, taken
whole to the closing braces:

```text
"to a penny: {{price:F2}}"        →  to a penny: 1234.50
"the date: {{when:yyyy-MM-dd}}"   →  the date: 2026-08-15
```

The patterns are .NET's, unchanged, so what is learned here transfers. A pattern may only be
given where the value answers `Format` — the measured and the dated types do, and asking it of
anything else is `PC0341`. The same patterns are available without a string around them,
through `Format` itself.

**An interpolated string is a `string`**, whatever it holds, and means exactly the
concatenation it looks like: each hole becomes `ToString()`, or `Format(pattern)` where one was
named, and the pieces are joined with `+`.

A block string does **not** interpolate, per [§1.5](#15-literals).

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
| `\{` | opening brace |
| `\}` | closing brace |
| `\u####` | the character with the given four-digit hexadecimal code |

A brace is already ordinary text and needs no escape; `\{` exists only so that a literal pair
can be written without opening a hole ([§1.5a](#15a-interpolated-strings)).

An escape outside this set is an error, as is a `\u` not followed by four hexadecimal digits.
An escape counts as one character for the purpose of the character-literal rule above.

The set matches C#, so an escape a student learns here works unchanged there.

---

## 2. Tokens and reserved words

### 2.1 Reserved words

Profi-C has **63** reserved words. A name may take one back by writing `@` in front of it —
`@end`, `@each` — which is the only place a name may begin with something other than a letter.

```text
abstract     and          as           base         begin        bitwise      boolean
break        case         catch        character    constant     continue     default
delegate     each         else         end          enumeration  extends      false
finally      float        for          fraction     function     if           import
in           integer      internal     is           let          loop         model
namespace    new          not          or           override     protected    public
real         sealed       shared       shiftleft    shiftright   stepby       string
structure    switch       then         this         throw        to           true
try          until        using        virtual      while        xor          yield
```

These are every reserved word, and nothing is reserved outside the list. A comment is marked
rather than named ([§1.3](#13-comments)), so it takes no word away from a program.

Words a C# author might expect to be reserved and which are **not**: `private`, `static`,
`null`, `void`, `return`, `class`, `interface`, `enum`, `struct`, `var`, `do`, `foreach`,
`const`, `bool`, `int`. Members are private by default, so `public` and `protected` opt out
rather than `private` opting in; `shared` fills the role of `static`; there is no `null`; and
nothing the language defines is abbreviated.

### 2.2 Operators and punctuation

```text
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

**`^` is exponentiation, not exclusive-or.** In C# the same symbol is a bitwise operation,
where `10 ^ 2` evaluates to 8, so the meaning does not carry across — the operation it names
there is spelled `xor` here.

The boolean operators are the reserved words `and`, `or`, and `not`, not symbols. So are the
ones that work on bits: `bitwise and`, `bitwise or`, `xor`, `shiftleft` and `shiftright`
([§5.2](#52-operators)).

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

Scanning never stops at the first error. Each lexical diagnostic — `PC0001` through `PC0021`,
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

Seven types are built in and spelled as reserved words.

| Type | Holds | Literals |
|---|---|---|
| `integer` | A whole number, 64 bits, signed | `0`, `42`, `-7` |
| `real` | A number counted in tens, 28 significant digits | `3.14`, `0.5` |
| `float` | Binary floating point, 64 bits | `3.14f`, `1e3f` |
| `fraction` | An exact rational | `1\|3`, `22\|7` |
| `character` | One Unicode character | `'A'`, `'\n'` |
| `string` | Text, immutable | `"hello"` |
| `boolean` | `true` or `false` | `true`, `false` |

**`real` counts in tens, not in binary.** A tenth has no exact binary form, so in a language
where a decimal point means binary floating point `0.1 + 0.2` is not `0.3`. Here it is: the
digits are held as digits. A real stops at its bounds rather than passing into an infinity,
which is the same choice `integer` makes, and it has no value meaning "not a number".

**`float` is binary floating point**, and is in the language to be met rather than avoided. It
is what C, C#, Java and Go spell `float` or `double`, and it keeps every behavior that comes
with that: a tenth that does not round-trip, a division by zero that produces an infinity, and
`Float.NotANumber`, which is not equal to itself.

`fraction` is the one with no counterpart in C#. It is a numerator and a denominator held
separately and kept reduced, so `1|3 + 1|3 + 1|3` is exactly `1|1`, which the same sum in
either `real` or `float` is not.

Which conversions among the four happen on their own and which are written out is
[§3.1a](#31a-converting-between-numbers).

<a id="31a-converting-between-numbers"></a>

### 3.1a Converting between numbers

**A conversion that loses nothing happens on its own.** Everything else is written out.

| from ↓ to → | `integer` | `real` | `float` | `fraction` |
|---|---|---|---|---|
| **`integer`** | — | automatic | `.ToFloat()` | automatic |
| **`real`** | `Math.Round(x)` | — | `.ToFloat()` | automatic |
| **`float`** | `Math.Round(x)` | `.ToReal()` | — | `.ToFraction()` |
| **`fraction`** | `Math.Round(x)` | `.ToReal()` | `.ToFloat()` | — |

`Math.Floor` and `Math.Ceiling` reach an `integer` the same way `Math.Round` does. No single
`ToInteger` exists, because it would have to choose among the three silently and which one is
the question being asked.

Two conversions lose nothing and are still written out, because the answer is surprising rather
than lossy: `fraction.ToReal()`, since a third has no decimal that ends, and
`float.ToFraction()`, since `0.1f` is really `3602879701896397|36028797018963968`.

**Nothing reaches a `float` on its own**, an integer included. Every member of `Math` exists in
a `real` form and a `float` form, so a whole number widening to both would leave `Math.Sqrt(2)`
with two readings and no way to choose.

A `real` becoming a `fraction` is exact but can outgrow one, since a fraction's parts are whole
numbers. Written down, that is `PC0346`; arriving in a variable, it stops when it runs — the
same division `PC0324` draws around dividing by zero.

### 3.2 The two suffixes

Any type takes two suffixes, and they nest:

```text
integer[]     a set of integers
integer?      an integer that may be absent
Node?[]       a set of optionals
Node[]?       an optional set
```

`Node?[]` and `Node[]?` are different types and both are legal. Suffixes read left to right,
so the one written last is the outermost.

A **set** is Profi-C's one collection. It is ordered, indexed from zero, grows as you insert,
and holds one type. There is no array/list distinction to learn: `integer[] scores = {};`
then `scores.Insert(60);`. [§11](#11-the-standard-library) lists its members.

An **optional** is how a value may be absent. There is no `null`; a `Node` always holds a
node, and `Node?` is the type that may not. [§8](#8-optionals) gives the rules.

### 3.2a Sets of sets

`[]` means "a set of", and what it is said about may be a set already. Nothing is added for
this; it falls out of the suffix applying to any type.

```text
integer[][]     a set of sets of integers — a grid
integer[][][]   a set of those — a cube
```

Indexing is the same operation twice. `grid[1][2]` takes row 1, which is a set, and then takes
element 2 of it. A literal nests the same way, `{{1, 2}, {3, 4}}`, and either level may be
written, passed, yielded, and assigned into: `grid[0][1] = 9;` is legal, as is handing a whole
row to a function that takes `integer[]`.

**A grid of sets is not a rectangle.** Every row is a set in its own right, so rows may differ
in length, a row may be replaced by one of another size, and a row handed out and then grown is
grown inside the grid it came from. Squareness is a property of how a particular grid was built
rather than of its type, so anything walking a grid must ask each row its own length rather than
measuring one and assuming. A fixed-shape kind, indexed `grid[row, column]`, is deferred.

`samples/matrices.pc` works through both, and then through what a grid of numbers is for.

### 3.3 Function types

A function type is written with **`delegate`** — the result, then `delegate`, then what it
takes:

```text
integer delegate(integer)             takes an integer, yields an integer
integer delegate(integer, integer)    takes two, yields one
delegate(string)                      takes a string, yields nothing
string delegate()                     takes nothing, yields a string
integer delegate(integer)?            an optional one
delegate(string)[]                    a set of them
```

**Two words, two jobs.** `function` declares a function or makes one on the spot; `delegate`
writes the type of one and does nothing else. Writing `function` where a type belongs is
`PC0117`, which names the fix.

The split is what lets these nest. A result may itself be a function type, and since only
`delegate` may follow a result, each one plainly begins another type rather than a
declaration:

```text
integer delegate(integer) delegate(integer)    takes an integer, yields a function
```

`delegate` builds a type, as `[]` and `?` do, rather than naming one. It is the third such
mark and the only one spelled as a word.

Omitting the result means the function yields nothing. A type that yields nothing and a type
whose result is some "void type" are not two ideas here — there is only the first, and
nothing names the second.

**`Function` is the root of them all.** Every function type descends from it, so a function
may be held without its signature being named:

```
Function held = (integer n) yield n + 1;
Function[] all = { held, (integer a, integer b) yield a + b, (string s) yield Console.WriteLine(s) };
```

The set is what it is for: a set holds one type, so without a root there is no way to keep
functions of different shapes together. `Function` sits between `Model` and each concrete
signature — a `Function` is a `Model`, and nothing that is not a function reaches it.

It says nothing about what the parameters hold, so a lambda written into one has nothing to
take a type from and writes its own ([§9.2](#92-where-a-lambdas-parameter-types-go)). It cannot be called, since calling needs a
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
is why [§4.2](#42-constants)'s `constant` does not yet accept a structure that can reach a model.

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
| `T?` | `T` | none — see [§8](#8-optionals) | |
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

**Every model converts to `Model`**, whether or not it wrote `extends Model`. The two spellings
declare the same model, so writing the implicit thing out changes nothing:

```text
model Thing
end model

Model held = new Thing();           either spelling of Thing reaches Model
```

**A value type never does.** That conversion is boxing, which the language does not have — so
`Model m = 1;`, and the same with a structure or an enumeration member, is rejected rather than
quietly allocating. Every type still *inherits* `Model`'s members, including `ToString` and
`Equals`; inheriting them and converting to it are different things. `string` is a reference
type and converts, needing no boxing to do it.

**A set of squares is not a set of shapes.** If it were, a circle could be inserted into it
through the wider name, and the squares would no longer all be squares.

**An optional travels wherever the value it holds would.** A `string?` fits a `character[]?`,
a `Square?` fits a `Shape?`, and an `integer?` fits a `real?` — each staying absent if that is
what it was:

```text
string? word = "abc";
character[]? letters = word;      converts, and an absent word stays absent
```

This softens nothing. Nothing is unwrapped: what comes out is still an optional, and getting
a plain value out of one still means proving it holds something ([§8](#8-optionals)). The rule is about which
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

```text
integer count = 0;
integer count;              declared now, assigned before it is read
let name = "Ada";           the value on the right says what it holds
```

`let` requires an initializer, because there is nothing else for it to learn from. A written
type does not: a variable may be declared and assigned later, and [§4.5](#45-definite-assignment) says what the compiler
demands in return.

A `let` is not a different kind of variable. It is the same variable with its type worked out
rather than written, and it may be assigned again afterwards.

**A name may not be reused while an earlier one is still visible.** A local, a parameter, a
loop's binding, a lambda's parameter, or a caught exception may not take a name already in
use by a scope around it (`PC0237`):

```text
let value = 1;
begin
    let value = 2;          PC0237: 'value' is already the name of something here
end

let items = {1, 2};
let show = (integer items) yield items;    PC0237, for the same reason
```

Two scopes that cannot see one another are not in conflict, so the same name may be used in
each:

```text
begin
    let value = 1;
end
begin
    let value = 2;          fine — neither can see the other
end
```

Inside a function body, then, a bare name means one thing throughout. That is what allows a
lambda to reach a local of the function around it with nothing marking the reach: there is
nothing the name could be confused with, so a marking would carry no information.

**A local may still carry a field's name.** Fields are never reached by a bare name — that is
what `this.` is for — so the two never compete:

```text
model Box
    public string name;

    public function Show()
        string name = "local";
        Console.WriteLine(name);        the local
        Console.WriteLine(this.name);   the field
    end function
end model
```

The rules differ because the problems differ. A field may be declared in an ancestor model or
in another file, so `this.` tells a reader to stop looking in this function; and forbidding the
overlap would mean adding a field could break methods that have nothing to do with it. A local
in an enclosing scope is always a few lines above, in the same body.

**A bare `_` is a throwaway, and binds nothing.** It stands in for a name the language *obliges*
a program to write, where the program has no use for one. There are three such places, and what
they share is that leaving the name out is not an option:

```text
loop for _ = 1 to 3             count three times, and never ask which
loop each _ in numbers          one line per element, whatever the element is
catch ArgumentException _       the type was the whole answer
```

Both rules above pass over it, because there is nothing to pass over: a throwaway enters no
scope, so several in one body are ordinary rather than a clash, and none of them hides
anything.

```text
loop each _ in numbers
    loop for _ = 1 to 2         neither PC0202 nor PC0237 — nothing was bound
    end loop
end loop
```

**Nowhere else takes one** (`PC0256`), because nowhere else is a name obliged. Any expression is
already a statement, so a value is dropped by writing it on its own — and a throwaway written to
drop the same value spends a line agreeing:

```text
Announce();                     drops what it yields
let _ = Announce();             PC0256: the same thing, one line longer
_ = Announce();                 PC0256, for the same reason
integer _;                      PC0256 — and this one does nothing at all
```

That reasoning reaches a shape the language does not have yet. Destructuring several values at
once would be a fourth place a name is obliged, so a throwaway would belong there for the parts
nobody wants — but not for *all* of them, since a left side that keeps nothing is a call written
the long way round.

Because it binds nothing, **it cannot be read** (`PC0254`), and it cannot name anything that is
reached by writing its name — a field, a function, a type, an enumeration member, a namespace
(`PC0255`).

**A parameter is not a place for one** (`PC0257`), and it is the one receiving position that is
not. Every other is invisible outside the body it sits in; a parameter is part of a signature
somebody else reads to work out what to pass, and is shown to them at every call. A function
with no use for an argument should not ask for one.

Only the bare underscore. `_count` and `_x` are names like any other, read and written like any
other.

### 4.2 Constants

`constant` marks a binding that never changes:

```text
constant integer maxScore = 100;
shared constant real Pi = 3.14159;
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

```text
model Account
    integer balance;                    private
    protected integer limit;            and anything extending Account
    internal integer revision;          and anything in this project
    public string owner;                and anywhere at all
    shared integer opened;              one per program, not one per account
end model
```

There is no `private` keyword, because private is what you get by writing nothing. `protected`,
`internal`, and `public` each widen that, and only one of them may be written on a declaration
(`PC0219`). `shared` is what other languages call `static`: there is one of the member for the
whole program rather than one per instance, and it says nothing about who may reach it.

Reaching a member from further away than it reaches is an error (`PC0339`), reported where the
member is named. See [§4.6](#46-visibility) for what each word means and where a project comes from.

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
    integer balance;

    public function Account(integer opening)
        this.balance = opening;
    end function
end model
```

Modifiers are `public`, `protected`, `internal`, `shared`, and `virtual` or `override`. [§7.2](#72-virtual-dispatch)
covers the last two. **A function that declares a result must reach a `yield` on every path** — `PC0404`
— so a function cannot promise an integer and fall off the end without one. A constructor
must leave every field assigned (`PC0402`), and **a `shared` field is given its value where it is
declared** (`PC0408`) — the same obligation arriving in the only place it can, since a shared
field belongs to the type rather than to any instance and no constructor runs that could fill it
in.

**Two kinds of field are exempt, and for the same reason: they already hold something.** Every
primitive starts at a zero of its own — a counter at nought, a flag at `false`, a string empty,
a fraction at `0|1` — and an optional starts empty, which is a value like any other and is what
makes a self-referential model constructible. Everything else has no such value: a model, a set,
a function or an enumeration left alone would hold nothing, and nothing for those is the null
[§8](#8-optionals) exists to do without. So they are asked for, or written as an optional so
that absence is in the type and the reader is made to prove it away.

Functions may be declared among statements, capturing the locals around them. Types may not:
a type introduced by a statement would tie name resolution to statement order, and forward
references contradict that.

**A function declared among statements is in scope for the whole run it sits in**, not from its
own line onward, so a call may be written above it and two of them may call each other. Where a
declaration sits says where to read it rather than when it exists — the same as for a member.

What that costs is paid by `PC0405`. The locals such a function names come into being in order,
so calling one from above a local it uses would read a place holding nothing yet:

```text
Console.WriteLine(Doubled());       PC0405: 'Doubled' uses 'total', which is not ready
integer total = 7;

integer function Doubled()
    yield total * 2;
end function
```

Asked of the name rather than of the call, since handing the function elsewhere is as good as
calling it. Move the call below what it needs, or move what it needs above the call.

### 4.5 Definite assignment

**A variable must be assigned before it is read**, and the compiler proves it rather than
zeroing anything. Two diagnostics say it, because the two cases read differently: `PC0400`
where no path assigns it, and `PC0401` where only some do.

```text
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

**A local nothing ever reads is reported too** (`PC0409`), as a warning. Reading is what
counts: a local assigned twice and read never has done as little as one nothing mentions again.

```text
integer forgotten = Total();    PC0409: nothing reads forgotten

integer written = 1;
written = 2;                    PC0409 as well — assigning is not reading
```

It is a warning rather than a refusal because the program runs either way. It is worth saying
because a result worked out and then forgotten looks exactly like one meant to be dropped, and
[a throwaway](#41-variables) is how a program says which it is — so `_` is exempt, having
already said nothing will read it.

**A private member nothing reaches is reported the same way** (`PC0410`), for fields and
functions alike. Private is the only visibility this can be asked about: a private member is
seen by its declaring type and no further, so the compilation holding that type holds every use
it can ever have. Anything wider is reachable from code that is not here, and silence about it
would mean nothing.

Three are left out, because none of them is reached by writing its name: a constructor, which
answers to `new`; an overridable function, which answers to whatever the value turns out to be;
and the `Main` a program starts at.

**A statement that does nothing whatever is reported too** (`PC0411`). A statement keeps nothing,
which is how a value is dropped on purpose — but where the value settles while compiling, or is
a bare name, working it out cannot do anything either:

```text
1 + 2;                          PC0411: nothing happens
"hello";                        PC0411, the same
count;                          PC0411, the same
```

**Arithmetic in general is not one of these**, because arithmetic is checked. Written on its own,
`2147483647 * 2147483647` raises an overflow and `1 / zero` raises a division by zero, and a
program may be written to do exactly that. So the test is what the compiler can settle, not what
the line looks like.

**Where the statement is a call that yields something, only the answer goes unheld** (`PC0412`),
and that is an opinion rather than a warning: the call runs and does its job. What it points at
is the function — one that both acts and answers, called for half of itself, is usually two
functions. Where it is not yours to split, keeping the value is the other fix;
`Directory.Delete` answers whether there was a folder to remove, which is worth having.

An editor shows the unused ones faded rather than underlined, keeping the color the name already
had — so a name still reads as the field or the local it is, and reads as one nothing reaches.
A dropped call result is not faded: the call is not spare.

### 4.6 Visibility

Four reaches, narrowest first. A declaration writes at most one of them.

| Written | A member reaches | A type reaches |
|---|---|---|
| nothing | the type that declares it | the project that declares it |
| `protected` | and anything extending that type | — |
| `internal` | and anything in the same project | the project that declares it |
| `public` | anywhere | anywhere |

**The two defaults are one rule applied twice: a declaration with no word belongs to the
smallest thing that could own it.** A member's owner is its type, so silence means private. A
type's owner is its project, so silence means internal. Nothing has to be memorized separately
— what is written down is always a widening of what silence already said.

There is no `private` keyword: private is what writing nothing gets you. Writing `internal` on
a type is legal and says what silence says, which is worth writing where a reader might wonder.

`protected` may not be written on a type (`PC0220`). It means "and anything extending the type
that declares this", which is a sentence about a member; a type has no declaring type, so the
word has nothing to name.

Reaching further than a declaration reaches is an error — `PC0339` for a member, `PC0221` for a
type — reported where the name is written. A constructor is a member like any other, so a
private one is how a type says it makes its own instances.

**Where a project comes from.** A project is a `.pcp` file and the files it lists ([§12.1](#121-what-a-compilation-is-made-of)). A
compilation nobody divided is **one project**, so `internal` reaches everything in it and the
rule costs a single-file program nothing. Projects only start to matter once one references
another, which is exactly when a boundary is worth having: without `internal`, a project
reference would be nothing but a shorter way to list somebody else's folders.

```text
public model Book                       Library, which references Books, may use this
    integer copies;                     Book alone
    internal integer shelf;             anything in Books
end model

model Wording                           nothing outside Books can even name this
end model
```

A file brought in by `import` ([§12.1](#121-what-a-compilation-is-made-of)) belongs to the project of the file that imported it. No
project listed it, and the file that asked for it is the only claim there is.

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

**A `float` is exempt from both.** Dividing one by zero is defined rather than mistaken: it
yields `Float.Infinity`, its negative, or `Float.NotANumber`, which are values the type has and
its own arithmetic produces. Refusing the expression would leave the one type with an answer as
the one type unable to ask. C# draws the line in the same place — `int` and `decimal` refuse it,
`double` answers.

`+` also joins strings, and converts the other side when one side is a string.

`and` and `or` are the words; `&&` and `||` report `PC0006` and name the spelling to use.
Both short-circuit. `not` is the word for `!`.

**There is no compound assignment, no increment, and no decrement.** `x += 1`, `x++`, and
`x--` are each reported by name with the rewrite. There is no ternary either — `if ... then
... else` is an expression and does that job.

`^` raises to a power, and is **not** exclusive-or. In C# the same symbol is bitwise, where
`10 ^ 2` is 8, so the meaning does not carry across — which is why the operation it names
there is spelled `xor` here.

**The operations on bits are written as words**, and take integers on both sides:

```text
flags bitwise and mask      the bits both have
flags bitwise or mask       the bits either has
flags xor mask              the bits exactly one has
flags shiftleft 2           every bit two places up, so the value quadruples
flags shiftright 2          every bit two places down
```

`&` and `|` were not available to be borrowed: `|` already writes a fraction, and adding
punctuation for the rest would have been the only symbol operators in a language that spells
`and`, `or` and `not`. So `bitwise` qualifies the two words that already mean something.
Nothing else claims `xor`, `shiftleft` or `shiftright`, so those stand alone, and a word after
`bitwise` that is not `and` or `or` is reported (`PC0118`).

The three sit on three levels, in C#'s order among themselves — `or` loosest, then `xor`, then
`and` — so `a bitwise or b bitwise and c` groups as `a bitwise or (b bitwise and c)`. A shift
binds tighter than a comparison and looser than arithmetic.

**Two booleans are refused** (`PC0342`) rather than treated as one bit each: `a != b` already
asks whether exactly one of them holds, and the language keeps one spelling for one idea. This
is a deliberate divergence from C#, whose `^` covers both.

**A shift of fewer than zero places, or of 64 or more, is an error** (`PC0343`) — an integer
holds 64 bits and a shift past all of them has nothing left to move. A literal amount is caught
while compiling; one that arrives in a variable raises `ArgumentException`. C# folds the amount
into range instead, so `x << 64` quietly means `x << 0` there.

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

```text
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

```text
let label = if score > 50 then "pass" else "fail";
```

The `else` is required — an expression must produce something on every path — and both
branches must agree on a type. This is what Profi-C has instead of a ternary operator, and it
is spelled with the same three words the statement uses.

### 5.7 Other primary expressions

`this` is the current instance; `base` reaches the parent's members. `new T(...)` constructs.
A lambda is written as [§9](#9-functions-and-closures) describes. Parentheses group.

**Assignment is a statement, not an expression.** `if x = 5` cannot be written at all, which
removes the whole family of bugs where `=` was typed for `==`.

## 6. Statements

### 6.1 Blocks and the qualified `end`

**Every construct closes with `end` and the word that opened it** — `end if`, `end loop`,
`end function`, `end model`. The parser records what opened and rejects a mismatched closer by
naming both (`PC0104`), so a misplaced `end` is caught where it is written rather than at the
end of the file.

**One construct is closed by something other than `end`.** A `loop` with no qualifier is closed
by `until` and its condition ([§6.3](#63-looping)). The word doing the closing carries
information, which is what earns it the exception; nothing else in the language departs from
the rule.

**A construct's body has no opening token.** `begin` opens a block, and a block is always an
anonymous scope rather than any construct's body:

```
begin
    integer scratch = 1;
end
```

**Conditions take no parentheses.** `if ready` and `while count < 10`, not `if (ready)`.

### 6.2 Choosing

```text
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

```text
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

**Every loop opens with `loop`**, and the word after it says which kind:

```text
loop for i = 1 to 10            counts 1 through 10
loop for i = 1 until 10         counts 1 through 9
loop for i = 10 to 1 stepby -1  counts down
loop each item in items         takes each element in turn
loop while count < 10           asks before each turn
loop ... until count == 10      asks after each turn
loop ... end loop               does not ask at all
```

One opener and one closer: every form but `until` closes with `end loop`. That one is the only
construct in the language `end` does not close, because `until` carries the condition and so is
already saying the loop is over.

That regularity is the point. A reader learns that `loop` means something repeats and then asks
one question — which kind — rather than learning three unrelated words and discovering that one
of them has two forms.

`to` includes its bound and `until` excludes it, which is the distinction other languages
leave to remembering whether `<` or `<=` was written.

**Neither counting form writes a type for the variable it binds.** A range loop counts, and
counting is done with integers, so `loop for i = 1 to 10` has no type to write and writing one
is an error; a `loop each` takes its element's type from the sequence. Both are fixed by the
construct rather than inferred from a value, which is why neither needs `let`. A range loop's
bounds and step must themselves be integers (`PC0317`), and its counter cannot be assigned to
inside the loop (`PC0206`).

**`loop ... until` tests after the body, so the body always runs at least once.** That is the
whole reason it exists, and it has a consequence the others do not share: whatever the body
definitely assigns is definitely assigned afterwards, because the first turn is unconditional.
Every other loop may run no times at all.

The condition is written at the bottom because that is where it is tested. Written at the top
it would be indistinguishable from a `loop while` that checks at a different moment, and
nothing in the line would say so.

**A `loop` closed by `end loop` has no condition anywhere**, and is for the case where the
reason to stop is not a question that can be asked at the top or the bottom but something that
happens partway through. Saying that plainly beats writing a condition that is always true and
leaving a reader to work out what it stood in for.

Something inside still has to end it — a `break`, a `yield`, or a `throw`. One with none of the
three is `PC0406`, an **opinion** rather than an error: a program that means to run until it is
stopped from outside is one somebody may legitimately write, and the language cannot tell which
it has.

**The opinion suppresses nothing.** A function that yields a value and holds a loop nothing can
end still gets `PC0404`, because that is not a question about the loop — it is a function
promising a result and having no path that produces one. Both are reported, and they say
different things.

Which way out is written matters to what follows the loop. A `break` leaves the loop, so the
next statement runs. A `yield` or a `throw` leaves the whole function, so nothing falls out of
the bottom at all — which is what lets a function end in one of these and still satisfy the rule
that every path yields.

**A range loop reads its header on every turn.** The starting value is read once, because
there is nothing else it could mean; the bound and the step are read again at the top of each
turn, so `until x` is a condition about `x` as it stands rather than as it was:

```text
integer limit = 10;

loop for i = 1 until limit    three turns: the bound moves down to meet the counter
    limit = limit - 2;
end loop
```

A bound that moves the other way never ends the loop, which is allowed and is the same thing
`loop while true` is. Both are the program saying so.

The step is read at the same moment as the bound, so one turn reads the header once: whatever
decided that this turn runs is what advances to the next. Only the counter is out of reach —
it belongs to the loop and cannot be assigned to (`PC0206`).

This matches a C-style `for`, whose condition and increment are both live:
`for (int i = 0; i < x; i++) x++;` never finishes there either.

**A `loop each` takes its sequence as it stands.** It names a sequence rather than a bound, so
its length is read once, when the loop begins. Modifying the sequence inside its own loop is
refused (`PC0243`) rather than left to mean something subtle.

**A loop variable is fresh on every turn.** A function made inside a loop closes over that
turn's variable, so three functions made in three turns report three values. This is the trap
that catches people in languages where the variable is shared and every function reports the
last value.

`break` leaves the innermost loop and `continue` goes to its next turn. Neither may appear
outside one, and writing one where there is no loop is refused (`PC0407`) rather than given a
meaning it does not have.

**A `switch` is not a loop, and neither word notices it.** A switch runs one arm and stops, so
there is nothing about it for a `break` to end and no next turn for a `continue` to go to. Both
pass straight through to the loop around them:

```text
loop for i = 1 to 3
    switch i
        case 2:
            break;             leaves the loop, not the case
    end switch

    Console.WriteLine(i);      not reached when i is 2
end loop
```

This is the one place the language deliberately reads differently from C#, where a `break` is
required at the end of every case and ends the switch. It is required there because a case falls
through into the next one without it; a Profi-C case never does, so the word was free to keep the
single meaning it has everywhere else. A `break` written at the end of a case out of habit is not
harmless — it leaves the loop — which is why a `break` with no loop around it at all is refused
rather than quietly given one.

### 6.4 Other statements

`yield` produces a function's result and ends it. Written bare, it just ends the function,
which is what a function yielding nothing does. There is no `return`: producing a value and
ending are the same act, and one word says it.

`throw` raises an exception, and `try` handles one — [§10](#10-exceptions) covers both.

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
constructing.

**A parent is finished before a child begins**, which fixes three things about construction:

- **`base(...)` is the first statement in a constructor** (`PC0248`). Statements above it would
  read the parent's fields before the parent had decided what they hold.
- **A constructor reaches its parent's whether or not it says so.** Where nothing is written, the
  one that takes nothing is used — so `base()` with no arguments changes nothing about what runs.
  Where the parent has nothing to choose between, writing it anyway is an opinion (`PC0251`)
  rather than a mistake; where the parent declares several constructors, `base()` says which one
  builds it and nothing is reported.
- **A constructor must be able to reach one** (`PC0250`). Where the parent declares constructors
  and none of them takes nothing, the child has to write `base(...)` — nobody else knows what to
  hand over. A parent that declares no constructor at all takes nothing, so nothing is required.
- **Field initializers run before any constructor body**, nearest type first: a child's starting
  values, then its parent's, then the parent's constructor, then the child's. Which of the two
  sets ran first is observable only through a side effect, and the order is fixed so that it is
  fixed rather than incidental.

**`this` is not available in a field's starting value** (`PC0249`). Nothing is built yet — the
fields hold nothing until their own initializers have run — so a name reached through `this`
would answer with whatever it happened to hold, and which fields had run would depend on the
order they were written in. A field whose value depends on another belongs in a constructor.

All four are C#'s rules, chosen for that reason: construction is a place where a wrong mental
model is expensive, and this is one a reader carries forward rather than unlearns.

**An `abstract` function is declared and left open**, ending at a semicolon where a body would
begin:

```
abstract model Shape
    public abstract real function Area();
end model
```

It closes no block, so it takes no `end function`, and it is asked for no result here — that
is the obligation of whatever writes it. Four rules follow, each with its own diagnostic:

- Only an **abstract model** may carry one (`PC0240`). An instance of a model that could be
  constructed would reach a function nobody wrote.
- A model that **can** be constructed must write every function still open above it
  (`PC0241`), reported once on the model and naming each. An abstract descendant passes the
  obligation down instead; a descendant that writes one discharges it for everything below.
- It carries **no body** (`PC0239`), and a function without `abstract` must have one
  (`PC0238`).

Written with no visibility beside it, an abstract function is **protected** rather than
private. [§4.6](#46-visibility) gives a declaration with no word to the smallest thing that
could own it, and the declaring type is not that thing here — nothing in it writes the
function. The narrowest reach the word admits is the type and everything extending it, so that
is what silence means. `public` and `internal` still say so where they are wanted.

`abstract` is what offers the function for overriding, so `virtual` beside it says nothing
further and is an opinion (`PC0242`).

**`this.` is required to reach an instance member.** `name` and `this.name` are not two ways
to write one thing — the first is a local and the second is a field, and the difference is
visible in every line that touches either. This costs five characters and removes the
question of which one a bare name means.

### 7.2 Virtual dispatch

A member is dispatched on the runtime type only where it says so. `virtual` permits
overriding, `override` does it, and both words are required — an override that omits
`override` is rejected rather than silently hiding the parent's member.

```text
Shape shape = new Square(3);
Console.WriteLine(shape.Area());     9, from Square
```

**The claim `override` makes is checked.** A function marked `override` must find one above it
with the same name and the same parameter types, and that one must have been offered — marked
`virtual`, or an `override` itself, since the word carries down a chain without every link
repeating it. Four ways it can fail, and each is an error:

| Written | Reported |
|---|---|
| `override` matching nothing above | `PC0222` |
| `override` of a function that is not `virtual` | `PC0223` |
| A function redeclaring one above without `override` | `PC0224` |
| An `override` yielding something else | `PC0225` |

An unchecked `override` fails quietly, which is why it is checked: a base function renamed, or
a parameter type that drifted, leaves a function still marked `override` and overriding
nothing. It compiles, it runs, and every call through the base type reaches the base's.

A function differing in **parameter types** is an overload rather than an override, and
overloading across a base and a derived model is ordinary. `PC0222` is what tells the two apart
when a type meant the second and wrote the first.

**`ToString` and `Equals` are inherited from `Model`**, which every model extends whether or not
it wrote `extends`, so both may be overridden with no base named:

```
model Tag
    string label;

    public function Tag(string named)
        this.label = named;
    end function

    public override string function ToString()
        yield "<" + this.label + ">";
    end function
end model
```

A declared `ToString` is what a value prints — written out, printed on its own, joined to a
string with `+`, or sitting inside a set. All of them reach the same function, dispatched on
the runtime type, so printing and calling can never disagree. Structures may override it as
freely as models; a structure declaring none prints field by field, and a model declaring none
prints its type name ([§3.3](#33-function-types) of the summary explains why the two defaults differ).

### 7.3 Structures

A **structure** is a value type. Assigning one copies it:

```text
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
model, which is the case [§3.4](#34-values-and-references) flags and the reason `constant` does not accept such a
structure.

Copying happens where a structure is **kept** — stored in a name, put in a set, passed to a
function, handed back from one. Reading one does not copy it, so reaching through a read to
change what was read changes the original:

```text
Point[] grid = {new Point(1, 2)};

grid[0].x = 99;              the point in the set is now 99
Point taken = grid[0];       this is a copy
taken.x = 55;                the set still reads 99
```

`Reference.Equals` may not be asked about a structure. "Are these two names reaching one thing"
is a question only a reference can answer, and a value is not somewhere a name points — so it is
`PC0347` rather than an answer, the same rule that refuses a structure in a `Model`-typed slot.
`==` is the comparison that applies.

> Readers coming from C# should note the first of these. A C# `struct` in a `List` cannot be
> changed through the list at all — the compiler refuses it, because the indexer hands back a
> copy — while here the set holds the structures themselves and reaching through it works.

### 7.4 Equality

`==` on two models compares them **field by field, all the way down** — not by reference.
Two separately built accounts with the same owner and balance are equal.

```text
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
inner instance. Types may not be declared inside a function body — see [§4.4](#44-functions).

## 8. Optionals

**There is no `null`.** A `Node` always holds a node. `Node?` is the type that may not, and
it is a different type, so absence appears in the signature rather than lurking behind every
reference.

**One `?` and no more** (`PC0252`). `Node??` is refused, because the two ways of being empty it
would create cannot be told apart: nothing an optional offers can see past the first level, so
"absent" and "present, holding an absence" would answer alike to every question a program can
ask. A language whose whole claim about absence is that the compiler can prove it should not
offer a shape where it cannot. C# refuses the same thing for the same reason.

### 8.1 The three members

```text
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

```text
if found.HasValue()
    Console.WriteLine(found + 1);      found is an integer here
end if
```

The compiler tracks this the same way it tracks definite assignment — forward, joining at
merge points. It also follows the negative case, so an early exit narrows the rest of the
function:

```text
if not found.HasValue()
    yield 0;
end if

Console.WriteLine(found + 1);          narrowed for everything after
```

An arm that always leaves — by `yield`, `throw`, `break` or `continue` — never arrives at the
join after it, so it has no say in what holds there. That is what makes the example above work,
and it is not only about guards: where one arm leaves, what the other one stored is what holds.

**Assignment narrows too, and stops at the same joins.** Storing a value in an optional proves
presence exactly as a guard does, and it survives a join only where every way through the branch
stored one:

```text
integer? n;
if ready
    n = 5;
end if
Console.WriteLine(n + 1);              refused: nothing ran when ready was false
```

**Nothing a loop stores is narrowed after the loop.** A loop may run no turns at all, a turn
after the first begins wherever the one before it ended, and a `break` leaves from wherever it is
written. A value stored in a loop is still there — it is an optional, and `Or` and `Value` reach
it — but the compiler will not read it as its underlying type. The same goes for a `try`: an
exception leaves the body from anywhere in it, so a `catch` does not begin knowing what the body
had stored.

**A name something else can change is never narrowed.** Only locals and parameters are narrowed
at all; a field is not, because any call in between could replace it and a check made before that
call says nothing about after it. A local is in the same position the moment a lambda or a nested
function assigns it, since that function holds the name and may be called at any point:

```text
integer? n;
n = 5;

delegate() clear = function()
    n = Program.Nothing();      n is written from inside a closure
end function;

clear();
Console.WriteLine(n + 1);       refused (PC0345), and rightly: n is empty here
```

Writing `HasValue()` does not help, and the message says so rather than sending its author to
write a check that changes nothing. **Copy it into a local first** — one nothing else holds sits
still, and narrowing works on it as it does everywhere else.

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
model Temperature
    real degrees;

    public function Temperature(real at)
        this.degrees = at;
    end function

    public real function Value()
        yield this.degrees;
    end function

    public string function Describe()
        yield this.degrees + " degrees";
    end function
end model

shared model Program
    function Main()
        Temperature? reading = new Temperature(21.5);

        # No guard, so this is still an optional: Value() unwraps it.
        let t = reading.Value();            # t is a Temperature
        Console.WriteLine(t.Describe());

        if reading.HasValue()
            # Narrowed, so this is a Temperature: Value() is the model's.
            let degrees = reading.Value();  # degrees is a real
            Console.WriteLine(reading.Describe());
        end if
    end function
end model
```

Anything the narrowed type does not declare still falls back to the optional's members, so
writing `HasValue()` on a narrowed optional keeps working. Only a name the underlying type
claims for itself is taken from the optional.

The question does not arise before narrowing. An optional exposes its own three members and
nothing else, so the underlying type's members are unreachable until presence is proven:

```text
Temperature? t = new Temperature(21.5);

let unwrapped = t.Value();          # the optional's Value; unwrapped is a Temperature
Console.WriteLine(unwrapped.Value());   # now the model's; 21.5

let d = t.Describe();               # PC0306: a Temperature? has no member 'Describe'
```

The two names are in scope together only after narrowing, which is where the rule above
applies.

## 9. Functions and closures

**A function is a value.** It has a type ([§3.3](#33-function-types)), and it can be stored in a variable, held in
a set, passed to another function, and handed back from one.

A function that already has a name is already a value and needs no lambda around it:

```
model Counter
    integer at;

    public function Counter(integer from)
        this.at = from;
    end function

    public integer function Next()
        this.at = this.at + 1;
        yield this.at;
    end function
end model

shared model Program
    function Main()
        integer delegate(integer) tripled = Program.Triple;

        Counter counter = new Counter(10);
        integer delegate() advance = counter.Next;

        Console.WriteLine(tripled(2) + advance());
    end function

    integer function Triple(integer n)
        yield n * 3;
    end function
end model
```

A member reached through an instance is that member *bound to that instance*, so calling it
later still knows which one it belongs to.

**A lambda closes over the variables around it.** The function handed back below remembers
`by`, which belonged to the call that made it:

```text
integer delegate(integer) function AdderOf(integer by)
    yield (n) yield n + by;
end function

let addFive = Program.AdderOf(5);

Console.WriteLine(addFive(3));            8
```

**That first line comes apart in four pieces**, and each word says which:

```text
integer delegate(integer)   function   AdderOf   (integer by)
└──── the result type ───┘  └keyword┘  └─name─┘  └parameter─┘
```

Every declaration is *result*, then `function`, then the name, then what it takes — the same
shape as `integer function Twice(integer n)`. The result here happens to be a function type,
which `delegate` writes ([§3.3](#33-function-types)), so the line stays readable left to right:
`delegate` can only be building a type, and `function` can only be starting the declaration.

**The lambda has no name.** `(n) yield n + by;` is a value, as `42` is a value; it is handed
back, and whoever receives it decides what to call it. `addFive` is not the lambda's name, it
is the name of a local that holds one.

**A loop variable is fresh on every turn**, so a function made inside a loop closes over that
turn's variable rather than a shared one. Three functions made in three turns report three
values, which is the opposite of what the same code does in languages where the variable is
shared.

Overloads are chosen by argument count first, then by exact match, then by what the arguments
can convert to. Two versions reachable only by conversion is a tie, and a tie is reported
(`PC0310`) rather than broken by a rule nobody remembers.

**A name belongs to one member** (`PC0253`). Two members of a type share a name only when they
are versions of one function, told apart by what they take — so two fields cannot, a field and a
function cannot, and neither can two functions taking the same types. Without the rule the second
declaration is simply unreachable: every use of the name finds the first, and the reader who
wrote the second watches their code run somebody else's. It is reported where the second is
written, and names the line the first is on.

### 9.1 Writing a function as a value

A function value is written in one of two forms, and both say what they produce with `yield`:

```
integer delegate(integer) increment = (integer a) yield a + 1;

integer delegate(integer, integer) larger = function(integer a, integer b)
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
integer delegate(integer) increment = (a) yield a + 1;
```

Four things say it, and between them they cover every place a lambda can be written: a
declared type, the element type of a set being built, the parameter of the function being
called, and the result of the function doing the yielding.

```text
integer delegate(integer)[] steps = { (n) yield n + 1 };     # element type
Console.WriteLine(Program.Apply(numbers, (n) yield n * 2));  # parameter
yield (n) yield n + by;                                      # result
```

An optional function type is a target like any other, since the lambda is wrapped on the way
in and what it has to be is the type underneath.

**Where the type is already said, writing it again is reported.** `PC0115` is an opinion: the
program says one thing and says it twice, which is the same argument `PC0111` makes about a
range loop's counter.

**Where nothing says it, leaving it out is reported.** `PC0336` names the parameter that has
no type.

The two rules meet with no gap and no overlap, which leaves exactly one place a lambda writes
its own types — a `let`, where nothing on the left says anything:

```
let halve = (integer n) yield n / 2;              # nothing else says it, so this does
integer delegate(integer) double = (n) yield n * 2;   # the declared type says it
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

Eleven exception types, and every one descends from `Exception`:

| | Raised when |
|---|---|
| `Exception` | The root. A `catch` naming it takes them all |
| `DivideByZeroException` | Dividing by a value that turned out to be zero |
| `IndexOutOfRangeException` | Indexing a set or string outside it |
| `EmptyOptionalException` | `Value()` on an optional holding nothing |
| `SequenceChangedException` | a set changed while a `loop each` was walking it |
| `InvalidCastException` | A conversion that could not be made |
| `FormatException` | Text that could not be read as what was wanted |
| `ArgumentException` | An argument a function will not accept |
| `OverflowException` | A result too large for the type to hold |
| `RecursionTooDeepException` | Calls nested deeper than the language will follow. The one no `catch` takes ([§10.1b](#101b-calling-too-deeply)) |
| `IOException` | Anything going wrong with a file except its not being there, which is an absent optional instead ([`File`](standard-library/input-output.md#file)) |

Every one carries a `Message()`. A name the language can raise is a name a program can write,
because the two come from one list — so nothing can be thrown that cannot be named.
`RecursionTooDeepException` is the one that can be named and not caught
([§10.1b](#101b-calling-too-deeply)).

Eight of the eleven are the names .NET uses, unchanged. That is deliberate: a reader who learns
what `DivideByZeroException` means here already knows what it means in C#, Java, and near
enough in Python, and a name that transfers is worth more than one tuned to this language
alone. The wording each carries is *not* .NET's — a message is read once and never carried
anywhere, so it is written here to say what happened and what to do about it.

### 10.1a What a `catch` does not take

A `catch` takes what the program caused: an exception it threw, and one the language raised on
its behalf. It does not take a failure in the implementation itself.

The distinction matters because every failure on the platform answers to `Exception`, so a
clause naming the root would otherwise take a bug in the compiler as readily as a divide by
zero — and, having taken it, would report it as something the program did. A program cannot
handle a fault it did not cause, and hiding one behind a handler written for something else
costs the only report the person who could fix it would ever get.

### 10.1b Calling too deeply

Calling too deeply — most often a function that calls itself without ever reaching the case
that stops it — raises `RecursionTooDeepException`. **It is the one exception no `catch` takes,
`catch Exception` included.**

Being nameable and being catchable are separate things, and this is the one place they come
apart. The name exists so a reader can be told what stopped their program. Catching it would
help nobody: the depth is the implementation's number rather than a property of the program, so
a handler would run at an arbitrary point with every frame beneath it abandoned half-finished,
and a program that has run away is not one more code recovers from. Naming it in a `catch` is
reported as `PC0344` rather than left as a clause that looks like a handler and never runs.

**It is not a stack overflow, which is why it does not carry that name.** The limit is a count
the language keeps, and it is reached long before the machine is near the end of its stack —
deliberately, so that the program stops while there is still room to say why. A real stack
overflow offers no such chance, and .NET's `StackOverflowException` is uncatchable for that
harder reason: by the time it happens the process is already going down. The bargain here is
the same in shape — a name to read, and no pretence that catching it would help — but the cause
is the language's own guard rather than the machine giving out.

### 10.2 Throwing and catching

```
integer[] numbers = {1, 2, 3};

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
[§4.5](#45-definite-assignment)'s rule without a yield after the throw.

### 10.3 Declaring an exception

A program may extend any of them:

```
model InsufficientFunds extends Exception
    public function InsufficientFunds(string message)
        base(message);
    end function
end model
```

Extending is not redeclaring: the names above cannot be declared, but they can be extended, and
a declared exception is caught by a `catch` naming any of its ancestors.

**There are no checked exceptions.** A function does not declare what it may throw, and
nothing forces a caller to handle it. That choice follows the same reasoning as the rest of
the language: the alternative teaches people to write `catch` clauses that swallow.

## 11. The standard library

The library is small, and lives in a namespace named **`Standard`**: `Model`, `Function`,
`Exception` and its subtypes, `Console`, `Reference`, `Math`, `Random`, `DateTime`, `TimeSpan`,
`Date`, `Time`, `File`, `Directory`, and the capitalized name beside each primitive — `Integer`,
`Real`, `Float`, `Fraction`, `Boolean`, `Character` and `String`. Of these only `Exception` may
be extended.

That last group is there because **a reserved word cannot stand in front of a dot**. `integer`
names the type; `Integer` is where the facts about it live, since `integer.MaxValue` is not
something the grammar can read.

**`Standard` is in scope in every file with nothing written**, so the library is reached
without importing anything, and `Standard.Math` is legal without a `using` too — qualifying a
name never needed one. Writing `using Standard;` is legal and reported (`PC0230`, an opinion):
it brings nothing that is not already there.

It sits at the same rank a `using` would put it at, rather than beneath. That matters only
once a second namespace can offer one of these names — .NET interop, in a later version —
and then it matters a great deal: at equal rank a bare `DateTime` with both in scope is
**ambiguous** (`PC0226`) and the program says which it meant, where a lower rank would have
let the import quietly take the name. Nothing collides with `Standard` today, which is why
the rule is worth fixing now: it costs nothing until it costs everything.

**A program may declare these names.** A `Math` of your own is legal, wins over the library's
by the ordinary nearest-name rule, and is warned about (`PC0203`) because losing `Math.Sqrt`
is almost never what was meant. `Standard.Math` still reaches the other one.

**A program may not declare `namespace Standard`** (`PC0229`). Namespaces merge, so it would
let a program add types that then read as the language's own, and `Standard.X` can only keep
meaning "the language gives you this" if nothing else may write there.

`Program` is not part of `Standard`. It is a name reserved for something a program *provides*
rather than something the language does, and must be declared exactly once ([§12](#12-execution-and-entry-point)).

**Twelve of them hold no values** — `Boolean`, `Character`, `Console`, `Directory`, `File`,
`Float`, `Fraction`, `Integer`, `Math`, `Real`, `Reference` and `String`. They are names to
reach members through, and naming one where a value's type belongs is an error (`PC0233`), as
it is for any `shared model`, which has no instances by definition:

```text
Math m;              PC0233: nothing can be of this type
fraction half = 1|2;  the type; Fraction is the model beside it
```

`Fraction` is the one this mostly catches, a capital letter away from the type meant, so the
message names `fraction` rather than only refusing what was written. Without the rule the
declaration is accepted by every other rule taken singly, and produces a variable nothing can
ever fill.

`Model` and `Function` are **not** in that set: neither can be constructed, and both hold
values all the same, since every model converts to one and every function to the other.

`Model` and `Function` are the two roots ([§3.3](#33-function-types), [§3.4](#34-values-and-references)) rather than things to call.

**`Random`, `DateTime`, `TimeSpan`, `Date` and `Time` are the ones a program may construct.**
Every other name here is reached through the name itself; writing `new Math()` is reported
(`PC0328`).

### 11.1 The reference

**The members themselves are in [docs/standard-library/](standard-library/README.md)**, whose
index lists every type and then every member by name, each linking to the page that explains it.
This section says what the library *is*; that says what is *in* it.

| Page | What is on it |
|---|---|
| [Every value](standard-library/every-value.md) | `ToString`, `Equals`, `Reference.Equals`, an enumeration's `ToInteger` |
| [Text](standard-library/text.md) | Every member of a `string`, and `String.Empty` |
| [Sets](standard-library/sets.md) | Every member of a `T[]` |
| [Optionals](standard-library/optionals.md) | The three members of a `T?` |
| [Numbers](standard-library/numbers.md) | The members of a number, `Fraction`, what each type knows about itself, and every conversion between them |
| [Math](standard-library/math.md) | Roots, logarithms, angles, rounding and sizing |
| [Random](standard-library/random.md) | Chance, held or drawn through the name |
| [Dates and times](standard-library/dates-and-times.md) | `DateTime`, `Date`, `Time`, `TimeSpan` |
| [Input and output](standard-library/input-output.md) | `Console`, `File`, `Directory` |
| [Exceptions](standard-library/exceptions.md) | `Message`, and every exception the language raises |

It is kept apart from this document for the reason every reference is: the two are read
differently. A specification is read once, in order, to learn what the language does; a reference
is opened at one member, answered, and closed. Written into one file they crowd each other out,
which is what the member tables here had begun to do.

**A test holds the two together.** Every member the compiler provides must have a row in that
index, nothing may be listed there that the compiler does not provide, every page must be
reachable, and every link must land on a heading that is there — so a member added to the
language and left undocumented fails the build rather than going quietly unlearned.

### 11.2 Two rules the reference relies on

**A member written without parentheses is a value rather than something to call.** `Math.Pi` and
`landing.Year` are read; `word.ToUpper()` is called. Writing parentheses on a value is reported
(`PC0338`), as is naming a function without them (`PC0330`) — the two diagnostics are a pair, so
whichever a reader guesses, the compiler says which it is.

**A member that may have no answer yields an optional** rather than raising: `File.Read` yields
`string?`, `"12".ToInteger()` yields `integer?`, `Console.Read` yields `string?`. Absence is an
ordinary outcome and is handled by [§8](#8-optionals); a fault is an exception. Which of the two
a member chooses is the single most useful thing to know about it, and every table in the
reference says so.

### 11.3 How a value prints

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

```text
pc run hello.pc                 one file, with the shared code beside it
pc run bookshelf/Program.pc     the same rule across a folder — see §12.1
pc run app.pcp                  a project file listing what to build
pc check app.pcp                check without running
pc tokens hello.pc              the token stream
pc ast hello.pc                 the tree
```

Every command takes a **file**, never a folder — a folder is reached by naming a file in it,
which [§12.1](#121-what-a-compilation-is-made-of) explains. The extension may be omitted: `pc run hello` finds `hello.pc`, and asks
for the extension only where both a `.pc` and a `.pcp` of that name exist.

**`Program` may be declared exactly once in a compilation, and must be
`shared model Program` containing `Main`.** This differs from `Model`, `Exception`, `Console`,
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

```text
# A storefront, spread across folders.

project Storefront
    source Program.pc
    source models
    source pricing
end project
```

A `source` naming a folder takes every `.pc` directly inside it and does not descend, so a
nested folder is named by its own `source` and what a project builds can be read off the file.
Paths are relative to the project file and are written with forward slashes on every platform.
Comments are marked as they are in a program — `#` to the end of a line, `##` opening a block —
and a blank line is ignored.

A project file is not Profi-C. It describes a build rather than a computation, nothing in it
is compiled, and its vocabulary is only `project`, `source`, `reference`, and `end project`.

**A project names another project with `reference`.** The referenced project's types are then
available, exactly as though they were declared in this one:

```text
project Storefront
    reference ../Core/Core.pcp
    source Program.pc
    source models
end project
```

References are followed to closure, and a project reached more than one way is brought once —
which is what makes a project shareable between several others. What a project references is
built before the project itself, so a build reads in the order it depends. A reference is
transitive: referencing a project also reaches what *it* references, matching .NET, so a shared
project need be named only by whoever actually builds on it.

A project made only of references is composition rather than emptiness, and builds what its
references bring.

**One file belongs to one project.** Two projects in a build listing the same file leave
undecided which one it belongs to, and it is reported naming both. The fix is for the project
that owns the file to keep it, and the other to reference that project.

**Projects may not reference in a circle.** This is an error, where the same shape between
files is only a warning, and the difference is what a project is. Files in a circle still all
belong to one compilation, so nothing about reading them together is in question. A reference
crosses from one build to another, and a build that has to exist before itself cannot be
produced — which stays true, and becomes literal, the moment a project is something separately
built. The circle is reported at the reference that closes it and read back as a sentence:
`Ledger references Reports, which references Ledger`. A project referencing itself is the same
rule with one project in it. Code that two projects both need belongs in a third that both
reference.

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

**Imports that form a circle are warned about.** A warning rather than an error, because
nothing about a circle is unbuildable: a compilation reads every file it gathers together, and
reaching one twice adds nothing the first reach did not. What a circle costs is a reader, who
has no file to open first — and one drawn across four files is a circle nobody meant to draw.
The compiler reports it at the import that closes the circle and reads the circle back as a
sentence: `A.pc imports B.pc, which imports A.pc`. A file importing itself is the same rule
with one file in it.

The fix is usually to write less. A circle can only ever be drawn across folders, because files
beside one another are already compiled together and need no import between them — so mutually
recursive types in one folder are written with nothing said. Across folders, a project file
names every file in the build without one of them importing another.

Contrast this with a circle between *projects*, which is an error: see above.

`import` and `using` do different jobs and neither does the other's. **An import decides which
files are compiled and affects no name; a using decides which names are reachable unqualified
and brings in no file.** Which to reach for follows the scale of what is wanted: one file, an
import; a group of related types, a namespace; a whole build across folders, a project.

#### Which program starts

A compilation may hold more than one `Program`, since namespaces make `Tools.Program` and
`App.Program` two types rather than one name used twice. Something then has to say which one
the build begins at, and it is not the compiler's to decide: an assembly holds one entry point
in its metadata, so the choice is made when the thing is built however it is spelled, and
picking by the order files were listed would make a build's behavior depend on the order of
its own file list.

**A project names it**, and only needs to where there is a choice:

```text
project tools
    entry Tools.Program
    source Tools.pc
    source App.pc
end project
```

Written where the sources declare exactly one `Program`, the line decides nothing and is
reported as an opinion (`PC0236`). Left out where they declare several, the compilation is rejected and
the programs are named (`PC0234`). Naming something that is not one of them is `PC0235`, which
lists what was there. A project starts in one place, so a second `entry` is `PC0627`.

**`pc run <file>` needs none of this**: it runs the `Program` that file declares.

The name is written as a `using` would write it — the namespaces in front of the type, and the
type — because that is the name the type has. It is not a path to a file.

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

### 12.3 Namespaces

A namespace is written in either of two forms:

```text
namespace Shapes;              file-scoped: everything after it belongs to Shapes

namespace Shapes               block: closes with "end namespace"
    model Circle
    end model
end namespace
```

Namespaces nest, either by writing a dotted name or by writing one inside another. **Two
namespaces may each declare a type of the same name**, which is most of what they are for.

#### How a bare name is read

From the namespace it sits in, then outward through every namespace around it, ending at the
global one. If none of those has it, then the namespaces in scope: whatever the file wrote
`using` of, and `Standard` ([§11](#11-the-standard-library)).

**Nearest wins.** A `Circle` beside you is the one you meant, whatever else in the program
shares the name — so a namespace reaches its own types with nothing written, and reaches
anything outside it without a `using` either. The global namespace is on that path, and a
namespace is not: a type declared outside every namespace is reachable from inside one, and a
type inside one is not reachable from outside without saying so.

Only a tie **among the namespaces in scope** is ambiguous (`PC0226`), because those name no
order between themselves. It is reported where the name is read, not at the `using`.

#### Qualifying

Writing the namespace in front reaches past all of that:

```text
Shapes.Circle flat = new Shapes.Circle();
Solids.Circle round = new Solids.Circle();
```

A qualified name works in every position a type appears — a variable's type, a parameter, a
result, after `new`, and as the receiver of a shared member — and needs **no `using`**, since a
using shortens a name and a qualified one already says where to look. That is what makes
`Standard.Math` reachable from a program that declared a `Math` of its own.

What comes before the last part is itself read the way a bare name is: from where it is
written, outward. So `Shapes.Circle` inside `Tour` reaches `Tour.Shapes` if there is one, and
the top-level `Shapes` otherwise.

A run of names is read as a type before any of it is read as a value, longest first — the more
specific reading wins, and a type whose name matches a namespace's is not otherwise separable
from a namespace holding a type. A run that reaches no type is a value, which is why
`account.holder.Name()` still means what it says.

#### The two forms together

Both forms may appear in one file, and a block written under a file-scoped one is **a child of
it**:

```text
namespace App;

namespace Models              this is App.Models
    model Book
    end model
end namespace
```

So `App.Models.Book` names it from outside, and `Models.Book` from anywhere already inside
`App`. This differs from C#, which forbids mixing the two: here the file-scoped form says what
the file is, and a block inside it says where in the file.

#### Two rules about writing them

**`using` and `import` come above every namespace** (`PC0231`). Both are statements about the
whole file — which names it reaches, and which files are compiled with it — and neither
narrows to part of one, so writing one inside a namespace would say something the language has
no way to mean.

**A namespace repeating a name it sits inside is reported** (`PC0232`), whether written as
a nested block or as a dotted name repeating itself. `Shapes.Shapes.Circle` is what a reader
has to write afterwards, and it reads as a slip rather than a distinction. An opinion rather
than an error, because it is only a name and a program that means it works.

**A `using` cannot form a circle, and will not be made to.** Two namespaces whose types name
each other are ordinary, and stay legal however the files are arranged. A namespace is a way of
naming things rather than a thing that gets built: every file in a compilation is resolved
together, so a `using` introduces no order for a circle to violate. This is exactly the
difference [§12.1](#121-what-a-compilation-is-made-of) draws. An import decides what is compiled, and what is compiled has to be
reachable from somewhere, so its circles matter; a using decides what is spelled short, and
nothing is spelled first.




---

## Appendix A. Diagnostics

Every identifier the compiler reports. Identifiers are stable: once assigned, one is never
reused for a different rule, so a link or a note written today keeps its meaning.

Each carries one of three severities, and what separates them is how much is known about what
the program means.

| Severity | The program's meaning | Blocks compilation |
|---|---|---|---|
| `error` | genuinely unpredictable without the diagnostic | yes |
| `warning` | clear, and unlikely to be what was intended | no |
| `opinion` | clear, intended, and correct — and the language would write it differently | no |

An opinion always says that some written token has no effect. Nothing is wrong with a program
that has one, and reading them in order is a reasonable way to learn what the language expects
of a reader.

### PC0000 to PC0099

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0001` | error | Unrecognized character | Unrecognized character '{0}'. Nothing in the language uses it; remove it. |
| `PC0002` | error | Unterminated string literal | Unterminated string literal. Add a closing '"'. |
| `PC0003` | error | Unterminated character literal | Unterminated character literal. Add a closing quote mark. |
| `PC0004` | error | Malformed character literal | A character literal must contain exactly one character. For more than one, write a string with '"'. |
| `PC0005` | error | Unterminated block comment | Unterminated block comment; expected '##'. |
| `PC0006` | error | Not an operator in Profi-C | '{0}' is not an operator in Profi-C. {1} |
| `PC0007` | error | Unrecognized escape sequence | Unrecognized escape sequence '\{0}'. The escapes are \n, \t, \\, \", \', \0 and \uFFFF. |
| `PC0008` | error | Malformed Unicode escape sequence | A Unicode escape must be '\u' followed by four hexadecimal digits. |
| `PC0009` | opinion | This name needs no '@' | '{0}' is not a reserved word, so the '@' does nothing. Write '{0}'. |
| `PC0010` | error | Nothing to escape | '@' marks a reserved word being used as a name, so a name must follow it. |
| `PC0011` | error | Unterminated interpolation | Unterminated interpolation; expected '}}'. |
| `PC0012` | error | Nothing to interpolate | An interpolation holds an expression. Write '{{name}}', or a single brace for a literal one. |
| `PC0013` | error | Unterminated block string | Unterminated block string; expected '{0}'. |
| `PC0014` | error | Nothing to format by | A ':' in an interpolation is followed by how to format the value, as in '{{total:F2}}'. Leave it out to format the value the ordinary way. |
| `PC0015` | warning | More quotes in a row than close the block | {0} quotes in a row, where {1} close the block string. The last {1} end it and the rest are held. Open and close it with '{2}' to hold all {0}. |
| `PC0016` | error | Block string delimiters differ in length | {0} quotes do not close a block string opened with {1}, so this is text and the block runs on. Open it with '{2}', or close it with '{3}'. |
| `PC0017` | error | This base has no digits | '{0}' says the number that follows is {1}, and none follows. |
| `PC0018` | error | This digit is not in the base | '{0}' is not a {1} digit, which is {2}. |
| `PC0019` | error | This exponent has no digits | An 'e' says how many places to move the point, so it needs digits after it â€” '1e3' is 1000.0. |
| `PC0020` | error | This separator has no digits after it | An '_' in a number separates digits, so more have to follow it â€” '1_000' is a thousand. |
| `PC0021` | error | A name cannot begin with a digit | '{0}' is written against the number before it. A name begins with a letter or an underscore, and nothing in the language puts two values side by side. |
| `PC0022` | warning | This 'ignore' names no diagnostic | '{0}' is not something this compiler reports, so this line silences nothing. Check the identifier against the one in the message. |
| `PC0023` | warning | That diagnostic cannot be ignored | '{0}' stops compilation, and only a warning or an opinion can be ignored. This line cannot do anything; what it names has to be fixed. |
| `PC0024` | opinion | This 'ignore' silences nothing | Nothing it reaches reports '{0}', so this line has no effect. Remove it. |
| `PC0025` | warning | This 'ignore' names neither a severity nor a diagnostic | '{0}' is not 'warning', not 'opinion', and not an identifier such as 'PC0340'. |
| `PC0026` | error | Number too large to hold | {0} is too large for {1}. {2} |
| `PC0027` | error | A fraction over zero | {0} is a fraction over zero. The bar is division, so what sits under it can be anything but zero. |

### PC0100 to PC0199

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0100` | error | Unexpected token | Expected {0}, but found {1}. |
| `PC0101` | error | Expected an expression | Expected an expression, but found {0}. |
| `PC0102` | error | Expected a type | Expected a type, but found {0}. |
| `PC0103` | error | Expected a name | Expected a name, but found {0}. |
| `PC0104` | error | Mismatched block closer | Expected 'end {0}' to close the {0} beginning on line {1}, but found 'end {2}'. |
| `PC0105` | error | Unterminated construct | The {0} beginning on line {1} is never closed; expected 'end {0}'. |
| `PC0106` | error | Statement cannot start here | A statement may not begin with '{0}'. Give the value a name first, as in 'let value = ...;', and then use it. |
| `PC0107` | error | Expected a statement | Expected a statement, but found {0}. |
| `PC0108` | error | Expected a declaration | Expected a declaration, but found {0}. |
| `PC0109` | error | Cannot assign to this expression | The left side of an assignment must be a name, an index, or a member access. |
| `PC0110` | error | Type declared inside a function | A {0} cannot be declared inside a function. Move it out to the enclosing model or namespace. |
| `PC0111` | opinion | A range loop's counter has no written type | A range loop counts with integers, so its counter takes no type. Remove the '{0}'. |
| `PC0112` | error | An if expression has no 'else' | This if expression has no 'else'. It produces a value, so it must say what the value is when the condition is false. |
| `PC0113` | error | Too many problems | Too many problems; stopped after {0}. Fixing the ones above may account for the rest. |
| `PC0114` | error | This word is reserved | '{0}' is a reserved word, so it cannot be a name on its own. Write '@{0}' to use it as one. |
| `PC0115` | opinion | This parameter's type is already known | The surrounding code already says what '{0}' holds, so writing its type says it twice. Leave the type out. |
| `PC0116` | error | A function's type is written with 'delegate' | 'Function' takes no parentheses. For a particular shape write 'delegate(...)', with a result before it if it has one, as in 'integer delegate(string)'. |
| `PC0117` | error | A function's type is written with 'delegate' | 'function' declares a function or makes one on the spot. To write the type of one, use 'delegate' â€” 'integer delegate(string)' takes a string and yields an integer. |
| `PC0118` | error | Only 'and' or 'or' may follow 'bitwise' | 'bitwise' says which of two operations follows, and {0} is neither. Write 'bitwise and' or 'bitwise or' â€” 'xor', 'shiftleft' and 'shiftright' need no word before them. |
| `PC0119` | error | 'let' declares a local, not a field | 'let' works inside a function, where the value it holds is written beside it. A field is read far from here, so it says its type: '{0} {1} = ...'. |
| `PC0120` | error | A loop begins with 'loop' | Every loop opens with 'loop', so this is written 'loop {0}'. The word after 'loop' says which kind: 'for', 'each', 'while', or nothing at all. |

### PC0200 to PC0299

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0200` | error | Name not found | '{0}' is not defined here. Check the spelling, or declare it above this line. |
| `PC0201` | error | Type not found | There is no type named '{0}'. Check the spelling, or the 'using' that would reach it. |
| `PC0202` | error | Name already declared | '{0}' is already declared in this scope. Rename one of them. |
| `PC0203` | warning | This shadows a type the language provides | '{0}' is also the name of a type in Standard, and a name declared here wins over one in scope. Write 'Standard.{0}' to reach the other, or rename this. |
| `PC0204` | error | Member access needs a receiver | '{0}' is a {1} of '{2}', so it must be written as '{3}.{0}'. A bare name reaches only locals and parameters. |
| `PC0205` | error | Cannot assign to a constant | '{0}' is a constant and cannot be assigned to. Drop 'constant' from its declaration, or assign a different name. |
| `PC0206` | error | Cannot assign to a loop variable | '{0}' is a loop variable and is read-only inside the loop. Each iteration binds a fresh one, so assigning to it would change nothing. |
| `PC0207` | error | Circular inheritance | '{0}' cannot extend itself, directly or through its ancestors. Break the circle. |
| `PC0208` | error | Cannot extend a sealed model | '{0}' is sealed and cannot be extended. Drop 'sealed' from it, or extend what it extends. |
| `PC0209` | error | Cannot extend this type | '{0}' is a {1}, and only a model can be extended. Hold one as a field instead. |
| `PC0210` | error | Sealed and abstract together | '{0}' cannot be both sealed and abstract; it could then be neither extended nor instantiated, so nothing could use it. |
| `PC0211` | error | Instance member on a shared model | '{0}' is a shared model, which is never instantiated. Mark the member 'shared', or drop 'shared' from '{0}'. |
| `PC0212` | error | No entry point | A program needs a 'shared model Program' containing a function named 'Main'. |
| `PC0213` | error | Program must be a shared model | 'Program' must be declared 'shared model', since there is no such thing as an instance of a running program. |
| `PC0214` | error | '{0}' used outside a model | '{0}' can only be used inside a model's instance member. Drop 'shared' from this member, or reach what you want through its type name. |
| `PC0215` | error | No parent to reach | 'base' needs a parent model, and '{0}' extends nothing. Give it one with 'extends', or write 'this' instead. |
| `PC0216` | error | Cannot extend a built-in type | '{0}' is provided by the language and has nothing to inherit. Of the built-in types only 'Model' and the exceptions may follow 'extends'. |
| `PC0217` | error | Type already declared | '{0}' is already declared {1}. Two types cannot share a name, whether they are written in one file or across several. Rename one of them. |
| `PC0218` | error | Main declares no result or an integer | 'Main' must declare no result, or an integer, which becomes the program's exit code. |
| `PC0219` | error | Two visibilities on one declaration | '{0}' is written {1}, and one declaration has one visibility. Keep the word that says how far this should reach. |
| `PC0220` | error | A type cannot be protected | '{0}' is a type, and 'protected' is for members. Write 'internal' for its project, or 'public' for anywhere. |
| `PC0221` | error | Type belongs to another project | '{0}' is internal to {1}, and this is {2}. Mark it 'public' if {2} is meant to use it. |
| `PC0222` | error | Nothing to override | '{0}' is marked 'override', but {1} declares no '{0}' with these parameters. Check the name and the parameter types, or drop 'override' if this is a new function. |
| `PC0223` | error | Overridden function is not virtual | '{0}' overrides a function in {1} that is not marked 'virtual', so {1} did not offer it for overriding. Mark the one in {1} 'virtual'. |
| `PC0224` | error | This hides a function from the base | {1} already declares '{0}' with these parameters. Write 'override' to replace it, or rename this one. |
| `PC0225` | error | Override yields a different result | '{0}' yields {1}, and the one it overrides in {2} yields {3}. An override yields what it overrides, since a caller holding a {2} reads the result as {2} declared it. |
| `PC0226` | error | This name is offered by more than one namespace | '{0}' could mean {1}. Both are used here and neither is nearer, so write the one you mean in full. |
| `PC0227` | error | No such namespace | No namespace named '{0}' is declared in this compilation. Check the spelling, or that the file declaring it is being compiled. |
| `PC0228` | error | This namespace is already used here | '{0}' is already used in this file. Remove this line. |
| `PC0229` | error | Standard belongs to the language | 'Standard' is the namespace the language's own types live in, and a program may not add to it. Name this namespace something else. |
| `PC0230` | opinion | Standard is already in scope | Every file reaches Standard without saying so, so this line brings nothing. |
| `PC0231` | error | This belongs above any namespace | '{0}' is a statement about the whole file, so it goes above every namespace in it. Move it to the top. |
| `PC0232` | opinion | This namespace repeats one around it | '{0}' already sits inside a namespace of that name, so its types are reached as '{0}.{0}.â€¦'. Rename this one if that was not meant. |
| `PC0233` | error | Nothing can be of this type | '{0}' has no instances, so nothing can ever be held here. {1} |
| `PC0234` | error | Which program starts? | These sources declare more than one Program: {0}. Write 'entry {1}' in the project file to say which one begins. |
| `PC0235` | error | No such program | '{0}' is not a Program among these sources. {1} |
| `PC0236` | opinion | This 'entry' decides nothing | Only '{0}' declares a Program, so it begins whether or not this line is here. |
| `PC0237` | error | This name is already in use here | '{0}' is already the name of something in an enclosing scope, so this one would hide it. Give it a name of its own. |
| `PC0238` | error | This function needs a body | '{0}' ends at the semicolon, so nothing says what it does. Give it a body, or mark it 'abstract' to leave it to whatever extends this model. |
| `PC0239` | error | An abstract function has no body | '{0}' is abstract, so every model extending this one writes what it does and this body would never run. End the declaration at ';', or drop the 'abstract'. |
| `PC0240` | error | Only an abstract model may leave a function open | '{0}' is abstract, but '{1}' can be constructed â€” so an instance of it would reach a function nothing ever wrote. Mark '{1}' abstract too. |
| `PC0241` | error | An inherited function is still open | '{0}' can be constructed, so it must write every function left open above it. Still open: {1}. Override each, or mark '{0}' abstract. |
| `PC0242` | opinion | An abstract function is already virtual | '{0}' is abstract, which is what offers it for overriding, so 'virtual' says nothing further. Remove it. |
| `PC0243` | error | This changes the sequence being walked | '{0}' is the sequence this 'for each' is walking, and '{1}' changes it. Collect the changes into another set, or count with a range loop. |
| `PC0244` | warning | This documentation has nothing to document | Nothing follows this that an '@summary:' can document, so nothing will show it. Move it directly above a declaration, or make it an ordinary comment. |
| `PC0245` | warning | This documents a parameter that is not there | '{0}' is documented, but '{1}' takes {2}. Rename the line or take it out. |
| `PC0246` | warning | This describes a value that is never given back | '{0}' yields nothing, so there is no value for '@yields:' to describe. |
| `PC0247` | opinion | This is documented twice | '{0}' already has a line above this one, and the first is the one that shows. For a second paragraph, leave a blank line and keep writing. |
| `PC0248` | error | 'base' has to come first | 'base(...)' must be the first statement in a constructor, so that '{0}' is fully built before anything here runs. |
| `PC0249` | error | 'this' is not available yet | '{1}' is still being built here, so '{0}' cannot be reached from a field's starting value. Give '{2}' its value in a constructor instead. |
| `PC0250` | error | Nothing here builds the parent | '{0}' extends '{1}', which cannot be built without being given something. Begin this constructor with 'base(...)': '{1}' takes {2}. |
| `PC0251` | opinion | This 'base()' changes nothing | '{0}' is built before this constructor's body whether or not 'base()' is written, so this line does what would happen without it. Keep it if saying so helps. |
| `PC0252` | error | An optional of an optional | This is already optional, so the second '?' says nothing new. Write one '?'. |
| `PC0253` | error | Member name already taken | {0} already has a member named '{1}', declared on line {2}. Rename one, unless they are versions of one function taking different types. |
| `PC0254` | error | A throwaway holds nothing | '_' throws its value away, so there is nothing here to use. Give it a name. |
| `PC0255` | error | A throwaway cannot be a name | '_' throws a value away, so it cannot name {0}, which is reached by name. Give it a name. |
| `PC0256` | error | Nothing here asks for a name | '_' stands in for a name the language asks for, and nothing asks for one here. Write the value on its own, or remove the line. |
| `PC0257` | error | A parameter needs a name | '_' cannot name a parameter: it is part of the signature a caller reads. Give it a name. |

### PC0300 to PC0399

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0300` | error | Cannot convert | Cannot use {0} where {1} is expected. {2} |
| `PC0301` | error | Conversion must be written out | {0} does not become {1} on its own, because the result would surprise you. Write '{2}' to ask for it. |
| `PC0302` | error | Condition must be a boolean | {0} must be a boolean, and this is {1}. Write a comparison. |
| `PC0303` | error | Operator not defined for these types | '{0}' is not defined for {1} and {2}. Convert one side, or use a member. |
| `PC0304` | error | Operator not defined for this type | '{0}' is not defined for {1}. Convert it, or use a member. |
| `PC0305` | error | Branches of an if expression have different types | The branches of an if expression must have the same type, and these are {0} and {1}. Make them agree, or write an 'if' statement. |
| `PC0306` | error | Member not found | {0} has no member named '{1}'. |
| `PC0307` | error | Not something that can be called | {0} cannot be called. {1} |
| `PC0308` | error | Wrong number of arguments | '{0}' takes {1}, but was given {2}. |
| `PC0309` | error | No overload matches | No version of '{0}' accepts these arguments. Convert the ones that do not match. |
| `PC0310` | error | Ambiguous call | Several versions of '{0}' match these arguments equally well. Give one argument the exact type a version takes. |
| `PC0311` | error | Not something that can be indexed | {0} cannot be indexed. Only a set and a string can. |
| `PC0312` | error | Index must be an integer | An index must be an integer, and this is {0}. Write a whole number. |
| `PC0313` | error | Cannot infer the type of an empty set | The type of an empty set cannot be worked out from the set alone. Write the type, as in 'integer[] values = {};'. |
| `PC0314` | error | Set elements have different types | The elements of a set must have one type, and these are {0} and {1}. Write the set's type, as in 'Shape[] values = {{...}};'. |
| `PC0315` | error | Cannot switch on this type | A switch cannot examine {0}. Equality on it is unreliable, so a case label could never be trusted to match. |
| `PC0316` | error | Cannot iterate this type | 'for each' needs a set or a string, and this is {0}. Ask it for one, or count with 'loop for'. |
| `PC0317` | error | Range loop needs integers | A range loop counts with integers, and this is {0}. Count with whole numbers, or walk it with 'loop each'. |
| `PC0318` | error | This function yields nothing | '{0}' declares no result, so 'yield' cannot carry a value. Declare a result, or write 'yield;'. |
| `PC0319` | error | Missing value to yield | '{0}' yields a {1}, so 'yield' needs a value. Give it one, or drop the result type. |
| `PC0320` | error | Constant needs a value | '{0}' is a constant, so it must be given a value where it is declared. Write one, or drop 'constant'. |
| `PC0321` | error | Constant value must be known while compiling | The value of '{0}' must be worked out while compiling, so it can only be built from literals and other constants. |
| `PC0322` | error | This type cannot be constant | {0} cannot be declared constant, because the binding could stay fixed while what it names changed. This may widen in a later version. |
| `PC0323` | error | Nothing to infer from | 'let' works out the type from the value, so it needs one. Give it a value, or write the type instead. |
| `PC0324` | error | Division by zero | This divides by zero. |
| `PC0325` | error | Case label must be a constant | A case label must be known while compiling. |
| `PC0326` | error | Duplicate case label | The value {0} is already handled by another case. |
| `PC0327` | warning | This test is always false | {0} can never be {1}, so this is always false. |
| `PC0328` | error | Cannot be instantiated | '{0}' is {1} and cannot be instantiated. |
| `PC0329` | error | Optional must be unwrapped first | This is {0}, which may be empty. Use 'HasValue()' to check, 'Or(...)' for a fallback, or 'Value()' to insist. |
| `PC0330` | error | This member is a function | '{0}' is a function, so it has to be called: write '{0}()'. |
| `PC0331` | error | Member needs an instance | '{0}' belongs to each {1} rather than to the {1} type, so it cannot be reached through the name '{1}'. Mark it 'shared', or read it from a value. |
| `PC0332` | error | This produces no value | This produces no value, so there is nothing to use here. |
| `PC0333` | error | Negative exponent on an integer | An integer raised to the power {0} is not a whole number. Raise a fraction instead, as in '(1\|2) ^ {0}', or use 'Math.Pow(...)' for a real result. |
| `PC0334` | warning | This test is always true | {0} is always {1}, so this is always true. |
| `PC0335` | error | Cannot cast to a value type | {0} is a value type, and value types have no inheritance for a cast to follow. |
| `PC0336` | error | Parameter needs a type | Nothing here says what '{0}' holds. Write its type, as in '(integer {0})'. |
| `PC0337` | warning | Not every member is handled | This switch does not handle every {0}: {1} {2} no case. Add one for each, or a 'default' for everything else. |
| `PC0338` | error | This member is a value | '{0}' is a value rather than a function, so it is written without '()'. |
| `PC0339` | error | Member cannot be reached from here | '{0}' is {1} in {2}, so it cannot be reached here. {3} |
| `PC0340` | opinion | This empty string does nothing | 'WriteLine' ends the line by itself. Write 'Console.WriteLine()'. |
| `PC0341` | error | This cannot be formatted | {0} has no 'Format', so ':{1}' says nothing. Leave the ':' out to write it the ordinary way. |
| `PC0342` | error | This works on bits, not on booleans | '{0}' works on the bits of a whole number. For two booleans, '!=' asks whether exactly one of them holds. |
| `PC0343` | error | This shift is outside the width of an integer | An integer holds 64 bits, so a shift of {0} places moves past all of them. An amount from 0 to 63 is what there is to move. |
| `PC0344` | warning | This exception cannot be caught | Nothing catches {0}, so this clause would never run. Remove it. |
| `PC0345` | error | Optional is changed by something that captured it | This is {0}, and checking it proves nothing because a function that captured '{1}' may assign it at any point. Copy it into a local and check that, or use 'Or(...)'. |
| `PC0346` | error | This real has no fraction to become | {0} needs a numerator or denominator larger than an integer holds. Up to eighteen places after the point will convert. |
| `PC0347` | error | A value has no identity to compare | {0} is a value, so asking whether two of them are the same object has no answer. Use '==' to compare what they hold. |

### PC0400 to PC0499

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0400` | error | Used before it is given a value | '{0}' is used here before it has been given a value. Give it one above this line. |
| `PC0401` | error | Not given a value on every path | '{0}' is not given a value on every path that reaches this point. Give it one on every branch, or where it is declared. |
| `PC0402` | error | Field not given a value | '{0}' must be given a value before this constructor ends. Give it one here, or an initializer where it is declared, or make it optional. |
| `PC0403` | warning | Unreachable code | This can never be reached. |
| `PC0404` | error | Not every path yields a value | '{0}' yields {1}, but it can reach its end without yielding one. Yield on every path out. |
| `PC0405` | error | Called before a name it uses is ready | '{0}' uses '{1}', which has not been given a value yet. Call it after '{1}' is set, or move what it needs above this line. |
| `PC0406` | opinion | Nothing here can end this loop | Nothing here breaks, yields, or throws, so nothing will stop this loop. Add a 'break', or give it a condition with 'loop while' or 'until'. |
| `PC0407` | error | Nothing here for this to leave | '{0}' needs a loop around it, and there is none here. A 'switch' is not one: it runs one arm and stops, so there is nothing about it to leave or to go on with. |
| `PC0408` | error | Shared field not given a value | '{0}' is shared, so no constructor runs that could give it a value. Give it one where it is declared, or make it optional. |
| `PC0409` | warning | This is never read | Nothing reads '{0}'. Remove it, or write '_' if the value is not wanted. |
| `PC0410` | warning | Nothing uses this | Nothing uses '{0}'. Remove it, or widen it past 'private'. |
| `PC0411` | warning | Nothing happens here | This works a value out and drops it, and nothing else happens. Remove the line. |
| `PC0412` | opinion | This call's result is dropped | Nothing keeps what this yields. Keep it, or give the function a version that yields nothing. |

### PC0500 to PC0599

Lowering and emission, which report nothing: every program that checks is a program that
compiles.

### PC0600 to PC0699

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC0600` | error | Project file not found | There is no project file at '{0}'. |
| `PC0601` | error | Project has no header | A project file opens with 'project' and a name. |
| `PC0602` | error | Project has no name | 'project' must be followed by a name. |
| `PC0603` | error | Project is not closed | This project is never closed. Add 'end project'. |
| `PC0604` | error | Unrecognized project entry | '{0}' is not something a project file says. A project names files with 'source' and other projects with 'reference'. |
| `PC0605` | error | Source with no path | 'source' must be followed by a file or folder path. |
| `PC0606` | error | Source not found | There is no file or folder at '{0}'. |
| `PC0607` | error | Source is not Profi-C | '{0}' is not a .pc file, so a project cannot build it. |
| `PC0608` | error | Source listed more than once | '{0}' is already part of this project. Remove this line. |
| `PC0609` | error | Folder holds no source | '{0}' holds no .pc files. |
| `PC0610` | error | Project builds nothing | This project lists no source, so there is nothing to build. |
| `PC0611` | error | Imported file not found | There is no file at '{0}', which is looked for beside {1}. |
| `PC0612` | error | Import is not Profi-C | '{0}' is not a .pc file, so it cannot be compiled with this one. |
| `PC0613` | warning | Import names an absolute path | '{0}' names a path from the root of a disk, so it resolves only on the machine it was written on. A path relative to this file travels with it. |
| `PC0614` | warning | Imports form a circle | This import closes a circle: {0}. Files beside one another need no import between them, and a project file spans folders without one. |
| `PC0620` | error | Reference with no path | 'reference' must be followed by the path of a project file. |
| `PC0621` | error | Referenced project not found | There is no project file at '{0}'. |
| `PC0622` | error | Reference is not a project | '{0}' is not a .pcp file. A project references projects; it names files with 'source'. |
| `PC0623` | error | Project referenced more than once | '{0}' is already referenced by this project. Remove this line. |
| `PC0624` | error | Projects reference each other | This reference closes a circle: {0}. Move what both need into a third project they both reference. |
| `PC0625` | error | Two projects claim one file | '{0}' is listed by {1} and by {2}. A file belongs to one project. Let the project that owns it keep it, and have the other reference that project. |
| `PC0626` | error | Nothing named to start at | 'entry' says which Program begins, so a name must follow it, as in 'entry Tools.Program'. |
| `PC0627` | error | More than one 'entry' | A project starts in one place, so it names one 'entry'. |

### PC9000 and up

Numbered well clear of the ranges above, because these are not about the program being compiled.
Every other identifier names something a reader wrote; these name the compiler failing, and no
program should ever produce one.

**The .NET stack trace is printed underneath, in full.** It is of no use to whoever hit it —
there is nothing to write differently that would avoid the fault — and it is the only thing of
use to whoever fixes it, so the message says which of the two the reader is and what the trace
below it is for.

| Identifier | | Reported when | What it says |
|---|---|---|---|
| `PC9000` | error | The compiler hit a problem it has no message for | {0}. This is a fault in the compiler rather than a mistake in this program, and there is nothing to write differently that would avoid it. The .NET stack trace below is what will fix it: please report it, along with the program that caused it. |

