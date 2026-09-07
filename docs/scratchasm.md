# ScratchASM Language Reference

ScratchASM is a source language for Scratch. Source files use `.sasm`; legacy `.mono` and `.cts` files are also accepted.

ScratchASM compiles to Scratch 3 `project.json`, validates the generated project, and packages the result as `.sb3`. Scratch stores costumes and sounds as assets, not as executable block code. ScratchASM costume drawing tools therefore generate SVG files, add them to the `.sb3`, and reference them from costume metadata.

The minimal forms are `score = 5`, `score += 3`, `repeat 10:`, `forever:`, category commands such as `motion.move 10`, and expressions such as `(score + 2) * 3`. Generic Scratch opcode syntax remains available for exact interoperability.

For importing existing projects, the complete specification of `project`, `origin`, and `rawblocks`, companion assets, and preservation rules is in [Scratch project round trips](round-trips.md). These forms extend the declarations below and are supported by the IDE, CLI, and language services. Standard JSON string escapes, including `\uXXXX`, and scientific-notation numbers such as `1e-4` are accepted.

## Complete Alias Set

For a shorter introduction, see [Start here](quick-start.md). The desktop Guide
includes both documents. [Project tools](project-tools.md) covers media editing,
TurboWarp declarations, appearance, and safe compatibility exports.

Custom extensions use the target-level declaration:

~~~text
extension ID ["https://extension-url.js" ["#RRGGBB"]]
~~~

Square brackets above indicate optional parts, not literal syntax. Quote an ID
that starts with a digit. URLs must use HTTPS and colors must have six hexadecimal
digits. These declarations preserve extensionURLs and extensionColors in the
project. Use the generic opcode forms below for extension blocks that have no alias.

This table is the compiler's complete native alias catalog. Arguments marked `Input` accept expressions; `Field` arguments are Scratch dropdown/data fields; `Menu` arguments generate canonical Scratch menu shadows. Reporter and Boolean aliases are used as functions inside expressions. Hat aliases start with `@`. C-block aliases end in `:` and own indented bodies.

<!-- ALIAS_TABLE_START -->

