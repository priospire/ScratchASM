# ScratchASM

ScratchASM is a language, Windows IDE, and bidirectional converter for Scratch 3 projects. Repository: [priospire/ScratchASM](https://github.com/priospire/ScratchASM).

Compile `.sasm` into native Scratch `.sb3` files, import `.sb3` into editable source, and repair recoverable project damage. Legacy `.mono` and `.cts` source files are accepted.

## Run

The root executables are stored with Git LFS. After cloning, run `git lfs pull` to download the binaries. A source-only download can build them with `tools/publish.ps1`.

Start the UI:

```powershell
.\ScratchASM.exe
```

New projects start with a green-flag script that sets "my variable" to "Hello World!".
The IDE has customizable opaque dark/light themes, dark title and status bars,
native scrollbar theming, and exact Scratch category colors. Visible text colors
first while full analysis runs in the background, including on multi-megabyte files.

The editor includes line numbers, outline navigation, search, bounded undo/redo,
context menus, automatic indentation and bracket pairing, completion suggestions,
and error/warning/info underlines with hover details. Use Tab or Enter to accept
a suggestion, or Ctrl+Space to request one.

Choose **Guide** for the embedded documentation. **Project** shows the saved stage
and sprite costumes, with buttons to add sprites, import .sprite3 files, and add
costumes or WAV/MP3 sounds. This preview is not a runtime player. **Tools** contains
appearance settings, extension search, optimization, and vanilla export.

Unsaved changes are protected when opening a different document or closing.
Browse, drag a file into the window, or enable **Tools > Input/output paths** to
type paths. Start with the [quick guide](docs/quick-start.md).

For development builds:

```powershell
dotnet run --project src/OpenCTS.App
```

Run from the command line:

```powershell
.\ScratchASM.exe samples\hello.sasm artifacts\hello-from-scratchasm.sb3
dotnet run --project src/OpenCTS.App -- samples/minimal-project artifacts/minimal-project.sb3
dotnet run --project src/OpenCTS.App -- samples/hello.sasm artifacts/hello-from-scratchasm.sb3
.\ScratchASM.exe --repair artifacts\damaged.sb3 artifacts\repaired.sb3
.\ScratchASM.exe --emit-aliases samples
.\ScratchASM.exe game.sb3 game.sasm
.\ScratchASM.exe game.sasm rebuilt.sb3
.\ScratchASM.exe --open samples\roundtrip.sasm
.\ScratchASM.exe --optimize game.sb3 compact.sb3
.\ScratchASM.exe --vanilla game.sb3 vanilla.sb3
```

The input can be:

- A `.sasm` ScratchASM file, or legacy `.mono` / `.cts` source.
- A `.sb3` file.
- A folder containing `project.json` and asset files.
- A `project.json` file with asset files beside it.

Use `.sb3` for compiled output, or `.sasm` when importing a Scratch archive. Add `--overwrite` to replace an existing output.

Imported source comes with an adjacent `*.assets.sb3` companion containing costumes, sounds, and original metadata. Keep it beside the `.sasm` file. Complex block graphs use editable `rawblocks` JSON for exact preservation. See [round-trip conversion](docs/round-trips.md) for the format, preservation rules, and limits.

## Editor And MCP Support

- VS Code support is in `editors/vscode-scratchasm`; it provides Scratch-colored syntax, diagnostics, completions, and ScratchASM light/dark themes.
- `ScratchASM.LanguageHost.exe` supports `--lsp` for editors and `--mcp --workspace <folder>` for MCP clients.
- MCP tools include analysis, compile, decompile, merge edited source, repair, catalog lookup, and project info.

## Extensions And Project Tools

The bundled TurboWarp gallery has 104 entries. Generic opcode forms preserve
extension inputs, fields, mutations, URLs, and category colors. This supports
authoring and round trips, not running extension JavaScript inside the IDE or
providing a native alias for every third-party block. Custom extensions produce
a vanilla-compatibility warning.

The IDE warns at 4.5 MiB of uncompressed project.json and flags projects over
5 MiB. Compact / Optimize removes JSON whitespace and folds safe constant
arithmetic, including calculations used in variable assignments and list indices.
It does not delete data, change variable scope, or promise a speedup for every project.

Vanilla export additionally lowers supported literal Bitwise operations to native
Scratch blocks. Unsupported extensions, extension monitors, runtime settings, or
JSON that still exceeds the budget stop the export without deleting content.
Both tools write separate outputs. See [project tools and limits](docs/project-tools.md).

## Validation

ScratchASM validates actual Scratch `.sb3` project structure. It does not translate Rust, Python, JavaScript, or other source languages into Scratch.

For ScratchASM input, the compiler emits Scratch block JSON and generated SVG costume assets, validates the generated project, and reports diagnostics with severity, code, line, and column. The editor colors aliases and contextual syntax with their Scratch category colors; double-click a diagnostic to select its source location. The language includes native aliases for every cataloged core and bundled-extension block, structured control, expressions, variable/list operations, procedure-local variables, structs, enums, sprite-only variables, and generic opcode forms. Warnings allow output; errors block output.

For readable but structurally damaged `.sb3`, `project.json`, or folder inputs, opt-in safe repair can restore missing containers/defaults, add a stage, and replace unusable costume references with a generated SVG. For ScratchASM source, repair can normalize line endings, replace unsafe text characters, rewrite common Scratch-like variable/list phrases, wrap an implicit stage, and add missing closing braces. Repair never mutates the source input and never writes output unless the repaired project validates.

It reports:

- JSON syntax errors with line and column.
- Missing required Scratch fields with JSON paths.
- Incorrect core field types.
- Missing referenced asset files.
- Asset `md5ext` values that look like paths instead of root zip file names.
- ScratchASM language errors and raw opcode warnings.

## Build And Test

```powershell
dotnet build OpenCTS.slnx
dotnet test OpenCTS.slnx
npm --prefix editors/vscode-scratchasm test
npm --prefix tools/runtime-smoke ci
npm --prefix tools/runtime-smoke test
npm --prefix tools/runtime-smoke run test:fixtures
node tools/runtime-smoke/project-tools.cjs
node tools/runtime-smoke/host-smoke.cjs
.\tools\publish.ps1
```

## Project Layout

- `src/OpenCTS.Core` contains input loading, validation, source location mapping, and `.sb3` writing.
- `src/OpenCTS.App` contains the WinForms UI and CLI entry point.
- `src/ScratchASM.LanguageHost` contains the LSP/MCP-compatible ScratchASM language host.
- `editors/vscode-scratchasm` contains VS Code syntax highlighting, diagnostics, completions, and themes.
- `tests/OpenCTS.Tests` contains focused conversion and diagnostic tests.
- `tests/OpenCTS.LanguageServices.Tests` contains language service tests.
- `tests/ScratchASM.LanguageHost.Tests` contains LSP/MCP protocol tests.
- `tests/OpenCTS.App.Tests` checks IDE document workflows, rendering, and editing history.
- `tools/runtime-smoke` runs native Scratch runtime and upstream fixture round-trip checks.
- `samples/roundtrip.sasm` exercises custom blocks, local variables, lists, math, and sprite state.
- `samples/minimal-project` contains a valid folder-style Scratch input.
- `samples/hello.sasm` contains the primary ScratchASM smoke sample.
- `samples/all-aliases.sasm` compiles every registered alias, including legacy blocks, and demonstrates custom blocks.
- `samples/all-aliases.json` is the schema-v1 machine-readable alias, binding, shape, extension, and color catalog.

For Scratch formatting details, see [docs/sb3-format.md](docs/sb3-format.md).
For ScratchASM, see [docs/scratchasm.md](docs/scratchasm.md).
