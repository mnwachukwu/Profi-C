# Dates and times

[← Back to the index](README.md)

**Four types, because there are four different questions.**

| Type | Answers | Example |
|---|---|---|
| `Date` | Which day | a birthday |
| `Time` | What time of day | when a shop opens |
| `TimeSpan` | How long | how long a journey takes |
| `DateTime` | Which moment | when a message was sent |

Keeping them apart is what makes the awkward cases come out right: **23:30 plus an hour is 00:30
on a clock, but the next day as a moment.** A `Time` wraps; a `DateTime` does not.

Every one of the four is a model with instances, and each has members reached two ways — through
the name (`DateTime.Now`) and through a value you are holding (`landing.Year`). The tables say
which.

| Section | Members |
|---|---|
| [Making one](#making-one) | `new DateTime` `new Date` `new Time` `new TimeSpan` |
| [Reading the clock](#reading-the-clock) | `Now` `Today` `Zero` |
| [Making a TimeSpan from an amount](#making-a-timespan-from-an-amount) | `FromDays` `FromHours` `FromMinutes` `FromSeconds` |
| [Moving between the four](#moving-between-the-four) | `FromDateTime` `Date` `Time` `ToDateTime` `ToTimeSpan` |
| [Reading the parts](#reading-the-parts) | `Year` `Month` `Day` `Hour` `Minute` `Second` `DayOfWeek` `DayOfYear` `Days` `Hours` `Minutes` `Seconds` `TotalDays` `TotalHours` `TotalMinutes` `TotalSeconds` |
| [Moving forward and back](#moving-forward-and-back) | `AddYears` `AddMonths` `AddDays` `AddHours` `AddMinutes` `AddSeconds` `Add` `Subtract` `Negate` `Duration` |
| [Comparing](#comparing) | `CompareTo` |
| [Writing one out, and reading one back](#writing-one-out-and-reading-one-back) | `Format` `Parse` |

## Making one

| Member | Yields |
|---|---|
| `new DateTime(integer year, integer month, integer day)` | `DateTime` |
| `new DateTime(integer year, integer month, integer day, integer hour, integer minute, integer second)` | `DateTime` |
| `new DateTime(Date day)` | `DateTime` |
| `new DateTime(Date day, Time time)` | `DateTime` |
| `new Date(integer year, integer month, integer day)` | `Date` |
| `new Time(integer hour, integer minute)` | `Time` |
| `new Time(integer hour, integer minute, integer second)` | `Time` |
| `new TimeSpan(integer hours, integer minutes, integer seconds)` | `TimeSpan` |
| `new TimeSpan(integer days, integer hours, integer minutes, integer seconds)` | `TimeSpan` |

## Reading the clock

Reached through the name. All are **values**, so none takes parentheses.

| Member | Yields | What it is |
|---|---|---|
| `DateTime.Now` | `DateTime` | This moment |
| `DateTime.Today` | `DateTime` | Midnight at the start of today |
| `Date.Today` | `Date` | Today's day |
| `Time.Now` | `Time` | The time of day now |
| `TimeSpan.Zero` | `TimeSpan` | No time at all |

## Making a `TimeSpan` from an amount

Reached through the name, and each takes a `real` so half-hours work.

| Member | Yields |
|---|---|
| `TimeSpan.FromDays(real)` | `TimeSpan` |
| `TimeSpan.FromHours(real)` | `TimeSpan` |
| `TimeSpan.FromMinutes(real)` | `TimeSpan` |
| `TimeSpan.FromSeconds(real)` | `TimeSpan` |

## Moving between the four

| Member | Yields | What it does |
|---|---|---|
| `Date.FromDateTime(DateTime)` | `Date` | The day part |
| `Time.FromDateTime(DateTime)` | `Time` | The time-of-day part |
| `someDateTime.Date` | `Date` | The same, read off a value |
| `someDateTime.Time` | `Time` | The same, read off a value |
| `someDate.ToDateTime(Time)` | `DateTime` | A day and a time joined into a moment |
| `someTime.ToTimeSpan()` | `TimeSpan` | Time of day as time since midnight |

## Reading the parts

All **values** — read off a value you are holding, without parentheses, exactly as .NET reads
them.

| On | Members | Each yields |
|---|---|---|
| `DateTime` | `Year` `Month` `Day` `Hour` `Minute` `Second` `DayOfWeek` `DayOfYear` | `integer` |
| `Date` | `Year` `Month` `Day` `DayOfWeek` `DayOfYear` | `integer` |
| `Time` | `Hour` `Minute` `Second` | `integer` |
| `TimeSpan` | `Days` `Hours` `Minutes` `Seconds` | `integer` |
| `TimeSpan` | `TotalDays` `TotalHours` `TotalMinutes` `TotalSeconds` | `real` |

**`Hours` and `TotalHours` are not the same question.** For an hour and a half, `Hours` is `1` and
`Minutes` is `30`; `TotalHours` is `1.5`. The first four break the span into parts, the last four
express the whole span in one unit.

```
TimeSpan journey = TimeSpan.FromMinutes(90);

Console.WriteLine(journey.Hours);        # 1
Console.WriteLine(journey.Minutes);      # 30
Console.WriteLine(journey.TotalHours);   # 1.5
```

## Moving forward and back

| On | Member | Yields |
|---|---|---|
| `DateTime` | `AddDays(real)` `AddHours(real)` `AddMinutes(real)` `AddSeconds(real)` | `DateTime` |
| `DateTime` | `AddYears(integer)` `AddMonths(integer)` | `DateTime` |
| `DateTime` | `Add(TimeSpan)` | `DateTime` |
| `DateTime` | `Subtract(TimeSpan)` | `DateTime` |
| `DateTime` | `Subtract(DateTime)` | `TimeSpan` |
| `Date` | `AddDays(integer)` `AddMonths(integer)` `AddYears(integer)` | `Date` |
| `Time` | `AddHours(real)` `AddMinutes(real)` | `Time` |
| `TimeSpan` | `Add(TimeSpan)` `Subtract(TimeSpan)` | `TimeSpan` |
| `TimeSpan` | `Negate()` | `TimeSpan` |
| `TimeSpan` | `Duration()` | `TimeSpan` |

**`Subtract` on a `DateTime` has two readings, and they give back different types.** Taking one
moment from another asks *how long between them* and yields a `TimeSpan`; taking a span from a
moment asks *what moment then* and yields a `DateTime`.

**`Date.AddDays` takes a whole number** where `DateTime.AddDays` takes a `real` — half a day is
not a day.

`Negate` turns a span around; `Duration` gives its size regardless of direction, so a span of
minus two hours has a duration of two hours.

```
DateTime sent = new DateTime(2026, 3, 1, 9, 0, 0);
DateTime read = new DateTime(2026, 3, 1, 11, 30, 0);

TimeSpan waited = read.Subtract(sent);
Console.WriteLine(waited.TotalMinutes);          # 150

Console.WriteLine(sent.AddDays(1).Day);          # 2
```

### The one that catches people out

```
Time late = new Time(23, 30, 0).AddHours(1);
Console.WriteLine(late.Hour);                    # 0 — a clock wraps

DateTime moment = new DateTime(2026, 3, 1, 23, 30, 0).AddHours(1);
Console.WriteLine(moment.Day);                   # 2 — a moment does not
```

## Comparing

| Member | Yields | What it does |
|---|---|---|
| `CompareTo(same type)` | `integer` | Negative if earlier, zero if equal, positive if later |

Every one of the four answers it against its own type. Ordinary `<` and `>` work too; `CompareTo`
is there for when you want the three-way answer in one go, as when sorting.

## Writing one out, and reading one back

| Member | Yields | What it does |
|---|---|---|
| `Format(string pattern)` | `string` | Written out by a pattern |
| `Parse(string text)` | `T?` | Read back, or nothing |
| `Parse(string text, string pattern)` | `T?` | Read back by a pattern, or nothing |

`Format` is on a value; `Parse` is reached through the name. All four types have both.

**The patterns are .NET's own, unchanged** — `yyyy-MM-dd` is a date, `HH:mm` a 24-hour time,
`hh\:mm` a span. **`Parse` yields an [optional](optionals.md)** because text that does not read is
the ordinary case, not a fault: most of it was typed by somebody.

```
DateTime landing = new DateTime(1969, 7, 20, 20, 17, 0);

Console.WriteLine(landing.Format("yyyy-MM-dd"));      # 1969-07-20
Console.WriteLine(landing.Format("HH:mm"));           # 20:17

DateTime? read = DateTime.Parse("1969-07-20", "yyyy-MM-dd");
Console.WriteLine(read.HasValue());                   # true
Console.WriteLine(DateTime.Parse("not a date").HasValue());   # false
```

## Also on every one of the four

[`ToString()` and `Equals()`](every-value.md).