| Alias | Scratch opcode | Shape | Arguments | Notes |
| --- | --- | --- | --- | --- |
| `motion.move` | `motion_movesteps` | Stack | `STEPS` Input | - |
| `motion.turnright` | `motion_turnright` | Stack | `DEGREES` Input | - |
| `motion.turnleft` | `motion_turnleft` | Stack | `DEGREES` Input | - |
| `motion.goto` | `motion_goto` | Stack | `TO` Menu | - |
| `motion.goto` | `motion_gotoxy` | Stack | `X` Input, `Y` Input | - |
| `motion.glide` | `motion_glideto` | Stack | `SECS` Input, `TO` Menu | - |
| `motion.glide` | `motion_glidesecstoxy` | Stack | `SECS` Input, `X` Input, `Y` Input | - |
| `motion.setdirection` | `motion_pointindirection` | Stack | `DIRECTION` Input | - |
| `motion.pointtowards` | `motion_pointtowards` | Stack | `TOWARDS` Menu | - |
| `motion.changex` | `motion_changexby` | Stack | `DX` Input | - |
| `motion.setx` | `motion_setx` | Stack | `X` Input | - |
| `motion.changey` | `motion_changeyby` | Stack | `DY` Input | - |
| `motion.sety` | `motion_sety` | Stack | `Y` Input | - |
| `motion.ifonedgebounce` | `motion_ifonedgebounce` | Stack | none | - |
| `motion.rotationstyle` | `motion_setrotationstyle` | Stack | `STYLE` Field | - |
| `motion.x` | `motion_xposition` | Reporter | none | - |
| `motion.y` | `motion_yposition` | Reporter | none | - |
| `motion.direction` | `motion_direction` | Reporter | none | - |
| `legacy.motion.scrollright` | `motion_scroll_right` | Stack | `DISTANCE` Input | legacy |
| `legacy.motion.scrollup` | `motion_scroll_up` | Stack | `DISTANCE` Input | legacy |
| `legacy.motion.align` | `motion_align_scene` | Stack | `ALIGNMENT` Field | legacy |
| `legacy.motion.xscroll` | `motion_xscroll` | Reporter | none | legacy |
| `legacy.motion.yscroll` | `motion_yscroll` | Reporter | none | legacy |
| `looks.say` | `looks_say` | Stack | `MESSAGE` Input | - |
| `looks.say` | `looks_sayforsecs` | Stack | `MESSAGE` Input, `SECS` Input | - |
| `looks.think` | `looks_think` | Stack | `MESSAGE` Input | - |
| `looks.think` | `looks_thinkforsecs` | Stack | `MESSAGE` Input, `SECS` Input | - |
| `looks.costume` | `looks_switchcostumeto` | Stack | `COSTUME` Menu | - |
| `looks.nextcostume` | `looks_nextcostume` | Stack | none | - |
| `looks.backdrop` | `looks_switchbackdropto` | Stack | `BACKDROP` Menu | - |
| `looks.backdropwait` | `looks_switchbackdroptoandwait` | Stack | `BACKDROP` Menu | - |
| `looks.nextbackdrop` | `looks_nextbackdrop` | Stack | none | - |
| `looks.changeeffect` | `looks_changeeffectby` | Stack | `EFFECT` Field, `CHANGE` Input | - |
| `looks.seteffect` | `looks_seteffectto` | Stack | `EFFECT` Field, `VALUE` Input | - |
| `looks.cleareffects` | `looks_cleargraphiceffects` | Stack | none | - |
| `looks.changesize` | `looks_changesizeby` | Stack | `CHANGE` Input | - |
| `looks.setsize` | `looks_setsizeto` | Stack | `SIZE` Input | - |
| `looks.show` | `looks_show` | Stack | none | - |
| `looks.hide` | `looks_hide` | Stack | none | - |
| `looks.layer` | `looks_gotofrontback` | Stack | `FRONT_BACK` Field | - |
| `looks.movelayers` | `looks_goforwardbackwardlayers` | Stack | `FORWARD_BACKWARD` Field, `NUM` Input | - |
| `looks.costumenumbername` | `looks_costumenumbername` | Reporter | `NUMBER_NAME` Field | - |
| `looks.backdropnumbername` | `looks_backdropnumbername` | Reporter | `NUMBER_NAME` Field | - |
| `looks.size` | `looks_size` | Reporter | none | - |
| `legacy.looks.hideall` | `looks_hideallsprites` | Stack | none | legacy |
| `legacy.looks.changestretch` | `looks_changestretchby` | Stack | `CHANGE` Input | legacy |
| `legacy.looks.setstretch` | `looks_setstretchto` | Stack | `STRETCH` Input | legacy |
| `sound.playuntil` | `sound_playuntildone` | Stack | `SOUND_MENU` Menu | - |
| `sound.play` | `sound_play` | Stack | `SOUND_MENU` Menu | - |
| `sound.stopall` | `sound_stopallsounds` | Stack | none | - |
| `sound.changeeffect` | `sound_changeeffectby` | Stack | `EFFECT` Field, `VALUE` Input | - |
| `sound.seteffect` | `sound_seteffectto` | Stack | `EFFECT` Field, `VALUE` Input | - |
| `sound.clear` | `sound_cleareffects` | Stack | none | - |
| `sound.changevolume` | `sound_changevolumeby` | Stack | `VOLUME` Input | - |
| `sound.setvolume` | `sound_setvolumeto` | Stack | `VOLUME` Input | - |
| `sound.volume` | `sound_volume` | Reporter | none | - |
| `legacy.sound.playdrum` | `music_playDrumForBeats` | Stack | `DRUM` Input, `BEATS` Input | extension `music`; legacy |
| `legacy.sound.rest` | `music_restForBeats` | Stack | `BEATS` Input | extension `music`; legacy |
| `legacy.sound.playnote` | `music_playNoteForBeats` | Stack | `NOTE` Input, `BEATS` Input | extension `music`; legacy |
| `legacy.sound.instrument` | `music_setInstrument` | Stack | `INSTRUMENT` Input | extension `music`; legacy |
| `legacy.sound.changetempo` | `music_changeTempo` | Stack | `TEMPO` Input | extension `music`; legacy |
| `legacy.sound.settempo` | `music_setTempo` | Stack | `TEMPO` Input | extension `music`; legacy |
| `event.greenflag` | `event_whenflagclicked` | Hat | none | - |
| `event.key` | `event_whenkeypressed` | Hat | `KEY_OPTION` Field | - |
| `event.clicked` | `event_whenthisspriteclicked` | Hat | none | - |
| `event.stageclicked` | `event_whenstageclicked` | Hat | none | - |
| `event.backdrop` | `event_whenbackdropswitchesto` | Hat | `BACKDROP` Field | - |
| `event.greaterthan` | `event_whengreaterthan` | Hat | `WHENGREATERTHANMENU` Field, `VALUE` Input | - |
| `event.received` | `event_whenbroadcastreceived` | Hat | `BROADCAST_OPTION` Field | - |
| `legacy.event.touching` | `event_whentouchingobject` | Hat | `TOUCHINGOBJECTMENU` Menu | legacy |
| `event.broadcast` | `event_broadcast` | Stack | `BROADCAST_INPUT` Menu | - |
| `event.broadcastwait` | `event_broadcastandwait` | Stack | `BROADCAST_INPUT` Menu | - |
| `control.wait` | `control_wait` | Stack | `DURATION` Input | - |
| `repeat` | `control_repeat` | CBlock | `TIMES` Input | substack `SUBSTACK` |
| `forever` | `control_forever` | CBlock | none | substack `SUBSTACK`; AlwaysCaps |
| `if` | `control_if` | CBlock | `CONDITION` Input | substack `SUBSTACK` |
| `ifelse` | `control_if_else` | CBlock | `CONDITION` Input | substack `SUBSTACK`, `SUBSTACK2` |
| `waituntil` | `control_wait_until` | Stack | `CONDITION` Input | - |
| `repeatuntil` | `control_repeat_until` | CBlock | `CONDITION` Input | substack `SUBSTACK` |
| `control.stop` | `control_stop` | Cap | `STOP_OPTION` Field | CapsUnlessOtherScripts |
| `control.clone` | `control_start_as_clone` | Hat | none | - |
| `control.createclone` | `control_create_clone_of` | Stack | `CLONE_OPTION` Menu | - |
| `control.deleteclone` | `control_delete_this_clone` | Cap | none | AlwaysCaps |
| `legacy.control.while` | `control_while` | Stack | `CONDITION` Input | legacy |
| `legacy.control.foreach` | `control_for_each` | Stack | `VARIABLE` Field, `VALUE` Input | legacy |
| `legacy.control.allatonce` | `control_all_at_once` | Stack | none | legacy |
| `legacy.control.counter` | `control_get_counter` | Reporter | none | legacy |
| `legacy.control.incrcounter` | `control_incr_counter` | Stack | none | legacy |
| `legacy.control.clearcounter` | `control_clear_counter` | Stack | none | legacy |
| `sensing.touching` | `sensing_touchingobject` | Boolean | `TOUCHINGOBJECTMENU` Menu | - |
| `sensing.touchingcolor` | `sensing_touchingcolor` | Boolean | `COLOR` Input | - |
| `sensing.colorstouching` | `sensing_coloristouchingcolor` | Boolean | `COLOR` Input, `COLOR2` Input | - |
| `sensing.distance` | `sensing_distanceto` | Reporter | `DISTANCETOMENU` Menu | - |
| `sensing.ask` | `sensing_askandwait` | Stack | `QUESTION` Input | - |
| `sensing.answer` | `sensing_answer` | Reporter | none | - |
| `sensing.key` | `sensing_keypressed` | Boolean | `KEY_OPTION` Menu | - |
| `sensing.mousedown` | `sensing_mousedown` | Boolean | none | - |
| `sensing.mousex` | `sensing_mousex` | Reporter | none | - |
| `sensing.mousey` | `sensing_mousey` | Reporter | none | - |
| `sensing.dragmode` | `sensing_setdragmode` | Stack | `DRAG_MODE` Field | - |
| `sensing.loudness` | `sensing_loudness` | Reporter | none | - |
| `sensing.timer` | `sensing_timer` | Reporter | none | - |
| `sensing.resettimer` | `sensing_resettimer` | Stack | none | - |
| `sensing.of` | `sensing_of` | Reporter | `PROPERTY` Field, `OBJECT` Menu | - |
| `sensing.current` | `sensing_current` | Reporter | `CURRENTMENU` Field | - |
| `sensing.dayssince2000` | `sensing_dayssince2000` | Reporter | none | - |
| `sensing.username` | `sensing_username` | Reporter | none | - |
| `sensing.online` | `sensing_online` | Boolean | none | - |
| `legacy.sensing.loud` | `sensing_loud` | Boolean | none | legacy |
| `legacy.sensing.userid` | `sensing_userid` | Reporter | none | legacy |
| `add` | `operator_add` | Reporter | `NUM1` Input, `NUM2` Input | - |
| `subtract` | `operator_subtract` | Reporter | `NUM1` Input, `NUM2` Input | - |
| `multiply` | `operator_multiply` | Reporter | `NUM1` Input, `NUM2` Input | - |
| `divide` | `operator_divide` | Reporter | `NUM1` Input, `NUM2` Input | - |
| `random` | `operator_random` | Reporter | `FROM` Input, `TO` Input | - |
| `greater` | `operator_gt` | Boolean | `OPERAND1` Input, `OPERAND2` Input | - |
| `less` | `operator_lt` | Boolean | `OPERAND1` Input, `OPERAND2` Input | - |
| `equals` | `operator_equals` | Boolean | `OPERAND1` Input, `OPERAND2` Input | - |
| `and` | `operator_and` | Boolean | `OPERAND1` Input, `OPERAND2` Input | - |
| `or` | `operator_or` | Boolean | `OPERAND1` Input, `OPERAND2` Input | - |
| `not` | `operator_not` | Boolean | `OPERAND` Input | - |
| `join` | `operator_join` | Reporter | `STRING1` Input, `STRING2` Input | - |
| `letter` | `operator_letter_of` | Reporter | `LETTER` Input, `STRING` Input | - |
| `length` | `operator_length` | Reporter | `STRING` Input | - |
| `contains` | `operator_contains` | Boolean | `STRING1` Input, `STRING2` Input | - |
| `mod` | `operator_mod` | Reporter | `NUM1` Input, `NUM2` Input | - |
| `round` | `operator_round` | Reporter | `NUM` Input | - |
| `mathop` | `operator_mathop` | Reporter | `OPERATOR` Field, `NUM` Input | - |
| `abs` | `operator_mathop` | Reporter | `NUM` Input | - |
| `floor` | `operator_mathop` | Reporter | `NUM` Input | - |
| `ceil` | `operator_mathop` | Reporter | `NUM` Input | - |
| `sqrt` | `operator_mathop` | Reporter | `NUM` Input | - |
| `sin` | `operator_mathop` | Reporter | `NUM` Input | - |
| `cos` | `operator_mathop` | Reporter | `NUM` Input | - |
| `tan` | `operator_mathop` | Reporter | `NUM` Input | - |
| `asin` | `operator_mathop` | Reporter | `NUM` Input | - |
| `acos` | `operator_mathop` | Reporter | `NUM` Input | - |
| `atan` | `operator_mathop` | Reporter | `NUM` Input | - |
| `ln` | `operator_mathop` | Reporter | `NUM` Input | - |
| `log10` | `operator_mathop` | Reporter | `NUM` Input | - |
| `exp` | `operator_mathop` | Reporter | `NUM` Input | - |
| `pow10` | `operator_mathop` | Reporter | `NUM` Input | - |
| `data.set` | `data_setvariableto` | Stack | `VARIABLE` Field, `VALUE` Input | - |
| `data.change` | `data_changevariableby` | Stack | `VARIABLE` Field, `VALUE` Input | - |
| `data.show` | `data_showvariable` | Stack | `VARIABLE` Field | - |
| `data.hide` | `data_hidevariable` | Stack | `VARIABLE` Field | - |
| `data.value` | `data_variable` | Reporter | `VARIABLE` Field | - |
| `list.add` | `data_addtolist` | Stack | `ITEM` Input, `LIST` Field | - |
| `list.delete` | `data_deleteoflist` | Stack | `INDEX` Input, `LIST` Field | - |
| `list.deleteall` | `data_deletealloflist` | Stack | `LIST` Field | - |
| `list.insert` | `data_insertatlist` | Stack | `INDEX` Input, `ITEM` Input, `LIST` Field | - |
| `list.replace` | `data_replaceitemoflist` | Stack | `INDEX` Input, `ITEM` Input, `LIST` Field | - |
| `list.item` | `data_itemoflist` | Reporter | `INDEX` Input, `LIST` Field | - |
| `list.index` | `data_itemnumoflist` | Reporter | `ITEM` Input, `LIST` Field | - |
| `list.length` | `data_lengthoflist` | Reporter | `LIST` Field | - |
| `list.contains` | `data_listcontainsitem` | Boolean | `ITEM` Input, `LIST` Field | - |
| `list.show` | `data_showlist` | Stack | `LIST` Field | - |
| `list.hide` | `data_hidelist` | Stack | `LIST` Field | - |
| `list.contents` | `data_listcontents` | Reporter | `LIST` Field | - |
| `pen.clear` | `pen_clear` | Stack | none | extension `pen` |
| `pen.stamp` | `pen_stamp` | Stack | none | extension `pen` |
| `pen.down` | `pen_penDown` | Stack | none | extension `pen` |
| `pen.up` | `pen_penUp` | Stack | none | extension `pen` |
| `pen.color` | `pen_setPenColorToColor` | Stack | `COLOR` Input | extension `pen` |
| `pen.changecolor` | `pen_changePenColorParamBy` | Stack | `COLOR_PARAM` Field, `VALUE` Input | extension `pen` |
| `pen.setcolor` | `pen_setPenColorParamTo` | Stack | `COLOR_PARAM` Field, `VALUE` Input | extension `pen` |
| `pen.changesize` | `pen_changePenSizeBy` | Stack | `SIZE` Input | extension `pen` |
| `pen.setsize` | `pen_setPenSizeTo` | Stack | `SIZE` Input | extension `pen` |
| `legacy.pen.setshade` | `pen_setPenShadeToNumber` | Stack | `SHADE` Input | extension `pen`; legacy |
| `legacy.pen.changeshade` | `pen_changePenShadeBy` | Stack | `SHADE` Input | extension `pen`; legacy |
| `legacy.pen.sethue` | `pen_setPenHueToNumber` | Stack | `HUE` Input | extension `pen`; legacy |
| `legacy.pen.changehue` | `pen_changePenHueBy` | Stack | `HUE` Input | extension `pen`; legacy |
| `music.playdrum` | `music_playDrumForBeats` | Stack | `DRUM` Field, `BEATS` Input | extension `music` |
| `music.rest` | `music_restForBeats` | Stack | `BEATS` Input | extension `music` |
| `music.playnote` | `music_playNoteForBeats` | Stack | `NOTE` Input, `BEATS` Input | extension `music` |
| `music.instrument` | `music_setInstrument` | Stack | `INSTRUMENT` Field | extension `music` |
| `music.settempo` | `music_setTempo` | Stack | `TEMPO` Input | extension `music` |
| `music.changetempo` | `music_changeTempo` | Stack | `TEMPO` Input | extension `music` |
| `music.tempo` | `music_getTempo` | Reporter | none | extension `music` |
| `legacy.music.mididrum` | `music_midiPlayDrumForBeats` | Stack | `DRUM` Input, `BEATS` Input | extension `music`; legacy |
| `legacy.music.midiinstrument` | `music_midiSetInstrument` | Stack | `INSTRUMENT` Input | extension `music`; legacy |
| `video.motiongreater` | `videoSensing_whenMotionGreaterThan` | Hat | `REFERENCE` Input | extension `videoSensing` |
| `video.on` | `videoSensing_videoOn` | Reporter | `ATTRIBUTE` Field, `SUBJECT` Field | extension `videoSensing` |
| `video.setstate` | `videoSensing_videoToggle` | Stack | `VIDEO_STATE` Field | extension `videoSensing` |
| `video.transparency` | `videoSensing_setVideoTransparency` | Stack | `TRANSPARENCY` Input | extension `videoSensing` |
| `text2speech.speak` | `text2speech_speakAndWait` | Stack | `WORDS` Input | extension `text2speech` |
| `text2speech.voice` | `text2speech_setVoice` | Stack | `VOICE` Field | extension `text2speech` |
| `text2speech.language` | `text2speech_setLanguage` | Stack | `LANGUAGE` Field | extension `text2speech` |
| `translate.translate` | `translate_getTranslate` | Reporter | `WORDS` Input, `LANGUAGE` Field | extension `translate` |
| `translate.language` | `translate_getViewerLanguage` | Reporter | none | extension `translate` |
| `speech2text.listen` | `speech2text_listenAndWait` | Stack | none | extension `speech2text` |
| `speech2text.hear` | `speech2text_whenIHearHat` | Hat | `PHRASE` Input | extension `speech2text` |
| `speech2text.speech` | `speech2text_getSpeech` | Reporter | none | extension `speech2text` |
| `faceSensing.goto` | `faceSensing_goToPart` | Stack | `PART` Field | extension `faceSensing` |
| `faceSensing.point` | `faceSensing_pointInFaceTiltDirection` | Stack | none | extension `faceSensing` |
| `faceSensing.setsize` | `faceSensing_setSizeToFaceSize` | Stack | none | extension `faceSensing` |
| `faceSensing.whentilted` | `faceSensing_whenTilted` | Hat | `DIRECTION` Field | extension `faceSensing` |
| `faceSensing.whentouching` | `faceSensing_whenSpriteTouchesPart` | Hat | `PART` Field | extension `faceSensing` |
| `faceSensing.whendetected` | `faceSensing_whenFaceDetected` | Hat | none | extension `faceSensing` |
| `faceSensing.detected` | `faceSensing_faceIsDetected` | Boolean | none | extension `faceSensing` |
| `faceSensing.tilt` | `faceSensing_faceTilt` | Reporter | none | extension `faceSensing` |
| `faceSensing.size` | `faceSensing_faceSize` | Reporter | none | extension `faceSensing` |
| `makeymakey.key` | `makeymakey_whenMakeyKeyPressed` | Hat | `KEY` Field | extension `makeymakey` |
| `makeymakey.code` | `makeymakey_whenCodePressed` | Hat | `SEQUENCE` Field | extension `makeymakey` |
| `microbit.button` | `microbit_whenButtonPressed` | Hat | `BTN` Field | extension `microbit` |
| `microbit.pressed` | `microbit_isButtonPressed` | Boolean | `BTN` Field | extension `microbit` |
| `microbit.gesture` | `microbit_whenGesture` | Hat | `GESTURE` Field | extension `microbit` |
| `microbit.display` | `microbit_displaySymbol` | Stack | `MATRIX` Input | extension `microbit` |
| `microbit.text` | `microbit_displayText` | Stack | `TEXT` Input | extension `microbit` |
| `microbit.clear` | `microbit_displayClear` | Stack | none | extension `microbit` |
| `microbit.whentilted` | `microbit_whenTilted` | Hat | `DIRECTION` Field | extension `microbit` |
| `microbit.tilted` | `microbit_isTilted` | Boolean | `DIRECTION` Field | extension `microbit` |
| `microbit.tilt` | `microbit_getTiltAngle` | Reporter | `DIRECTION` Field | extension `microbit` |
| `microbit.pin` | `microbit_whenPinConnected` | Hat | `PIN` Field | extension `microbit` |
| `ev3.motor` | `ev3_motorTurnClockwise` | Stack | `PORT` Field, `TIME` Input | extension `ev3` |
| `ev3.motorback` | `ev3_motorTurnCounterClockwise` | Stack | `PORT` Field, `TIME` Input | extension `ev3` |
| `ev3.power` | `ev3_motorSetPower` | Stack | `PORT` Field, `POWER` Input | extension `ev3` |
| `ev3.position` | `ev3_getMotorPosition` | Reporter | `PORT` Field | extension `ev3` |
| `ev3.button` | `ev3_whenButtonPressed` | Hat | `PORT` Field | extension `ev3` |
| `ev3.distancebelow` | `ev3_whenDistanceLessThan` | Hat | `DISTANCE` Input | extension `ev3` |
| `ev3.brightnessbelow` | `ev3_whenBrightnessLessThan` | Hat | `DISTANCE` Input | extension `ev3` |
| `ev3.pressed` | `ev3_buttonPressed` | Boolean | `PORT` Field | extension `ev3` |
| `ev3.distance` | `ev3_getDistance` | Reporter | none | extension `ev3` |
| `ev3.brightness` | `ev3_getBrightness` | Reporter | none | extension `ev3` |
| `ev3.beep` | `ev3_beep` | Stack | `NOTE` Input, `TIME` Input | extension `ev3` |
| `gdxfor.gesture` | `gdxfor_whenGesture` | Hat | `GESTURE` Field | extension `gdxfor` |
| `gdxfor.forceevent` | `gdxfor_whenForcePushedOrPulled` | Hat | `PUSH_PULL` Field | extension `gdxfor` |
| `gdxfor.force` | `gdxfor_getForce` | Reporter | none | extension `gdxfor` |
| `gdxfor.whentilted` | `gdxfor_whenTilted` | Hat | `TILT` Field | extension `gdxfor` |
| `gdxfor.tilted` | `gdxfor_isTilted` | Boolean | `TILT` Field | extension `gdxfor` |
| `gdxfor.tilt` | `gdxfor_getTilt` | Reporter | `TILT` Field | extension `gdxfor` |
| `gdxfor.falling` | `gdxfor_isFreeFalling` | Boolean | none | extension `gdxfor` |
| `gdxfor.spin` | `gdxfor_getSpinSpeed` | Reporter | `DIRECTION` Field | extension `gdxfor` |
| `gdxfor.acceleration` | `gdxfor_getAcceleration` | Reporter | `DIRECTION` Field | extension `gdxfor` |
| `wedo2.motorfor` | `wedo2_motorOnFor` | Stack | `MOTOR_ID` Field, `DURATION` Input | extension `wedo2` |
| `wedo2.motoron` | `wedo2_motorOn` | Stack | `MOTOR_ID` Field | extension `wedo2` |
| `wedo2.motoroff` | `wedo2_motorOff` | Stack | `MOTOR_ID` Field | extension `wedo2` |
| `wedo2.power` | `wedo2_startMotorPower` | Stack | `MOTOR_ID` Field, `POWER` Input | extension `wedo2` |
| `wedo2.direction` | `wedo2_setMotorDirection` | Stack | `MOTOR_ID` Field, `MOTOR_DIRECTION` Field | extension `wedo2` |
| `wedo2.light` | `wedo2_setLightHue` | Stack | `HUE` Input | extension `wedo2` |
| `legacy.wedo2.playnote` | `wedo2_playNoteFor` | Stack | `NOTE` Input, `DURATION` Input | extension `wedo2`; legacy |
| `wedo2.distanceevent` | `wedo2_whenDistance` | Hat | `OP` Field, `REFERENCE` Input | extension `wedo2` |
| `wedo2.whentilted` | `wedo2_whenTilted` | Hat | `TILT_DIRECTION_ANY` Field | extension `wedo2` |
| `wedo2.distance` | `wedo2_getDistance` | Reporter | none | extension `wedo2` |
| `wedo2.tilted` | `wedo2_isTilted` | Boolean | `TILT_DIRECTION_ANY` Field | extension `wedo2` |
| `wedo2.tilt` | `wedo2_getTiltAngle` | Reporter | `TILT_DIRECTION` Field | extension `wedo2` |
| `boost.motorfor` | `boost_motorOnFor` | Stack | `MOTOR_ID` Field, `DURATION` Input | extension `boost` |
| `boost.rotations` | `boost_motorOnForRotation` | Stack | `MOTOR_ID` Field, `ROTATION` Input | extension `boost` |
| `boost.motoron` | `boost_motorOn` | Stack | `MOTOR_ID` Field | extension `boost` |
| `boost.motoroff` | `boost_motorOff` | Stack | `MOTOR_ID` Field | extension `boost` |
| `boost.power` | `boost_setMotorPower` | Stack | `MOTOR_ID` Field, `POWER` Input | extension `boost` |
| `boost.direction` | `boost_setMotorDirection` | Stack | `MOTOR_ID` Field, `MOTOR_DIRECTION` Field | extension `boost` |
| `boost.position` | `boost_getMotorPosition` | Reporter | `MOTOR_REPORTER_ID` Field | extension `boost` |
| `boost.color` | `boost_whenColor` | Hat | `COLOR` Field | extension `boost` |
| `boost.seeing` | `boost_seeingColor` | Boolean | `COLOR` Field | extension `boost` |
| `boost.whentilted` | `boost_whenTilted` | Hat | `TILT_DIRECTION_ANY` Field | extension `boost` |
| `boost.tilt` | `boost_getTiltAngle` | Reporter | `TILT_DIRECTION` Field | extension `boost` |
| `boost.light` | `boost_setLightHue` | Stack | `HUE` Input | extension `boost` |
<!-- ALIAS_TABLE_END -->

