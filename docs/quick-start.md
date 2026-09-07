# Start here

ScratchASM writes normal Scratch projects. Open a source file, edit it, and choose
Export .sb3. Open the result in Scratch with File > Load from your computer.

## Your first variable

New projects set "my variable" to "Hello World!" when the green flag is clicked.
The source uses my_variable because names in code cannot contain spaces.

~~~scratchasm
stage {
  var my_variable = ""

  @greenflag:
    my_variable = "Hello World!"
}
~~~

A declaration creates a variable. An assignment changes its value.
Use += to add a number:

~~~scratchasm
stage {
  var score = 0
  var message = ""

  @greenflag:
    score = 5
    score += 3
    message = "Score ready"
}
~~~

Declare shared variables on the stage. Use sprite var inside a sprite for a
sprite-only variable. Inside a proc, local creates a function-scoped value.
Write local var followed by the name and initial value.

~~~scratchasm
stage {
  var score = 0
}
sprite Player {
  sprite var speed = 4

  @greenflag:
    motion.move speed
    score += 1
}
~~~

## Lists

List positions start at 1, just like Scratch.

~~~scratchasm
stage {
  list scores = [10, 20]

  @greenflag:
    scores.add 30
    scores.replace 1 15
    scores.insert 2 18
    scores.delete 3
}
~~~

## Math and custom blocks

Use +, -, *, /, %, and ^. Scratch's sin, cos, sqrt, ln, and other math operators
are available too. Trigonometry uses degrees. Powers are lowered to native blocks.

~~~scratchasm
stage {
  var answer = 0

  proc calculate(value:num):
    local var doubled = value * 2
    answer = doubled

  @greenflag:
    call calculate(5)
    answer = 2 ^ 8
}
~~~

## Sprites and sounds

Open Project in the toolbar. Add a sprite, import a .sprite3, or select a target
and add costumes or sounds. Double-click an asset to preview it. The stage view
shows the saved positions and costumes; it is not a running Scratch player.

Save source after adding assets. Keep the generated .assets.sb3 companion beside
the .sasm file. The companion contains images, audio, and original project data.

Asset changes regenerate the displayed source and may change formatting or use
raw blocks. Tools > Undo last asset change restores the previous source and
assets. Undo any later text edits first.

## Editing

Suggestions appear as you type. Ctrl+Space opens them on demand; Up/Down chooses
a suggestion, Tab or Enter accepts it, and Escape dismisses it. At the start of
a line, common commands offer a complete line template. Brackets and quotes pair
automatically, and Enter keeps the script indentation.

Red underlines are errors, amber is a warning, and blue is information. Hover for
the message, or double-click a diagnostic to jump to its source. Stale marks clear
as soon as you edit. Suggestions are not a substitute for validation.

## Good habits

- Do use names such as score, player_speed, and high_scores. Avoid a, b, and c
  when their purpose is not obvious.
- Do declare a variable before using it. Do not write points += 1 without
  declaring points.
- Do write text in double quotes: message = "Ready". Do not write message = Ready
  unless Ready is a declared value.
- Do indent a script body by two spaces. Do not mix tabs and spaces.
- Do reset game state in a green-flag script when every run should start fresh.
- Do keep reusable behavior in a proc. Do not copy the same long script to
  several places.
- Do use a list for a sequence of items. Do not build hundreds of numbered
  variables unless each has a separate role.
- Do refresh the project preview after source edits. Do not mistake the static
  preview for runtime execution.
- Do test the exported project in the target player. Do not assume a TurboWarp
  extension will work in vanilla Scratch.

For every command and its arguments, choose Language reference in the guide.
