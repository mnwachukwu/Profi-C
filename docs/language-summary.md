# Profi-C Language Summary

A condensed reference and a full comparison to C#. For the normative definition see
[language-spec.md](language-spec.md).

**Profi-C is a teaching language.** It aims to make concepts legible to a beginner while staying close enough to C# that what a student learns transfers. The comparison in sections 5 and 6 is therefore a map of the bridge a student will eventually cross, not a list of gotchas for working C# developers.

---

## 1. Reserved words

### 1.1 The 61 reserved words

```
abstract    and         as          base        begin       bitwise
boolean     break       case        catch       character   constant
continue    default     delegate    each        else        end
enumeration extends     false       finally     for         fraction
function    global      if          import      in          integer
internal    is          let         model       namespace   new
not         or          override    protected   public      real
sealed      shiftleft   shiftright  step        string      structure
switch      then        this        throw       to          true
try         until       using       virtual     while       xor
yield
```

A name may take one back by writing `@` in front of it — `@end`, `@step` — which is the only
place a name may begin with something other than a letter.

### 1.1a How many words that is

| | Profi-C | C# |
|---|---|---|
| Reserved everywhere | **61** | 77 |
| Contextual — reserved only in one position | **0** | 46 |
| Words that are special somewhere | **61** | 123 |

C#'s figures are Roslyn's own, from `SyntaxFacts.GetReservedKeywordKinds` and
`GetContextualKeywordKinds`, minus four undocumented `__`-prefixed ones it also counts.

**The second row is the difference that shows up while writing.** In C#, `value`, `var`,
`record`, `await` and forty-odd others are keywords in one position and ordinary names
everywhere else, so whether a word is reserved depends on where it sits.

In Profi-C a word is reserved everywhere or nowhere, and `@` takes one back as a name — one
rule, written at the point it applies.

The count is asserted by a test, so the numbers above cannot drift from the compiler.

### 1.2 Comments

```
# this is a line comment, running to end of line
let count = 1;      # and may end a line of code

##
    this is a block comment,
    spanning as many lines as needed
##
```

Indenting the body is convention rather than rule — the compiler reads a block the same either
way — and it is what lets an editor fold a long one away by indentation alone.

`##` closes at the next `##` **and takes the rest of that line with it**. Two things follow. Comments do not nest, and cannot: the first pair after the opener closes the block whatever was written between, so a comment about comments cannot half-close itself. And a comment is a line of its own or the end of a line, never the middle of one — since the closer eats its line, nothing can follow one and still be code.

A run of marks is a heading rather than an error, the extra ones being comment text. A single `#` cannot close a block. An unclosed block is a scan error, reported at the opener.

### 1.3 Not reserved

`private`, `static`, `null`, `void`, `return`, `class`, `interface`, `enum`, `struct`, `var`, `do`, `foreach`, `select`, `when`, `const`.

Members are private by default, so `protected`, `internal`, and `public` opt out and `private` is unnecessary. `global` fills the role of `static`. There is no `null` at all. And note the spellings: `enumeration`, not `enum`; `constant`, not `const`.

**Types default to `internal`, exactly as in C#** — and for the same reason members default to private: a declaration with no word belongs to the smallest thing that could own it. A member's owner is its type; a type's owner is its project. A compilation nobody divided into projects is one project, so the default costs a single-file program nothing and starts to matter when one project references another.

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
| `SequenceChangedException` | thrown when a set is changed while a `for each` is walking it, where the change was not visible to `PC0243` |
| `InvalidCastException` | thrown when a forced cast fails |
| `FormatException` | thrown when a parse or format operation fails |
| `ArgumentException` | thrown when an argument is invalid |
| `OverflowException` | thrown when an arithmetic result does not fit its type |
| `IOException` | thrown when a file operation fails for any reason other than the file not being there |

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

A declared `ToString` is what a value prints everywhere: written out, printed on its own,
joined to a string with `+`, or inside a set. All of them dispatch on the runtime type, so
printing and calling never disagree.

`Model` also defines `Equals()`, likewise `virtual`.

### 3.4 Built-in type members