## Project Shape

Targets are declared with braces:

```scratchasm
stage {
}

sprite "Player" {
}
```

Every project must contain one `stage` target. A project can contain any number of sprites. Script bodies are indentation based. Hat and procedure declarations end with `:`.

## Target Declarations

Declarations live directly inside `stage` or `sprite` blocks, before or between scripts.

### Variables

```scratchasm
var score = 0
cloud var highScore = 0
```

Variables are emitted into the Scratch target `variables` map with deterministic IDs. Cloud variables add Scratch's cloud flag.

Declared variables can be read directly in expressions and changed with minimal assignment syntax:

```scratchasm
score = 5
score += 3
score = (score + 2) * 4
```

Safe repair also understands Scratch-like source phrases and rewrites them to canonical ScratchASM before compilation:

```scratchasm
set score to 5      # repair rewrites to: score = 5
change score by 3   # repair rewrites to: score += 3
```

Names containing spaces can be wrapped in backticks: `` `high score` += 1 ``. A variable must be declared in the current target or on the stage.

### Lists

```scratchasm
list inventory = ["key", "coin"]
```

Lists are emitted into the Scratch target `lists` map with deterministic IDs.

List commands use the declared list name:

```scratchasm
inventory.add "coin"
inventory.delete 1
inventory.delete_all
inventory.insert 1 "key"
inventory.replace 1 "gem"
inventory.show
inventory.hide
```

