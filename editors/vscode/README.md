# Profi-C for VS Code

Syntax highlighting for `.pc` programs and `.pcp` project files, and the editor behavior that
comes with knowing the language: bracket matching, `Ctrl+/` inserting `#`, auto-closing quotes,
and indentation that follows `end`.

Nothing here runs the compiler. There are no diagnostics, no completion, and no hover — those
need a language server, which is a later piece of work.

## Installing it

**This is not on the VS Code Marketplace**, and will not be for a while — Profi-C is young
enough that publishing an extension for it would be putting up a shopfront before there is
anything to sell. Installing it means putting this folder where VS Code looks, which is all the
Marketplace would do anyway. There is no build step: everything here is declarative, so nothing
is compiled and nothing is downloaded. Once it is published, `code --install-extension` replaces
all of this.

There are two ways to do it, and which one is right depends on whether the grammar is going to
change under you.

### Linking it — for anyone editing the language

The extensions directory holds a pointer to this folder, so the editor reads the very files in
the repository. A change shows up on the next window reload and there is no copy to remember.

**Windows** needs neither an elevated shell nor Developer Mode if you use a *junction*:

```powershell
$repo = "D:\Repos\Profi-C"
$dest = "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0"
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Junction -Path $dest -Target "$repo\editors\vscode"
```

A **symbolic link** does the same job and needs an elevated shell, or Developer Mode turned on:

```powershell
New-Item -ItemType SymbolicLink -Path $dest -Target "$repo\editors\vscode"
```

The two differ in ways that do not matter here. A junction is resolved by the file system and
works only for a directory on a local volume; a symbolic link may point at a file, at a
relative path, or across the network, and is the more general tool. Pointing one local folder
at another is exactly what a junction is for, so it is the one to reach for on Windows — the
elevation a symbolic link asks for buys nothing in this case.

**macOS and Linux** have one answer:

```bash
repo=~/Profi-C
dest=~/.vscode/extensions/profi-c-0.1.0
rm -rf "$dest" && ln -s "$repo/editors/vscode" "$dest"
```

> **Removing a link, when the time comes.** On Windows, do **not** use
> `Remove-Item -Recurse -Force`: Windows PowerShell has been known to follow a junction and
> delete what is on the other side of it, which here is the repository. Remove the link alone:
>
> ```powershell
> (Get-Item "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0" -Force).Delete()
> ```
>
> or `cmd /c rmdir "%USERPROFILE%\.vscode\extensions\profi-c-0.1.0"` with no `/s`. On macOS and
> Linux, `rm` on the link removes the link.

### Copying it — for anyone who only wants to read Profi-C

Change the first line to wherever you cloned the repository, then run the rest as-is.

**Windows** (PowerShell):

```powershell
$repo = "D:\Repos\Profi-C"
$dest = "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0"
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$repo\editors\vscode\*" $dest -Recurse -Force
```

**macOS and Linux**:

```bash
repo=~/Profi-C
dest=~/.vscode/extensions/profi-c-0.1.0
rm -rf "$dest" && mkdir -p "$dest" && cp -R "$repo/editors/vscode/." "$dest"
```

### Either way

Reload the window — `Ctrl+Shift+P`, "Developer: Reload Window" — and open a `.pc` file.

- **VS Code Insiders** uses `.vscode-insiders` rather than `.vscode`. Everything else is the
  same.
- **The folder name carries the version**, and VS Code will keep serving an old copy if a new
  one arrives under the same name. Bump it to match `package.json` when that changes.
- **A grammar edit lands on the next reload. A `package.json` edit does not.** Grammar files
  are read each time one is needed; everything under `contributes` is read once, at the scan,
  and written to `~/.vscode/extensions/extensions.json` with the version beside it. Raise
  `version` when you change what the manifest contributes, or the editor goes on serving what
  it recorded — silently, with the file on disk plainly saying otherwise. Deleting that cache
  and restarting forces a rescan if a bump ever fails to take.

**How a stale copy shows itself.** The editor colors by whatever rules it has, so a construct
the copy has never heard of is colored by the rules for something else, and the symptom looks
nothing like the cause. A word the copy does not know to be a keyword is caught by the rule for
a name followed by a bracket, and reads as a function call. A block string the copy predates
reads as an empty string followed by an ordinary one, so the first `"` inside it closes a
string that was never open and the rest of the file is colored as text.

