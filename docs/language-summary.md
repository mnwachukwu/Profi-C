# Profi-C Language Summary

A condensed reference and a full comparison to C#. For the normative definition see
[language-spec.md](language-spec.md).

**Profi-C is a teaching language.** It aims to make concepts legible to a beginner while staying close enough to C# that what a student learns transfers. The comparison in sections 5 and 6 is therefore a map of the bridge a student will eventually cross, not a list of gotchas for working C# developers.

---

## 1. Reserved words

### 1.1 The 54 reserved words

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

### 1.3 Not reserved

`private`, `static`, `null`, `void`, `return`, `class`, `interface`, `enum`, `struct`, `var`, `do`, `foreach`, `select`, `when`, `const`.

Members are private by default, so `public` and `protected` opt out and `private` is unnecessary. `global` fills the role of `static`. There is no `null` at all. And note the spellings: `enumeration`, not `enum`; `constant`, not `const`.

---

## 2. Reserved models

| Model | Role |
|---|---|
| `Model` | the root of **every** type, values included; extended implicitly |
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
| `OverflowException` | thrown when an arithmetic result does not fit its type |

Users may extend `Exception` and its subtypes. `Model` **is** extendable and is extended implicitly by everything, exactly as `object` is in C#; what cannot be done is redeclaring the name. `Console` and `Reference` are `global model`s, so neither can be extended or instantiated.

**`Model` roots the value types too.** Structures and enumerations inherit its members, which is where their `ToString()` and `Equals()` come from, exactly as a C# struct inherits from `object`. What they cannot do — **permanently, not merely for now** — is be *assigned* to a `Model` variable. That conversion is boxing, and Profi-C does not have it.

Inheriting members without being convertible is not a Profi-C peculiarity: a C# `ref struct` such as `Span<T>` sits in the `object` hierarchy and has `ToString()`, yet assigning one to `object` is a compile error. Unboxable value types are a shape the runtime is built around rather than a fringe case.

The guarantees this buys are therefore permanent properties of the language: no allocation hiding behind an ordinary-looking assignment, no two copies of one value comparing unequal by reference, and `Reference.Equals` on a structure rejected while compiling rather than answering false at runtime.

Generics do not change this. .NET generics are reified, so `Set<Point>` stores its elements inline with no boxing and no common root required — unlike Java, which erases to `Object` and therefore must box. Nor does .NET interop: a curated wrapper may box internally when calling a BCL method that takes `object`, but that is an implementation detail of the call, invisible from Profi-C, and never a conversion the language admits.

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
| `Console.Write(value)` | prints any value, leaving the cursor on the same line |
| `Console.WriteLine(value)` | prints any value, then ends the line |
| `Console.WriteLine()` | a blank line |
| `Console.Write(string, string[])` | formats with `{0}`, `{1}`, `{2}` |
| `Console.Read(...)` | input |
| `Reference.Equals(Model, Model)` | reference identity |

`Write` and `WriteLine` behave exactly as in C#: only the second ends the line.

Neither is an overload set. Both accept a value of **any** type and the compiler selects how
to render it from the static type, the same way the optional members in section 3.4 are
compiler-known rather than generic. This is why printing an enumeration or a structure needs
no ceremony even though neither is a `Model`.

### 3.3 `ToString()`

Inherited from `Model` by **every** type, values included, so there is one place it comes
from. It is `virtual`, and structures may override it as freely as models do. Calling it on a
value type does not box: it compiles to a direct call, the same way `5.ToString()` does in C#.

The defaults differ, and the difference is forced rather than chosen:

| Type | Default |
|---|---|
| Structure | Field by field, as `Point { X = 1, Y = 2 }` |
| Enumeration | The member name |
| Model | The type name |

A structure cannot contain itself, so walking its fields terminates. A model can take part in
a cycle, and while deep `==` solves that with bisimulation, no equivalent trick exists for
printing — so models print their type name and an author who wants more overrides it.

`Model` also defines `Equals()`, likewise `virtual`.

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

**Keywords are lowercase. Built-ins are PascalCase.** The casing tells a reader at a glance what is language and what is library.