List reporters are `inventory.item(index)`, `inventory.index(value)`, `inventory.length()`, `inventory.contains(value)`, and `inventory.contents()`.

Safe repair can rewrite these Scratch-like list phrases:

```scratchasm
add "coin" to inventory
delete 1 of inventory
delete all of inventory
insert "key" at 1 of inventory
replace 1 of inventory with "gem"
show list inventory
hide list inventory
```

### Broadcasts

```scratchasm
broadcast gameOver = "game over"
```

The declaration name is the ScratchASM alias. The value is the Scratch broadcast message.

### Extensions

```scratchasm
extension pen
```

Extensions are emitted into the root Scratch `extensions` array. Native extension aliases automatically register their extension, so explicit declarations are only needed when generic opcode syntax is used. Duplicate declarations are written once.

### Sprite And Stage State

```scratchasm
state x=0 y=0 direction=90 size=100 visible=true layer=1
rotationStyle "all around"
```

Supported state keys are `x`, `y`, `direction`, `size`, `visible`, `layer`, and `layerOrder`. For sprites these map to normal Scratch target fields. Stage targets can use `layer` or `layerOrder`.

Common rotation styles are:

- `"all around"`
- `"left-right"`
- `"don't rotate"`

## Costume Drawing

ScratchASM can generate Scratch-compatible SVG costume assets:

