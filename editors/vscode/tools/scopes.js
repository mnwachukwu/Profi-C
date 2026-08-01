// Tokenizes lines of Profi-C with the grammar this extension ships, and prints
// the scopes each token carries.
//
// This exists because nothing else can answer the question. A test can read the
// grammar's JSON and assert what it says, and that is what the C# suite did for
// a long time — but "the file names this scope" and "a reader sees this scope"
// are different claims, and only the second one matters. Several confident
// statements about the editor turned out to be wrong in exactly that gap.
//
// The engine here is the one VS Code runs: vscode-textmate over the Oniguruma
// regex library. A rule that behaves differently here behaves differently there.
//
// Reads lines as JSON on standard input, writes JSON on standard output:
//
//     echo '["# @summary: A thing."]' | node tools/scopes.js
//     [[{"text":"# ","scopes":["source.profi-c","comment.line..."]}, ...]]

const fs = require("node:fs");
const path = require("node:path");
const oniguruma = require("vscode-oniguruma");
const textmate = require("vscode-textmate");

const here = path.dirname(__dirname);

async function main() {
    const wasm = fs.readFileSync(
        require.resolve("vscode-oniguruma/release/onig.wasm"));

    await oniguruma.loadWASM(wasm.buffer);

    const registry = new textmate.Registry({
        onigLib: Promise.resolve({
            createOnigScanner: (sources) => new oniguruma.OnigScanner(sources),
            createOnigString: (text) => new oniguruma.OnigString(text),
        }),

        // Only the one grammar is offered, so a scope name it does not know
        // returns null and the caller sees an empty result rather than a crash.
        loadGrammar: async (scope) =>
            scope === "source.profi-c"
                ? textmate.parseRawGrammar(
                    fs.readFileSync(
                        path.join(here, "syntaxes", "profi-c.tmLanguage.json"),
                        "utf8"),
                    "profi-c.tmLanguage.json")
                : null,
    });

    const grammar = await registry.loadGrammar("source.profi-c");
    const lines = JSON.parse(fs.readFileSync(0, "utf8"));

    // State is carried from one line to the next, which is what makes a block
    // comment spanning lines tokenize the way it does in an editor.
    let state = textmate.INITIAL;
    const scanned = [];

    for (const line of lines) {
        const result = grammar.tokenizeLine(line, state);
        state = result.ruleStack;

        scanned.push(result.tokens.map(token => ({
            text: line.substring(token.startIndex, token.endIndex),
            scopes: token.scopes,
        })));
    }

    process.stdout.write(JSON.stringify(scanned));
}

main().catch(problem => {
    process.stderr.write(String(problem));
    process.exit(1);
});
