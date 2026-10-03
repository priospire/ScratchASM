namespace OpenCTS.Core;

internal static class CtsBlockCatalog
{
    public static readonly string[] ExtensionPrefixes =
    [
        "pen_", "music_", "videoSensing_", "text2speech_", "translate_", "speech2text_",
        "faceSensing_", "makeymakey_", "microbit_", "ev3_", "wedo2_", "gdxfor_", "boost_"
    ];

    public static IReadOnlyList<CtsAliasDefinition> Create()
    {
        List<CtsAliasDefinition> definitions = [];
        AddCore(definitions);
        AddExtensions(definitions);
        return definitions;
    }

    private static void AddCore(List<CtsAliasDefinition> d)
    {
        string c = ScratchCategoryColors.Motion;
        d.AddRange([
            S("motion.move", "motion_movesteps", c, I("STEPS")),
            S("motion.turnright", "motion_turnright", c, I("DEGREES")),
            S("motion.turnleft", "motion_turnleft", c, I("DEGREES")),
            S("motion.goto", "motion_goto", c, M("TO", "motion_goto_menu")),
            S("motion.goto", "motion_gotoxy", c, I("X"), I("Y")),
            S("motion.glide", "motion_glideto", c, I("SECS"), M("TO", "motion_glideto_menu")),
            S("motion.glide", "motion_glidesecstoxy", c, I("SECS"), I("X"), I("Y")),
            S("motion.setdirection", "motion_pointindirection", c, I("DIRECTION")),
            S("motion.pointtowards", "motion_pointtowards", c, M("TOWARDS", "motion_pointtowards_menu")),
            S("motion.changex", "motion_changexby", c, I("DX")),
            S("motion.setx", "motion_setx", c, I("X")),
            S("motion.changey", "motion_changeyby", c, I("DY")),
            S("motion.sety", "motion_sety", c, I("Y")),
            S("motion.ifonedgebounce", "motion_ifonedgebounce", c),
            S("motion.rotationstyle", "motion_setrotationstyle", c, F("STYLE")),
            R("motion.x", "motion_xposition", c),
            R("motion.y", "motion_yposition", c),
            R("motion.direction", "motion_direction", c)
            ,Legacy("legacy.motion.scrollright", "motion_scroll_right", c, I("DISTANCE"))
            ,Legacy("legacy.motion.scrollup", "motion_scroll_up", c, I("DISTANCE"))
            ,Legacy("legacy.motion.align", "motion_align_scene", c, F("ALIGNMENT"))
            ,LegacyReporter("legacy.motion.xscroll", "motion_xscroll", c)
            ,LegacyReporter("legacy.motion.yscroll", "motion_yscroll", c)
        ]);

        c = ScratchCategoryColors.Looks;
        d.AddRange([
            S("looks.say", "looks_say", c, I("MESSAGE")),
            S("looks.say", "looks_sayforsecs", c, I("MESSAGE"), I("SECS")),
            S("looks.think", "looks_think", c, I("MESSAGE")),
            S("looks.think", "looks_thinkforsecs", c, I("MESSAGE"), I("SECS")),
            S("looks.costume", "looks_switchcostumeto", c, M("COSTUME", "looks_costume")),
            S("looks.nextcostume", "looks_nextcostume", c),
            S("looks.backdrop", "looks_switchbackdropto", c, M("BACKDROP", "looks_backdrops")),
            S("looks.backdropwait", "looks_switchbackdroptoandwait", c, M("BACKDROP", "looks_backdrops")),
            S("looks.nextbackdrop", "looks_nextbackdrop", c),
            S("looks.changeeffect", "looks_changeeffectby", c, F("EFFECT"), I("CHANGE")),
            S("looks.seteffect", "looks_seteffectto", c, F("EFFECT"), I("VALUE")),
            S("looks.cleareffects", "looks_cleargraphiceffects", c),
            S("looks.changesize", "looks_changesizeby", c, I("CHANGE")),
            S("looks.setsize", "looks_setsizeto", c, I("SIZE")),
            S("looks.show", "looks_show", c),
            S("looks.hide", "looks_hide", c),
            S("looks.layer", "looks_gotofrontback", c, F("FRONT_BACK")),
            S("looks.movelayers", "looks_goforwardbackwardlayers", c, F("FORWARD_BACKWARD"), I("NUM")),
            R("looks.costumenumbername", "looks_costumenumbername", c, F("NUMBER_NAME")),
            R("looks.backdropnumbername", "looks_backdropnumbername", c, F("NUMBER_NAME")),
            R("looks.size", "looks_size", c),
            Legacy("legacy.looks.hideall", "looks_hideallsprites", c),
            Legacy("legacy.looks.changestretch", "looks_changestretchby", c, I("CHANGE")),
            Legacy("legacy.looks.setstretch", "looks_setstretchto", c, I("STRETCH"))
        ]);

        c = ScratchCategoryColors.Sound;
        d.AddRange([
            S("sound.playuntil", "sound_playuntildone", c, M("SOUND_MENU", "sound_sounds_menu")),
            S("sound.play", "sound_play", c, M("SOUND_MENU", "sound_sounds_menu")),
            S("sound.stopall", "sound_stopallsounds", c),
            S("sound.changeeffect", "sound_changeeffectby", c, F("EFFECT"), I("VALUE")),
            S("sound.seteffect", "sound_seteffectto", c, F("EFFECT"), I("VALUE")),
            S("sound.clear", "sound_cleareffects", c),
            S("sound.changevolume", "sound_changevolumeby", c, I("VOLUME")),
            S("sound.setvolume", "sound_setvolumeto", c, I("VOLUME")),
            R("sound.volume", "sound_volume", c),
            XL("legacy.sound.playdrum", "music_playDrumForBeats", "music", I("DRUM"), I("BEATS")),
            XL("legacy.sound.rest", "music_restForBeats", "music", I("BEATS")),
            XL("legacy.sound.playnote", "music_playNoteForBeats", "music", I("NOTE"), I("BEATS")),
            XL("legacy.sound.instrument", "music_setInstrument", "music", I("INSTRUMENT")),
            XL("legacy.sound.changetempo", "music_changeTempo", "music", I("TEMPO")),
            XL("legacy.sound.settempo", "music_setTempo", "music", I("TEMPO"))
        ]);

        c = ScratchCategoryColors.Events;
        d.AddRange([
            H("event.greenflag", "event_whenflagclicked", c),
            H("event.key", "event_whenkeypressed", c, F("KEY_OPTION")),
            H("event.clicked", "event_whenthisspriteclicked", c),
            H("event.stageclicked", "event_whenstageclicked", c),
            H("event.backdrop", "event_whenbackdropswitchesto", c, F("BACKDROP")),
            H("event.greaterthan", "event_whengreaterthan", c, F("WHENGREATERTHANMENU"), I("VALUE")),
            H("event.received", "event_whenbroadcastreceived", c, F("BROADCAST_OPTION")),
            LegacyHat("legacy.event.touching", "event_whentouchingobject", c, M("TOUCHINGOBJECTMENU", "sensing_touchingobjectmenu")),
            S("event.broadcast", "event_broadcast", c, M("BROADCAST_INPUT", "event_broadcast_menu", "BROADCAST_OPTION")),
            S("event.broadcastwait", "event_broadcastandwait", c, M("BROADCAST_INPUT", "event_broadcast_menu", "BROADCAST_OPTION"))
        ]);

        c = ScratchCategoryColors.Control;
        d.AddRange([
            S("control.wait", "control_wait", c, I("DURATION")),
            C("repeat", "control_repeat", c, ["SUBSTACK"], I("TIMES")),
            C("forever", "control_forever", c, ["SUBSTACK"], CtsTerminalPolicy.AlwaysCaps),
            C("if", "control_if", c, ["SUBSTACK"], I("CONDITION")),
            C("ifelse", "control_if_else", c, ["SUBSTACK", "SUBSTACK2"], I("CONDITION")),
            S("waituntil", "control_wait_until", c, I("CONDITION")),
            C("repeatuntil", "control_repeat_until", c, ["SUBSTACK"], I("CONDITION")),
            new CtsAliasDefinition("control.stop", "control_stop", c, CtsBlockShape.Cap, [F("STOP_OPTION")], TerminalPolicy: CtsTerminalPolicy.CapsUnlessOtherScripts),
            H("control.clone", "control_start_as_clone", c),
            S("control.createclone", "control_create_clone_of", c, M("CLONE_OPTION", "control_create_clone_of_menu")),
            new CtsAliasDefinition("control.deleteclone", "control_delete_this_clone", c, CtsBlockShape.Cap, [], TerminalPolicy: CtsTerminalPolicy.AlwaysCaps),
            C("legacy.control.while", "control_while", c, ["SUBSTACK"], I("CONDITION")) with { IsLegacy = true },
            C("legacy.control.foreach", "control_for_each", c, ["SUBSTACK"], F("VARIABLE"), I("VALUE")) with { IsLegacy = true },
            C("legacy.control.allatonce", "control_all_at_once", c, ["SUBSTACK"]) with { IsLegacy = true },
            LegacyReporter("legacy.control.counter", "control_get_counter", c),
            Legacy("legacy.control.incrcounter", "control_incr_counter", c),
            Legacy("legacy.control.clearcounter", "control_clear_counter", c)
        ]);

        c = ScratchCategoryColors.Sensing;
        d.AddRange([
            B("sensing.touching", "sensing_touchingobject", c, M("TOUCHINGOBJECTMENU", "sensing_touchingobjectmenu")),
            B("sensing.touchingcolor", "sensing_touchingcolor", c, I("COLOR")),
            B("sensing.colorstouching", "sensing_coloristouchingcolor", c, I("COLOR"), I("COLOR2")),
            R("sensing.distance", "sensing_distanceto", c, M("DISTANCETOMENU", "sensing_distancetomenu")),
            S("sensing.ask", "sensing_askandwait", c, I("QUESTION")),
            R("sensing.answer", "sensing_answer", c),
            B("sensing.key", "sensing_keypressed", c, M("KEY_OPTION", "sensing_keyoptions")),
            B("sensing.mousedown", "sensing_mousedown", c),
            R("sensing.mousex", "sensing_mousex", c),
            R("sensing.mousey", "sensing_mousey", c),
            S("sensing.dragmode", "sensing_setdragmode", c, F("DRAG_MODE")),
            R("sensing.loudness", "sensing_loudness", c),
            R("sensing.timer", "sensing_timer", c),
            S("sensing.resettimer", "sensing_resettimer", c),
            R("sensing.of", "sensing_of", c, F("PROPERTY"), M("OBJECT", "sensing_of_object_menu")),
            R("sensing.current", "sensing_current", c, F("CURRENTMENU")),
            R("sensing.dayssince2000", "sensing_dayssince2000", c),
            R("sensing.username", "sensing_username", c),
            B("sensing.online", "sensing_online", c),
            new CtsAliasDefinition("legacy.sensing.loud", "sensing_loud", c, CtsBlockShape.Boolean, [], IsLegacy: true),
            LegacyReporter("legacy.sensing.userid", "sensing_userid", c)
        ]);

        c = ScratchCategoryColors.Operators;
        d.AddRange([
            R("add", "operator_add", c, I("NUM1"), I("NUM2")),
            R("subtract", "operator_subtract", c, I("NUM1"), I("NUM2")),
            R("multiply", "operator_multiply", c, I("NUM1"), I("NUM2")),
            R("divide", "operator_divide", c, I("NUM1"), I("NUM2")),
            R("random", "operator_random", c, I("FROM"), I("TO")),
            B("greater", "operator_gt", c, I("OPERAND1"), I("OPERAND2")),
            B("less", "operator_lt", c, I("OPERAND1"), I("OPERAND2")),
            B("equals", "operator_equals", c, I("OPERAND1"), I("OPERAND2")),
            B("and", "operator_and", c, I("OPERAND1"), I("OPERAND2")),
            B("or", "operator_or", c, I("OPERAND1"), I("OPERAND2")),
            B("not", "operator_not", c, I("OPERAND")),
            R("join", "operator_join", c, I("STRING1"), I("STRING2")),
            R("letter", "operator_letter_of", c, I("LETTER"), I("STRING")),
            R("length", "operator_length", c, I("STRING")),
            B("contains", "operator_contains", c, I("STRING1"), I("STRING2")),
            R("mod", "operator_mod", c, I("NUM1"), I("NUM2")),
            R("round", "operator_round", c, I("NUM")),
            R("mathop", "operator_mathop", c, F("OPERATOR"), I("NUM")),
            Math("abs", "abs"), Math("floor", "floor"), Math("ceil", "ceiling"),
            Math("sqrt", "sqrt"), Math("sin", "sin"), Math("cos", "cos"), Math("tan", "tan"),
            Math("asin", "asin"), Math("acos", "acos"), Math("atan", "atan"), Math("ln", "ln"),
            Math("log10", "log"), Math("exp", "e ^"), Math("pow10", "10 ^")
        ]);

        c = ScratchCategoryColors.Variables;
        d.AddRange([
            S("data.set", "data_setvariableto", c, F("VARIABLE"), I("VALUE")),
            S("data.change", "data_changevariableby", c, F("VARIABLE"), I("VALUE")),
            S("data.show", "data_showvariable", c, F("VARIABLE")),
            S("data.hide", "data_hidevariable", c, F("VARIABLE")),
            R("data.value", "data_variable", c, F("VARIABLE"))
        ]);

        c = ScratchCategoryColors.Lists;
        d.AddRange([
            S("list.add", "data_addtolist", c, I("ITEM"), F("LIST")),
            S("list.delete", "data_deleteoflist", c, I("INDEX"), F("LIST")),
            S("list.deleteall", "data_deletealloflist", c, F("LIST")),
            S("list.insert", "data_insertatlist", c, I("INDEX"), I("ITEM"), F("LIST")),
            S("list.replace", "data_replaceitemoflist", c, I("INDEX"), I("ITEM"), F("LIST")),
            R("list.item", "data_itemoflist", c, I("INDEX"), F("LIST")),
            R("list.index", "data_itemnumoflist", c, I("ITEM"), F("LIST")),
            R("list.length", "data_lengthoflist", c, F("LIST")),
            B("list.contains", "data_listcontainsitem", c, I("ITEM"), F("LIST")),
            S("list.show", "data_showlist", c, F("LIST")),
            S("list.hide", "data_hidelist", c, F("LIST")),
            R("list.contents", "data_listcontents", c, F("LIST"))
        ]);
    }

