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
- [2. Tokens and reserved words](#2-tokens-and-reserved-words) — the 57 words, the operators, end of file
  - [2.1 Reserved words](#21-reserved-words)
  - [2.2 Operators and punctuation](#22-operators-and-punctuation)
  - [2.3 End of file](#23-end-of-file)
  - [2.4 Recovery](#24-recovery)
- [3. Types](#3-types) — base types, suffixes, function types, values and references
  - [3.1 The base types](#31-the-base-types)
  - [3.2 The two suffixes](#32-the-two-suffixes)
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
  - [10.2 Throwing and catching](#102-throwing-and-catching)
  - [10.3 Declaring an exception](#103-declaring-an-exception)
- [11. The standard library](#11-the-standard-library) — the built-in models and what they provide
  - [11.1 On every type](#111-on-every-type)
  - [11.2 On a set](#112-on-a-set)
  - [3.2b Only on a set of optionals](#32b-only-on-a-set-of-optionals)
  - [11.3 On a string](#113-on-a-string)
  - [11.4 On a value of a particular type](#114-on-a-value-of-a-particular-type)
  - [11.5 The standard models](#115-the-standard-models)
  - [11.5b Chance](#115b-chance)
  - [11.5c Moments](#115c-moments)
  - [11.5d Spans, days, and times of day](#115d-spans-days-and-times-of-day)
  - [11.5e Files and folders](#115e-files-and-folders)
  - [11.5a How far a real answer can be trusted](#115a-how-far-a-real-answer-can-be-trusted)
  - [11.6 How a value prints](#116-how-a-value-prints)
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
  - [PC0600 to PC0699](#pc0600-to-pc0699)

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

**Every construct says what it closes.** `end if`, `end while`, `end model`. The compiler
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

```
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

```
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

```
"""
say "hi"
"""
```

### 1.5a Interpolated strings

A string literal may hold expressions, written between **doubled braces**:

```
Console.WriteLine("{{apples}} apples and {{pears}} pears is {{apples + pears}} fruit");
```

**A single brace is ordinary text.** Only a pair opens a hole, so `"a set is {1, 2}"` needs
nothing done to it. This is why the braces are doubled rather than the literal being marked
with a prefix: the cost is paid only where interpolation is used, instead of by every string
that happens to contain a brace. To write a literal pair, escape the first: `"\{{"`.

**A hole holds any expression**, including a call, a conditional, or a string that interpolates
in turn. The scanner counts braces opened inside a hole, so `"{{ {1, 2}.Count() }}"` closes at
the right pair.

**A colon says how to write the value.** What follows it is a pattern rather than code, taken
whole to the closing braces:

```
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

Profi-C has **56** reserved words. A name may take one back by writing `@` in front of it —
`@end`, `@step` — which is the only place a name may begin with something other than a letter.

```
abstract     and          as           base         begin        bitwise
boolean      break        case         catch        character    constant
continue     default      delegate     each         else         end
enumeration  extends      false        finally      for          fraction
function     global       if           import       in           integer
internal     is           let          model        namespace    new
not          or           override     protected    public       real
sealed       shiftleft    shiftright   step         string       structure
switch       then         this         throw        to           true
try          until        using        virtual      while        xor
yield
```

These are every reserved word, and nothing is reserved outside the list. A comment is marked
rather than named ([§1.3](#13-comments)), so it takes no word away from a program.

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
then `scores.Insert(60);`. [§11](#11-the-standard-library) lists its members.

An **optional** is how a value may be absent. There is no `null`; a `Node` always holds a
node, and `Node?` is the type that may not. [§8](#8-optionals) gives the rules.

### 3.3 Function types

A function type is written with **`delegate`** — the result, then `delegate`, then what it
takes:

```
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

```
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
Function[] all = { held, Program.Twice, (string s) yield Console.WriteLine(s) };
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

```
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

```
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

```
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

```
let value = 1;
begin
    let value = 2;          PC0237: 'value' is already the name of something here
end

let items = {1, 2};
let show = (integer items) yield items;    PC0237, for the same reason
```

Two scopes that cannot see one another are not in conflict, so the same name may be used in
each:

```
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

```
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
    protected integer limit;            and anything extending Account
    internal integer revision;          and anything in this project
    public string owner;                and anywhere at all
    global integer opened;              one per program, not one per account
end model
```

There is no `private` keyword, because private is what you get by writing nothing. `protected`,
`internal`, and `public` each widen that, and only one of them may be written on a declaration
(`PC0219`). `global` is what other languages call `static`: the member belongs to the type
rather than to an instance, and says nothing about who may reach it.

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
    public function Account(integer opening)
        this.balance = opening;
    end function
end model
```

Modifiers are `public`, `protected`, `internal`, `global`, and `virtual` or `override`. [§7.2](#72-virtual-dispatch)
covers the last two. **A function that declares a result must reach a `yield` on every path** — `PC0404`
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

```
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

```
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
A lambda is written as [§9](#9-functions-and-closures) describes. Parentheses group.

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

**A range loop reads its header on every turn.** The starting value is read once, because
there is nothing else it could mean; the bound and the step are read again at the top of each
turn, so `until x` is a condition about `x` as it stands rather than as it was:

```
integer limit = 10;

for i = 1 until limit    three turns: the bound moves down to meet the counter
    limit = limit - 2;
end for
```

A bound that moves the other way never ends the loop, which is allowed and is the same thing
`while true` is. Both are the program saying so.

The step is read at the same moment as the bound, so one turn reads the header once: whatever
decided that this turn runs is what advances to the next. Only the counter is out of reach —
it belongs to the loop and cannot be assigned to (`PC0206`).

This matches a C-style `for`, whose condition and increment are both live:
`for (int i = 0; i < x; i++) x++;` never finishes there either.

**A `for each` takes its sequence as it stands.** It names a sequence rather than a bound, so
its length is read once, when the loop begins. Modifying the sequence inside its own loop is
refused (`PC0243`) rather than left to mean something subtle.

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

```
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
model, which is the case [§3.4](#34-values-and-references) flags and the reason `constant` does not accept such a
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
inner instance. Types may not be declared inside a function body — see [§4.4](#44-functions).

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

# No guard, so this is still an optional: Value() unwraps it.
let t = reading.Value();            # t is a Temperature
Console.WriteLine(t.Describe());

if reading.HasValue()
    # Narrowed, so this is a Temperature: Value() is the model's.
    let degrees = reading.Value();  # degrees is a real
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
integer delegate(integer) tripled = Program.Triple;

Counter counter = new Counter(10);
integer delegate() advance = counter.Next;
```

A member reached through an instance is that member *bound to that instance*, so calling it
later still knows which one it belongs to.

**A lambda closes over the variables around it.** The function handed back below remembers
`by`, which belonged to the call that made it:

```
integer delegate(integer) function AdderOf(integer by)
    yield (n) yield n + by;
end function

let addFive = Program.AdderOf(5);

Console.WriteLine(addFive(3));            8
```

**That first line comes apart in four pieces**, and each word says which:

```
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

```
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

Eight exception types, and every one descends from `Exception`:

| | Raised when |
|---|---|
| `Exception` | The root. A `catch` naming it takes them all |
| `DivideByZeroException` | Dividing by a value that turned out to be zero |
| `IndexOutOfRangeException` | Indexing a set or string outside it |
| `EmptyOptionalException` | `Value()` on an optional holding nothing |
| `SequenceChangedException` | a set changed while a `for each` was walking it |
| `InvalidCastException` | A conversion that could not be made |
| `FormatException` | Text that could not be read as what was wanted |
| `ArgumentException` | An argument a function will not accept |
| `OverflowException` | A result too large for the type to hold |
| `IOException` | Anything going wrong with a file except its not being there, which is an absent optional instead ([§11.5e](#115e-files-and-folders)) |

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
`Exception` and its subtypes, `Console`, `Reference`, `Math`, `Fraction`, `Random`,
`DateTime`, `TimeSpan`, `Date`, `Time`, `File`, and `Directory`. Of these only `Exception` may
be extended.

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

**Four of them hold no values** — `Console`, `Math`, `Reference`, and `Fraction`. They are
names to reach members through, and naming one where a value's type belongs is an error
(`PC0233`), as it is for any `global model`, which has no instances by definition:

```
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
| `Union(other)` | a new set: this one's elements then the other's |
| `Intersect(other)` | a new set: the elements also in the other, in this one's order |
| `Except(other)` | a new set: the elements not in the other |
| `Distinct()` | a new set: one of each, keeping the first of every repeat |
| `Join(separator)` | `string`; every element written out, with the separator between |

`Subset`'s end is **exclusive**, the reading `until` has, which is what makes
`xs.Subset(0, n)` and `xs.Subset(n)` add back up to the whole set.

**These are not the operations of the same name in mathematics**, because a Profi-C set is not
one: it keeps its order and a value may appear in it twice. So `Union` **appends** rather than
merging, and `{1, 2}.Union({2, 3})` is `{1, 2, 2, 3}`. `Distinct` is how a program asks for one
of each, and `xs.Union(ys).Distinct()` is the union of mathematics said in two steps — which is
two steps because they are two decisions: put them together, then decide about the repeats.

`Intersect` and `Except` divide a set in two between them: every element goes to exactly one
answer. Putting those back together therefore returns every element, though in the order they
were divided rather than the order they started in.

Membership everywhere here — `Contains`, `IndexOf`, `Intersect`, `Except`, `Distinct` — is the
same deep comparison `==` makes ([§11.1](#111-on-every-type)), so all of them agree about what counts as the same
value.

`Join` reads on the set rather than on a string, because the thing being joined is the
collection; it is the counterpart of `Split` on a string ([§11.3](#113-on-a-string)). Any set answers it, not only
a set of strings: each element is written the way it would be written on its own.

**Nothing that yields a set changes one.** `Subset` and the four in [§3.2b](#32b-only-on-a-set-of-optionals) hand back a new set;
`Insert`, `InsertAt`, `RemoveAt` and `Clear` change the set and yield nothing, and `Remove`
yields only whether it found something. So the two groups are told apart by their result, and
a set you were given is never quietly the set someone else is holding.

The copy is **shallow**, which is the depth the rest of the language uses: assigning a model
copies the reference ([§3.4](#34-values-and-references)), so a set copied out holds the very same models.

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
| `optional.HasValue()`, `Or(fallback)`, `Value()` | [§8](#8-optionals) |
| `fraction.ToReal()` | `real` |
| `fraction.Reciprocal()` | `fraction`; the fraction turned over. Zero has no reciprocal and refuses |
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
| `Math.Sinh`, `Cosh`, `Tanh`, `Asinh`, `Acosh`, `Atanh` | the hyperbolic six, and their inverses |
| `Math.Atan2(y, x)` | `real`; takes the two sides, so it knows the quadrant |
| `Math.Abs(x)` | the type it was given — `integer`, `real`, or `fraction` |
| `Math.Min(a, b)`, `Math.Max(a, b)` | the type they were given |
| `Math.Floor(x)`, `Math.Ceiling(x)`, `Math.Round(x)` | `integer`, from a `real` or a `fraction` |
| `Math.Factorial(n)` | `integer`; overflows past 20 |
| `Fraction.Create(numerator, denominator)` | `fraction` |
| `Fraction.Create(whole)` | `fraction`; a whole number over one |
| `Random.Next()`, `Next(below)`, `Next(low, high)` | `integer`; the high bound is **excluded** |
| `Random.NextDouble()` | `real`, from zero up to but never reaching one |
| `DateTime.Now`, `DateTime.Today` | `DateTime`. **Values, so written without `()`** |
| `Format(pattern)` | `string`. On `integer`, `real`, `fraction`, and all four date and time types |
| `ToInteger()`, `ToReal()`, `ToBoolean()`, `ToFraction()` | On `string`; `integer?`, `real?`, `boolean?`, `fraction?` |
| `DateTime.Date`, `DateTime.Time` | `Date` and `Time`. **Values, so written without `()`** |
| `new DateTime(date)`, `new DateTime(date, time)` | A moment built from its halves; the first takes midnight |
| `DateTime.Parse(text)`, `Parse(text, pattern)` | `DateTime?`, and the same pair on `Date`, `Time` and `TimeSpan` |

**`Format` and `Parse` are the two directions of the same thing**, and both take .NET's own
patterns unchanged — `F2`, `N0`, `yyyy-MM-dd`, `dddd` — so a pattern learned here is one that
works in C#. Everything is invariant unless the pattern says otherwise, which is what lets a
program print and read the same on any machine. A pattern the runtime cannot use raises
`FormatException`; a pattern given to a type that has no `Format` is `PC0341`, caught while
compiling.

**`Parse` yields an optional rather than raising.** Text that will not read is the ordinary
case, not an exceptional one, since most of it arrives from a person typing — so there is
nothing to catch and no second variable to pass in, and the type says the answer may be absent
where C# needs either an exception or a `TryParse`. Given a pattern, it reads exactly that
pattern, which is how a value written by one is read back by the same one.

**A number, a truth or a ratio is read off the text rather than off the type**, because
`integer` is a reserved word and cannot stand in front of a dot: `"42".ToInteger()`, not
`integer.Parse("42")`. Each yields an optional for the same reason `Parse` does. `ToFraction`
accepts either mark between the halves — the language writes `22|7` because a slash already
means division, a person writes `22/7` because that is what a fraction looks like everywhere
else, and reading takes both. A bare whole number reads as a ratio over one.

Together with `Console.Read`, which yields `string?` at the end of input, this is what makes
asking a person for a number two questions rather than one: was anything typed, and did what
was typed read. Neither can be skipped, because an optional cannot be used until its presence
is proven.

**`Math.Log` of one number is the natural logarithm** — log to base `e`, what mathematicians
write as `ln`. C#, Java and C all mean the same by the name, so a program moved between them
gives the same answer. For base ten, write `Math.Log10(x)`, or `Math.Log(x, 10)`.

**A root, a power and a logarithm leave the rationals**, so all of them answer in reals
whatever they were given: the square root of a fraction is usually irrational. Everything else
has a version for each number the language has, because an answer that arrives as a `real`
cannot be counted with and a `fraction` that widens to one stops being exact.

### 11.5b Chance

Two shapes, and both are .NET's, unchanged:

```
Random rolls = new Random(42);        a generator of your own, seeded
Random any = new Random();            seeded from the clock

rolls.Next(1, 7)                      a die: 1 to 6
Random.Next(1, 7)                     the same, from the one the language keeps
```

**`Next` excludes its upper bound**, so a die is `Next(1, 7)` rather than `Next(1, 6)`. Everyone
reads that wrong once; reading it the other way here would mean reading it wrong a second time
in whatever language they moved to afterwards.

**The shared generator cannot be seeded**, as .NET's shared one cannot. A program that needs
the same sequence twice holds its own — and holding its own is the thing that makes it
reproducible, since nothing else can then disturb it.

### 11.5c Moments

A `DateTime` is constructed from a date, or from a date and a time:

```
DateTime landing = new DateTime(1969, 7, 20);
DateTime liftoff = new DateTime(1969, 7, 16, 13, 32, 0);
```

**What .NET reads as a property is read as one here** — `landing.Year`, `landing.DayOfWeek`,
`DateTime.Now` — without parentheses. `AddDays`, `AddHours`, `AddMinutes`, `AddSeconds`,
`AddYears` and `AddMonths` are functions and are called.

A moment never changes: adding to one yields another and leaves the first alone, as adding to
a string does. Two moments holding the same instant are equal, since `==` compares values.

Ordering is `CompareTo`, which yields a negative number when this moment comes first, zero
when they are the same, and a positive number when it comes after. There are no comparison
operators on it.

A date that is not one — the thirty-first of February — throws `ArgumentException` naming the
numbers that were written.

### 11.5d Spans, days, and times of day

Three more types sit beside `DateTime`, and the difference between them is what each leaves
out:

| | Holds | Leaves out |
|---|---|---|
| `DateTime` | a moment | nothing |
| `Date` | a day | the time of day |
| `Time` | a time of day | the day |
| `TimeSpan` | how long something lasted | when it happened |

**`TimeSpan` is what subtracting one moment from another leaves behind**, and what adding to a
moment takes:

```
TimeSpan mission = landing.Subtract(liftoff);     4.06:45:40
Console.WriteLine(mission.TotalHours);            102.76...
Console.WriteLine(mission.Days);                  4
liftoff.Add(mission)                              back to landing
```

Its **parts** and its **totals** answer different questions. An hour and a half has `Hours` of
1 and `Minutes` of 30 — how you would say it — and `TotalMinutes` of 90, which is how you would
measure it. A span may run backwards, and the sign survives being printed: `-00:30:00`.

**`Date` is a day with no time of day.** A birthday is one: it is the same day wherever you are
and whatever hour it is, and holding it as a moment forces a midnight onto it that nobody
meant. **`Time` is a time of day with no day** — opening hours are these. Adding to a `Time`
wraps around midnight, because a clock does.

The two make a moment together, and a moment comes apart into them:

```
birthday.ToDateTime(opening)      a Date and a Time make a DateTime
Date.FromDateTime(moment)         and a moment comes apart
Time.FromDateTime(moment)
```

.NET calls these two `DateOnly` and `TimeOnly`.

A `Time` is not a `TimeSpan`, though both are written with colons. A span is how long something
lasted, may exceed a day, and may run backwards; a `Time` is a reading on a clock and always
sits between midnight and the next one.

### 11.5e Files and folders

**A file is read whole or written whole.** There is nothing to open and nothing to close, and
no way to read part of one. Holding a file open needs an object with state that must be
released afterwards, and v1 has neither interfaces nor anything that releases itself — but the
restriction is not only that: whole-file is what a program being learned on wants, and it
removes the mistake every beginner makes with the other kind.

| `File` | |
|---|---|
| `Read(path)` | `string?`; the whole file as text |
| `ReadLines(path)` | `string[]?`; one entry per line, endings removed |
| `Write(path, text)` | replaces what was there, making the file if there is none |
| `WriteLines(path, lines)` | the same, one line per entry |
| `Append(path, text)` | adds to the end |
| `Exists(path)` | `boolean` |
| `Delete(path)` | `boolean`; whether there was one to delete |
| `Copy(from, to)`, `Move(from, to)` | both replace what is at the destination |
| `Size(path)` | `integer?`; bytes |
| `Changed(path)` | `DateTime?`; when it was last written |

| `Directory` | |
|---|---|
| `Current` | `string`. **A value, so written without `()`** |
| `Exists(path)` | `boolean` |
| `Create(path)` | makes every folder on the way |
| `Delete(path)` | `boolean`; takes what is inside with it |
| `Files(path)`, `Folders(path)` | `string[]?`; sorted |

**A missing file is an absent optional; everything else raises `IOException`.** These are
different questions and the language keeps them apart. Whether a file is there is ordinary, and
answering it with absence means the common case — read it if it is there — needs no guard, and
no `Exists` check that the file could slip out from under between the asking and the reading.
A locked file, a folder that is not there, a path the system will not take, a disk with no room:
none of those can be said with absence, because absence cannot say which happened.

`Folders` rather than `Directories` only because `Directory.Directories` reads as a stutter;
the two words mean the same thing. Both members correspond to .NET's `GetFiles` and
`GetDirectories`, with the `Get` dropped as it is everywhere else here.

**Text is UTF-8 with no byte-order mark.** Writing ends every line with `\n`; reading accepts
either that or `\r\n` and returns neither, so a file written on one machine reads as the same
lines on another. Listings come back sorted rather than in whatever order the file system
offers, so a program prints the same list twice and on two machines.

**Writing does not create the folder it writes into.** A path with a typo in it fails, rather
than quietly building somewhere nobody meant. `Directory.Create` is the way to say it on
purpose, and it does make every folder on the way.

Paths are handed to the system as written. A forward slash separates folders on every platform.

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
which [§12.1](#121-what-a-compilation-is-made-of) explains. The extension may be omitted: `pc run hello` finds `hello.pc`, and asks
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

```
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
picking by the order files were listed would make a build's behaviour depend on the order of
its own file list.

**A project names it**, and only needs to where there is a choice:

```
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

```
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

```
Shapes.Circle flat = new Shapes.Circle();
Solids.Circle round = new Solids.Circle();
```

A qualified name works in every position a type appears — a variable's type, a parameter, a
result, after `new`, and as the receiver of a global member — and needs **no `using`**, since a
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

```
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
|---|---|---|
| `error` | genuinely unpredictable without the diagnostic | yes |
| `warning` | clear, and unlikely to be what was intended | no |
| `opinion` | clear, intended, and correct — and the language would write it differently | no |

An opinion always says that some written token has no effect. Nothing is wrong with a program
that has one, and reading them in order is a reasonable way to learn what the language expects
of a reader.

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
| `PC0009` | opinion | This name needs no '@' |
| `PC0010` | error | Nothing to escape |
| `PC0011` | error | Unterminated interpolation |
| `PC0012` | error | Nothing to interpolate |
| `PC0013` | error | Unterminated block string |
| `PC0014` | error | Nothing to format by |
| `PC0015` | warning | More quotes in a row than close the block |
| `PC0016` | error | Block string delimiters differ in length |
| `PC0017` | error | This base has no digits |
| `PC0018` | error | This digit is not in the base |
| `PC0019` | error | This exponent has no digits |
| `PC0020` | error | This separator has no digits after it |
| `PC0021` | error | A name cannot begin with a digit |
| `PC0022` | warning | This 'ignore' names no diagnostic |
| `PC0023` | warning | That diagnostic cannot be ignored |
| `PC0024` | opinion | This 'ignore' silences nothing |
| `PC0025` | warning | This 'ignore' names neither a severity nor a diagnostic |

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
| `PC0111` | opinion | A range loop's counter has no written type |
| `PC0112` | error | An if expression has no 'else' |
| `PC0113` | error | Too many problems |
| `PC0114` | error | This word is reserved |
| `PC0115` | opinion | This parameter's type is already known |
| `PC0116` | error | A function's type is written with 'delegate' |
| `PC0117` | error | A function's type is written with 'delegate' |
| `PC0118` | error | Only 'and' or 'or' may follow 'bitwise' |
| `PC0119` | error | 'let' declares a local, not a field |

### PC0200 to PC0299

| Identifier | | Reported when |
|---|---|---|
| `PC0200` | error | Name not found |
| `PC0201` | error | Type not found |
| `PC0202` | error | Name already declared |
| `PC0203` | warning | This shadows a type the language provides |
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
| `PC0219` | error | Two visibilities on one declaration |
| `PC0220` | error | A type cannot be protected |
| `PC0221` | error | Type belongs to another project |
| `PC0222` | error | Nothing to override |
| `PC0223` | error | Overridden function is not virtual |
| `PC0224` | error | This hides a function from the base |
| `PC0225` | error | Override yields a different result |
| `PC0226` | error | This name is offered by more than one namespace |
| `PC0227` | error | No such namespace |
| `PC0228` | error | This namespace is already used here |
| `PC0229` | error | Standard belongs to the language |
| `PC0230` | opinion | Standard is already in scope |
| `PC0231` | error | This belongs above any namespace |
| `PC0232` | opinion | This namespace repeats one around it |
| `PC0233` | error | Nothing can be of this type |
| `PC0234` | error | Which program starts? |
| `PC0235` | error | No such program |
| `PC0236` | opinion | This 'entry' decides nothing |
| `PC0237` | error | This name is already in use here |
| `PC0238` | error | This function needs a body |
| `PC0239` | error | An abstract function has no body |
| `PC0240` | error | Only an abstract model may leave a function open |
| `PC0241` | error | An inherited function is still open |
| `PC0242` | opinion | An abstract function is already virtual |
| `PC0243` | error | This changes the sequence being walked |
| `PC0244` | warning | This documentation has nothing to document |
| `PC0245` | warning | This documents a parameter that is not there |
| `PC0246` | warning | This describes a value that is never given back |
| `PC0247` | opinion | This is documented twice |

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
| `PC0339` | error | Member cannot be reached from here |
| `PC0340` | opinion | This empty string does nothing |
| `PC0341` | error | This cannot be formatted |
| `PC0342` | error | This works on bits, not on booleans |
| `PC0343` | error | This shift is outside the width of an integer |

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
| `PC0614` | warning | Imports form a circle |
| `PC0620` | error | Reference with no path |
| `PC0621` | error | Referenced project not found |
| `PC0622` | error | Reference is not a project |
| `PC0623` | error | Project referenced more than once |
| `PC0624` | error | Projects reference each other |
| `PC0625` | error | Two projects claim one file |
| `PC0626` | error | Nothing named to start at |
| `PC0627` | error | More than one 'entry' |

