# Text

[← Back to the index](README.md)

Everything a `string` answers. Every member here yields a **new** string and leaves the original
exactly as it was — a Profi-C `string` cannot be changed once it exists, which is why `Insert`
gives you something back rather than doing something.

| Section | Members |
|---|---|
| [Asking about it](#asking-about-it) | `Count` `Contains` `IndexOf` |
| [Taking a piece](#taking-a-piece) | `Substring` `Subset` |
| [Building a new one](#building-a-new-one) | `Insert` `InsertAt` `Remove` `RemoveAt` `Replace` |
| [Trimming](#trimming) | `Trim` `TrimStart` `TrimEnd` |
| [Case](#case) | `ToUpper` `ToLower` `Capitalize` |
| [Splitting and joining](#splitting-and-joining) | `Split` `ToCharacters` |
| [Reading a value back out](#reading-a-value-back-out) | `ToInteger` `ToReal` `ToFloat` `ToBoolean` `ToCharacter` `ToFraction` |
| [Writing a number into text](#writing-a-number-into-text) | `Format` |
| [String](#string) | `String.Empty` |
| [Boolean and Character](#boolean-and-character) | `Boolean.Parse` `Character.Parse` |

A string's members deliberately mirror [a set's](sets.md), so that the two read alike: a string is
a run of characters, and asking how long it is, whether it contains something, or for a piece of
it are the same questions in both places.

**Text that is empty changes nothing.** Asked to replace nothing, remove nothing, or trim nothing
from an end, a string comes back as it was — and the questions answer the way an empty argument
should: `"ab".Contains("")` is `true`, `"ab".IndexOf("")` is `0`, and `"ab".Split("")` is one
piece holding the whole string. One rule covers the family, so no member here raises on an empty
argument and none treats one as a special case:

```
string pair = "ab";

Console.WriteLine(pair.Replace("", "X"));   # ab — replacing nothing is nothing to do
Console.WriteLine(pair.Remove(""));         # ab
Console.WriteLine(pair.Trim(""));           # ab
Console.WriteLine(pair.Contains(""));       # true
Console.WriteLine(pair.IndexOf(""));        # 0
```

C# refuses the first two outright, and Python and JavaScript read an empty search as matching
between every character, so `"ab".Replace("", "X")` is `"XaXbX"` there. Neither is what somebody
writing it means, and neither agrees with `Trim("")` leaving a string alone.

## Asking about it

| Member | Yields | What it does |
|---|---|---|
| `Count` | `integer` | How many characters |
| `Contains(string what)` | `boolean` | Whether `what` appears anywhere |
| `IndexOf(string what)` | `integer` | Where `what` starts, or `-1` where it does not appear |

```
string greeting = "Hello, world";

Console.WriteLine(greeting.Count);            # 12
Console.WriteLine(greeting.Contains("world"));  # true
Console.WriteLine(greeting.IndexOf("world"));   # 7
Console.WriteLine(greeting.IndexOf("moon"));    # -1
```

## Taking a piece

| Member | Yields | What it does |
|---|---|---|
| `Substring(integer start, integer length)` | `string` | `length` characters from `start` |
| `Subset(integer start)` | `string` | From `start` to the end |
| `Subset(integer start, integer end)` | `string` | From `start` up to but not including `end` |

**`Substring` and `Subset` do the same job and differ only in their second number.** `Substring`
takes *how many*; `Subset` takes *where to stop*. Use whichever matches the number already at
hand. `Substring` exists because it is the name a reader arriving from C# will type.

`Subset`'s end is exclusive — the same reading `until` has in a loop — so `Subset(0, n)` and
`Subset(n, count)` put the whole string back together.

```
string word = "computer";

Console.WriteLine(word.Substring(0, 3));   # com
Console.WriteLine(word.Subset(0, 3));      # com — same three characters, other number
Console.WriteLine(word.Subset(3));         # puter
```

## Building a new one

| Member | Yields | What it does |
|---|---|---|
| `Insert(string what)` | `string` | `what` added to the end |
| `InsertAt(integer where, string what)` | `string` | `what` put in at `where` |
| `Remove(string what)` | `string` | Every `what` taken out |
| `RemoveAt(integer where)` | `string` | The character at `where` taken out |
| `Replace(string what, string with)` | `string` | Every `what` swapped for `with` |

```
string name = "Ada";

Console.WriteLine(name.Insert(" Lovelace"));       # Ada Lovelace
Console.WriteLine(name.InsertAt(1, "----"));       # A----da
Console.WriteLine("banana".Replace("a", "o"));     # bonono
Console.WriteLine("banana".Remove("na"));          # ba — both of them
```

**`Remove` takes out every appearance, where [a set's `Remove`](sets.md#changing-it) takes out
the first.** The two are not the same question: a set answers whether there was one to remove and
alters the set it was called on, while a string cannot be altered at all, so removing from one
builds a new string and there is nothing to report back. Taking the first only would need a
position to say which, and that is what `RemoveAt` is for.

## Trimming

Three members, three forms each. Written with nothing, whitespace goes; written with a string, any
of *its* characters go; written with a set of characters, any in the set goes.

| Member | Yields | What it does |
|---|---|---|
| `Trim()` | `string` | Whitespace off both ends |
| `Trim(string characters)` | `string` | Any of those characters off both ends |
| `Trim(character[] characters)` | `string` | The same, from a set |
| `TrimStart()` · `TrimStart(string)` · `TrimStart(character[])` | `string` | The front only |
| `TrimEnd()` · `TrimEnd(string)` · `TrimEnd(character[])` | `string` | The end only |

The string form is the common one. The set form takes a `character[]`, which is what a program
already holds when the characters were computed rather than written literally.

```
Console.WriteLine("  spaced  ".Trim());          # spaced
Console.WriteLine("xxhellox".Trim("x"));         # hello
Console.WriteLine("xxhellox".TrimStart("x"));    # hellox
```

## Case

| Member | Yields | What it does |
|---|---|---|
| `ToUpper()` | `string` | Every letter raised |
| `ToLower()` | `string` | Every letter lowered |
| `Capitalize()` | `string` | The first letter raised, the rest left exactly as it was |

**`Capitalize` is the language's own** rather than .NET's. .NET's title-casing also *lowers*
everything it did not raise, so `"McDonald"` would come back as `"Mcdonald"`.

```
Console.WriteLine("hello".ToUpper());        # HELLO
Console.WriteLine("McDonald".Capitalize());  # McDonald
Console.WriteLine("hello".Capitalize());     # Hello
```

## Splitting and joining

| Member | Yields | What it does |
|---|---|---|
| `Split(string separator)` | `string[]` | The pieces between each `separator` |
| `ToCharacters()` | `character[]` | Every character as a set |

**Joining is a member of the set, not of the string** — see [`Join`](sets.md#joining). The thing
being joined is the collection, and reading it off the separator would put the sentence the wrong
way round.

```
string[] words = "one,two,three".Split(",");

Console.WriteLine(words.Count);     # 3
Console.WriteLine(words.Join(" & "));  # one & two & three
```

## Reading a value back out

Each of these yields an [optional](optionals.md) rather than raising, because text that will not
read is the ordinary case — most of it was typed by somebody.

| Member | Yields | Written the other way | What it does |
|---|---|---|---|
| `ToInteger()` | `integer?` | `Integer.Parse(text)` | The whole number the text spells, or nothing |
| `ToReal()` | `real?` | `Real.Parse(text)` | The number the text spells, or nothing |
| `ToFloat()` | `float?` | `Float.Parse(text)` | The same, held as a `float` |
| `ToBoolean()` | `boolean?` | `Boolean.Parse(text)` | `true` or `false`, or nothing |
| `ToCharacter()` | `character?` | `Character.Parse(text)` | The one character the text holds, or nothing |
| `ToFraction()` | `fraction?` | `Fraction.Parse(text)` | The ratio the text spells, or nothing |

**Both spellings are the same question**, and the same one method answers each pair, so they
cannot come to disagree. Which one reads better depends on where you are: a string already in
hand answers `typed.ToInteger()`, and text arriving from somewhere else reads more directly as
`Integer.Parse(Console.Read().Or(""))`. Neither is the preferred form.

**`ToCharacter` wants exactly one character.** Two is as empty an answer as none — there is no
reason to think a reader meant the first of them — so `"ab"` yields nothing rather than `'a'`.

**`ToFraction` reads either mark between the halves.** The language writes `22|7`, because a slash
already means division; a person writes `22/7`, because that is what a fraction looks like
everywhere outside a compiler. Reading takes both.

```
string typed = Console.Read().Or("");

if typed.ToInteger().HasValue()
    Console.WriteLine("that is " + typed.ToInteger().Value());
else
    Console.WriteLine("that is not a whole number");
end if
```

```
# The same reading, reached through the type's own name.
integer year = Integer.Parse(Console.Read().Or("")).Or(2026);
Console.WriteLine(year);
```

## Writing a number into text

See [`Format`](numbers.md#writing-a-number-out) on `integer`, `real` and `fraction`, and the
[interpolated string](../language-spec.md#10-strings) form `"{{ value }}"`, which is usually what
you want instead of joining with `+`.

## `String`

Note the two spellings, as with `fraction` and `Fraction`: **`string` is the type** and a reserved
word; **`String` is the model** beside it, and it holds one thing.

| Member | Yields | What it is |
|---|---|---|
| `String.Empty` | `string` | The string with nothing in it |

Not a bound, unlike the [capitalized names beside the numbers](numbers.md#what-each-type-knows-about-itself)
— a name for the emptiness, which reads better than `""` wherever the emptiness is the point.

```
string typed = String.Empty;
Console.WriteLine(typed.Count);   # 0
```

## `Boolean` and `Character`

The same two spellings again, and these two capitals hold one thing each: the way in from text.

| Member | Yields | What it does |
|---|---|---|
| `Boolean.Parse(string)` | `boolean?` | `true` or `false`, or nothing |
| `Character.Parse(string)` | `character?` | The one character the text holds, or nothing |

They exist for the sake of the convention rather than for anything they add. `Integer.Parse` and
`Real.Parse` are there because a number has bounds to keep beside them; a boolean and a character
have no such fact, and leaving these two out would have left a reader who found the first pair to
guess that the other two simply cannot be read from text — which is the guess a half-applied
convention invites, and it would be wrong.

The numbers keep their own capitals, holding [where each one runs
out](numbers.md#what-each-type-knows-about-itself) as well as a `Parse`.

```
boolean agreed = Boolean.Parse(Console.Read().Or("")).Or(false);
character grade = Character.Parse("A").Or('?');

Console.WriteLine(agreed);
Console.WriteLine(grade);
```

## Also on every string

[`ToString()` and `Equals()`](every-value.md).
