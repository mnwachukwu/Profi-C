# Math

[← Back to the index](README.md)

Reached through the name; there is no such thing as *a* `Math`.

| Section | Members |
|---|---|
| [Constants](#constants) | `Pi` `E` |
| [Roots and powers](#roots-and-powers) | `Sqrt` `Cbrt` `Root` `Pow` `Factorial` |
| [Logarithms](#logarithms) | `Log` `Log10` `Log2` |
| [Angles](#angles) | `Sin` `Cos` `Tan` `Asin` `Acos` `Atan` `Atan2` `Sinh` `Cosh` `Tanh` `Asinh` `Acosh` `Atanh` |
| [Comparing and sizing](#comparing-and-sizing) | `Abs` `Min` `Max` |
| [Rounding](#rounding) | `Floor` `Ceiling` `Round` |
| [How far an answer can be trusted](#how-far-an-answer-can-be-trusted) | — |

**Every member that takes a number takes a `real` or a `float`**, in two forms the argument
chooses between. Neither type can answer for the other — a real has no infinity and a float has no
twenty-eight digits — so a single version would force a conversion at every call. Three sections
go further and each says so: `Abs`, `Min` and `Max` take all four number types, the rounding
members take the three that have a fractional part, and `Factorial` counts arrangements and so
takes a whole number only.

The `real` forms compute in binary and convert back, which rounds to fifteen significant digits:
fewer than a float shows, and every one of them true.

## Constants

| Member | Yields |
|---|---|
| `Math.Pi` | `real` |
| `Math.E` | `real` |

**Values, not functions.** Writing `Math.Pi()` is reported (`PC0338`), as is naming a function
without calling it (`PC0330`) — the two are a pair, so whichever a reader guesses, the compiler
says which it is.

```
real area = Math.Pi * 4.0;
Console.WriteLine(area.Format("F2"));   # 12.57
```

## Roots and powers

| Member | Yields | What it does |
|---|---|---|
| `Math.Sqrt(real x)` | `real` | The square root |
| `Math.Cbrt(real x)` | `real` | The cube root |
| `Math.Root(real x, real degree)` | `real` | The root of any degree |
| `Math.Pow(real x, real by)` | `real` | `x` raised by `by` |
| `Math.Factorial(integer n)` | `integer` | `n!` |

`Math.Pow` and the `^` operator do the same job; `^` is usually the one to write. `Factorial`
counts arrangements, so it takes a whole number and gives one back — past 20 the answer outgrows
an `integer` and raises `OverflowException`, as any other overflow does.

```
Console.WriteLine(Math.Sqrt(144.0));       # 12
Console.WriteLine(Math.Root(32.0, 5.0));   # 2
Console.WriteLine(Math.Factorial(5));      # 120
```

## Logarithms

| Member | Yields | What it does |
|---|---|---|
| `Math.Log(real x)` | `real` | The **natural** logarithm |
| `Math.Log(real x, real base)` | `real` | In any base |
| `Math.Log10(real x)` | `real` | Base ten |
| `Math.Log2(real x)` | `real` | Base two |

`Log` with one argument is natural, as it is in C#, C and Java — not base ten.

```
Console.WriteLine(Math.Log2(1024.0));       # 10
Console.WriteLine(Math.Log(81.0, 3.0));     # 4
```

## Angles

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

The hyperbolic six are named for the circular ones they sit beside and are shaped the same way,
but measured against a hyperbola rather than a circle. A hanging chain takes the shape of `Cosh`,
which is where most people meet them.

```
Console.WriteLine(Math.Sin(0.0));            # 0
Console.WriteLine(Math.Cosh(0.0));           # 1
Console.WriteLine(Math.Atan2(1.0, 1.0).Format("F4"));   # 0.7854
```

## Comparing and sizing

Each of these has a form for every one of the four number types, and gives back what it was given.

| Member | Yields |
|---|---|
| `Math.Abs(integer)` · `Math.Abs(real)` · `Math.Abs(float)` · `Math.Abs(fraction)` | the same type |
| `Math.Min(integer, integer)` · `Math.Min(real, real)` · `Math.Min(float, float)` · `Math.Min(fraction, fraction)` | the same type |
| `Math.Max(integer, integer)` · `Math.Max(real, real)` · `Math.Max(float, float)` · `Math.Max(fraction, fraction)` | the same type |

Measuring keeps the type it measured, so a distance between integers is one. That is what stops
`Math.Abs(-3)` from arriving as something that cannot be counted with.

```
Console.WriteLine(Math.Abs(-3));           # 3 — still an integer
Console.WriteLine(Math.Min(1|2, 1|3));     # 1|3 — still exact
Console.WriteLine(Math.Max(2.5, 2.75));    # 2.75
```

## Rounding

| Member | Yields | What it does |
|---|---|---|
| `Math.Floor(real)` · `Math.Floor(float)` · `Math.Floor(fraction)` | `integer` | Down |
| `Math.Ceiling(real)` · `Math.Ceiling(float)` · `Math.Ceiling(fraction)` | `integer` | Up |
| `Math.Round(real)` · `Math.Round(float)` · `Math.Round(fraction)` | `integer` | To the nearest |
| `Math.Round(real x, integer places)` · `Math.Round(float x, integer places)` | the type given | To that many decimal places |

**Rounding lands on a whole number**, so each yields an `integer` and can be used as a count, an
index or a bound. These are the three ways from a `real`, a `float` or a `fraction` to an
`integer`, which is why no single `ToInteger` exists: it would have to pick one of the three
without being told which.

**A half goes away from zero.** `Math.Round(2.5)` is `3` — the rule taught in school, rather than
.NET's default of rounding to the even neighbor.

```
Console.WriteLine(Math.Floor(2.9));        # 2
Console.WriteLine(Math.Ceiling(2.1));      # 3
Console.WriteLine(Math.Round(2.5));        # 3 — away from zero
Console.WriteLine(Math.Round(2.567, 2));   # 2.57 — a real, since it still has a fraction
```

## How far an answer can be trusted

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

## Nearby

[Numbers](numbers.md) for what each number type answers about itself, and for the whole
conversion chart. [Random](random.md) for chance, which is the other model in this corner of the
library.
