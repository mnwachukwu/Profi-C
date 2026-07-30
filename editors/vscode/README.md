# Profi-C for VS Code

Syntax highlighting for `.pc` programs and `.pcp` project files, and the editor behaviour that
comes with knowing the language: bracket matching, `Ctrl+/` inserting `#`, auto-closing quotes,
and indentation that follows `end`.

Nothing here runs the compiler. There are no diagnostics, no completion, and no hover — those
need a language server, which is a later piece of work.

## Installing it

**This is not on the VS Code Marketplace**, and will not be for a while — Profi-C is young
enough that publishing an extension for it would be putting up a shopfront before there is
anything to sell. Installing it means copying this folder into your extensions directory,
which is all the Marketplace would do anyway. There is no build step: everything here is
declarative, so nothing is compiled and nothing is downloaded.

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

Then reload the window — `Ctrl+Shift+P`, "Developer: Reload Window" — and open a `.pc` file.

Three things worth knowing:

- **VS Code Insiders** uses `.vscode-insiders` rather than `.vscode`. Everything else is the
  same.
- **The folder name carries the version**, and VS Code will keep serving an old copy if a new
  one arrives under the same name. Bump it to match `package.json` when that changes.
- **If you are editing the grammar**, link the folder rather than copying it, so a change shows
  up on reload instead of after another copy. On Windows, in an elevated shell:
  `New-Item -ItemType SymbolicLink -Path $dest -Target "$repo\editors\vscode"`. Elsewhere:
  `ln -s "$repo/editors/vscode" "$dest"`.

## What it colours

Reserved words, the primitive types, the types the language provides, literals of every form
including fraction literals like `22|7`, block strings, the holes in an interpolated string,
both comment forms, and the name a declaration introduces. A closer and what it closes read as one thing, so `end function` colours together
rather than as a keyword beside a noun.

## Tweaking the colours

Every theme paints these differently, so the grammar picks scopes rather than colours. To set
them yourself, put this in `settings.json` and change what you like — anything left out keeps
whatever your theme says.

```jsonc
"editor.tokenColorCustomizations": {
  "textMateRules": [
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

**Inside an interpolated string**, the doubled braces are
`punctuation.section.interpolation.begin` and `.end`, and a pattern after the colon is
`constant.other.format`. What sits between the braces is code and is coloured as code — a call
reads as a call, an operator as an operator — which is what the marked edges are for. A block
string written with `"""` is `string.quoted.triple` and holds nothing else, since nothing
inside one is read.

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
