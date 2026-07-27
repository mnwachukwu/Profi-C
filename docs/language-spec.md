# The Profi-C Language Specification

**Version 0.1.0 (draft). This document is incomplete by design.**

Sections are written as each part of the language is implemented and covered by tests, so
that the specification never runs ahead of the compiler. A previous draft did exactly that
and drifted into describing a different language than the one being built; the discipline
here is meant to prevent a repeat.

The language design itself is settled. What is unfinished is this document and the compiler,
not the decisions. Until a section is written, [language-summary.md](language-summary.md)
is the best available description of the area.

| Section | State |
|---|---|
| 0. Overview | Written |
| 1. Lexical structure | Not yet written |
| 2. Tokens and reserved words | Not yet written |
| 3. Types | Not yet written |
| 4. Declarations | Not yet written |
| 5. Expressions | Not yet written |
| 6. Statements | Not yet written |
| 7. Models, structures, enumerations | Not yet written |
| 8. Optionals | Not yet written |
| 9. Functions and closures | Not yet written |
| 10. Exceptions | Not yet written |
| 11. The standard library | Not yet written |
| 12. Execution and entry point | Not yet written |

---

## 0. Overview

### 0.1 Identity

| | |
|---|---|
| Name | Profi-C |
| Source file extension | `.pfc` |
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
and calling convention. It deliberately matches C# on single inheritance, explicit
`virtual`/`override`, exceptions, overloading, reference semantics for classes, private-by-
default members, and truncating integer division.

It deliberately differs where C# would teach the wrong lesson. The differences that matter
most to a C# reader:

- **`yield` means return.** This is the single most dangerous difference, since C# uses
  `yield return` for iterators. In Profi-C it is an ordinary return statement.
- **There is no `null`.** Optionals replace it, and access is strict.
- **`==` is deep by default** on models and sets, comparing structurally with cycle-safe
  bisimulation. `Reference.Equals(a, b)` spells out C#'s default behavior.
- **`this.` is mandatory**, not conventional.
- **Assignment is a statement**, so `if x = 5` is a syntax error rather than a warning.

[language-summary.md](language-summary.md) carries the full comparison table.

### 0.5 Conformance and terminology

The key words **must**, **must not**, **required**, **shall**, **should**, and **may** are to
be interpreted as describing requirements on a conforming implementation.

A **diagnostic** is a message a conforming implementation produces about a source program.
Diagnostics carry a stable identifier of the form `PFC` followed by four digits, a severity,
and a source span. Two severities exist: **error**, which prevents compilation, and
**warning**, which does not.

### 0.6 Versioning

This document describes **v1**. Features named as deferred are not part of v1 and a
conforming v1 implementation must reject them:

- Generics, interfaces, and properties (v2)
- `out` and `ref` parameters, indexers, `params`, operator overloading, extension methods,
  and boxing (v3)
- `async`, `await`, and `Task` (v4)
- Direct binding to arbitrary .NET types (v5)

Deferred is not rejected. Each is expected to arrive as an additive change.

---

## 1. Lexical structure

*Not yet written.* Will cover source encoding, line terminators, whitespace, the two comment
forms, identifiers, and the five literal forms including escape sequences.

## 2. Tokens and reserved words

*Not yet written.* Profi-C has **55 reserved words**; they are listed in
[language-summary.md](language-summary.md) section 1.

## 3. Types

*Not yet written.* Will cover the base types, the `[]` set and `?` optional suffixes, the
value and reference split, conversions, and definite assignment.

## 4. Declarations

*Not yet written.* Will cover variables, `constant`, fields, functions, and visibility.

## 5. Expressions

*Not yet written.* Will cover the nine precedence levels, `is` and `as`, the
`if ... then ... else` expression, lambdas, and collection literals.

## 6. Statements

*Not yet written.* Will cover block structure and the qualified `end`, the two `for` forms,
`switch`, and `try`.

## 7. Models, structures, and enumerations

*Not yet written.* Will cover inheritance, constructors, virtual dispatch, deep equality,
value-typed structures, and enumerations.

## 8. Optionals

*Not yet written.* Will cover `HasValue`, `Or`, and `Value`, and the narrowing rules that
make optional access strict.

## 9. Functions and closures

*Not yet written.* Will cover function types, overload resolution, capture, and name
resolution.

## 10. Exceptions

*Not yet written.* Will cover `try`, `catch`, `finally`, `throw`, and the built-in hierarchy.

## 11. The standard library

*Not yet written.* Will cover the built-in models and the curated .NET wrappers.

## 12. Execution and entry point

*Not yet written.* Will cover program structure and `Program.Main`.