Two consequences of that convention:

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
| Nested types are isolated | a nested model holds no reference to the model it sits inside, exactly as a C# nested class does not. One that needs its enclosing instance takes it as a constructor argument |
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
| `for i = 0 until n` | `for (int i = 0; i < n; i++)` |
| `if c ... end if`, no parens | `if (c) { ... }` |
| `if c then a else b` expression | `c ? a : b` |
| `x as Dog` yields `Dog?` | `x as Dog` yields null on failure |
| `global` | `static` |
| `3\|4` fraction literal | no equivalent |
| `2 ^ 10` raises to a power | `^` is exclusive-or; use `Math.Pow` |
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

**Models are reference types, structures are value types.** Both are user-definable in v1. Structures cannot inherit from another type, cannot contain themselves, and compare field by field.

They do inherit `Model`'s members, which is where `ToString()` and `Equals()` come from, but they can never be **assigned** to a `Model` variable — that conversion is boxing, which Profi-C does not have and does not plan to add. So `Reference.Equals` on a structure stays a compile error rather than a runtime puzzle, and no assignment allocates behind your back. C# is looser here: assigning a struct to `object` compiles and quietly allocates.

**Sets and strings are reference types**, like models. `string` is immutable and is `System.String` outright; sets are mutable and are `List<T>`. Assignment aliases rather than copying, as in C#.

**`string` and `character[]` convert implicitly, in both directions.** Each conversion copies, since one is immutable and the other is not. C# requires `ToCharArray()` and `new string(...)`.

**`+` concatenates when either side is a string**, converting the other side implicitly through its `ToString()`. `"score: " + 42` is a string, in either order. As in C#, and deliberately without requiring an explicit call.

**`==` on models and sets is deep by default**, comparing fields and elements recursively with cycle-safe bisimulation. C# gives reference equality unless you override. `Reference.Equals(a, b)` is the C# default behavior, spelled explicitly.

**Deferred to v2:** generics, interfaces, and properties. `abstract` exists in v1 but is a marker only; with no bodiless functions, contracts wait for interfaces. Every C# property becomes a method here, which is why `Count()` has parentheses.

**Fractions are a primitive** with exact rational arithmetic. `fraction` and `real` never implicitly convert in either direction; both are explicit.

**`^` raises to a power** and is the only right-associative operator, so `2 ^ 3 ^ 2` is 512. It binds tighter than a leading minus, making `-2 ^ 2` equal to `-4` as on paper. A **whole** exponent preserves the base's type: an integer base gives an integer, and a fraction base stays exact, so `(1|2) ^ -3` is `8|1`. Any other exponent takes a root and gives a real, so `9 ^ 1|2` is `3` and `16 ^ 3|4` is `8` — the one place a fraction widens to a real unasked, since a root has no exact rational form to preserve. **In C# `^` is exclusive-or**; Profi-C has no bitwise operators, so the meaning does not carry across.

**Assignment is a statement.** `if (x = 5)` is a syntax error rather than a warning.

**`this.` is mandatory**, not conventional, and so is `ModelName.` for `global` members. Bare identifiers reach only locals and parameters. This makes the local-versus-state distinction visible at every use, which is the distinction a beginner most needs to see.

**No local types.** A model, structure, or enumeration may be declared at namespace level or inside a model, but not inside a function. C# has no local classes either. A type introduced by a statement would entangle name resolution with statement order, which is a cost with very little to buy it.

**Loop variables are fresh per iteration** and read-only inside the body. C# only did this for `foreach`, leaving the `for` capture trap intact. Profi-C has no three-clause `for` at all; `for i = 0 until n` and `for each` are the two forms.

**Every construct closes with a qualified `end`**, and the compiler verifies the match. `end while` closing an `if` is an error naming both. C# has one `}` for everything and cannot check intent.

### 6.3 Absent from Profi-C

**Deferred, in stages.** Direct .NET binding arrives over several versions:

| Stage | Contents | Lift |
|---|---|---|
| v2 | generics, interfaces, properties | large; the keystone |
| v3 | `out`/`ref`, indexers, `params`, operator overloading, extension methods | medium, and freely splittable |
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
