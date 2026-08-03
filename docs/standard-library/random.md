# Random

[← Back to the index](README.md)

Chance, in two shapes: a generator a program holds, which disturbs nothing of anyone else's, and
the same questions asked through the name, drawing from one the language keeps.

| Section | Members |
|---|---|
| [Making one](#making-one) | `new Random` |
| [Asking for a number](#asking-for-a-number) | `Next` `NextDouble` |
| [Through the name, or through one you hold](#through-the-name-or-through-one-you-hold) | — |

## Making one

| Member | Yields | What it does |
|---|---|---|
| `new Random()` | `Random` | Seeded from the clock; a different run each time |
| `new Random(integer seed)` | `Random` | Seeded by hand; the same run every time |

**Seed it by hand to make a program repeatable**, which is what a test wants and what makes a
shuffle worth debugging. There is no way to seed the shared generator, as in .NET: a program that
needs the same sequence twice holds its own, and holding it is the thing that makes it
reproducible.

```
Random dice = new Random(42);

loop for roll = 1 to 3
    Console.WriteLine(dice.Next(1, 7));
end loop
```

## Asking for a number

| Member | Yields | What it does |
|---|---|---|
| `Next()` | `integer` | Any non-negative whole number |
| `Next(integer below)` | `integer` | From `0` up to but not including `below` |
| `Next(integer from, integer below)` | `integer` | From `from` up to but not including `below` |
| `NextDouble()` | `real` | From `0.0` up to but not including `1.0` |

Both bounded forms exclude their upper end, the same reading `until` has in a loop — so
`Next(1, 7)` is a die. That surprises everyone exactly once, and would surprise them a second
time if this were the one language that read it the other way.

## Through the name, or through one you hold

Every member above answers both ways. `Random.Next(1, 7)` draws from a generator the language
keeps; `dice.Next(1, 7)` draws from yours.

Most programs want the first and should not have to build anything to get it. Reach for the
second when the sequence matters — when it has to repeat, or when one part of a program drawing
numbers must not disturb another.

```
Console.WriteLine(Random.Next(1, 7) >= 1);         # true — no generator to make

Random shuffle = new Random(7);
Console.WriteLine(shuffle.NextDouble() < 1.0);     # true — and the same every run
```

## Nearby

[Math](math.md) for the rest of the arithmetic reached through a name, and
[numbers](numbers.md) for what each number type answers about itself.
