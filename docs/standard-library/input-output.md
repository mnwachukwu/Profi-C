# Input and output

[← Back to the index](README.md)

Three models, all reached through their names: there is no such thing as *a* `Console`, *a* `File`
or *a* `Directory`. A file is not a thing a program holds — it is somewhere a program puts text
and takes it back.

<a id="console"></a>

## `Console`

| Member | Yields | What it does |
|---|---|---|
| `Console.Write(anything)` | nothing | Writes a value, staying on the line |
| `Console.WriteLine(anything)` | nothing | Writes a value and ends the line |
| `Console.WriteLine()` | nothing | Ends the line |
| `Console.Read()` | `string?` | The next line typed, or nothing when input has run out |

**Both writers take a value of any type**, and behave exactly as in C#: only `WriteLine` ends the
line. Neither is an overload set — both are known to the compiler, which decides how to render the
value from its static type. That is why a `boolean` prints `true` rather than `True`, and a
fraction prints `1|2`.

**`Read` yields an [optional](optionals.md)** because the input running out is an answer rather
than a fault.

```
Console.Write("What is your name? ");

string name = Console.Read().Or("stranger");
Console.WriteLine("Hello, " + name);
```

Reading a number means reading text and then asking it to be one — see
[reading a value back out](text.md#reading-a-value-back-out):

```
Console.Write("How old are you? ");

integer? age = Console.Read().Or("").ToInteger();

if age.HasValue()
    Console.WriteLine("Next year you will be " + (age.Value() + 1));
else
    Console.WriteLine("That was not a whole number.");
end if
```

<a id="file"></a>

## `File`

### Reading

| Member | Yields | Absent when |
|---|---|---|
| `File.Read(string path)` | `string?` | There is no such file |
| `File.ReadLines(string path)` | `string[]?` | There is no such file |

**Absence means "not there", and nothing else.** Everything else that can go wrong — a locked
file, a bad path, a full disk — raises `IOException`, because an absent optional cannot say which
of those it was.

**This is why there is no "check first" pattern to write.** Asking `File.Exists` and then reading
is the version that races: the file can go between the two lines. Reading and handling the absence
cannot.

### Writing

| Member | Yields | What it does |
|---|---|---|
| `File.Write(string path, string text)` | nothing | Replaces whatever was there |
| `File.WriteLines(string path, string[] lines)` | nothing | The same, one line each |
| `File.Append(string path, string text)` | nothing | Adds to the end |

All three make the file when there is none. **None of them makes the folder it sits in** — a path
with a typo in it should fail rather than quietly build somewhere new.

### Managing

| Member | Yields | What it does |
|---|---|---|
| `File.Exists(string path)` | `boolean` | Whether it is there |
| `File.Delete(string path)` | `boolean` | Removes it; whether there was one |
| `File.Copy(string from, string to)` | nothing | Copies it |
| `File.Move(string from, string to)` | nothing | Moves or renames it |
| `File.Size(string path)` | `integer?` | Bytes, or nothing where there is no file |
| `File.Changed(string path)` | `DateTime?` | When it last changed, or nothing |

`Delete` yields whether there was something to delete, exactly as removing from a
[set](sets.md#changing-it) does.

### How text is stored

**UTF-8 with no mark at the front.** Writing ends every line with `\n`; reading accepts either
that or `\r\n` and gives back neither — so a file written on one machine reads the same on
another.

```
File.WriteLines("notes.txt", {"first", "second"});
File.Append("notes.txt", "third\n");

string[] lines = File.ReadLines("notes.txt").Or({});

Console.WriteLine(lines.Count);       # 3
Console.WriteLine(lines.Join(" / "));   # first / second / third

File.Delete("notes.txt");
```

<a id="directory"></a>

## `Directory`

| Member | Yields | What it does |
|---|---|---|
| `Directory.Current` | `string` | The folder the program is running in |
| `Directory.Exists(string path)` | `boolean` | Whether it is there |
| `Directory.Create(string path)` | nothing | Makes it, and every folder on the way |
| `Directory.Delete(string path)` | `boolean` | Removes it; whether there was one |
| `Directory.Files(string path)` | `string[]?` | The files directly inside, or nothing where there is no folder |
| `Directory.Folders(string path)` | `string[]?` | The folders directly inside, or nothing |

`Directory.Current` is a **value** and takes no parentheses.

**`Create` makes every folder on the way**, since making one inside another that is not there yet
is the ordinary reason to ask. That is the opposite of `File.Write`, which deliberately does not —
writing a file is about the file, and making folders on the way would hide a mistyped path.

**`Files` and `Folders` do not descend.** What is directly inside is what you get, which is the
same rule a `.pcp` project follows for a `source` naming a folder.

```
Directory.Create("out/reports");

Console.WriteLine(Directory.Exists("out/reports"));       # true

File.Write("out/reports/one.txt", "hello");
Console.WriteLine(Directory.Files("out/reports").Or({}).Count);   # 1

Directory.Delete("out");
```

## What can go wrong

| Raised | When |
|---|---|
| `IOException` | Anything that is not the file simply being absent |
| `FormatException` | A pattern the runtime does not recognize, in `Format` |

See [exceptions](exceptions.md) for catching them.