    private static void AddExtensions(List<CtsAliasDefinition> d)
    {
        AddPen(d);
        AddMusic(d);
        AddSimpleExtensions(d);
        AddHardwareExtensions(d);
    }

    private static void AddPen(List<CtsAliasDefinition> d)
    {
        d.AddRange([
            X("pen.clear", "pen_clear", "pen"), X("pen.stamp", "pen_stamp", "pen"),
            X("pen.down", "pen_penDown", "pen"), X("pen.up", "pen_penUp", "pen"),
            X("pen.color", "pen_setPenColorToColor", "pen", I("COLOR")),
            X("pen.changecolor", "pen_changePenColorParamBy", "pen", XM("COLOR_PARAM", "pen"), I("VALUE")),
            X("pen.setcolor", "pen_setPenColorParamTo", "pen", XM("COLOR_PARAM", "pen"), I("VALUE")),
            X("pen.changesize", "pen_changePenSizeBy", "pen", I("SIZE")),
            X("pen.setsize", "pen_setPenSizeTo", "pen", I("SIZE")),
            XL("legacy.pen.setshade", "pen_setPenShadeToNumber", "pen", I("SHADE")),
            XL("legacy.pen.changeshade", "pen_changePenShadeBy", "pen", I("SHADE")),
            XL("legacy.pen.sethue", "pen_setPenHueToNumber", "pen", I("HUE")),
            XL("legacy.pen.changehue", "pen_changePenHueBy", "pen", I("HUE"))
        ]);
    }