| Type | Members |
|---|---|
| Sets | `Insert`, `InsertAt`, `Remove`, `RemoveAt`, `Count`, `Contains`, `IndexOf`, `Clear`, `Subset` |
| Sets of optionals | `Trim`, `TrimStart`, `TrimEnd` keep the optionals; `TrimAll` drops them and narrows the type |
| Sets | `Remove` yields `boolean`; other mutators return nothing, and nothing yielding a set changes one |
| `string` | `Insert`, `InsertAt`, `Remove`, `RemoveAt`, `Substring`, `Subset`, `Contains`, `Count`, `IndexOf`, `ToCharacters`, `Trim` and its two ends, plus indexing |
| Optionals | `HasValue`, `Or`, `Value` |
| enumerations | `ToInteger`; `n as Color` converts back, yielding `Color?` |
| `Math` | `Pi` and `E` as values; `Sqrt`, `Cbrt`, `Root`, `Pow`, `Log` and its family, the trig six and their hyperbolic counterparts, `Abs`, `Min`, `Max`, the three roundings, and `Factorial` |
| `Random` | `new Random()` or `new Random(seed)`, and the same members through the name; `Next` excludes its upper bound, as .NET's does |
| `DateTime` | `new DateTime(...)`; what .NET reads as a property is read as one here, so `Year` and `Now` take no parentheses |
| `fraction` | `ToReal`; `Reciprocal` turns it over, so `(2\|3).Reciprocal()` is `3\|2` |
| enumerations | `ToString` returns the member name |
| `real` | `ToFraction` |

File I/O is not here yet. Neither is a type for a span of time, so how far apart two moments
are cannot be asked; `CompareTo` answers which came first.

---

## 4. Naming convention

**Nothing at the language level is abbreviated.** `boolean` not `bool`, `enumeration` not `enum`, `function` not `func`, `integer` not `int`. A keyword should be readable aloud without a glossary.

**Library surface is exempt and keeps .NET spellings.** `Math.Sqrt` is `Math.Sqrt`, not `Mathematics.SquareRoot`. The line: anything Profi-C defines is spelled out, anything it borrows is written the way its source writes it, so a name a reader already knows is the name they type.

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
| `sealed` | a model no other may extend, as a sealed class |
| `abstract` | a model that cannot be constructed, and a function ending at `;` that every model built from it must write. `abstract` implies `virtual`, and only an abstract model may carry one — all as in C# |
| Block strings with `"""` | the same rule as a C# raw string literal, longer runs of quotes included. There is no separate verbatim form, because nothing inside a block is read |
| Number literals | `0xFF`, `0b1010`, `1.5e3` and `1_000_000` are written and mean what they do in C#. The one difference: an exponent always makes a real, so `1e3` is `1000.0` where C# reads it as a `double` too but lets a suffix say otherwise — Profi-C has no numeric suffixes |
| `base(...)` and `base.Method()` | constructor chaining and parent calls |
| Exceptions | `try` / `catch` / `finally` / `throw`, matched by type |
| Overloading | including constructors |
| Reference semantics for classes | models are references |
| Lambdas capture by reference | same trap surface, same power |
| No shadowing inside a function | a local, a parameter, a loop's binding, or a caught exception may not reuse a name a scope around it is using — C# `CS0136`, Profi-C `PC0237`. In both, a local may still carry a field's name, which is what `this.` distinguishes. Profi-C extends the rule to a lambda's parameters, where C# permits shadowing |
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
| `end if`, `end while`, `end model`, and no opener at all | `{` opens, `}` closes, and one `}` closes everything |
| `begin` is only an anonymous scope | bare `{ }` for the same job |
| `#` to end of line, `##` to the next `##` | `//` and `/* */` |
| `case 1:` with no `break` | `break` required on every case |
| `{1, 2, 3}` is a set literal | `{ }` is a block or initializer |
| `and`, `or`, `not` | `&&`, `\|\|`, `!` |
| `let x = 5;` | `var x = 5;` |
| `integer function Add(...)` | `int Add(...)` |
| `for each c in name` | `foreach (char c in name)` |
| `for i = 0 until n` | `for (int i = 0; i < n; i++)` |
| `if c ... end if`, no parens | `if (c) { ... }` |
| `if c then a else b` expression | `c ? a : b` |
| `"{{name}} is {{age}}"` | `$"{name} is {age}"` |
| `integer delegate(integer)` names a function type | `Func<int, int>` |
| `global` | `static` |
| `3\|4` fraction literal | no equivalent |
| `2 ^ 10` raises to a power | `^` is exclusive-or; use `Math.Pow` |
| `a bitwise and b`, `a bitwise or b`, `a xor b` | `a & b`, `a \| b`, `a ^ b`. `\|` was unavailable — it writes a fraction — so the operations are words. `xor` needs no qualifier because nothing else claims it, and it refuses two booleans where C#'s `^` accepts them |
| `a shiftleft 2`, `a shiftright 2` | `a << 2`, `a >> 2`. The direction is in the word, and an amount below 0 or above 63 is an error where C# folds it into range |
| `enumeration Color` | `enum Color` |
| `global model` | `static class` |
| a `global model` has global members | a `static class` marks each member `static` |
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