```scratchasm
costume "Player" 480x360 center 240,180 {
  rect 0,0 480,360 fill="#ffffff"
  line 40,300 440,300 stroke="#2f80ed" width=6
  circle 240,180 r=40 fill="none" stroke="#22c55e" width=6
  ellipse 240,220 rx=80 ry=32 fill="#bfdbfe"
  path "M 100 100 C 160 40, 320 40, 380 100" stroke="#111827" width=3 fill="none"
  text 40,80 "Hello" size=28 fill="#111827"
}
```

The declaration format is:

```scratchasm
costume "name" WIDTHxHEIGHT center X,Y {
  shape ...
}
```

Supported shapes:

- `line x1,y1 x2,y2 stroke="#hex" width=4`
- `rect x,y w,h fill="#hex" stroke="#hex" width=2`
- `circle x,y r=50 fill="none" stroke="#hex" width=6`
- `ellipse x,y rx=80 ry=32 fill="#hex"`
- `path "M ..." stroke="#hex" width=3 fill="none"`
- `text x,y "Text" size=28 fill="#hex"`

Supported emitted SVG attributes are `fill`, `stroke`, `width`, `opacity`, and `size` for text. `width` maps to SVG `stroke-width`; `size` maps to `font-size`. The generated SVG is self-contained, XML-escaped, and does not emit external references or scripts.

For each generated costume, OpenCTS computes `assetId` as the lowercase MD5 hex of the SVG bytes and writes `md5ext` as `assetId + ".svg"`.

## Hats

Supported compact hats:

```scratchasm
@greenflag:
@key "space":
```

Every native hat in the complete alias table uses the same form. Examples:

```scratchasm
@event.clicked:
  looks.say "clicked"

@event.received gameOver:
  looks.say "received"

@microbit.button "A":
  microbit.display "09090"
```

Any Scratch hat or top-level block can use the generic hat form:

```scratchasm
@block "event_whenbroadcastreceived" field BROADCAST_OPTION=gameOver:
  looks.say "received"

@block "control_start_as_clone":
  motion.move 10
```

`@block` accepts the same `input`, `field`, and `mutation` clauses as a generic block. The compiler emits it with `topLevel: true`, coordinates, and an automatically linked body. This is also the escape hatch for extension hats.

Examples:

```scratchasm
@greenflag:
  looks.say "Ready"

@key "space":
  motion.move 10
```

## Procedures

Custom procedures use typed parameters:

```scratchasm
proc jump(height:num=10, message:str="go") warp:
  motion.changey height
  looks.say message

@key "space":
  call jump(20, "up")
```

Parameter types:

- `num`
- `str`
- `bool`

Use `as` when the visible custom block label needs text between inputs:

```scratchasm
proc gate(limit:num=10, enabled:bool=false) as "run %n times when %b" warp:
  if enabled:
    repeat limit:
      motion.move 10
```

The placeholders must match the parameters in order: `%n` for `num`, `%s` for `str`, and `%b` for `bool`. A mismatch is error `CTS1011`.

The compiler emits Scratch procedure definition, prototype, call blocks, and the required mutation fields.

## Minimal Statements And Control

Native stack aliases use `alias` followed by space-separated expressions. Commas are optional separators:

```scratchasm
motion.move 10
motion.goto 20, -15
looks.say join("score: ", score)
sound.play "pop"
pen.down
```

Control blocks use a trailing colon and indentation:

```scratchasm
repeat 10:
  motion.move 5

repeatuntil score > 100:
  score += 1

if score > 10 and not inventory.contains("key"):
  looks.say "high"
else:
  looks.say "low"

forever:
  motion.move 1
```

`waituntil condition` is a normal stack command. `forever`, clone deletion, and terminal stop options end their stack. Statements after a terminal block produce warning `CTS2002` and are not linked into unreachable Scratch code.

## Expressions And Operators

Expressions can be nested anywhere an input is accepted. Precedence from strongest to weakest is parentheses, unary `not`/negative, `^`, `* / %`, `+ -`, comparisons, `and`, then `or`.

```scratchasm
score = (score + 3) * 2
score = random(1, 10) + round(2.4)
ready = score > 10 and not inventory.contains("missing")
```

Supported symbolic operators are `+`, `-`, `*`, `/`, `%`, `^`, `<`, `>`, `<=`, `>=`, `==`, `!=`, `and`, `or`, and `not`. `&&`, `||`, and `!` are readable alternatives for the Boolean words. Non-vanilla comparisons are exactly lowered through Scratch `not`, `<`, `>`, and `=` blocks.

The QoL `^` operator is deliberately reliable rather than approximate. Its exponent must be an integer literal from `-64` through `64`, and its base must be a stable variable, literal, or pure expression. The compiler lowers it to multiplication, uses `1` for exponent zero, and uses division for negative exponents. Dynamic/fractional exponents or nondeterministic bases such as `random()` produce `CTS1020`. For Scratch's native math operations, use `exp(value)`/`e^(value)` or `pow10(value)`/`10^(value)`.

All Scratch reporter functions are available:

- General: `random(a, b)`, `join(a, b)`, `letter(index, text)`, `length(text)`, `contains(text, part)`, `round(value)`
- Math: `abs`, `floor`, `ceil`, `sqrt`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `ln`, `log10`, `exp`, `pow10`
- Any Reporter or Boolean alias in the complete table, using `alias(arguments)`
- Declared variables by name and list reporters such as `inventory.item(1)`

Scratch trigonometric functions use degrees, matching the Scratch runtime.

## Generic Blocks

Every Scratch opcode can be emitted with `block`:

```scratchasm
block "data_setvariableto" field VARIABLE=score input VALUE=10
block "looks_seteffectto" field EFFECT="COLOR" input VALUE=25
```

Clauses:

- `input NAME=value`
- `field NAME=value`
- `field NAME=("display name", "id")`
- `mutation NAME=value`

Declared variables, lists, and broadcasts can be referenced in fields. For example, `field VARIABLE=score` resolves to the deterministic ID from `var score = 0`.

Stage variables, lists, and broadcasts are visible from sprite scripts. A sprite-local declaration with the same alias takes precedence.

### Reporter And Boolean Inputs

Square brackets create a nested reporter or Boolean block:

```scratchasm
block "data_setvariableto" field VARIABLE=score input VALUE=["operator_add" input NUM1=1 input NUM2=2]
block "control_if" input CONDITION=["operator_gt" input OPERAND1=["data_variable" field VARIABLE=score] input OPERAND2=10] {
  looks.say "high score"
}
```

Reporter blocks can nest recursively. The compiler creates their block IDs, parent links, inputs, fields, and mutations.

Use `shadow` for a Scratch menu/shadow block:

```scratchasm
block "motion_goto" input TO=[shadow "motion_goto_menu" field TO="_random_"]
```

Normal bracket blocks produce a non-shadow child input. `[shadow ...]` produces a Scratch shadow child input.

### C-Blocks

Use braces for generic C-block substacks:

```scratchasm
block "control_repeat" input TIMES=10 {
  motion.move 10
  looks.say "step"
}
```

The nested statements are linked through Scratch's `SUBSTACK` input.

Blocks with more than one branch use named substack sections:

```scratchasm
block "control_if_else" input CONDITION=["operator_equals" input OPERAND1=answer input OPERAND2="yes"] {
  substack SUBSTACK:
    looks.say "yes"
  substack SUBSTACK2:
    looks.say "no"
}
```

Named sections can use any Scratch input name. Use them exclusively within that block body; the short body form always maps to `SUBSTACK`.

### Generic Grammar

```text
block-statement  := block opcode clause* block-body?
generic-hat     := @block opcode clause* : statement*
reporter-value  := [ opcode clause* ] | [ shadow opcode clause* ]
clause          := input NAME=value
                 | field NAME=value
                 | field NAME=(value, value)
                 | mutation NAME=value
block-body      := { statement* }
                 | { (substack NAME: statement*)+ }
opcode          := string | identifier
```

The two-value field tuple is `(display value, Scratch ID)`. A single declared data alias automatically resolves its ID for `VARIABLE`, `LIST`, and `BROADCAST_OPTION`. Mutation values are emitted as Scratch mutation strings together with `tagName` and `children`.

## Scratch Opcode Reference

Opcode, input, field, and mutation names are case-sensitive. Every current opcode below has native syntax in the complete alias table. Generic forms remain available when exact serialized control is needed.

### Core Opcodes

| Category | Opcodes |
| --- | --- |
| Motion | `motion_movesteps`, `motion_gotoxy`, `motion_goto`, `motion_turnright`, `motion_turnleft`, `motion_pointindirection`, `motion_pointtowards`, `motion_glidesecstoxy`, `motion_glideto`, `motion_ifonedgebounce`, `motion_setrotationstyle`, `motion_changexby`, `motion_setx`, `motion_changeyby`, `motion_sety`, `motion_xposition`, `motion_yposition`, `motion_direction` |
| Looks | `looks_say`, `looks_sayforsecs`, `looks_think`, `looks_thinkforsecs`, `looks_show`, `looks_hide`, `looks_switchcostumeto`, `looks_switchbackdropto`, `looks_switchbackdroptoandwait`, `looks_nextcostume`, `looks_nextbackdrop`, `looks_changeeffectby`, `looks_seteffectto`, `looks_cleargraphiceffects`, `looks_changesizeby`, `looks_setsizeto`, `looks_gotofrontback`, `looks_goforwardbackwardlayers`, `looks_size`, `looks_costumenumbername`, `looks_backdropnumbername` |
| Sound | `sound_play`, `sound_playuntildone`, `sound_stopallsounds`, `sound_seteffectto`, `sound_changeeffectby`, `sound_cleareffects`, `sound_setvolumeto`, `sound_changevolumeby`, `sound_volume` |
| Events | `event_whenflagclicked`, `event_whenkeypressed`, `event_whenthisspriteclicked`, `event_whenstageclicked`, `event_whenbackdropswitchesto`, `event_whengreaterthan`, `event_whenbroadcastreceived`, `event_whentouchingobject`, `event_broadcast`, `event_broadcastandwait` |
| Control | `control_wait`, `control_repeat`, `control_forever`, `control_if`, `control_if_else`, `control_wait_until`, `control_repeat_until`, `control_stop`, `control_start_as_clone`, `control_create_clone_of`, `control_delete_this_clone` |
| Sensing | `sensing_touchingobject`, `sensing_touchingcolor`, `sensing_coloristouchingcolor`, `sensing_distanceto`, `sensing_askandwait`, `sensing_answer`, `sensing_keypressed`, `sensing_mousedown`, `sensing_mousex`, `sensing_mousey`, `sensing_setdragmode`, `sensing_loudness`, `sensing_timer`, `sensing_resettimer`, `sensing_of`, `sensing_current`, `sensing_dayssince2000`, `sensing_username` |
| Operators | `operator_add`, `operator_subtract`, `operator_multiply`, `operator_divide`, `operator_random`, `operator_gt`, `operator_lt`, `operator_equals`, `operator_and`, `operator_or`, `operator_not`, `operator_join`, `operator_letter_of`, `operator_length`, `operator_contains`, `operator_mod`, `operator_round`, `operator_mathop` |
| Variables | `data_variable`, `data_setvariableto`, `data_changevariableby`, `data_showvariable`, `data_hidevariable` |
| Lists | `data_listcontents`, `data_addtolist`, `data_deleteoflist`, `data_deletealloflist`, `data_insertatlist`, `data_replaceitemoflist`, `data_itemoflist`, `data_itemnumoflist`, `data_lengthoflist`, `data_listcontainsitem`, `data_showlist`, `data_hidelist` |
| My Blocks | `procedures_definition`, `procedures_prototype`, `procedures_call`, `argument_reporter_string_number`, `argument_reporter_boolean` |

