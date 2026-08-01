# Profi-C for VS Code

Syntax highlighting for `.pc` programs and `.pcp` project files, and the editor behaviour that
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

**How a stale copy shows itself.** The editor colours by whatever rules it has, so a construct
the copy has never heard of is coloured by the rules for something else, and the symptom looks
nothing like the cause. A word the copy does not know to be a keyword is caught by the rule for
a name followed by a bracket, and reads as a function call. A block string the copy predates
reads as an empty string followed by an ordinary one, so the first `"` inside it closes a
string that was never open and the rest of the file is coloured as text.

Nothing is wrong with the grammar in either case; the editor is reading an old one. Before
hunting for a bug, check the installed copy holds the rule you expect:

```powershell
Select-String delegate "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0\syntaxes\profi-c.tmLanguage.json"
```

A link cannot go stale, which is the whole argument for one.

## What it colours

Reserved words, the primitive types, the types the language provides, literals of every form
including fraction literals like `22|7`, block strings, the holes in an interpolated string,
both comment forms, and the name a declaration introduces. A closer and what it closes read as one thing, so `end function` colours together
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

A comment can also document what follows it, and the label inside one is coloured apart from
the prose:

```
##
    @summary: One person's money, and the rules about taking it out.
    @remarks: The longer explanation, for a hover rather than a list.
##
model Account
```

Only the label is coloured, never the text after it, and the name is coloured apart from the
`@` and `:` around it — `constant.language.documentation.profi-c` for the name,
`punctuation.definition.documentation.profi-c` for the marks.

The extension ships both as defaults. To change either, or to set colours the extension does
not, put the block below in `settings.json` — a block of your own replaces the defaults rather
than merging with them, so copy across anything you want to keep. A rule must name **one**
scope as a string: writing several as an array is accepted and then quietly ignored.

## Bump the version whenever package.json changes

**A grammar edit shows up on the next reload. A `package.json` edit does not.** Grammar files
are read each time one is needed; everything under `contributes` is read once, when the editor
scans the extension, and cached against its id and version. Edit a colour without touching the
version and the editor keeps serving the old one — no error, no warning, and the file on disk
plainly says otherwise.

So raise `version` in `package.json` with any change to it, and point the link at the new
number, since the folder name carries it:

```bash
cmd /c rmdir "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.0"; New-Item -ItemType SymbolicLink -Path "$env:USERPROFILE\.vscode\extensions\profi-c-0.1.1" -Target "D:\Repos\Profi-C\editors\vscode"
```

`Developer: Restart Extension Host` is worth trying first; a plain window reload is not enough.

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

## Tweaking the colours

Every theme paints these differently, so the grammar picks scopes rather than colours. To set
them yourself, put this in `settings.json` and change what you like — anything left out keeps
whatever your theme says.