**An `abstract` function with no visibility written is `protected`.** A declaration with no word belongs to the smallest thing that could own it, and the declaring type is not that thing here — nothing in it writes the function, so a descendant must. C# leaves such a member private and then rejects it (`CS0621`); Profi-C reads the word instead, so `abstract real function Area();` needs nothing beside it.

**`virtual` beside `abstract` is a warning rather than an error.** Being abstract is what offers a function for overriding, so the second word is true and adds nothing; C# rejects the pair.

**`x as Dog` yields `Dog?`**, an empty optional where C# yields `null`. The two spellings agree and the results do not, so a C# reader carries the syntax across and has to leave the habit of a null check behind: what comes back is proved present or it is not read at all.

**Deferred to v2:** generics, interfaces, and properties. Every C# property becomes a method here, which is why `Count()` has parentheses.

**Fractions are a primitive** with exact rational arithmetic. `fraction` and `real` never implicitly convert in either direction; both are explicit.

**`^` raises to a power** and is the only right-associative operator, so `2 ^ 3 ^ 2` is 512. It binds tighter than a leading minus, making `-2 ^ 2` equal to `-4` as on paper. A **whole** exponent preserves the base's type: an integer base gives an integer, and a fraction base stays exact, so `(1|2) ^ -3` is `8|1`. Any other exponent takes a root and gives a real, so `9 ^ 1|2` is `3` and `16 ^ 3|4` is `8` — the one place a fraction widens to a real unasked, since a root has no exact rational form to preserve. **In C# `^` is exclusive-or**, so the meaning does not carry across; the operation it names there is spelled `xor` here.

**Assignment is a statement.** `if (x = 5)` is a syntax error rather than a warning.

**`this.` is mandatory**, not conventional, and so is `ModelName.` for `global` members. Bare identifiers reach only locals and parameters. This makes the local-versus-state distinction visible at every use, which is the distinction a beginner most needs to see.

**No local types.** A model, structure, or enumeration may be declared at namespace level or inside a model, but not inside a function. C# has no local classes either. A type introduced by a statement would entangle name resolution with statement order, which is a cost with very little to buy it.

**Loop variables are fresh per iteration** and read-only inside the body. C# does this for `foreach` only, leaving the `for` capture trap intact. Profi-C has no three-clause `for` at all; `for i = 0 until n` and `for each` are the two forms.

**A range loop's header is live, as C#'s is.** The bound and the step are read again at the top of every turn, so `for i = 1 until x` follows `x` as it moves and a bound that grows never ends the loop — exactly as `for (int i = 0; i < x; i++) x++;` never ends. Only the counter is out of reach, where C# lets the body assign to it.

**A `for each` is the opposite, and says so at compile time.** It names a sequence rather than a bound, so the length is read once when the loop begins. Changing that sequence inside its own loop is an error (`PC0243`) naming the member that would have changed it. C# permits the same code and throws `InvalidOperationException` partway through instead.

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

**Present under another name.** A `structure` is what C# spells `struct`. An optional covers a value type, so `integer?`, `boolean?` and `fraction?` each hold a number or nothing and nullable value types are not missing — they are the same feature reached through §8 rather than through a second kind of type. And a model already compares field by field, which is the property a `record` is usually reached for.

**Planned, not yet placed in a version:** events, iterators, pattern matching, tuples, and partial types.

**Direct access to the .NET BCL** is also absent in v1. Profi-C reaches .NET only through curated built-in models that wrap it; `using` imports Profi-C namespaces, not CLR ones.

Note this is not a foreign function interface problem. Profi-C compiles to CIL and runs on the CLR, so calling `System.Math.Sqrt` is CIL calling CIL with the same collector and calling convention. The obstacle is that Profi-C cannot *name* a generic type, an interface, or a property, so any .NET member using one is unreachable regardless of conversion. Generics, interfaces, and properties are therefore the prerequisites for opening it up, which is why they head the v2 list.

Namespaces and `using` directives are present in both the file-scoped and block forms, and they
scope: a bare name is read from where it is written and then outward, a `using` decides which
further namespaces are reachable unqualified, and a qualified name reaches past all of it with
no `using` behind it. Everything the language provides sits in `Standard`, which is in scope
everywhere without one. See §12.3 of the specification.