Scratch also recognizes hidden legacy VM opcodes. Their native ScratchASM aliases use the `legacy.*` namespace and are marked `legacy` in the complete table.

### Menu And Shadow Opcodes

Menu blocks normally appear inside `[shadow ...]`:

| Category | Menu opcodes |
| --- | --- |
| Motion | `motion_goto_menu`, `motion_glideto_menu`, `motion_pointtowards_menu` |
| Looks | `looks_costume`, `looks_backdrops` |
| Sound | `sound_sounds_menu`, `sound_beats_menu`, `sound_effects_menu` |
| Events | `event_broadcast_menu` |
| Control | `control_create_clone_of_menu` |
| Sensing | `sensing_touchingobjectmenu`, `sensing_distancetomenu`, `sensing_keyoptions`, `sensing_of_object_menu` |

### Common Clause Names

These are the Scratch names used most often in generic code:

| Block family | Inputs | Fields |
| --- | --- | --- |
| Motion | `STEPS`, `DEGREES`, `X`, `Y`, `DX`, `DY`, `DIRECTION`, `SECS`, `TO`, `TOWARDS` | `STYLE`, menu field `TO` or `TOWARDS` |
| Looks | `MESSAGE`, `SECS`, `COSTUME`, `BACKDROP`, `CHANGE`, `VALUE`, `SIZE`, `NUM` | `EFFECT`, `FRONT_BACK`, `FORWARD_BACKWARD`, `NUMBER_NAME` |
| Sound | `SOUND_MENU`, `VALUE`, `VOLUME` | `EFFECT`, menu field `SOUND_MENU` |
| Events | `VALUE`, `BROADCAST_INPUT` | `KEY_OPTION`, `BACKDROP`, `WHENGREATERTHANMENU`, `BROADCAST_OPTION` |
| Control | `DURATION`, `TIMES`, `CONDITION`, `SUBSTACK`, `SUBSTACK2`, `CLONE_OPTION` | `STOP_OPTION` |
| Sensing | `TOUCHINGOBJECTMENU`, `COLOR`, `COLOR2`, `DISTANCETOMENU`, `QUESTION`, `KEY_OPTION`, `OBJECT`, `PROPERTY` | `DRAG_MODE`, `CURRENTMENU` |
| Operators | `NUM1`, `NUM2`, `OPERAND1`, `OPERAND2`, `OPERAND`, `STRING1`, `STRING2`, `STRING`, `LETTER`, `FROM`, `TO` | `OPERATOR` |
| Variables/lists | `VALUE`, `ITEM`, `INDEX` | `VARIABLE`, `LIST` |

When a menu is represented as a nested shadow block, the outer clause uses `input`; the nested menu selection uses `field`.

### Standard Extensions

Native extension aliases automatically add the required extension ID. Generic extension opcodes require an explicit declaration. Extension IDs and opcode suffixes are:

| Declaration | Opcode suffixes; prepend `ID_` |
| --- | --- |
| `extension pen` | `clear`, `stamp`, `penDown`, `penUp`, `setPenColorToColor`, `changePenColorParamBy`, `setPenColorParamTo`, `changePenSizeBy`, `setPenSizeTo`, `setPenShadeToNumber`, `changePenShadeBy`, `setPenHueToNumber`, `changePenHueBy` |
| `extension music` | `playDrumForBeats`, `midiPlayDrumForBeats`, `restForBeats`, `playNoteForBeats`, `setInstrument`, `midiSetInstrument`, `setTempo`, `changeTempo`, `getTempo` |
| `extension videoSensing` | `whenMotionGreaterThan`, `videoOn`, `videoToggle`, `setVideoTransparency` |
| `extension text2speech` | `speakAndWait`, `setVoice`, `setLanguage` |
| `extension translate` | `getTranslate`, `getViewerLanguage` |
| `extension speech2text` | `listenAndWait`, `whenIHearHat`, `getSpeech` |
| `extension faceSensing` | `goToPart`, `pointInFaceTiltDirection`, `setSizeToFaceSize`, `whenTilted`, `whenSpriteTouchesPart`, `whenFaceDetected`, `faceIsDetected`, `faceTilt`, `faceSize` |
| `extension makeymakey` | `whenMakeyKeyPressed`, `whenCodePressed` |
| `extension microbit` | `whenButtonPressed`, `isButtonPressed`, `whenGesture`, `displaySymbol`, `displayText`, `displayClear`, `whenTilted`, `isTilted`, `getTiltAngle`, `whenPinConnected` |
| `extension ev3` | `motorTurnClockwise`, `motorTurnCounterClockwise`, `motorSetPower`, `getMotorPosition`, `whenButtonPressed`, `whenDistanceLessThan`, `whenBrightnessLessThan`, `buttonPressed`, `getDistance`, `getBrightness`, `beep` |
| `extension wedo2` | `motorOnFor`, `motorOn`, `motorOff`, `startMotorPower`, `setMotorDirection`, `setLightHue`, `playNoteFor`, `whenDistance`, `whenTilted`, `getDistance`, `isTilted`, `getTiltAngle` |
| `extension gdxfor` | `whenGesture`, `whenForcePushedOrPulled`, `getForce`, `whenTilted`, `isTilted`, `getTilt`, `isFreeFalling`, `getSpinSpeed`, `getAcceleration` |
| `extension boost` | `motorOnFor`, `motorOnForRotation`, `motorOn`, `motorOff`, `setMotorPower`, `setMotorDirection`, `getMotorPosition`, `whenColor`, `seeingColor`, `whenTilted`, `getTiltAngle`, `setLightHue` |