    private static void AddMusic(List<CtsAliasDefinition> d)
    {
        d.AddRange([
            X("music.playdrum", "music_playDrumForBeats", "music", XM("DRUM", "music"), I("BEATS")),
            X("music.rest", "music_restForBeats", "music", I("BEATS")),
            X("music.playnote", "music_playNoteForBeats", "music", I("NOTE"), I("BEATS")),
            X("music.instrument", "music_setInstrument", "music", XM("INSTRUMENT", "music")),
            X("music.settempo", "music_setTempo", "music", I("TEMPO")),
            X("music.changetempo", "music_changeTempo", "music", I("TEMPO")),
            XR("music.tempo", "music_getTempo", "music"),
            XL("legacy.music.mididrum", "music_midiPlayDrumForBeats", "music", I("DRUM"), I("BEATS")),
            XL("legacy.music.midiinstrument", "music_midiSetInstrument", "music", I("INSTRUMENT"))
        ]);
    }

    private static void AddSimpleExtensions(List<CtsAliasDefinition> d)
    {
        d.AddRange([
            XH("video.motiongreater", "videoSensing_whenMotionGreaterThan", "videoSensing", I("REFERENCE")),
            XR("video.on", "videoSensing_videoOn", "videoSensing", XM("ATTRIBUTE", "videoSensing"), XM("SUBJECT", "videoSensing")),
            X("video.setstate", "videoSensing_videoToggle", "videoSensing", XM("VIDEO_STATE", "videoSensing")),
            X("video.transparency", "videoSensing_setVideoTransparency", "videoSensing", I("TRANSPARENCY")),
            X("text2speech.speak", "text2speech_speakAndWait", "text2speech", I("WORDS")),
            X("text2speech.voice", "text2speech_setVoice", "text2speech", XM("VOICE", "text2speech")),
            X("text2speech.language", "text2speech_setLanguage", "text2speech", XM("LANGUAGE", "text2speech")),
            XR("translate.translate", "translate_getTranslate", "translate", I("WORDS"), XM("LANGUAGE", "translate")),
            XR("translate.language", "translate_getViewerLanguage", "translate"),
            X("speech2text.listen", "speech2text_listenAndWait", "speech2text"),
            XH("speech2text.hear", "speech2text_whenIHearHat", "speech2text", I("PHRASE")),
            XR("speech2text.speech", "speech2text_getSpeech", "speech2text"),
            X("faceSensing.goto", "faceSensing_goToPart", "faceSensing", XM("PART", "faceSensing")),
            X("faceSensing.point", "faceSensing_pointInFaceTiltDirection", "faceSensing"),
            X("faceSensing.setsize", "faceSensing_setSizeToFaceSize", "faceSensing"),
            XH("faceSensing.whentilted", "faceSensing_whenTilted", "faceSensing", XM("DIRECTION", "faceSensing")),
            XH("faceSensing.whentouching", "faceSensing_whenSpriteTouchesPart", "faceSensing", XM("PART", "faceSensing")),
            XH("faceSensing.whendetected", "faceSensing_whenFaceDetected", "faceSensing"),
            XB("faceSensing.detected", "faceSensing_faceIsDetected", "faceSensing"),
            XR("faceSensing.tilt", "faceSensing_faceTilt", "faceSensing"),
            XR("faceSensing.size", "faceSensing_faceSize", "faceSensing"),
            XH("makeymakey.key", "makeymakey_whenMakeyKeyPressed", "makeymakey", XM("KEY", "makeymakey")),
            XH("makeymakey.code", "makeymakey_whenCodePressed", "makeymakey", XM("SEQUENCE", "makeymakey"))
        ]);
    }