---

## 7. Details worth knowing

### 7.1 Diagnostics

**A diagnostic carries one of three severities**, and what separates them is how much is known about what the program means. An **error** is reported where the meaning is genuinely unpredictable, and only an error blocks compilation. A **warning** is reported where the meaning is clear and unlikely to be what was intended. An **opinion** is reported where the meaning is clear, intended, and correct, and the language would still write it differently.

**Warnings are few and each one names its fix.** Fourteen exist: more quotes in a row than close a block string, a type shadowing one the language provides, a test that is always true, a test that is always false, a `switch` leaving enumeration members unhandled, unreachable code, an import naming an absolute path, imports that form a circle, three about an `ignore` that cannot work — one naming no diagnostic, one naming a diagnostic that stops compilation, and one in a project file naming neither a severity nor a diagnostic — and three about documentation that has come apart from what it documents: one above nothing that can carry it, one naming a parameter that is not there, and one describing a value never given back.

**Opinions are the language having taste, and every one says a written token has no effect.** Ten exist: an unnecessary `@` on a name, a type on a range loop's counter, a lambda parameter type the surrounding code already gave, `using Standard;` where Standard is already in scope, a namespace repeating a name it sits inside, an `entry` where only one program exists to choose, `virtual` beside `abstract`, `Console.WriteLine("")` where the empty string does nothing, an `ignore` that silences nothing, and a doc that says the same thing twice.

Nothing is wrong with a program that has an opinion against it. Reading them in order is a reasonable way to learn what the language expects. [Appendix A](language-spec.md#appendix-a-diagnostics) of the specification lists every diagnostic with its id and severity.

### 7.2 Ignoring one

A warning or an opinion can be silenced; an error never can, since the mechanism that quiets the compiler must not be able to make it lie. Nothing about `ignore` stops a build — getting the line wrong is itself only a warning.

A comment carries the directive, in one of three forms, and the word after `ignore` is never absent:

```
# ignore warning
# ignore opinion
# ignore PC0340
```

Each covers the next line carrying code, and each takes `in file` to cover the whole file instead:

```
# ignore opinion in file
```

A project file widens it again, over every file the project builds:

```
project Library
    source Program.pc
    ignore opinion
end project
```

**Prose that begins with "ignore" stays prose.** `# ignore the sign for now` is a comment a person writes, so a directive is recognized only once `warning`, `opinion`, or something shaped like `PC0340` follows. Words after that are prose too, so a directive can say why it is there.

Naming a diagnostic asserts that a particular one is there, so an `ignore` naming one nothing reports is itself reported (`PC0024`) — while `ignore opinion` claims nothing and stays silent with nothing to silence.

### 7.3 Documenting something

A comment opening `@summary:` documents whatever declaration follows it — a type, a member of one, or an enumeration's member. Nothing else is documentation, however it is placed: a block above a declaration stays an ordinary remark unless it says otherwise.

```
##
    @summary: One person's money, and the rules about taking it out.
##
model Account

    # @summary: Whose account this is.
    string owner;
```

Every part is a labeled line. `@summary:` carries the first, `@remarks:` the fuller explanation a completion list has no room for, and `@yields:` and `@throws:` describe what a function gives back and what it can raise. Any other name documents the parameter it names.

```
##
    @summary: The sum of 1|1 through 1|n.

    Every partial sum is exact, so this is the true rational value rather than the
    nearest a real could hold. A blank line keeps one label running, so a summary may
    be as many paragraphs as it needs.

    @n: how many terms to add.
    @yields: the exact rational sum.
##
fraction function Harmonic(integer n)
```

**The `@` is what makes a label a label**, and it is not decoration. Prose wraps, and a wrapped line often begins with a word and a colon — *"that is why it yields an / optional: ..."* — which is what every such line in this repository's samples turned out to be. Without the mark, telling those from a label takes a rule about where a line sits, and no such rule survives ordinary formatting.

The compiler holds a doc to what it documents: a parameter named that the function does not take, a `@yields:` on a function yielding nothing, and a label written twice are all reported. A *missing* doc never is — requiring one everywhere is how documentation stops being a help.

### 7.4 The root of every reference type

**`Model` is the root of every reference type**, not just user models, which is what lets `Reference.Equals(Model, Model)` accept sets and strings. In emitted CIL it corresponds to `System.Object`, which `System.String` and `List<T>` already derive from, so no adapter is needed.
