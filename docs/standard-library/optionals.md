# Optionals

[← Back to the index](README.md)

A `T?` holds a `T` or holds nothing. **There are three members and there are only three**, which
is the whole of the feature that replaces `null`.

The important part is not the members but the rule around them: **the compiler will not let you
read an optional it cannot prove is present.** Reaching for a value that might be absent stops
being a crash and becomes a line that does not compile.

| Section | Members |
|---|---|
| [Members](#members) | `HasValue` `Value` `Or` |
| [HasValue narrows](#hasvalue-narrows) | `HasValue` |
| [Or supplies a fallback](#or-supplies-a-fallback) | `Or` |
| [Where optionals come from](#where-optionals-come-from) | — |
| [A set of optionals](#a-set-of-optionals) | — |

## Members

| Member | Yields | What it does |
|---|---|---|
| `HasValue()` | `boolean` | Whether something is in there |
| `Value()` | `T` | What is in there — refused unless the compiler can prove it is |
| `Or(T fallback)` | `T` | What is in there, or `fallback` |
| `Or(T? fallback)` | `T?` | What is in there, or the other optional |

## `HasValue` narrows

Asking is what makes reading legal. Inside a block guarded by `HasValue()`, the compiler knows the
value is there and `Value()` is allowed.

```
string? typed = Console.Read();

if typed.HasValue()
    # Legal here and nowhere else: the guard is what proves it.
    Console.WriteLine("you typed " + typed.Value());
else
    Console.WriteLine("nothing was typed");
end if
```

Written without the guard, `typed.Value()` is `PC0401` — a compile error, not a crash. That is the
whole point: the mistake moved from run time to build time.

## `Or` supplies a fallback

Usually the shorter answer, and the one to reach for when there is a sensible default.

```
string name = Console.Read().Or("stranger");
Console.WriteLine("hello, " + name);
```

**The fallback is not evaluated unless it is needed**, so an expensive one costs nothing when the
optional turns out to be present.

## The two forms of `Or`, and chaining

Given a plain value, `Or` **ends** the chain with a definite one. Given another optional, it keeps
the chain going — which is what makes a run of fallbacks work:

```
string? fromFile = File.Read("settings.txt");
string? fromInput = Console.Read();

# Each step stays optional until the last, which ends it.
string chosen = fromFile.Or(fromInput).Or("a built-in default");
```

`fromFile.Or(fromInput)` is still a `string?`, because both might be absent. `.Or("...")` is a
`string`, because that one cannot be.

## When there is genuinely nothing to fall back on

`Value()` on an optional that turns out empty raises `EmptyOptionalException` — but reaching that
line means the compiler was told the value was there, so it is a claim that turned out false
rather than a check somebody forgot. See [exceptions](exceptions.md).

## Where optionals come from

The library hands one back wherever an answer may genuinely not exist:

| From | Yields | Absent when |
|---|---|---|
| [`Console.Read()`](input-output.md#console) | `string?` | The input has run out |
| [`File.Read(path)`](input-output.md#file) | `string?` | There is no such file |
| [`"12".ToInteger()`](text.md#reading-a-value-back-out) | `integer?` | The text does not spell a number |
| [`DateTime.Parse(text)`](dates-and-times.md) | `DateTime?` | The text does not read as a moment |

**Absence is an answer, not a fault.** Every one of these is an ordinary thing to happen, which is
why none of them raises — and why asking `File.Exists` before `File.Read` is the pattern that
races rather than the careful one.

## A set of optionals

`T?[]` holds values that may each be absent, and has [four members of its own](sets.md#dropping-the-empties)
for getting rid of them — `TrimAll` being the one that gives back a `T[]` so the unwrapping stops.

## Also on every optional

Nothing else. An optional does **not** answer [`ToString()` or `Equals()`](every-value.md): asking
what an absence prints as, or whether two absences are equal, are questions with no answer worth
guessing. Reach the value first.