    private static void AddHardwareExtensions(List<CtsAliasDefinition> d)
    {
        d.AddRange([
            XH("microbit.button", "microbit_whenButtonPressed", "microbit", XM("BTN", "microbit")),
            XB("microbit.pressed", "microbit_isButtonPressed", "microbit", XM("BTN", "microbit")),
            XH("microbit.gesture", "microbit_whenGesture", "microbit", XM("GESTURE", "microbit")),
            X("microbit.display", "microbit_displaySymbol", "microbit", I("MATRIX")),
            X("microbit.text", "microbit_displayText", "microbit", I("TEXT")),
            X("microbit.clear", "microbit_displayClear", "microbit"),
            XH("microbit.whentilted", "microbit_whenTilted", "microbit", XM("DIRECTION", "microbit")),
            XB("microbit.tilted", "microbit_isTilted", "microbit", XM("DIRECTION", "microbit")),
            XR("microbit.tilt", "microbit_getTiltAngle", "microbit", XM("DIRECTION", "microbit")),
            XH("microbit.pin", "microbit_whenPinConnected", "microbit", XM("PIN", "microbit")),

            X("ev3.motor", "ev3_motorTurnClockwise", "ev3", XM("PORT", "ev3"), I("TIME")),
            X("ev3.motorback", "ev3_motorTurnCounterClockwise", "ev3", XM("PORT", "ev3"), I("TIME")),
            X("ev3.power", "ev3_motorSetPower", "ev3", XM("PORT", "ev3"), I("POWER")),
            XR("ev3.position", "ev3_getMotorPosition", "ev3", XM("PORT", "ev3")),
            XH("ev3.button", "ev3_whenButtonPressed", "ev3", XM("PORT", "ev3")),
            XH("ev3.distancebelow", "ev3_whenDistanceLessThan", "ev3", I("DISTANCE")),
            XH("ev3.brightnessbelow", "ev3_whenBrightnessLessThan", "ev3", I("DISTANCE")),
            XB("ev3.pressed", "ev3_buttonPressed", "ev3", XM("PORT", "ev3")),
            XR("ev3.distance", "ev3_getDistance", "ev3"), XR("ev3.brightness", "ev3_getBrightness", "ev3"),
            X("ev3.beep", "ev3_beep", "ev3", I("NOTE"), I("TIME")),

            XH("gdxfor.gesture", "gdxfor_whenGesture", "gdxfor", XM("GESTURE", "gdxfor")),
            XH("gdxfor.forceevent", "gdxfor_whenForcePushedOrPulled", "gdxfor", XM("PUSH_PULL", "gdxfor")),
            XR("gdxfor.force", "gdxfor_getForce", "gdxfor"),
            XH("gdxfor.whentilted", "gdxfor_whenTilted", "gdxfor", XM("TILT", "gdxfor")),
            XB("gdxfor.tilted", "gdxfor_isTilted", "gdxfor", XM("TILT", "gdxfor")),
            XR("gdxfor.tilt", "gdxfor_getTilt", "gdxfor", XM("TILT", "gdxfor")),
            XB("gdxfor.falling", "gdxfor_isFreeFalling", "gdxfor"),
            XR("gdxfor.spin", "gdxfor_getSpinSpeed", "gdxfor", XM("DIRECTION", "gdxfor")),
            XR("gdxfor.acceleration", "gdxfor_getAcceleration", "gdxfor", XM("DIRECTION", "gdxfor")),

            X("wedo2.motorfor", "wedo2_motorOnFor", "wedo2", XM("MOTOR_ID", "wedo2"), I("DURATION")),
            X("wedo2.motoron", "wedo2_motorOn", "wedo2", XM("MOTOR_ID", "wedo2")),
            X("wedo2.motoroff", "wedo2_motorOff", "wedo2", XM("MOTOR_ID", "wedo2")),
            X("wedo2.power", "wedo2_startMotorPower", "wedo2", XM("MOTOR_ID", "wedo2"), I("POWER")),
            X("wedo2.direction", "wedo2_setMotorDirection", "wedo2", XM("MOTOR_ID", "wedo2"), XM("MOTOR_DIRECTION", "wedo2")),
            X("wedo2.light", "wedo2_setLightHue", "wedo2", I("HUE")),
            XL("legacy.wedo2.playnote", "wedo2_playNoteFor", "wedo2", I("NOTE"), I("DURATION")),
            XH("wedo2.distanceevent", "wedo2_whenDistance", "wedo2", XM("OP", "wedo2"), I("REFERENCE")),
            XH("wedo2.whentilted", "wedo2_whenTilted", "wedo2", XM("TILT_DIRECTION_ANY", "wedo2")),
            XR("wedo2.distance", "wedo2_getDistance", "wedo2"),
            XB("wedo2.tilted", "wedo2_isTilted", "wedo2", XM("TILT_DIRECTION_ANY", "wedo2")),
            XR("wedo2.tilt", "wedo2_getTiltAngle", "wedo2", XM("TILT_DIRECTION", "wedo2")),

            X("boost.motorfor", "boost_motorOnFor", "boost", XM("MOTOR_ID", "boost"), I("DURATION")),
            X("boost.rotations", "boost_motorOnForRotation", "boost", XM("MOTOR_ID", "boost"), I("ROTATION")),
            X("boost.motoron", "boost_motorOn", "boost", XM("MOTOR_ID", "boost")),
            X("boost.motoroff", "boost_motorOff", "boost", XM("MOTOR_ID", "boost")),
            X("boost.power", "boost_setMotorPower", "boost", XM("MOTOR_ID", "boost"), I("POWER")),
            X("boost.direction", "boost_setMotorDirection", "boost", XM("MOTOR_ID", "boost"), XM("MOTOR_DIRECTION", "boost")),
            XR("boost.position", "boost_getMotorPosition", "boost", XM("MOTOR_REPORTER_ID", "boost")),
            XH("boost.color", "boost_whenColor", "boost", XM("COLOR", "boost")),
            XB("boost.seeing", "boost_seeingColor", "boost", XM("COLOR", "boost")),
            XH("boost.whentilted", "boost_whenTilted", "boost", XM("TILT_DIRECTION_ANY", "boost")),
            XR("boost.tilt", "boost_getTiltAngle", "boost", XM("TILT_DIRECTION", "boost")),
            X("boost.light", "boost_setLightHue", "boost", I("HUE"))
        ]);
    }

