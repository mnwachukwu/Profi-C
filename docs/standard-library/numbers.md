# Numbers

[← Back to the index](README.md)

**Four number types**, and the models beside them.

| Type | What it is | Written |
|---|---|---|
| `integer` | A whole number, 64 bits | `42` |
| `real` | A number with a decimal point, counted **in tens** and exact about them | `3.14` |
| `float` | Binary floating point: `float` or `double` in C, C#, Java and Go | `3.14f` |
| `fraction` | An **exact** ratio of two whole numbers | `22|7` |

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

**A denominator of zero is rejected while compiling where it can be seen**, exactly as `1 / 0` is,
and raises `DivideByZeroException` where it cannot.

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
| `String.Empty` | `string` | The string with nothing in it, which reads better than `""` wherever the emptiness is the point |

### What only a `float` has

| Member | Yields | What it is |
|---|---|---|
| `Float.Infinity` | `float` | What `1.0f / 0.0f` produces |
| `Float.NegativeInfinity` | `float` | What `-1.0f / 0.0f` produces |
| `Float.NotANumber` | `float` | What `0.0f / 0.0f` produces — and the one value **not equal to itself**, so comparing against this name is always false |

Each is the value a float's own arithmetic gives back, so `1.0f / 0.0f == Float.Infinity` is
true. A `real` has none of them: it counts in tens and there is nothing in it to hold them, so
where a float carries on into an infinity a real stops — the same choice an `integer` makes.

`NotANumber` is written out rather than abbreviated, the way this language writes `shiftleft`
and `bitwise and`. It prints as that word too, so what a reader sees and what they would write
are the same.

## The whole conversion chart

Read a row as *from*, a column as *to*. **Bold** happens on its own; anything else is written out.

| from ↓ to → | `integer` | `real` | `float` | `fraction` |
|---|---|---|---|---|
| **`integer`** | — | **automatic** | `.ToFloat()` | **automatic** |
| **`real`** | `Math.Round(x)` | — | `.ToFloat()` | **automatic** |
| **`float`** | `Math.Round(x)` | `.ToReal()` | — | `.ToFraction()` |
| **`fraction`** | `Math.Round(x)` | `.ToReal()` | `.ToFloat()` | — |

`Math.Floor` and `Math.Ceiling` reach an `integer` the same way `Math.Round` does; each takes a
real, a float or a fraction and answers with a whole number, which is what makes them the honest
spelling of that conversion rather than a cast that silently picks a direction.

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

## `Math`

Reached through the name; there is no such thing as *a* `Math`. **Every member below exists in
two forms**, one taking a `real` and one taking a `float`, and the argument settles which runs.
Neither type can answer for the other — a real has no infinity and a float has no twenty-eight
digits — so a single version would force a conversion at every call.

The `real` forms compute in binary and convert back, which rounds to fifteen significant digits:
fewer than a float shows, and every one of them true.

### Constants

| Member | Yields |
|---|---|
| `Math.Pi` | `real` |
| `Math.E` | `real` |

**Values, not functions.** Writing `Math.Pi()` is reported (`PC0338`), as is naming a function
without calling it (`PC0330`) — the two are a pair, so whichever a reader guesses, the compiler
says which it is.

### Roots and powers

| Member | Yields | What it does |
|---|---|---|
| `Math.Sqrt(real x)` | `real` | The square root |
| `Math.Cbrt(real x)` | `real` | The cube root |
| `Math.Root(real x, real degree)` | `real` | The root of any degree |
| `Math.Pow(real x, real by)` | `real` | `x` raised by `by` |
| `Math.Factorial(integer n)` | `integer` | `n!` |

`Math.Pow` and the `^` operator do the same job; `^` is usually the one to write.

### Logarithms

| Member | Yields | What it does |
|---|---|---|
| `Math.Log(real x)` | `real` | The **natural** logarithm |
| `Math.Log(real x, real base)` | `real` | In any base |
| `Math.Log10(real x)` | `real` | Base ten |
| `Math.Log2(real x)` | `real` | Base two |

`Log` with one argument is natural, as it is in C#, C and Java — not base ten.

### Angles

Every one of these works in **radians**.

