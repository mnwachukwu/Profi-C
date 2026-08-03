# Numbers

[← Back to the index](README.md)

**Four number types**, and the capitalized names beside them.

| Type | What it is | Written |
|---|---|---|
| `integer` | A whole number, 64 bits | `42` |
| `real` | A number with a decimal point, counted **in tens** and exact about them | `3.14` |
| `float` | Binary floating point: `float` or `double` in C, C#, Java and Go | `3.14f` |
| `fraction` | An **exact** ratio of two whole numbers | `22|7` |

| Section | Members |
|---|---|
| [Members on a number](#members-on-a-number) | `Format` `ToFloat` `ToReal` `ToFraction` `Reciprocal` |
| [Writing a number out](#writing-a-number-out) | `Format` |
| [Fraction](#fraction) | `Fraction.Create` |
| [What each type knows about itself](#what-each-type-knows-about-itself) | `Integer.MaxValue` `Integer.MinValue` `Real.MaxValue` `Real.MinValue` `Float.MaxValue` `Float.MinValue` |
| [What only a float has](#what-only-a-float-has) | `Float.Infinity` `Float.NegativeInfinity` `Float.NotANumber` |
| [Writing one too large](#writing-one-too-large) | — |
| [The whole conversion chart](#the-whole-conversion-chart) | — |
| [Crossing between a real and a float](#crossing-between-a-real-and-a-float) | `ToFloat` `ToReal` `ToFraction` |

Arithmetic reached through a name lives on its own page: [`Math`](math.md) for roots, logarithms,
angles and rounding, and [`Random`](random.md) for chance.

**`real` is not floating point.** A tenth cannot be written exactly in binary, so writing `0.1`
elsewhere usually gets you the nearest binary number to a tenth rather than a tenth. Here the
digits are held as digits, and the arithmetic comes out as written:

```
Console.WriteLine((0.1 + 0.2) == 0.3);      # true
Console.WriteLine((0.1f + 0.2f) == 0.3f);   # false
```

Which is why both exist. `real` is what a decimal point means in this language; `float` is the
same thing every other language gives you for it, offered by name so that its behavior can be
met on purpose rather than by accident.

`fraction` is exact where neither can be — a third has no decimal form that ends and no binary
one either:

```
Console.WriteLine(1|3 + 1|3 + 1|3);   # 1|1
```

## Members on a number

| On | Member | Yields | What it does |
|---|---|---|---|
| `integer` | `Format(string pattern)` | `string` | Written out by a pattern |
| `integer` | `ToFloat()` | `float` | The same whole number in binary |
| `real` | `Format(string pattern)` | `string` | Written out by a pattern |
| `real` | `ToFloat()` | `float` | The same number in binary, to about sixteen digits |
| `float` | `Format(string pattern)` | `string` | Written out by a pattern |
| `float` | `ToReal()` | `real` | The same number in tens, where there is one |
| `float` | `ToFraction()` | `fraction` | The ratio it is really holding |
| `fraction` | `Format(string pattern)` | `string` | Written out by a pattern |
| `fraction` | `ToReal()` | `real` | The nearest real |
| `fraction` | `ToFloat()` | `float` | The nearest float |
| `fraction` | `Reciprocal()` | `fraction` | The fraction turned over |

A `real` has no `ToFraction()`, and needs none: it counts in tens, so it already is a fraction
over a power of ten and converts on its own. The [chart below](#the-whole-conversion-chart) has
every direction in one place.

**`Reciprocal` is exact where a real's is only nearly so.** A third turned over is three; but
`1.0 / (1.0 / 3.0)` is not quite one, in tens any more than in binary.

```
fraction third = 1|3;

Console.WriteLine(third.Reciprocal());   # 3|1
Console.WriteLine(third.ToReal());       # 0.3333333333333333333333333333
```

<a id="writing-a-number-out"></a>

## Writing a number out

`Format` takes **.NET's own patterns, unchanged** — `F2` is two decimal places, `N0` is a whole
number with separators, `P1` is a percentage. What a reader learns here is what they will type
next somewhere else. A pattern the runtime does not recognize raises `FormatException` rather
than producing a silent oddity.

```
real price = 1234.5678;

Console.WriteLine(price.Format("F2"));   # 1234.57
Console.WriteLine(price.Format("N0"));   # 1,235
Console.WriteLine(42.Format("D5"));      # 00042
```

## `Fraction`

Note the two spellings: **`fraction` is the type** and a reserved word; **`Fraction` is the model**
beside it, holding what a fraction needs that is not a member of one.

| Member | Yields | What it does |
|---|---|---|
| `Fraction.Create(integer numerator, integer denominator)` | `fraction` | A fraction from two values |
| `Fraction.Create(integer whole)` | `fraction` | That whole number over one |

A fraction literal is two numerals fixed when the program is written, so `Create` is the only way
to make one from values that exist only while it runs. What comes back is an ordinary fraction —
reduced, with its sign carried on the numerator.

**A denominator of zero is rejected while compiling where it can be seen**, exactly as `1 / 0` is.
Written as a literal it always can be, so `1|0` is `PC0027`; built from values it cannot, so
`Fraction.Create(top, 0)` raises `DivideByZeroException`.

The one-argument form earns its place only where nothing else says a fraction is wanted:
`let f = 3;` holds an integer, and `let f = Fraction.Create(3);` holds `3|1`.

```
integer top = 22;
integer bottom = 7;

Console.WriteLine(Fraction.Create(top, bottom));   # 22|7
Console.WriteLine(Fraction.Create(4, 8));          # 1|2 — reduced
```

## What each type knows about itself

Each number has a capitalized name beside its keyword, holding the facts about it. The
keyword names the type and the capital names where those facts live — a reserved word cannot
stand in front of a dot, so `integer.MaxValue` is not something the grammar can read and
`Integer.MaxValue` is. `Fraction` already read this way beside `fraction`.

Bounds are a number's business. Where a number runs out is a fact about that number, and
meeting it is how you learn a type has an edge at all. A `character` has no such fact to
tell — where the alphabet stops is a fact about how text is stored rather than about the
language — so there is no `Character` to ask.

| Member | Yields | What it is |
|---|---|---|
| `Integer.MaxValue` | `integer` | The largest whole number, 9223372036854775807 |
| `Integer.MinValue` | `integer` | The smallest, which has no literal — the minus is a separate operator, so this name is the only way to write it |
| `Real.MaxValue` | `real` | The largest real, about 79 followed by 27 zeros |
| `Real.MinValue` | `real` | Its negative |
| `Float.MaxValue` | `float` | The largest finite float, about 1.8 times ten to the 308th |
| `Float.MinValue` | `float` | Its negative |

A `string` has a capitalized name too, holding [`String.Empty`](text.md#string) — not a bound, but
the same idea: a fact about the type, kept where a reserved word cannot reach.

### What only a `float` has

| Member | Yields | What it is |
|---|---|---|
| `Float.Infinity` | `float` | What `1.0f / 0.0f` produces |
| `Float.NegativeInfinity` | `float` | What `-1.0f / 0.0f` produces |
| `Float.NotANumber` | `float` | What `0.0f / 0.0f` produces — and the one value **not equal to itself**, so comparing against this name is always false |

Each is the value a float's own arithmetic gives back, so `1.0f / 0.0f == Float.Infinity` is
true. **A float is the one type allowed to divide by a zero written down**: for every other,
`PC0324` refuses it while compiling, because for every other there is no answer. Here there is
one, so the expression is left alone and the constant merely names what it produces.

A `real` has none of them: it counts in tens and there is nothing in it to hold them, so where a
float carries on into an infinity a real stops — the same choice an `integer` makes.

`NotANumber` is written out rather than abbreviated, the way this language writes `shiftleft`
and `bitwise and`. It prints as that word too, so what a reader sees and what they would write
are the same.

### Writing one too large

A number written past its type's edge is reported (`PC0026`) rather than wrapping, saturating or
quietly becoming something else. The digits are a fine number to read — it is only holding them
that fails — so the scanner is content and the refusal comes later.

```text
integer counted = 9223372036854775808;   # PC0026 — one past Integer.MaxValue
real measured = 1e400;                   # PC0026 — past Real.MaxValue
```

**The most negative integer has no literal at all.** The minus sign is a separate operator, so
`-9223372036854775808` is a minus applied to a number one past the largest, and both halves of
that are reported together. `Integer.MinValue` is the way to write it, and is the reason the name
exists.

**A float is the exception, and deliberately.** It is the one type with a value for a number too
large, so a float literal past its edge becomes that value rather than being refused — which
agrees with what its own arithmetic already does:

```
Console.WriteLine(1e400f);          # Infinity
Console.WriteLine(1.0f / 0.0f);     # Infinity — the same answer, reached the other way
```

## The whole conversion chart

Read a row as *from*, a column as *to*. **Bold** happens on its own; anything else is written out.

| from ↓ to → | `integer` | `real` | `float` | `fraction` |
|---|---|---|---|---|
| **`integer`** | — | **automatic** | `.ToFloat()` | **automatic** |
| **`real`** | `Math.Round(x)` | — | `.ToFloat()` | **automatic** |
| **`float`** | `Math.Round(x)` | `.ToReal()` | — | `.ToFraction()` |
| **`fraction`** | `Math.Round(x)` | `.ToReal()` | `.ToFloat()` | — |

[`Math.Floor` and `Math.Ceiling`](math.md#rounding) reach an `integer` the same way `Math.Round`
does; each takes a real, a float or a fraction and answers with a whole number, which is what
makes them the honest spelling of that conversion rather than a cast that silently picks a
direction.

### One rule, and its two exceptions

**A conversion that loses nothing happens on its own.** That is the whole of the bold column
group: every whole number is a real and is a ratio over one, and a real counts in tens so it
already *is* a ratio over a power of ten.

Two conversions lose nothing and are still written out, because each answer is surprising enough
to be worth asking for:

- **`fraction.ToReal()`** — a third has no decimal that ends, so `1|3` becomes `0.3333…` and
  does not multiply back to one.
- **`float.ToFraction()`** — `0.1f` is really `3602879701896397|36028797018963968`, which is the
  clearest answer there is to why binary floating point surprises people.

**And one that is written out for a plainer reason: nothing reaches a `float` on its own.** Not
even an integer. If a whole number widened to both a real and a float, every member of `Math`
would have two readings and `Math.Sqrt(2)` would have no answer — so widening to a real is what
happens, and a float is asked for by name.

### What each conversion can cost

| Conversion | Can it fail? | What is lost |
|---|---|---|
| to `real`, to `fraction`, from `integer` | no | nothing |
| `real → fraction` | **yes** — `PC0346` when written down, otherwise at run time | nothing; the parts can outgrow a whole number |
| `real → float` | no | digits past the sixteenth |
| `integer → float` | no | digits past the sixteenth, for very large numbers |
| `fraction → real`, `fraction → float` | no | thirds and the like stop being exact |
| `float → real` | **yes**, three ways | see below |
| any `→ integer` | no | everything after the point, which is what you asked for |

## Crossing between a `real` and a `float`

Both directions are written out, and neither is because it loses accuracy in the ordinary sense
— it is because each answer is worth a reader's attention.

| Member | Yields | What it does |
|---|---|---|
| `real.ToFloat()` | `float` | The same number in binary. **Always answers**: every real fits well inside a float's range. What is lost is digits — a real holds about twenty-eight and a float sixteen |
| `float.ToReal()` | `real` | The same number in tens. **Can fail three ways**, and quietly changes what it does convert |
| `float.ToFraction()` | `fraction` | The ratio the float is really holding, which is the clearest look at what binary floating point does |

A `real` needs no `ToFraction()`: it counts in tens, so it already is a fraction over a power of
ten and widens on its own.

### What `ToReal()` can do to a number

```
Console.WriteLine((1.5f).ToReal());         # 1.5

# The same float, asked two ways.
Console.WriteLine((0.1f).ToFraction());     # 3602879701896397|36028797018963968
Console.WriteLine((0.1f).ToReal());         # 0.1
```

The last two lines are one value. Asked for its fraction it gives what it is really holding;
asked for a real it gives `0.1`, the shortest decimal it rounds to. **Nothing is reported** —
the mess simply disappears, and the number that comes back is not the number that went in. That
is the strongest reason this conversion is written rather than done quietly.

The three failures are ordinary by comparison: a float larger than a real can hold, an infinity,
and a value that is not a number. A real has no form for any of them, so each stops.

## Also on every number

[`ToString()` and `Equals()`](every-value.md).