For example, `pen.stamp` emits the Scratch Pen stamp block and registers `pen`. Native extension hats use forms such as `@microbit.button "A":`; reporters use forms such as `translate.translate("hello", "es")`. Hardware extensions still require the normal Scratch hardware connection.

### Coverage Boundary

ScratchASM native and generic syntax represents Scratch block JSON: opcodes, nested inputs, fields and IDs, mutations, shadow blocks, top-level hats, next/parent links, and one or more substacks. The compiler also generates SVG costume assets. Imported bitmap costumes, sounds, comments, and monitors are retained in an adjacent project companion referenced by the `project` directive. Use the converter's `.sb3` to `.sasm` export to create portable source with these records intact. Complex graphs use `rawblocks` JSON instead of a lossy alias conversion.

Arbitrary third-party extension blocks are preserved when converting an existing valid `.sb3`, but are not rewritten to vanilla blocks unless an exact semantic equivalent exists. No general conversion is attempted because extension behavior and external state are not encoded well enough for a reliable transformation. ScratchASM's literal `^` lowering is an example of a reliable vanilla workaround implemented by the compiler.

## Raw Opcode Escape

Raw `%` syntax remains supported for compatibility:

```scratchasm
% "motion_movesteps" input STEPS=10
% "data_setvariableto" field VARIABLE=("score", "score-id") input VALUE=0
```

Raw lines produce warning `CTS2001` because they bypass alias metadata. Warnings allow `.sb3` output. Errors block output.

Prefer `block` for new ScratchASM because it supports the same clause model without the raw-warning diagnostic.

## Values

Values can be:

- Numbers: `10`, `-4`, `1.5`, `2s`
- Strings: `"Hello"`
- Identifiers: `score`, `gameOver`, `true`
- Expressions: `(score + 2) * 3`, `not sensing.mousedown()`, `sin(30)`

String escapes include `\"`, `\\`, `\n`, `\r`, and `\t`.

## Comments

Use `#` for comments outside strings:

```scratchasm
# This is a comment.
looks.say "Hello # not a comment"
```

## Diagnostics

Language diagnostics include severity, code, line, and column. In the executable's editor, errors are red, warnings are amber, and double-clicking a diagnostic selects its exact source span. Warnings allow conversion; errors block output.

The editor uses Scratch category colors for native aliases, operators, data, lists, broadcasts, custom blocks and parameters, costume drawing tools, and bundled extensions. Generic `block`, `input`, `field`, `mutation`, and `%` syntax uses a separate raw-syntax color because its category depends on the supplied opcode. Literal and comment colors are editor syntax colors rather than Scratch block categories.

Common diagnostic codes:

- `CTS1001`: syntax error
- `CTS1004`: missing stage target
- `CTS1005`: duplicate custom procedure
- `CTS1006`: unsupported hat
- `CTS1007`: known alias with the wrong argument count
- `CTS1008`: unknown compact alias
- `CTS1009`: unknown procedure call
- `CTS1010`: wrong custom procedure argument count
- `CTS1011`: custom procedure display signature does not match parameter types
- `CTS1016`: undeclared variable
- `CTS1017`: undeclared list
- `CTS1018`: unknown reporter or unsupported operator
- `CTS1019`: reporter function argument count error
- `CTS1020`: unsupported or excessive literal exponent
- `CTS2001`: raw opcode escape warning
- `CTS2002`: unreachable statement after a terminal block
- `REPAIR100`: Scratch project/package repair action
- `REPAIR200`: ScratchASM source repair action

## Safe Repair

Repair is opt-in from the UI, CLI, and MCP host. It never edits the input file in place. It writes a new `.sb3` only after the repaired project validates.

Supported repair inputs:

- `.sasm` and legacy `.mono`: normalizes line endings, removes a UTF-8 BOM, expands tab indentation, replaces smart quotes/non-breaking spaces, rewrites safe Scratch-like variable/list phrases, wraps an implicit `stage { ... }` around source without a target, and appends missing closing braces when the brace balance is clearly positive.
- `.sb3`: validates the ZIP root entries, rejects duplicate or unsafe archive paths, repairs `project.json` structure when JSON is readable, generates a default SVG costume when a target has no usable costume, normalizes block containers, clears links to missing blocks, and restores required target defaults.
- `project.json` and project folders: applies the same readable `project.json` repair and generated-asset behavior.

Malformed JSON, unreadable ZIP data, unsafe ZIP paths, unknown semantics, and source that still fails after safe rewrites are reported as errors with exact diagnostics where available.

## IDE And VS Code

The Windows executable opens as a ScratchASM IDE when launched without CLI arguments. It supports file/folder browsing, typed paths, file drag-and-drop, `.sb3` import, source editing, Scratch-category colors, live diagnostics, a project outline, line numbers, search, undo/redo, resizable panes, persistent dark/light themes, export, repair, and portable source saving. Unsaved edits are protected when switching documents or closing.

Keyboard commands: Ctrl+N new, Ctrl+O open, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+F search, F3 next match, Shift+F3 previous match, Ctrl+Z undo, Ctrl+Y redo, and F5 export `.sb3`. Double-click a diagnostic or outline entry to navigate to its source. Tabs insert two spaces; Enter retains indentation.

The VS Code extension is in `editors/vscode-scratchasm`. It contributes `.sasm` and `.mono` language IDs, Scratch-colored TextMate scopes, dark/light ScratchASM themes, diagnostics, and completions through `ScratchASM.LanguageHost`. If the extension cannot find the host build output, set `scratchasm.languageHostPath` to `ScratchASM.LanguageHost.exe` or `ScratchASM.LanguageHost.dll`.

## MCP Language Host

`src/ScratchASM.LanguageHost` exposes both LSP (`--lsp`) and MCP (`--mcp`) JSON-RPC modes. MCP paths are workspace-relative and are rejected if they escape the configured workspace root.

MCP tools:

- `analyze_source`: returns diagnostics, symbols, and color spans.
- `compile_to_sb3`: compiles any supported input to `.sb3`.
- `decompile_sb3`: returns editable ScratchASM from an `.sb3`; optional `output` writes a portable `.sasm` file and companion. `overwrite` defaults to false for this export.
- `merge_edited_source`: merges edited ScratchASM source into a baseline `.sb3`.
- `repair_input`: attempts safe repair for any supported input and writes a repaired `.sb3` when possible.
- `lookup_catalog`: searches aliases, opcodes, categories, shapes, colors, and bindings.
- `get_project_info`: returns manifest/default entry and output paths plus workspace source files.

## Smoke Command

```powershell
dotnet run --project src/OpenCTS.App -- samples/hello.sasm artifacts/hello-from-scratchasm.sb3
```

## Alias Artifacts

The checked-in `samples/all-aliases.sasm` file contains a compile-safe use of every registered alias plus a custom-block demonstration. `samples/all-aliases.json` is the deterministic schema-v1 catalog with alias, opcode, category, color, shape, bindings, extension, substacks, terminal policy, legacy status, fixed fields, and the category/syntax palettes.

Regenerate both files directly from `CtsBlockRegistry`:

```powershell
dotnet run --project src/OpenCTS.App -- --emit-aliases samples
```