```jsonc
"editor.tokenColorCustomizations": {
  "textMateRules": [
    // A line comment the compiler acts on, such as '# ignore opinion'.
    // Addressed to the compiler rather than to a reader, so it is worth
    // setting apart from the prose around it.
    { "scope": "comment.line.number-sign.directive.profi-c",
      "settings": { "foreground": "#7A7A7A" } },

    // The name in a documentation label — the 'summary' of '@summary:'.
    // Most themes paint a language constant the same colour as a keyword,
    // so this is worth setting rather than leaving to the theme.
    { "scope": "constant.language.documentation.profi-c",
      "settings": { "foreground": "#00E5FF" } },

    // The '@' and the ':' around it, kept quieter than the name itself.
    { "scope": "punctuation.definition.documentation.profi-c",
      "settings": { "foreground": "#7A7A7A" } },

    // The primitive types: integer, real, boolean, character, string, fraction.
    { "scope": "storage.type.profi-c", "settings": { "foreground": "#569CD6" } },

    // How far something reaches, and what kind of thing it is: public,
    // protected, internal, global, constant, virtual, override, abstract,
    // sealed, extends. Kept apart from the primitives above, so a reader can
    // tell a modifier from a type at a glance.
    { "scope": "storage.modifier.profi-c", "settings": { "foreground": "#569CD6" } },

    // What runs, and the closer that ends it. 'end while' matches 'while'.
    { "scope": ["keyword.control.profi-c", "keyword.control.end.profi-c"],
      "settings": { "foreground": "#C586C0" } },

    // Words that introduce or compose a declaration: model, function, let,
    // namespace, using, import, new, as, is, base, this.
    { "scope": "keyword.other.profi-c", "settings": { "foreground": "#569CD6" } },

    // The name a declaration introduces, and a name being called.
    { "scope": "entity.name.type.profi-c", "settings": { "foreground": "#4EC9B0" } },
    { "scope": "entity.name.function.profi-c", "settings": { "foreground": "#DCDCAA" } },
    { "scope": "entity.name.function.call.profi-c", "settings": { "foreground": "#DCDCAA" } },

    // Types the language provides: Console, Math, DateTime, the exceptions.
    { "scope": "support.class.profi-c", "settings": { "foreground": "#4EC9B0" } },

    // and, or, not — spelled out, and worth reading as operators.
    { "scope": "keyword.operator.word.profi-c", "settings": { "foreground": "#C586C0" } },

    { "scope": "constant.numeric.fraction.profi-c", "settings": { "foreground": "#B5CEA8" } },
    { "scope": "constant.language.profi-c", "settings": { "foreground": "#569CD6" } },

    // Both comment forms together. A comment is a comment whichever mark
    // opened it, and naming only one leaves the other whatever grey the
    // theme had in mind.
    { "scope": ["comment.block.profi-c", "comment.line.number-sign.profi-c"],
      "settings": { "foreground": "#4C9A5A" } }
  ]
}
```

This repository already carries the comment colour in `.vscode/settings.json`, so a `.pc` file
opened here reads the same for everyone. That file is workspace-scoped: it changes nothing
about any other project, and editing a colour there applies at once with no reload.

**Inside an interpolated string**, the hole is `meta.interpolation`, its doubled braces are
`punctuation.section.interpolation.begin` and `.end`, and a pattern after the colon is
`constant.other.format`. What sits between the braces is code and is coloured as code — a call
reads as a call, an operator as an operator. A block string written with `"""` is
`string.quoted.triple` and holds nothing else, since nothing inside one is read.

**Give `meta.interpolation` a colour, even the plain one.** A hole is scanned inside the string
rule, so `string.quoted.double` stays on the scope stack while it is read, and anything in
there without a scope of its own — a local's name, a bracket, a comma — falls back to the
deepest scope that has a colour. Leave `meta.interpolation` out and that is the string, so the
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

**Only the type's own name is coloured.** In `Geometry.Solid.Circle`, `Circle` is the type and
`Geometry.Solid` says where to find it, so the namespace part is left plain — the same as the
name after `namespace` and after `using`, which are namespaces and nothing else. `Standard` is
left plain for the same reason, being the namespace the language provides rather than a type in
it.

Where a name is written to reach a member rather than to name a type — `Console.WriteLine`,
`Math.Pi`, `Color.Green` — the part before the dot is coloured as a type. A grammar cannot tell
`Namespace.Type` from `Type.Member` when both are capitalized, and a program's own namespace in
that position will be coloured as though it were a type. That is the same limit that makes a
local called `Total` look like one, and it goes away when the compiler is the thing answering.

## Keeping it honest

A TextMate grammar is a second, hand-written description of the same language, and adding a
keyword to the compiler does nothing to this file. `EditorGrammarTests` in the test project
reads this grammar and asserts that every reserved word and every type the language provides
appears in it, and that nothing it colours is a word the language dropped — so the two cannot
drift without a test failing at the moment they do.

The same tests check that the block comment rule closes where the scanner closes, including
the awkward lines: a heading run of marks, a block opened and closed on one line, and an opener
with text after it.

## GitHub

Fenced code blocks tagged `profi-c` render as plain text on GitHub today. Highlighting there
needs the language registered with [Linguist](https://github.com/github-linguist/linguist),
which asks for use across a few hundred repositories. Tagging them now costs nothing and starts
working if that ever happens.
