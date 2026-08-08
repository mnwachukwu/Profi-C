# Sets

[← Back to the index](README.md)

Everything a `T[]` answers, whatever `T` is. A Profi-C set is a row of things that **keeps its
order** and **allows a value twice** — it is what C# spells `List<T>` rather than what
mathematics calls a set, and [`Distinct`](#distinct) is how you ask for the mathematical one.

A set's members deliberately mirror [a string's](text.md), so that the two read alike.

| Section | Members |
|---|---|
| [Asking about it](#asking-about-it) | `Count` `Contains` `IndexOf` |
| [Changing it](#changing-it) | `Insert` `InsertAt` `Remove` `RemoveAt` `Clear` |
| [Taking a run](#taking-a-run) | `Subset` |
| [Two sets read together](#two-sets-read-together) | `Union` `Intersect` `Except` `Distinct` |
| [Joining](#joining) | `Join` |
| [Dropping the empties](#dropping-the-empties) | `Trim` `TrimStart` `TrimEnd` `TrimAll` |
| [Sets of sets](#sets-of-sets) | — |

Unlike a string, **a set can be changed**. `Insert` and `Remove` alter the set you called them on;
`Subset`, `Union` and the rest give back a new one and leave the original alone. The table says
which is which.

## Asking about it

| Member | Yields | What it does |
|---|---|---|
| `Count` | `integer` | How many elements |
| `Contains(T what)` | `boolean` | Whether `what` is in it |
| `IndexOf(T what)` | `integer` | Where `what` first sits, or `-1` where it is absent |

```
integer[] scores = {70, 85, 85, 90};

Console.WriteLine(scores.Count);        # 4
Console.WriteLine(scores.Contains(85));   # true
Console.WriteLine(scores.IndexOf(85));    # 1 — the first one
```

## Changing it

These five change the set in place and are the only members that do.

| Member | Yields | What it does |
|---|---|---|
| `Insert(T what)` | nothing | Adds `what` to the end |
| `InsertAt(integer where, T what)` | nothing | Puts `what` in at `where` |
| `Remove(T what)` | `boolean` | Takes the first `what` out; whether there was one |
| `RemoveAt(integer where)` | nothing | Takes out whatever is at `where` |
| `Clear()` | nothing | Empties it |

`Remove` is the only mutator that yields anything, matching the list it is built on: it answers
whether there was something to remove, which saves asking `Contains` first.

```
string[] queue = {};

queue.Insert("Ada");
queue.Insert("Grace");
queue.InsertAt(0, "Alan");

Console.WriteLine(queue.Join(", "));    # Alan, Ada, Grace
Console.WriteLine(queue.Remove("Ada")); # true
Console.WriteLine(queue.Remove("Ada")); # false — there was only one
```

**A set cannot be changed while a `loop each` is walking it.** Inserting, removing or clearing
mid-walk raises `SequenceChangedException`, and where the compiler can see it happening it is an
error instead. Collect what to remove and do it afterwards.

## Taking a run

| Member | Yields | What it does |
|---|---|---|
| `Subset(integer start)` | `T[]` | A new set, from `start` to the end |
| `Subset(integer start, integer end)` | `T[]` | A new set, from `start` up to but not including `end` |

The end is exclusive — the same reading `until` has in a loop — so `Subset(0, n)` and
`Subset(n, count)` put the whole set back together.

```
integer[] all = {1, 2, 3, 4, 5};

Console.WriteLine(all.Subset(2).Join(","));      # 3,4,5
Console.WriteLine(all.Subset(1, 3).Join(","));   # 2,3
```

## Two sets read together

All four give back a new set and leave both originals alone.

| Member | Yields | What it does |
|---|---|---|
| `Union(T[] other)` | `T[]` | This set, then the other, end to end |
| `Intersect(T[] other)` | `T[]` | What is in both, in this set's order |
| `Except(T[] other)` | `T[]` | What this has that the other does not |
| `Distinct()` | `T[]` | One of each, keeping the first of every run |

**These are not the operations of the same name in mathematics**, and the difference is worth
having straight. Because a Profi-C set keeps order and allows a value twice, `Union` *appends*
rather than merging — what was in both ends up in the answer twice. `Distinct` is what turns a
row of things into a mathematical set, and it is only ever done when asked.

`Intersect` and `Except` are each other's counterpart: every element of this set goes to exactly
one of the two, repeats included, so between them they account for all of it. Appending one to
the other does not rebuild it, though — each gathers its own in this set's order and the two runs
then sit end to end, so `{1, 2, 3}` against `{3, 4}` comes back as `3,1,2`.

<a id="distinct"></a>

```
integer[] mine = {1, 2, 3};
integer[] yours = {3, 4};

Console.WriteLine(mine.Union(yours).Join(","));               # 1,2,3,3,4
Console.WriteLine(mine.Union(yours).Distinct().Join(","));    # 1,2,3,4
Console.WriteLine(mine.Intersect(yours).Join(","));           # 3
Console.WriteLine(mine.Except(yours).Join(","));              # 1,2
```

<a id="joining"></a>

## Joining

| Member | Yields | What it does |
|---|---|---|
| `Join(string separator)` | `string` | Every element written out, with `separator` between |

**Any set answers it, not only a set of strings.** Each element is written out the way it would be
on its own, which is what a reader joining numbers expects and what they would otherwise have to
write a loop for.

```
integer[] scores = {70, 85, 90};
Console.WriteLine(scores.Join(" | "));   # 70 | 85 | 90
```

## Dropping the empties

**Only on a set of [optionals](optionals.md).** A `T[]` that cannot hold an absence has nothing to
trim, so these four members are not offered on one.

| Member | Yields | What it does |
|---|---|---|
| `Trim()` | `T?[]` | Empties off both ends |
| `TrimStart()` | `T?[]` | Empties off the front |
| `TrimEnd()` | `T?[]` | Empties off the end |
| `TrimAll()` | `T[]` | Every empty gone, anywhere |

**`TrimAll` is the one that changes the type**, and that is the point of it. Removing every empty
leaves a set where nothing can be absent, so it yields the underlying type and the caller stops
having to unwrap. The other three take from the ends only, so an empty in the middle survives and
the type has to keep saying so.

```
integer?[] readings = {}; # ... filled from somewhere that may answer nothing

integer[] certain = readings.TrimAll();

# No unwrapping needed: nothing in 'certain' can be absent.
loop each reading in certain
    Console.WriteLine(reading + 1);
end loop
```

## Sets of sets

`integer[][]` is a set whose elements are sets, and needs no feature of its own — every member
above works on it, with `T` being `integer[]`.

```
integer[][] grid = {{1, 2}, {3, 4}};

Console.WriteLine(grid.Count);          # 2
Console.WriteLine(grid[0].Join(","));     # 1,2
```

## Also on every set

[`ToString()` and `Equals()`](every-value.md), as on every value. `Equals` compares element by
element, so two sets holding equal values in the same order are equal.
