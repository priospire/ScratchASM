# Project tools

## Appearance

Tools > Appearance changes the accent, window, panels, editor background, UI
text, editor font size, and animations. Reset colors restores the defaults.
Dark mode includes the title bar, footer, menus, and native scrollbars where
Windows supports them. Scratch block syntax keeps the original category colors.

The editor colors visible text first and checks source in the background.
Large files remain editable while diagnostics are being calculated.

## TurboWarp extensions

Tools > TurboWarp extensions lists the released official gallery entries bundled
with this build. Search and choose Add declaration. An extension can also be
declared explicitly:

This build includes 104 gallery entries, refreshed on September 7, 2026. Categories
with a static color use that color; dynamic or missing colors use Scratch's
extension teal unless you supply an explicit color.

~~~scratchasm
stage {
  extension Bitwise "https://extensions.turbowarp.org/bitwise.js" "#0FBD8C"
  var result = 0

  @greenflag:
    result = [Bitwise_bitwiseAnd input LEFT=6 input RIGHT=3]
}
~~~

Use the extension's exact ID, opcode, input names, field names, and mutation data.
The raw block forms in the language reference work with any serialized extension
block. Imported projects retain their extension URLs and raw block graphs.
The optional color is the extension's category color.

This is authoring and round-trip support, not an implementation of the extension's
JavaScript. The IDE never downloads or runs extension scripts. Run the exported
project in TurboWarp. Dynamic menus, custom runtime behavior, and external services
still belong to that extension. Arbitrary extension input semantics cannot be
validated without its implementation.

The bundled gallery catalog can be refreshed by running
node tools/update-turbowarp-catalog.cjs and rebuilding. Custom HTTPS extension URLs
are supported even when they are not in the catalog.

Gallery: https://extensions.turbowarp.org/
Extension format: https://docs.turbowarp.org/development/extensions/introduction

## Scratch save budget

A warning appears at 4.5 MiB of uncompressed project.json. Over 5 MiB, the
compatibility export refuses to claim that the project fits.
This measures JSON, not the size of the zipped .sb3 or its images and sounds.
The actual Scratch service can apply other upload limits.

## Compact / Optimize

The tool exports a separate project and shows the before/after JSON size.
It removes JSON whitespace, not project content.

For vanilla graphs it also folds finite literal arithmetic used as input values,
including calculations assigned to variables and calculations used as list indices.
It does not rename data, clear lists, remove scripts, change scheduling, or turn
global variables into local variables. Those changes can alter behavior.

Runtime rewrites are skipped when custom extensions could inspect the graph.
If there is nothing safe to simplify, the tool says so through the change report.
No universal speedup is promised.

## Export vanilla Scratch

The tool tries safe workarounds, compacts the JSON, and checks what remains.
It only writes an output when all its compatibility checks pass.

Supported extension workaround:

- Bitwise AND, OR, XOR, NOT, left shift, arithmetic right shift, and logical right
  shift with literal signed 32-bit integer operands. These become native Scratch
  arithmetic with the same numeric result.

Variable operands, other Bitwise operations, and other extension blocks are
reported as unsupported. Runtime-setting changes also require manual review.
Network access, files, video, and similar capabilities cannot generally be
reproduced by vanilla Scratch.

If compaction still leaves more than 5 MiB, the tool stops without deleting data.
No tool can guarantee that an arbitrarily large project fits without changing it.
Your input and its asset companion are never overwritten.

## Import limits

Costume previews and media import accept files up to 16 MiB; image previews are
limited to 4096 pixels per side. SVG previews disable external resources. Sounds
are decoded to WAV with a 10-minute / 128 MiB decoded limit. Sprite archives are
limited to 4096 entries and 128 MiB of uncompressed data.
