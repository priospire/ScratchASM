# Scratch Projects And ScratchASM

ScratchASM converts in both directions. Its compiler emits native Scratch blocks; no custom runtime or extension is needed for the language itself.

```powershell
.\ScratchASM.exe game.sb3 game.sasm
.\ScratchASM.exe game.sasm rebuilt.sb3
# Equivalent explicit import command:
.\ScratchASM.exe --decompile game.sb3 game.sasm
```

Add `--overwrite` to replace an existing output. Input archives and project companions cannot be overwritten by compilation.

## Portable Source

Import creates two adjacent files:

- `game.sasm`: editable ScratchASM source.
- `game.<content-hash>.assets.sb3`: the original project data and assets.

The source begins with a project reference:

```scratchasm
project "game.0123456789abcdef.assets.sb3"
```

Keep these files together when moving or sharing the project. The original input file is no longer needed. The companion holds costumes (SVG and bitmap), sounds, monitors, comments, hidden editor information, and unknown metadata. A missing companion produces an error. The compiler does not silently substitute blank costumes.

The `project` directive accepts one quoted `.sb3` filename in the same directory as the source. Absolute paths, parent paths, and linked companion files are rejected. Only one directive is allowed. It is used by the converter; `CtsCompiler.Compile` is the lower-level syntax-to-JSON API and does not load files.

In the Windows IDE, open an `.sb3`, edit the source, then use **Export .sb3** to build it. **Save** or **Save as** writes the source and companion. Source saved to another directory gets its own companion there. The IDE can reopen these source files with all assets intact.

## Exact Blocks

Import uses normal aliases where compiling those aliases reproduces the block graph. Otherwise, the target uses `rawblocks`, a JSON object keyed by the original Scratch block IDs:

```scratchasm
stage {
  rawblocks {
    "flag": {
      "opcode": "event_whenflagclicked",
      "next": null,
      "parent": null,
      "inputs": {},
      "fields": {},
      "shadow": false,
      "topLevel": true,
      "x": 40,
      "y": 40
    }
  }
}
```

The closing JSON brace must have the same indentation as `rawblocks`. Single-line JSON objects are also accepted. JSON uses double-quoted property names, commas, and standard string escapes; comments inside the JSON object are not allowed. Error diagnostics identify the source line and column.

`rawblocks` preserves procedure definitions, calls, parameter IDs, mutations, input and shadow tuples, disconnected stacks, floating reporters, and extension-specific block properties. It can coexist with ordinary scripts if block IDs do not collide. References must resolve to existing blocks and data; cyclic graphs are rejected. Raw data references retain their original IDs through the project companion. A manually authored raw graph without a companion must use the IDs of the data declared by its compiled target.

Exact form is deliberately more verbose than aliases. It prevents an import from changing behavior merely to produce shorter source. In particular, arbitrary third-party extension behavior is retained as its original opcode, not converted into an approximation that claims to work in standard Scratch.

Imported sprites have an identity binding:

```scratchasm
sprite "New name" {
  origin "Original name"
}
```

Keep `origin` when renaming an imported sprite so its costumes, sounds, and metadata stay attached. Newly authored sprites do not need it. Import also emits supported position, size, direction, visibility, layer, and rotation settings. Other sprite properties stay in the companion. Adding explicit costume drawing declarations replaces that target's costume set.

## Preservation And Limits

An unchanged source round trip retains the original `project.json` and every archive entry byte for byte. ZIP compression, timestamps, and archive ordering may differ. Edits retain assets and unmodified metadata while rebuilding the affected project data; generated IDs are remapped to original IDs where possible. Comments on deleted blocks are detached. Monitors for explicitly deleted data are removed.

The converter validates readable JSON, field types, graph references, data references, required assets, and archive paths before writing output. Safe relative ZIP metadata folders are preserved during source round trips; assets referenced by Scratch still use root filenames. Duplicate ZIP entries, path traversal, malformed JSON, and cyclic graphs remain errors. Output writes use a temporary file followed by a move, so failed validation does not replace an existing output.

There is no fixed entry-count or total-expanded-size limit for .sb3 archives.
Imports keep a private compressed snapshot on disk and load media on demand.
Exports stream assets one at a time, including assets larger than 128 MiB.
You need enough free temporary disk space for the compressed snapshot.

The in-memory project.json safety limit is 128 MiB. Operations that request an
individual asset as a byte array retain a 128 MiB bound; this does not block
opening or streaming that asset through an archive export. Compiling edited
source is limited to 64 MiB and 128 nested delimiters. These are resource safeguards,
not Scratch upload limits. Large or structurally unusual targets use exact block
form. Repair cannot reconstruct lost media or infer ambiguous damaged code.

## Verification

```powershell
dotnet test OpenCTS.slnx
npm --prefix editors/vscode-scratchasm test
npm --prefix tools/runtime-smoke ci
npm --prefix tools/runtime-smoke test
npm --prefix tools/runtime-smoke run test:fixtures
node tools/runtime-smoke/host-smoke.cjs
```

The runtime tests execute `samples/roundtrip.sasm` with the official Scratch VM, including an edited round trip. The fixture tests download nine projects from a pinned revision of [Scratch's upstream fixtures](https://github.com/scratchfoundation/scratch-editor/tree/d4eb4878970a4958020946018669c8e5d255e5fc/packages/scratch-vm/test/fixtures) and check exact and edited round trips. Downloads and generated files stay under `artifacts/`.

Runtime tests run without a renderer: they verify execution, project state, and preserved asset bytes, not visual rendering or audio playback in the Scratch website. Windows tests render light/dark IDE captures at 1240x820 and 900x600 and check document loading, outline population, and undo/redo after highlighting. Manual testing in the main Scratch editor remains useful for visual, sound, and hardware-extension behavior.

`tools/publish.ps1` publishes both Windows executables to the repository root. They are tracked through Git LFS; source files are tracked normally.
