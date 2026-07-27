# Profi-C; Language Summary

A condensed reference and a full comparison to C#. For the normative definition see
[language-spec.md](language-spec.md).

**Profi-C is a teaching language.** It aims to make concepts legible to a beginner while staying close enough to C# that what a student learns transfers. The comparison in sections 5 and 6 is therefore a map of the bridge a student will eventually cross, not a list of gotchas for working C# developers.

---

## 1. Reserved words

### 1.1 The 55 reserved words

```
abstract     and          as           base         begin        boolean
break        case         catch        character    constant     continue
default      each         else         end          enumeration  extends
false        finally      for          fraction     function     global
if           in           integer      is           let          model
namespace    new          not          or           outer        override
protected    public       real         sealed       step         string
structure    switch       then         this         throw        to
true         try          until        using        virtual      while
yield
```

### 1.2 Reserved outside the keyword table (1)

`comment` does not appear in the `Keywords` dictionary, but it is reserved in practice: the scanner tests for it before tokenizing, so the word always starts a comment and can never be an identifier.

```
comment this is a line comment, running to end of line

comment begin
    this is a block comment,
    spanning as many lines as needed
end comment
```

The scanner consumes `comment`, then looks ahead on the same line for `begin`. Finding it opens a block comment closed by `end comment`; anything else makes it a line comment terminating at the newline. An unclosed block comment is a scan error.

Note the asymmetry: the opener is `comment begin` and the closer is `end comment`, reversed rather than repeated.

### 1.3 Deliberately not reserved

`private`, `static`, `null`, `void`, `return`, `class`, `interface`, `enum`, `struct`, `var`, `do`, `foreach`, `select`, `when`, `const`.

Members are private by default, so `public` and `protected` opt out and `private` is unnecessary. `global` fills the role of `static`. There is no `null` at all. And note the spellings: `enumeration`, not `enum`; `constant`, not `const`.

---

## 2. Reserved models

| Model | Role |
|---|---|
| `Model` | implicit root; every model extends it |
| `Program` | `global model`; the entry point container |
| `Console` | `global model`; holds `Write` and `Read` |
| `Reference` | `global model`; holds `Equals` for reference identity |
| `Exception` | root of the throwable hierarchy |
| `DivideByZeroException` | thrown on runtime division by zero |
| `IndexOutOfRangeException` | thrown on out-of-bounds set or string access |
| `EmptyOptionalException` | thrown when an empty optional is unwrapped |
| `InvalidCastException` | thrown when a forced cast fails |
| `FormatException` | thrown when a parse or format operation fails |
| `ArgumentException` | thrown when an argument is invalid |

Users may extend `Exception` and its subtypes. `Model` **is** extendable and is extended implicitly by everything, exactly as `object` is in C#; what cannot be done is redeclaring the name. `Console` and `Reference` are `global model`s, so neither can be extended or instantiated.

---

## 3. Reserved functions and members

### 3.1 Entry point

A `global model` named `Program`, holding `Main()` or `Main(string[] args)`. There is no such
thing as an instance of a running program, so the entry point cannot be instantiated and
cannot hold instance state.

A `global model` has global members; in C#, a `static class` must mark each of its members
`static`. So `function Main()` and `global function Main()` are the same declaration — the
explicit form is legal and redundant.

### 3.2 Built-in model functions

| Call | Notes |
|---|---|
| `Console.Write()` | blank line |
| `Console.Write(string)` | prints the string |
| `Console.Write(string, string[])` | formats with `{0}`, `{1}`, `{2}` |
| `Console.Read(...)` | input |
| `Reference.Equals(Model, Model)` | reference identity |

### 3.3 Members on `Model`

`ToString()` and `Equals()`, both `virtual`.

### 3.4 Built-in type members

