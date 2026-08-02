# Numbers

[← Back to the index](README.md)

Three number types, and the models beside them. `integer` is a whole number, `real` is a floating
point number, and `fraction` is an **exact** ratio of two integers — `1|3 + 1|3 + 1|3` is exactly
`1`, which the same sum in `real` is not.

## Members on a number

| On | Member | Yields | What it does |
|---|---|---|---|
| `integer` | `Format(string pattern)` | `string` | Written out by a pattern |
| `real` | `Format(string pattern)` | `string` | Written out by a pattern |
| `real` | `ToFraction()` | `fraction` | The exact ratio of the value as stored |
| `fraction` | `Format(string pattern)` | `string` | Written out by a pattern |
| `fraction` | `ToReal()` | `real` | The nearest floating point value |
| `fraction` | `Reciprocal()` | `fraction` | The fraction turned over |

**`Reciprocal` is exact where a real's is only nearly so.** A third turned over is three; but
`1.0 / (1.0 / 3.0)` is not quite one.

```
fraction third = 1|3;

Console.WriteLine(third.Reciprocal());   # 3|1
Console.WriteLine(third.ToReal());       # 0.3333333333333333
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

## `Math`

Reached through the name; there is no such thing as *a* `Math`.

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

Each of these has a form per number type, and gives back what it was given.

| Member | Yields |
|---|---|
| `Math.Abs(integer)` · `Math.Abs(real)` · `Math.Abs(fraction)` | the same type |
| `Math.Min(integer, integer)` · `Math.Min(real, real)` · `Math.Min(fraction, fraction)` | the same type |
| `Math.Max(integer, integer)` · `Math.Max(real, real)` · `Math.Max(fraction, fraction)` | the same type |

### Rounding

| Member | Yields | What it does |
|---|---|---|
| `Math.Floor(real)` · `Math.Floor(fraction)` | `integer` | Down |
| `Math.Ceiling(real)` · `Math.Ceiling(fraction)` | `integer` | Up |
| `Math.Round(real)` · `Math.Round(fraction)` | `integer` | To the nearest |
| `Math.Round(real x, integer places)` | `real` | To that many decimal places |

**Rounding lands on a whole number**, so each yields an `integer` and can be used as a count, an
index or a bound. Between them these are the three honest ways from a `real` to an `integer`,
which is why no single `ToInteger` exists — it would have to pick one of the three silently, and
which one is the question being asked.

**A half goes away from zero.** `Math.Round(2.5)` is `3` — the rule taught in school, rather than
.NET's default of rounding to the even neighbor.

### How far a real answer can be trusted

`Sqrt` is required by IEEE 754 to be correctly rounded, so it gives the same answer on every
machine. **The rest of the transcendental members are not**, and may differ in the last bit
between one machine and another. That is true of C, C#, Java and Python alike: each defers to the
arithmetic library the platform ships, and those are permitted to disagree by a fraction of an
ulp.

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