    private static CtsArgumentBinding I(string name) => new(name, CtsBindingKind.Input);
    private static CtsArgumentBinding F(string name) => new(name, CtsBindingKind.Field);
    private static CtsArgumentBinding M(string name, string menuOpcode, string? menuField = null) =>
        new(name, CtsBindingKind.Menu, menuOpcode, menuField ?? name);
    private static CtsArgumentBinding XM(string name, string extension) => F(name);

    private static CtsAliasDefinition S(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Stack, args);
    private static CtsAliasDefinition R(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Reporter, args);
    private static CtsAliasDefinition B(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Boolean, args);
    private static CtsAliasDefinition H(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Hat, args);
    private static CtsAliasDefinition C(string name, string opcode, string color, IReadOnlyList<string> substacks, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.CBlock, args, Substacks: substacks);
    private static CtsAliasDefinition C(string name, string opcode, string color, IReadOnlyList<string> substacks, CtsTerminalPolicy terminal, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.CBlock, args, Substacks: substacks, TerminalPolicy: terminal);
    private static CtsAliasDefinition Legacy(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Stack, args, IsLegacy: true);
    private static CtsAliasDefinition LegacyReporter(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Reporter, args, IsLegacy: true);
    private static CtsAliasDefinition LegacyHat(string name, string opcode, string color, params CtsArgumentBinding[] args) =>
        new(name, opcode, color, CtsBlockShape.Hat, args, IsLegacy: true);
    private static CtsAliasDefinition Math(string name, string operation) =>
        new(
            name,
            "operator_mathop",
            ScratchCategoryColors.Operators,
            CtsBlockShape.Reporter,
            [I("NUM")],
            FixedFields: new Dictionary<string, string>(StringComparer.Ordinal) { ["OPERATOR"] = operation });
    private static CtsAliasDefinition X(string name, string opcode, string extension, params CtsArgumentBinding[] args) =>
        new(name, opcode, ScratchCategoryColors.GetExtensionColor(extension), CtsBlockShape.Stack, args, extension);
    private static CtsAliasDefinition XR(string name, string opcode, string extension, params CtsArgumentBinding[] args) =>
        new(name, opcode, ScratchCategoryColors.GetExtensionColor(extension), CtsBlockShape.Reporter, args, extension);
    private static CtsAliasDefinition XB(string name, string opcode, string extension, params CtsArgumentBinding[] args) =>
        new(name, opcode, ScratchCategoryColors.GetExtensionColor(extension), CtsBlockShape.Boolean, args, extension);
    private static CtsAliasDefinition XH(string name, string opcode, string extension, params CtsArgumentBinding[] args) =>
        new(name, opcode, ScratchCategoryColors.GetExtensionColor(extension), CtsBlockShape.Hat, args, extension);
    private static CtsAliasDefinition XL(string name, string opcode, string extension, params CtsArgumentBinding[] args) =>
        new(name, opcode, ScratchCategoryColors.GetExtensionColor(extension), CtsBlockShape.Stack, args, extension, IsLegacy: true);
}
