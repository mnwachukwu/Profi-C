# Changelog

What changed in each release of Profi-C, newest first.

Versions are `major.minor.patch`. A **major** goes up where a program that compiled stops
compiling or starts meaning something else. A **minor** adds something the language did not have.
A **patch** fixes what was already meant to work. Released builds carry a fourth number, which
identifies the build rather than the language, and never changes what a program means.

## 1.0.0

The first release. The language is complete as the [specification](docs/language-spec.md)
describes it, and everything below is checked on every build rather than remembered.

**The language.** Models with single inheritance and virtual dispatch, structures, enumerations,
exceptions, and optionals in place of null. Exact rational arithmetic — a `fraction` is a third,
not `0.333`. Definite assignment, so a value is never read before it is known to be there. First
class functions and closures.

**Two engines that must agree.** Every sample is run interpreted and again as a built assembly,
and the two outputs are held against each other. The interpreter is the oracle; where they
disagree, one of them has a bug and the corpus finds out which. Every emitted assembly is also
read by the runtime's own IL verifier, which checks the methods a run never reaches.

**A command line.** `pc run`, `pc build`, `pc format`, `pc new`, and `pc debug`, plus `pc lsp`
for an editor to talk to.

**An editor.** Syntax highlighting, diagnostics as you type, completion, hover, go to definition,
find all references, rename, folding, formatting, and a debugger with breakpoints and stepping.

**Documentation held to the compiler.** The specification's examples are compiled on every build,
the standard library reference's examples are run and their printed output checked, and the
sample corpus is what the tests read.
