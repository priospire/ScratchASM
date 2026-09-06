# Scratch `.sb3` Formatting

A Scratch 3 `.sb3` file is a zip archive. The archive root contains:

- `project.json`
- One file per referenced costume or sound asset

Asset files are normally named with the value stored in each asset object's `md5ext` field, such as `cd21514d0531fdffb22204e0ec5ed84a.svg`.

Scratch stores artwork as assets, not as block code. When ScratchASM costume drawing syntax is used, OpenCTS generates self-contained SVG asset bytes, computes the lowercase MD5 hex `assetId`, writes `md5ext` as `assetId + ".svg"`, and includes that SVG file at the `.sb3` zip root.

## Supported Source Forms

ScratchASM accepts these source forms:

- A ready-made `.sb3` zip.
- A folder containing `project.json` and asset files.
- A `project.json` file with asset files in the same folder.
- A ScratchASM `.sasm` source file, or legacy `.mono` / `.cts` file, compiled to `project.json` plus assets.

Existing archives can also be exported as portable ScratchASM source; see [round-trip conversion](round-trips.md).

Folder example:

```text
my-project/
  project.json
  cd21514d0531fdffb22204e0ec5ed84a.svg
```

## Required Top-Level JSON Shape

`project.json` is a JSON object with this standard shape. `monitors` and `extensions` may be omitted in existing projects; when present they must be arrays.

```json
{
  "targets": [],
  "monitors": [],
  "extensions": [],
  "meta": {}
}
```

`targets` must contain exactly one stage target. It must be the first target, its `isStage` value must be `true`, and its name must be `Stage`.

## Target Shape

Each target should contain the core Scratch target fields:

```json
{
  "isStage": true,
  "name": "Stage",
  "variables": {},
  "lists": {},
  "broadcasts": {},
  "blocks": {},
  "comments": {},
  "currentCostume": 0,
  "costumes": [],
  "sounds": []
}
```

Stage targets commonly also include:

```json
{
  "volume": 100,
  "layerOrder": 0,
  "tempo": 60,
  "videoTransparency": 50,
  "videoState": "on",
  "textToSpeechLanguage": null
}
```

Sprite targets commonly also include fields such as `visible`, `x`, `y`, `size`, `direction`, `draggable`, and `rotationStyle`.

## Asset Entries

Each costume or sound entry must be an object with:

```json
{
  "assetId": "cd21514d0531fdffb22204e0ec5ed84a",
  "name": "backdrop1",
  "md5ext": "cd21514d0531fdffb22204e0ec5ed84a.svg",
  "dataFormat": "svg"
}
```

The file named by `md5ext` must exist beside `project.json` for folder/file input, or at the zip root for `.sb3` input. OpenCTS copies referenced assets unchanged into the output `.sb3`.

## Blocks

OpenCTS checks the core shape of block objects when blocks are present. A Scratch block entry should include:

```json
{
  "opcode": "looks_say",
  "next": null,
  "parent": null,
  "inputs": {},
  "fields": {},
  "shadow": false,
  "topLevel": true
}
```

This validator is intentionally conservative. It checks project packaging and core Scratch JSON syntax, but it does not execute the Scratch VM or prove that block graphs are semantically meaningful.

## Safe Repair

The UI includes an opt-in `Attempt safe repair` checkbox for `.sb3` inputs. The CLI equivalent is:

```powershell
.\ScratchASM.exe --repair damaged.sb3 repaired.sb3
```

Repair never modifies the source archive. It only operates on a readable ZIP containing parseable `project.json`, then validates the result before writing output. Recoverable repairs include missing root arrays/metadata, missing standard target properties, a missing stage, malformed or missing costume references that can be replaced with a generated default SVG, malformed block entries without usable opcodes, missing required block properties, and dangling top-level block links. Every change is reported as a warning.

Unreadable ZIP data, malformed JSON, unsafe or duplicate ZIP paths, missing sound assets, and damage without a reliable correction remain errors. The tool does not guess at unknown behavior or silently discard an entire project.