| Type | Members |
|---|---|
| Sets | `Insert`, `InsertAt`, `Remove`, `RemoveAt`, `Count`, `Contains`, `IndexOf`, `Clear` |
| `string` | `Insert`, `InsertAt`, `Remove`, `RemoveAt`, `Substring`, `Contains`, `Count`, `IndexOf`, plus indexing |
| Sets | `Remove` yields `boolean`; other mutators return nothing |
| Optionals | `HasValue`, `Or`, `Value` |
| enumerations | `ToInteger`; `n as Color` converts back, yielding `Color?` |
| `Math` | `Sqrt`, `Abs`, `Pow`, `PI`, and friends |
| `Random` | constructed with `new`; returns primitives |
| `DateTime` | properties re-exposed as methods |
| File I/O | reads and writes text |
| `fraction` | `ToReal` |
| enumerations | `ToString` returns the member name |
| `real` | `ToFraction` |

---

## 4. Naming convention

**Nothing at the language level is abbreviated.** `boolean` not `bool`, `enumeration` not `enum`, `function` not `func`, `integer` not `int`. A keyword should be readable aloud without a glossary.

**Library surface is exempt and keeps .NET spellings.** `Math.Sqrt` stays as-is rather than becoming `Mathematics.SquareRoot`, because Profi-C is expected to gain real .NET imports eventually; renaming now would leave two spellings for one function once that lands. The line: anything Profi-C defines is spelled out, anything it borrows keeps its source spelling.

**Keywords are lowercase. Built-ins are PascalCase.** This is deliberate and gives the reader an instant signal about what is language and what is library.

Two consequences of that convention worth knowing:

- `model` declares a type; `Model` is the root type.
- `or` is the boolean operator; `Or` is the optional fallback method.

The second pair is more confusable than the first, since both live near the idea of "otherwise." `a or b` and `a.Or(b)` mean different things. The language is case-sensitive, so they never actually collide, but it is worth watching in code review.

---

## 5. Similar to C#

| Feature | Notes |
|---|---|
| Single inheritance | one base type, no multiple inheritance |
| Explicit `virtual` and `override` | opt-in dispatch, not Java's opt-out |
| `base(...)` and `base.Method()` | constructor chaining and parent calls |
| Exceptions | `try` / `catch` / `finally` / `throw`, matched by type |
| Overloading | including constructors |
| Reference semantics for classes | models are references |
| Lambdas capture by reference | same trap surface, same power |
| Private by default | C# class members are also private by default |
| Definite assignment | C# enforces it for locals; Profi-C extends it to all model references |
| Truncating integer division | `7 / 2` is `3` |
| Compiles to CIL | same runtime, same tooling |
| PascalCase library naming | familiar to any C# reader |

---

## 6. Different from C#

### 6.1 Syntax

| Profi-C | C# |
|---|---|
| `end if`, `end while`, `end model` | `}` for everything |
| no opener; `end if` closes it | `{` opens, `}` closes |
| `begin` is only an anonymous scope | bare `{ }` for the same job |
| `case 1:` with no `break` | `break` required on every case |
| `{1, 2, 3}` is a set literal | `{ }` is a block or initializer |
| `and`, `or`, `not` | `&&`, `\|\|`, `!` |
| `let x = 5;` | `var x = 5;` |
| `integer function Add(...)` | `int Add(...)` |
| `for each c in name` | `foreach (char c in name)` |
| `for integer i = 0 until n` | `for (int i = 0; i < n; i++)` |
| `if c ... end if`, no parens | `if (c) { ... }` |
| `if c then a else b` expression | `c ? a : b` |
| `x as Dog` yields `Dog?` | `x as Dog` yields null on failure |
| `global` | `static` |
| `3\|4` fraction literal | no equivalent |
| `enumeration Color` | `enum Color` |
| `global model` | `static class` |
| a `global model` has global members | a `static class` marks each member `static` |
| `sealed`, `abstract` | identical |
| `structure Point ... end structure` | `struct Point { }` |
| `boolean` | `bool` |


### 6.2 Semantics

**`yield` means return.** This is the single most dangerous difference for a C# reader, since C# uses `yield return` for iterators. In Profi-C it is an ordinary return statement and has nothing to do with lazy sequences.

**No `null`.** Optionals with a `?` suffix replace it, and definite assignment is enforced at compile time. Optional access is **strict**: reading one the compiler cannot prove present is a compile error, not a runtime crash. `Value()` is the explicit escape hatch, as Kotlin's `!!` or Rust's `.unwrap()`.

