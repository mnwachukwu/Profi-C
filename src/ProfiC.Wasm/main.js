// The entry point the .NET WebAssembly build wants, and the only JavaScript in this repository.
//
// It does as little as it can: start the runtime, hand back the exported functions, and get out
// of the way. Everything about the language lives on the other side of this file, and whatever
// draws a page lives on the other side of the module this exports.

import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig } = await dotnet.create();

const exports = await getAssemblyExports(getConfig().mainAssemblyName);

/**
 * The compiler, as three functions.
 *
 * `check` and `run` both answer with JSON, parsed here so that nothing above this deals in
 * strings that happen to be objects.
 */
export const compiler = {
    check: source => JSON.parse(exports.ProfiC.Wasm.Playground.Check(source)),
    run: (source, input) => JSON.parse(exports.ProfiC.Wasm.Playground.Run(source, input ?? '')),
    version: () => exports.ProfiC.Wasm.Playground.Version(),
};

export default compiler;
