# Exceptions

[← Back to the index](README.md)

`Exception` is the root of everything that can be thrown, and one of only two built-in models a
program may extend — the other being `Model`.

| Section | Members |
|---|---|
| [The member every exception carries](#the-member-every-exception-carries) | `Message` |
| [Declaring your own](#declaring-your-own) | — |
| [What the language raises](#what-the-language-raises) | the eleven names |
| [When to throw and when to yield an optional](#when-to-throw-and-when-to-yield-an-optional) | — |

## The member every exception carries

| Member | Yields | What it does |
|---|---|---|
| `Message()` | `string` | What went wrong, as text |

Carried by every exception, including one a program declares.

```
integer divisor = 0;

try
    integer half = 10 / divisor;
catch DivideByZeroException problem
    Console.WriteLine(problem.Message());
end try
```

**The divisor arrives in a variable on purpose.** `10 / 0` written out does not compile at all —
`PC0324` catches a zero the compiler can see, so there is nothing left to catch at run time. The
exception is for the zero it cannot see.

## Declaring your own

Extend `Exception` and hand the message up with `base(...)`. `Exception` declares no constructor a
program can see, but it takes the message every exception carries — which is what makes
`base("...")` reach `Message()`.

```
model NotEnoughMoney extends Exception
    public function NotEnoughMoney(string why)
        base(why);
    end function
end model

shared model Program
    function Main()
        try
            throw new NotEnoughMoney("the account is empty");
        catch NotEnoughMoney problem
            Console.WriteLine(problem.Message());
        end try
    end function
end model
```

**Catching by an ancestor catches the children too.** A `catch Exception` reaches everything, and a
`catch` on a model you declared reaches anything extending it.

## What the language raises

Eleven names. Each is the same name at run time as the one a program writes, so what the language
raises is what a program names — and all but the last are ones a `catch` can take.

| Exception | Raised when |
|---|---|
| `Exception` | The root; catches every one below |
| `DivideByZeroException` | Dividing by a zero the compiler could not see, including a zero denominator in `Fraction.Create` |
| `IndexOutOfRangeException` | An index outside the set or string it is used on |
| `EmptyOptionalException` | `Value()` on an [optional](optionals.md) that turned out empty |
| `SequenceChangedException` | A [set](sets.md) changed while a `loop each` was walking it |
| `InvalidCastException` | An `as` to a type the value is not |
| `FormatException` | A pattern `Format` does not recognize |
| `ArgumentException` | A value a member cannot work with |
| `OverflowException` | A number grown too large to hold, including a fraction's parts |
| `RecursionTooDeepException` | Recursion with no base case; **nothing catches this one** |
| `IOException` | Anything that goes wrong with a file except its absence |

**`RecursionTooDeepException` is nameable but not catchable.** It has a name so that a reader can
be told what stopped their program, and it stops the program because there is nothing useful to
do about it: the stack that would run the handler is the stack that just ran out. A `catch`
naming it is reported (`PC0344`) rather than left sitting there looking like a handler.

**Absence is never an exception.** A file that is not there, text that does not read as a number,
input that has run out — each of those yields an [optional](optionals.md) instead, because each is
an ordinary thing to happen rather than a fault.

## Two that are worth reading twice

**`OverflowException` on a fraction rarely names the culprit.** Denominators multiply every time
two unlike fractions are added, so a long chain of them can outgrow an integer even where no
single fraction looks large. The operand you are looking at is rarely the one at fault.

**`SequenceChangedException` has a compile-time twin.** Where the compiler can see a set being
changed inside a walk of itself, it is an error rather than something to catch. The exception is
for the cases it cannot see — a set reached through a parameter, for instance.

## When to throw and when to yield an optional

The library's own answer, worth copying: **throw when the caller has made a claim that turned out
false; yield an optional when the answer genuinely might not exist.**

`Value()` on an empty optional throws, because reaching that line meant telling the compiler the
value was there. `File.Read` on a missing file yields nothing, because nobody claimed the file
existed.

## Also on every exception

[`ToString()` and `Equals()`](every-value.md).