Null and .NET coexist because **null is translated at the boundary and never enters Profi-C's type system.** Every .NET reference-typed return maps to `T?` unless documented non-null; an empty optional is what a Profi-C program sees. This makes the v1 curated wrappers do exactly what an automatic binder would do later, so no signature churn when one arrives.

**Models are references, structures are values.** Both are user-definable in v1. Structures cannot inherit, cannot contain themselves, and compare field by field.

**Sets and strings are reference types**, like models. `string` is immutable and is `System.String` outright; sets are mutable and are `List<T>`. Assignment aliases rather than copying, as in C#.

**`string` and `character[]` convert implicitly, in both directions.** Each conversion copies, since one is immutable and the other is not. C# requires `ToCharArray()` and `new string(...)`.

**`==` on models and sets is deep by default**, comparing fields and elements recursively with cycle-safe bisimulation. C# gives reference equality unless you override. `Reference.Equals(a, b)` is the C# default behavior, spelled explicitly.

**Deferred to v2:** generics, interfaces, and properties. `abstract` exists in v1 but is a marker only; with no bodiless functions, contracts wait for interfaces. Every C# property becomes a method here, which is why `Count()` has parentheses.

**Fractions are a primitive** with exact rational arithmetic. `fraction` and `real` never implicitly convert in either direction; both are explicit.

**Assignment is a statement.** `if (x = 5)` is a syntax error rather than a warning.

**`this.` is mandatory**, not conventional, and so are `outer.` for an enclosing instance and `ModelName.` for `global` members. Bare identifiers reach only locals and parameters. This makes the local-versus-state distinction visible at every use, which is the distinction a beginner most needs to see.

**Nested models capture their enclosing scope.** C# nested classes are like Java's *static* nested classes and hold no reference to an enclosing instance. Profi-C's do, reached via `outer`, and models may also be declared inside function bodies where they capture locals.

**Loop variables are fresh per iteration** and read-only inside the body. C# only did this for `foreach`, leaving the `for` capture trap intact. Profi-C has no three-clause `for` at all; `for i = 0 until n` and `for each` are the two forms.

**Every construct closes with a qualified `end`**, and the compiler verifies the match. `end while` closing an `if` is an error naming both. C# has one `}` for everything and cannot check intent.

### 6.3 Absent from Profi-C

**Deferred, in stages.** Direct .NET binding arrives over several versions:

| Stage | Contents | Lift |
|---|---|---|
| v2 | generics, interfaces, properties | large; the keystone |
| v3 | `out`/`ref`, indexers, `params`, operator overloading, extension methods, boxing | medium, and freely splittable |
| v4 | `async`/`await`/`Task` | very large on its own |
| v5 | attributes, CLR array type, variance, assembly references, the import mechanism | larger than the v1 compiler |

Every stage is independently valuable as a language improvement. None is justified only by the binder, which sits several versions out.

**Absent with no plans:** events, `struct`, attributes, extension methods, `async`/`await`, iterators, pattern matching, tuples, records, nullable value types, operator overloading, indexer declarations, and partial types.

**Direct access to the .NET BCL** is also absent in v1. Profi-C reaches .NET only through curated built-in models that wrap it; `using` imports Profi-C namespaces, not CLR ones.

Note this is not a foreign function interface problem. Profi-C compiles to CIL and runs on the CLR, so calling `System.Math.Sqrt` is CIL calling CIL with the same collector and calling convention. The obstacle is that Profi-C cannot *name* a generic type, an interface, or a property, so any .NET member using one is unreachable regardless of conversion. Generics, interfaces, and properties are therefore the prerequisites for opening it up, which is why they head the v2 list.

Namespaces and `using` directives **are** present, in both the file-scoped and block forms.

---

## 7. Two details worth knowing

**Exactly one warning exists in the language.** A `switch` over an enumeration that omits members is the only warning-level diagnostic; everything else is an error or is silent. Warnings do not block compilation.

**`Model` is the root of every reference type**, not just user models, which is what lets `Reference.Equals(Model, Model)` accept sets and strings. In emitted CIL it corresponds to `System.Object`, which `System.String` and `List<T>` already derive from, so no adapter is needed.