| Member | Yields | | Member | Yields |
|---|---|---|---|---|
| `Math.Sin(real)` | `real` | | `Math.Asin(real)` | `real` |
| `Math.Cos(real)` | `real` | | `Math.Acos(real)` | `real` |
| `Math.Tan(real)` | `real` | | `Math.Atan(real)` | `real` |
| `Math.Sinh(real)` | `real` | | `Math.Asinh(real)` | `real` |
| `Math.Cosh(real)` | `real` | | `Math.Acosh(real)` | `real` |
| `Math.Tanh(real)` | `real` | | `Math.Atanh(real)` | `real` |
| `Math.Atan2(real y, real x)` | `real` | | | |

`Atan2` takes `y` first, as everywhere else, and gives the angle of the point from the origin
across the whole circle rather than only half of it.

### Comparing and sizing

Each of these has a form for every one of the four number types, and gives back what it was given.

| Member | Yields |
|---|---|
| `Math.Abs(integer)` · `Math.Abs(real)` · `Math.Abs(float)` · `Math.Abs(fraction)` | the same type |
| `Math.Min(integer, integer)` · `Math.Min(real, real)` · `Math.Min(float, float)` · `Math.Min(fraction, fraction)` | the same type |
| `Math.Max(integer, integer)` · `Math.Max(real, real)` · `Math.Max(float, float)` · `Math.Max(fraction, fraction)` | the same type |

### Rounding

| Member | Yields | What it does |
|---|---|---|
| `Math.Floor(real)` · `Math.Floor(float)` · `Math.Floor(fraction)` | `integer` | Down |
| `Math.Ceiling(real)` · `Math.Ceiling(float)` · `Math.Ceiling(fraction)` | `integer` | Up |
| `Math.Round(real)` · `Math.Round(float)` · `Math.Round(fraction)` | `integer` | To the nearest |
| `Math.Round(real x, integer places)` · `Math.Round(float x, integer places)` | the type given | To that many decimal places |

**Rounding lands on a whole number**, so each yields an `integer` and can be used as a count, an
index or a bound. Between them these are the three honest ways from a `real`, a `float` or a
`fraction` to an `integer`, which is why no single `ToInteger` exists — it would have to pick one
of the three silently, and which one is the question being asked.

**A half goes away from zero.** `Math.Round(2.5)` is `3` — the rule taught in school, rather than
.NET's default of rounding to the even neighbor.

### How far an answer can be trusted

`Sqrt` is required by IEEE 754 to be correctly rounded, so it gives the same answer on every
machine. **The rest of the transcendental members are not**, and may differ in the last bit
between one machine and another. That is true of C, C#, Java and Python alike: each defers to the
arithmetic library the platform ships, and those are permitted to disagree by a fraction of an
ulp.

The `real` forms work in binary and convert back, which rounds to fifteen significant digits.
That is fewer digits than the `float` forms show, and it is the better half of the trade: every
digit that survives is one the calculation actually had, where a float's tail is often noise. A
program wanting the raw binary answer asks for it with an `f`.

Two guarantees are made against that:

- **A root of an exact power is exact.** `Math.Cbrt(27.0)` is `3` and `Math.Root(32.0, 5.0)` is
  `2`, on every machine. Where raising the nearest whole number by the degree gives the value back
  exactly, that whole number *is* a root of it, so it is used — a better answer as well as the
  same one everywhere.
- **Nothing else is corrected.** `Math.Cbrt(28.0)` is left as the library worked it out. A program
  that needs a real answer to be identical across machines should round it to as many places as it
  means to claim, which is what saying "to four places" amounts to.

## `Random`

**A model with instances**, unlike `Math`. Two ways to make one:

| Member | Yields | What it does |
|---|---|---|
| `new Random()` | `Random` | Seeded from the clock; a different run each time |
| `new Random(integer seed)` | `Random` | Seeded by hand; the same run every time |

| Member | Yields | What it does |
|---|---|---|
| `Next()` | `integer` | Any non-negative whole number |
| `Next(integer below)` | `integer` | From `0` up to but not including `below` |
| `Next(integer from, integer below)` | `integer` | From `from` up to but not including `below` |
| `NextDouble()` | `real` | From `0.0` up to but not including `1.0` |

Both bounded forms exclude their upper end, the same reading `until` has in a loop — so
`Next(1, 7)` is a die.

**Seed it by hand to make a program repeatable**, which is what a test wants and what makes a
shuffle worth debugging.

```
Random dice = new Random(42);

loop for roll = 1 to 3
    Console.WriteLine(dice.Next(1, 7));
end loop
```

## Also on every number

[`ToString()` and `Equals()`](every-value.md).