Nothing is wrong with the grammar in either case; the editor is reading an old one. Before
hunting for a bug, check the installed copy holds the rule you expect:

```powershell
Select-String delegate "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0\syntaxes\profi-c.tmLanguage.json"
```

A link cannot go stale, which is the whole argument for one.

## What it colors

Reserved words, the primitive types, the types the language provides, literals of every form
including fraction literals like `22|7`, block strings, the holes in an interpolated string,
both comment forms, and the name a declaration introduces. A closer and what it closes read as one thing, so `end function` colors together
rather than as a keyword beside a noun.

## Comments the compiler heeds

A line comment can carry an `ignore` directive, which silences a warning or an opinion:

```
# ignore opinion
Console.WriteLine("");
```

Only the comments the compiler acts on are set apart — a remark that merely begins with the
word, like `# ignore the sign for now`, stays an ordinary comment, and a `##` block never
carries a directive at all. The scope is `comment.line.number-sign.directive.profi-c`.

A comment can also document what follows it, and the label inside one is colored apart from
the prose:

```
##
    @summary: One person's money, and the rules about taking it out.
    @remarks: The longer explanation, for a hover rather than a list.
##
model Account
```

Only the label is colored — the mark, the name and the colon together — never the prose after
it. The scope is `constant.language.documentation.profi-c`.

## Where the colors come from

**This extension sets no colors, and none of the ones above come from it.** An extension can
offer token colors through `configurationDefaults`, and the editor accepts the manifest and
then ignores them. There is no error and no warning; the manifest sits there naming a color
nobody ever sees, which is why this extension no longer carries one.

So every color on a Profi-C file comes from one of two places:

1. **A `textMateRules` entry**, in your user or workspace settings. This repository has one in
   `.vscode/settings.json`, which is what colors these files while working on the language.
2. **Your theme**, for any scope no rule names.

That second case is where the confusion lives. A theme knows nothing about `.profi-c` scopes,
so it falls back on the general part of the name — `constant.language.documentation.profi-c`
is painted as whatever the theme does with `constant.language`. In several dark themes that is
the same color as `keyword`, so a scope that looks wrong may simply be a scope with no rule.

**When a color will not take, it is nearly always a missing rule rather than a broken one.**
Put the cursor on the token and run:

```
Developer: Inspect Editor Tokens and Scopes
```

The last line names the rule that won. A `.profi-c` scope means a rule is being applied; a bare
`constant.language` or `keyword` means none is, and the theme is deciding.

## Checking what the grammar really does

The scopes above are what a theme paints, so being wrong about them is easy and quiet. The
test suite runs the grammar through the engine VS Code itself uses and asserts the scopes that
come out, rather than reading the grammar file and believing it.

It needs the engine installed once:

```bash
npm install
```

After that `dotnet test` covers it. Without it those tests skip rather than fail, since a fresh
checkout not having fetched them is an ordinary state to be in. To look at the scopes on a line
by hand:

```bash
echo '["# @summary: A thing."]' | node tools/scopes.js
```

## The Profi-C palette

Everything above is already colored by whatever theme you use, because the grammar names its
scopes the way every other language does and a theme's rule for `keyword` reaches
`keyword.declaration.profi-c`. Nothing has to be installed for a `.pc` file to read properly.

What a theme cannot do is tell one Profi-C construct from another where it has no reason to.
A primitive type and a visibility word are both `storage`, so most themes paint them alike; a
documentation label inherits from `constant.language`, which several dark themes paint the same
color as a keyword. **A palette written for the language does better**, because it can separate
the things a reader of *this* language wants separated.

That palette lives in [`.vscode/settings.json`](../../.vscode/settings.json) at the root of
this repository, which is why a `.pc` file opened here looks the same for everyone. To use it
in your own projects, copy its `editor.tokenColorCustomizations` block into your user
`settings.json`. It applies as soon as it is saved — no reload, and nothing to install.

It is the only copy, deliberately: two palettes in two files drift, and the one in this README
had already gone stale on three colors before it was removed. A shortened version, to show the
shape:

```jsonc
"editor.tokenColorCustomizations": {
  "textMateRules": [
    // A line comment the compiler acts on, such as '# ignore opinion'.
    // Addressed to the compiler rather than to a reader, so it is worth
    // setting apart from the prose around it.
    { "scope": "comment.line.number-sign.directive.profi-c",
      "settings": { "foreground": "#7A7A7A" } },

    // The label in a documentation comment: '@summary:', '@yields:', or a
    // parameter's name, mark and colon together. Worth setting rather than
    // leaving to the theme, which paints a language constant the same color
    // as a keyword in several of the dark ones.
    { "scope": "constant.language.documentation.profi-c",
      "settings": { "foreground": "#00E5FF" } },

    // Both comment forms together. A comment is a comment whichever mark
    // opened it, and naming only one leaves the other whatever gray the
    // theme had in mind.
    { "scope": ["comment.block.profi-c", "comment.line.number-sign.profi-c"],
      "settings": { "foreground": "#4C9A5A" } }

    // ... and the rest, in .vscode/settings.json
  ]
}
```

A workspace `settings.json` is scoped to the folder it sits in: it changes nothing about any
other project, and a color edited there applies at once with no reload. A user
`settings.json` does the same for everything you open.

**Inside an interpolated string**, the hole is `meta.interpolation`, its doubled braces are
`punctuation.section.interpolation.begin` and `.end`, and a pattern after the colon is
`constant.other.format`. What sits between the braces is code and is colored as code — a call
reads as a call, an operator as an operator. A block string written with `"""` is
`string.quoted.triple` and holds nothing else, since nothing inside one is read.

**Give `meta.interpolation` a color, even the plain one.** A hole is scanned inside the string
rule, so `string.quoted.double` stays on the scope stack while it is read, and anything in
there without a scope of its own — a local's name, a bracket, a comma — falls back to the
deepest scope that has a color. Leave `meta.interpolation` out and that is the string, so the
hole reads as the text around it. Naming it gives those tokens something nearer to fall back
to, which is what makes a hole look like code.

The full list, if you want something not above: `comment.line.number-sign`,
`string.quoted.double`, `string.quoted.single`, `constant.character.escape`,
`invalid.illegal.unknown-escape`, `constant.numeric.integer`, `constant.numeric.real`,
`keyword.other.declaration`, `keyword.operator.comparison`, `keyword.operator.assignment`,
`keyword.operator.arithmetic`, `keyword.operator.optional` — each with `.profi-c` on the end.

A type name is `entity.name.type.profi-c` wherever it appears: after `model`, after
`extends`, after `new`, after `catch`, after `is` and `as`, and standing in front of the field,
local, or parameter it describes.

**Only the type's own name is colored.** In `Geometry.Solid.Circle`, `Circle` is the type and
`Geometry.Solid` says where to find it, so the namespace part is left plain — the same as the
name after `namespace` and after `using`, which are namespaces and nothing else. `Standard` is
left plain for the same reason, being the namespace the language provides rather than a type in
it.

Where a name is written to reach a member rather than to name a type — `Console.WriteLine`,
`Math.Pi`, `Color.Green` — the part before the dot is colored as a type. A grammar cannot tell
`Namespace.Type` from `Type.Member` when both are capitalized, and a program's own namespace in
that position will be colored as though it were a type. That is the same limit that makes a
local called `Total` look like one, and it goes away when the compiler is the thing answering.

## Keeping it honest

A TextMate grammar is a second, hand-written description of the same language, and adding a
keyword to the compiler does nothing to this file. `EditorGrammarTests` in the test project
reads this grammar and asserts that every reserved word and every type the language provides
appears in it, and that nothing it colors is a word the language dropped — so the two cannot
drift without a test failing at the moment they do.

The same tests check that the block comment rule closes where the scanner closes, including
the awkward lines: a heading run of marks, a block opened and closed on one line, and an opener
with text after it.

## GitHub

Fenced code blocks tagged `profi-c` render as plain text on GitHub today. Highlighting there
needs the language registered with [Linguist](https://github.com/github-linguist/linguist),
which asks for use across a few hundred repositories. Tagging them now costs nothing and starts
working if that ever happens.
