using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class CtsLanguageTests
{
    private static readonly string[] RequiredCoreOpcodes =
    [
        "motion_movesteps", "motion_turnright", "motion_turnleft", "motion_goto", "motion_gotoxy",
        "motion_glideto", "motion_glidesecstoxy", "motion_pointindirection", "motion_pointtowards",
        "motion_changexby", "motion_setx", "motion_changeyby", "motion_sety", "motion_ifonedgebounce",
        "motion_setrotationstyle", "motion_xposition", "motion_yposition", "motion_direction",
        "motion_scroll_right", "motion_scroll_up", "motion_align_scene", "motion_xscroll", "motion_yscroll",
        "looks_sayforsecs", "looks_say", "looks_thinkforsecs", "looks_think", "looks_switchcostumeto",
        "looks_nextcostume", "looks_switchbackdropto", "looks_switchbackdroptoandwait", "looks_nextbackdrop",
        "looks_changeeffectby", "looks_seteffectto", "looks_cleargraphiceffects", "looks_changesizeby",
        "looks_setsizeto", "looks_show", "looks_hide", "looks_gotofrontback", "looks_goforwardbackwardlayers",
        "looks_costumenumbername", "looks_backdropnumbername", "looks_size",
        "looks_hideallsprites", "looks_changestretchby", "looks_setstretchto",
        "sound_playuntildone", "sound_play", "sound_stopallsounds", "sound_changeeffectby", "sound_seteffectto",
        "sound_cleareffects", "sound_changevolumeby", "sound_setvolumeto", "sound_volume",
        "event_whenflagclicked", "event_whenkeypressed", "event_whenthisspriteclicked", "event_whenstageclicked",
        "event_whenbackdropswitchesto", "event_whengreaterthan", "event_whenbroadcastreceived", "event_broadcast",
        "event_broadcastandwait", "event_whentouchingobject", "control_wait", "control_repeat", "control_forever", "control_if",
        "control_if_else", "control_wait_until", "control_repeat_until", "control_stop", "control_start_as_clone",
        "control_create_clone_of", "control_delete_this_clone", "sensing_touchingobject", "sensing_touchingcolor",
        "control_while", "control_for_each", "control_all_at_once", "control_get_counter", "control_incr_counter", "control_clear_counter",
        "sensing_coloristouchingcolor", "sensing_distanceto", "sensing_askandwait", "sensing_answer",
        "sensing_keypressed", "sensing_mousedown", "sensing_mousex", "sensing_mousey", "sensing_setdragmode",
        "sensing_loudness", "sensing_timer", "sensing_resettimer", "sensing_of", "sensing_current",
        "sensing_dayssince2000", "sensing_username", "sensing_loud", "sensing_online", "sensing_userid",
        "operator_add", "operator_subtract", "operator_multiply",
        "operator_divide", "operator_random", "operator_gt", "operator_lt", "operator_equals", "operator_and",
        "operator_or", "operator_not", "operator_join", "operator_letter_of", "operator_length",
        "operator_contains", "operator_mod", "operator_round", "operator_mathop", "data_setvariableto",
        "data_changevariableby", "data_showvariable", "data_hidevariable", "data_variable", "data_addtolist",
        "data_deleteoflist", "data_deletealloflist", "data_insertatlist", "data_replaceitemoflist",
        "data_itemoflist", "data_itemnumoflist", "data_lengthoflist", "data_listcontainsitem",
        "data_showlist", "data_hidelist", "data_listcontents"
    ];

    private static readonly string[] RequiredExtensionOpcodes =
    [
        "pen_clear", "pen_stamp", "pen_penDown", "pen_penUp", "pen_setPenColorToColor",
        "pen_changePenColorParamBy", "pen_setPenColorParamTo", "pen_changePenSizeBy", "pen_setPenSizeTo",
        "pen_setPenShadeToNumber", "pen_changePenShadeBy", "pen_setPenHueToNumber", "pen_changePenHueBy",
        "music_playDrumForBeats", "music_midiPlayDrumForBeats", "music_restForBeats", "music_playNoteForBeats",
        "music_setInstrument", "music_midiSetInstrument", "music_setTempo", "music_changeTempo", "music_getTempo",
        "videoSensing_whenMotionGreaterThan", "videoSensing_videoOn", "videoSensing_videoToggle",
        "videoSensing_setVideoTransparency", "text2speech_speakAndWait", "text2speech_setVoice",
        "text2speech_setLanguage", "translate_getTranslate", "translate_getViewerLanguage",
        "speech2text_listenAndWait", "speech2text_whenIHearHat", "speech2text_getSpeech",
        "faceSensing_goToPart", "faceSensing_pointInFaceTiltDirection", "faceSensing_setSizeToFaceSize",
        "faceSensing_whenTilted", "faceSensing_whenSpriteTouchesPart", "faceSensing_whenFaceDetected",
        "faceSensing_faceIsDetected", "faceSensing_faceTilt", "faceSensing_faceSize",
        "makeymakey_whenMakeyKeyPressed", "makeymakey_whenCodePressed",
        "microbit_whenButtonPressed", "microbit_isButtonPressed", "microbit_whenGesture", "microbit_displaySymbol",
        "microbit_displayText", "microbit_displayClear", "microbit_whenTilted", "microbit_isTilted",
        "microbit_getTiltAngle", "microbit_whenPinConnected", "ev3_motorTurnClockwise",
        "ev3_motorTurnCounterClockwise", "ev3_motorSetPower", "ev3_getMotorPosition", "ev3_whenButtonPressed",
        "ev3_whenDistanceLessThan", "ev3_whenBrightnessLessThan", "ev3_buttonPressed", "ev3_getDistance",
        "ev3_getBrightness", "ev3_beep", "gdxfor_whenGesture", "gdxfor_whenForcePushedOrPulled",
        "gdxfor_getForce", "gdxfor_whenTilted", "gdxfor_isTilted", "gdxfor_getTilt",
        "gdxfor_isFreeFalling", "gdxfor_getSpinSpeed", "gdxfor_getAcceleration", "wedo2_motorOnFor",
        "wedo2_motorOn", "wedo2_motorOff", "wedo2_startMotorPower", "wedo2_setMotorDirection",
        "wedo2_setLightHue", "wedo2_playNoteFor", "wedo2_whenDistance", "wedo2_whenTilted",
        "wedo2_getDistance", "wedo2_isTilted", "wedo2_getTiltAngle", "boost_motorOnFor",
        "boost_motorOnForRotation", "boost_motorOn", "boost_motorOff", "boost_setMotorPower",
        "boost_setMotorDirection", "boost_getMotorPosition", "boost_whenColor", "boost_seeingColor",
        "boost_whenTilted", "boost_getTiltAngle", "boost_setLightHue"
    ];

    [TestMethod]
    public void RegistryCoversEveryCoreOpcodeAndHasUniqueAliasSignatures()
    {
        IReadOnlyList<CtsAliasDefinition> definitions = CtsBlockRegistry.Definitions;
        string[] duplicates = definitions
            .GroupBy(definition => $"{definition.Name}/{definition.ArgumentCount}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.HasCount(0, duplicates, "Duplicate alias signatures: " + string.Join(", ", duplicates));

        HashSet<string> opcodes = definitions.Select(definition => definition.Opcode).ToHashSet(StringComparer.Ordinal);
        string[] missingCore = RequiredCoreOpcodes.Where(opcode => !opcodes.Contains(opcode)).ToArray();
        Assert.HasCount(0, missingCore, "Missing core opcodes: " + string.Join(", ", missingCore));
        string[] missingExtensions = RequiredExtensionOpcodes.Where(opcode => !opcodes.Contains(opcode)).ToArray();
        Assert.HasCount(0, missingExtensions, "Missing extension opcodes: " + string.Join(", ", missingExtensions));
        HashSet<string> expectedOpcodes = RequiredCoreOpcodes.Concat(RequiredExtensionOpcodes).ToHashSet(StringComparer.Ordinal);
        string[] unexpected = opcodes.Where(opcode => !expectedOpcodes.Contains(opcode)).Order(StringComparer.Ordinal).ToArray();
        Assert.HasCount(0, unexpected, "Unpinned catalog opcodes: " + string.Join(", ", unexpected));

        foreach (CtsAliasDefinition definition in definitions)
        {
            Assert.AreEqual(definition.ArgumentCount, definition.Bindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count(), definition.Name);
            if (definition.Shape == CtsBlockShape.CBlock)
            {
                Assert.IsGreaterThan(0, definition.SubstackNames.Count, definition.Name);
            }

            foreach (CtsArgumentBinding menu in definition.Bindings.Where(binding => binding.Kind == CtsBindingKind.Menu))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(menu.MenuOpcode), definition.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(menu.MenuField), definition.Name);
            }
        }
    }

    [TestMethod]
    public void NearTopDocumentationTableCoversEveryCatalogDefinition()
    {
        string documentationPath = FindRepositoryFile(Path.Combine("docs", "scratchasm.md"));
        string documentation = File.ReadAllText(documentationPath);
        int aliasHeading = documentation.IndexOf("## Complete Alias Set", StringComparison.Ordinal);
        int projectHeading = documentation.IndexOf("## Project Shape", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, aliasHeading, "Missing complete alias heading.");
        Assert.IsGreaterThan(aliasHeading, projectHeading, "Complete aliases must appear before project details.");

        foreach (CtsAliasDefinition definition in CtsBlockRegistry.Definitions)
        {
            string expected = $"| `{definition.Name}` | `{definition.Opcode}` |";
            StringAssert.Contains(documentation, expected, definition.Name);
        }
    }

    [TestMethod]
    public void EveryCatalogAliasCompilesToItsDeclaredOpcode()
    {
        foreach (CtsAliasDefinition definition in CtsBlockRegistry.Definitions)
        {
            string source = CreateAliasSmokeSource(definition);
            CtsCompileResult result = CtsCompiler.Compile(source);
            AssertNoErrors(result.Diagnostics, definition.Name + Environment.NewLine + source);
            using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
            _ = FindBlockByOpcode(GetStageBlocks(document), definition.Opcode);
            if (definition.ExtensionId is not null)
            {
                Assert.IsTrue(
                    document.RootElement.GetProperty("extensions").EnumerateArray()
                        .Any(extension => extension.GetString() == definition.ExtensionId),
                    definition.Name);
            }
        }
    }

    [TestMethod]
    public void CompilesStructuredControlDataAndExpressionPrecedence()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var score = 0
  list items = []

  @greenflag:
    score = 5
    score += 3
    items.add "coin"
    items.insert 1 "key"
    items.replace 1 "gem"
    repeat 4:
      score = score + 2 * 3
    if score > 10 and not items.contains("missing"):
      looks.say join("score: ", score)
    else:
      items.delete 1
    forever:
      items.delete_all
    looks.say "unreachable"
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        _ = FindBlockByOpcode(blocks, "data_setvariableto");
        _ = FindBlockByOpcode(blocks, "data_changevariableby");
        _ = FindBlockByOpcode(blocks, "data_addtolist");
        _ = FindBlockByOpcode(blocks, "data_insertatlist");
        _ = FindBlockByOpcode(blocks, "data_replaceitemoflist");
        _ = FindBlockByOpcode(blocks, "control_repeat");
        _ = FindBlockByOpcode(blocks, "control_if_else");
        JsonElement forever = FindBlockByOpcode(blocks, "control_forever");
        Assert.AreEqual(JsonValueKind.Null, forever.GetProperty("next").ValueKind);
        _ = FindBlockByOpcode(blocks, "operator_and");
        _ = FindBlockByOpcode(blocks, "operator_not");
        JsonElement add = FindBlockByOpcode(blocks, "operator_add");
        string multiplyId = add.GetProperty("inputs").GetProperty("NUM2")[1].GetString()!;
        Assert.AreEqual("operator_multiply", blocks.GetProperty(multiplyId).GetProperty("opcode").GetString());
    }

    [TestMethod]
    public void CompilesAllScratchMathFunctionsAndLiteralPower()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var value = 0
  @greenflag:
    value = random(1, 10) + round(2.4)
    value = abs(-2) + floor(2.9) + ceil(2.1) + sqrt(9)
    value = sin(30) + cos(30) + tan(45)
    value = asin(0.5) + acos(0.5) + atan(1)
    value = ln(2) + log10(100) + exp(2) + pow10(2)
    value = 3 ^ 4
    value = 2 ^ -3
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        _ = FindBlockByOpcode(blocks, "operator_random");
        _ = FindBlockByOpcode(blocks, "operator_round");
        string[] operations = blocks.EnumerateObject()
            .Where(block => block.Value.GetProperty("opcode").GetString() == "operator_mathop")
            .Select(block => block.Value.GetProperty("fields").GetProperty("OPERATOR")[0].GetString()!)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "abs", "floor", "ceiling", "sqrt", "sin", "cos", "tan", "asin", "acos", "atan", "ln", "log", "e ^", "10 ^" },
            operations.Distinct().ToArray());
        Assert.IsGreaterThanOrEqualTo(4, blocks.EnumerateObject().Count(block => block.Value.GetProperty("opcode").GetString() == "operator_multiply"));
        _ = FindBlockByOpcode(blocks, "operator_divide");
    }

    [TestMethod]
    public void DynamicOrFractionalPowerReportsFocusedError()
    {
        CtsCompileResult dynamicResult = CtsCompiler.Compile("""
stage {
  var x = 2
  @greenflag:
    x = x ^ x
}
""");
        CtsDiagnostic dynamicDiagnostic = dynamicResult.Diagnostics.Single(diagnostic => diagnostic.Code == "CTS1020");
        Assert.AreEqual(4, dynamicDiagnostic.Span.Start.Line);
        Assert.AreEqual(13, dynamicDiagnostic.Span.Start.Column);

        CtsCompileResult fractionalResult = CtsCompiler.Compile("""
stage {
  var x = 2
  @greenflag:
    x = x ^ 1.5
}
""");
        Assert.IsTrue(fractionalResult.Diagnostics.Any(diagnostic => diagnostic.Code == "CTS1020"));
    }

    [TestMethod]
    public void CompilesDirectExponentMathopForms()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var value = 0
  @greenflag:
    value = e^(2) + 10^(3)
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        string[] operations = GetStageBlocks(document).EnumerateObject()
            .Where(block => block.Value.GetProperty("opcode").GetString() == "operator_mathop")
            .Select(block => block.Value.GetProperty("fields").GetProperty("OPERATOR")[0].GetString()!)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { "e ^", "10 ^" }, operations);
    }

    [TestMethod]
    public void CompilesReliableComparisonQoLOperators()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    if 1 <= 2 and 3 >= 2 and 1 != 2:
      looks.show
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        Assert.AreEqual(3, blocks.EnumerateObject().Count(block => block.Value.GetProperty("opcode").GetString() == "operator_not"));
        _ = FindBlockByOpcode(blocks, "operator_gt");
        _ = FindBlockByOpcode(blocks, "operator_lt");
        _ = FindBlockByOpcode(blocks, "operator_equals");
    }

    [TestMethod]
    public void LiteralPowerRejectsNondeterministicBase()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var value = 0
  @greenflag:
    value = random(1, 10) ^ 2
}
""");

        CtsDiagnostic diagnostic = result.Diagnostics.Single(item => item.Code == "CTS1020");
        StringAssert.Contains(diagnostic.Message, "stable");
        Assert.AreEqual(4, diagnostic.Span.Start.Line);
    }

    [TestMethod]
    public void TerminalBlocksNeverLinkToFollowingStatements()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    control.stop "other scripts in sprite"
    looks.show

  @greenflag:
    control.stop "all"
    looks.hide

  @greenflag:
    control.deleteclone
    looks.say "unreachable"
}
""");

        AssertNoErrors(result.Diagnostics);
        Assert.HasCount(2, result.Diagnostics.Where(diagnostic => diagnostic.Code == "CTS2002").ToArray());
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        JsonElement[] stops = blocks.EnumerateObject()
            .Where(block => block.Value.GetProperty("opcode").GetString() == "control_stop")
            .Select(block => block.Value)
            .ToArray();
        JsonElement continuingStop = stops.Single(stop =>
            stop.GetProperty("fields").GetProperty("STOP_OPTION")[0].GetString() == "other scripts in sprite");
        Assert.AreEqual(JsonValueKind.String, continuingStop.GetProperty("next").ValueKind);
        JsonElement cappedStop = stops.Single(stop =>
            stop.GetProperty("fields").GetProperty("STOP_OPTION")[0].GetString() == "all");
        Assert.AreEqual(JsonValueKind.Null, cappedStop.GetProperty("next").ValueKind);
        Assert.AreEqual(
            JsonValueKind.Null,
            FindBlockByOpcode(blocks, "control_delete_this_clone").GetProperty("next").ValueKind);
    }

    [TestMethod]
    public void ExtensionAliasesAutoRegisterTheirScratchExtensions()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var translated = ""
  @greenflag:
    pen.down
    music.playdrum 1 0.25
    text2speech.speak "hello"
    translated = translate.translate("hello", "es")
    video.setstate "on"
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        string[] extensions = document.RootElement.GetProperty("extensions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        CollectionAssert.IsSubsetOf(new[] { "pen", "music", "text2speech", "translate", "videoSensing" }, extensions);
    }

    [TestMethod]
    public void ExtensionDropdownsCompileAsScratchFields()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    music.playdrum 1 0.25
    pen.changecolor "color" 10
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);

        JsonElement drum = FindBlockByOpcode(blocks, "music_playDrumForBeats");
        Assert.IsTrue(drum.GetProperty("fields").TryGetProperty("DRUM", out _));
        Assert.IsFalse(drum.GetProperty("inputs").TryGetProperty("DRUM", out _));

        JsonElement pen = FindBlockByOpcode(blocks, "pen_changePenColorParamBy");
        Assert.IsTrue(pen.GetProperty("fields").TryGetProperty("COLOR_PARAM", out _));
        Assert.IsFalse(pen.GetProperty("inputs").TryGetProperty("COLOR_PARAM", out _));
        Assert.IsFalse(blocks.EnumerateObject().Any(block =>
            block.Value.GetProperty("opcode").GetString() is string opcode &&
            (opcode.StartsWith("music_menu_", StringComparison.Ordinal) ||
             opcode.StartsWith("pen_menu_", StringComparison.Ordinal))));
    }

    [TestMethod]
    public void ParserReportsSyntaxErrorLineAndColumn()
    {
        CtsParseResult result = CtsParser.Parse("""
stage {
  @greenflag
    motion.move 10
}
""");

        CtsDiagnostic diagnostic = AssertSingleDiagnostic(result.Diagnostics);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("CTS1001", diagnostic.Code);
        Assert.AreEqual(2, diagnostic.Span.Start.Line);
        Assert.AreEqual(13, diagnostic.Span.Start.Column);
    }

    [TestMethod]
    public void ParserReportsMissingTargetCloseBrace()
    {
        CtsParseResult result = CtsParser.Parse("""
stage {
  @greenflag:
    motion.move 10
""");

        CtsDiagnostic diagnostic = AssertSingleDiagnostic(result.Diagnostics);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.Message, "}");
        Assert.AreEqual(3, diagnostic.Span.Start.Line);
    }

    [TestMethod]
    public void CompilesSimpleGreenFlagStack()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    motion.move 10
    looks.say "Hello" 2s
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);

        JsonElement hat = FindBlockByOpcode(blocks, "event_whenflagclicked");
        JsonElement move = blocks.GetProperty(hat.GetProperty("next").GetString()!);
        Assert.AreEqual("motion_movesteps", move.GetProperty("opcode").GetString());

        JsonElement say = blocks.GetProperty(move.GetProperty("next").GetString()!);
        Assert.AreEqual("looks_sayforsecs", say.GetProperty("opcode").GetString());
        Assert.AreEqual(JsonValueKind.Number, hat.GetProperty("x").ValueKind);
        Assert.AreEqual(JsonValueKind.Number, hat.GetProperty("y").ValueKind);
    }

    [TestMethod]
    public void CompilerAlwaysWritesTheStageAsTheFirstTarget()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
sprite "Player" {
  @greenflag:
    looks.show
}

stage {
  @greenflag:
    looks.hide
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement targets = document.RootElement.GetProperty("targets");
        Assert.IsTrue(targets[0].GetProperty("isStage").GetBoolean());
        Assert.AreEqual("Stage", targets[0].GetProperty("name").GetString());
        Assert.IsFalse(targets[1].GetProperty("isStage").GetBoolean());
        Assert.AreEqual("Player", targets[1].GetProperty("name").GetString());
    }

    [TestMethod]
    public void SoundClearAliasUsesScratchClearEffectsOpcode()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    sound.clear
}
""");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        _ = FindBlockByOpcode(GetStageBlocks(document), "sound_cleareffects");
    }

    [TestMethod]
    public void CompilesCustomProcedureDefinitionAndCallMutation()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  proc jump(height:num=10, message:str="go") warp:
    motion.changey height
    looks.say message

  @key "space":
    call jump(20, "up")
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);

        JsonElement prototype = FindBlockByOpcode(blocks, "procedures_prototype");
        JsonElement prototypeMutation = prototype.GetProperty("mutation");
        Assert.AreEqual("jump %n %s", prototypeMutation.GetProperty("proccode").GetString());
        Assert.AreEqual("true", prototypeMutation.GetProperty("warp").GetString());
        StringAssert.Contains(prototypeMutation.GetProperty("argumentnames").GetString(), "\"height\"");
        StringAssert.Contains(prototypeMutation.GetProperty("argumentdefaults").GetString(), "\"go\"");

        JsonElement call = FindBlockByOpcode(blocks, "procedures_call");
        JsonElement callMutation = call.GetProperty("mutation");
        Assert.AreEqual("jump %n %s", callMutation.GetProperty("proccode").GetString());
        StringAssert.Contains(callMutation.GetProperty("argumentids").GetString(), "height");
    }

    [TestMethod]
    public void RawOpcodeEscapeEmitsWarningAndBlock()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  @greenflag:
    % "data_setvariableto" field VARIABLE=("score", "score-id") input VALUE=0
}
""");

        CtsDiagnostic warning = AssertSingleDiagnostic(result.Diagnostics);
        Assert.AreEqual(DiagnosticSeverity.Warning, warning.Severity);
        Assert.AreEqual("CTS2001", warning.Code);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement raw = FindBlockByOpcode(GetStageBlocks(document), "data_setvariableto");
        Assert.IsTrue(raw.GetProperty("fields").TryGetProperty("VARIABLE", out JsonElement variableField));
        Assert.AreEqual("score", variableField[0].GetString());
        Assert.IsTrue(raw.GetProperty("inputs").TryGetProperty("VALUE", out _));
    }

    [TestMethod]
    public void CompilesScratchAsmDeclarationsCostumesAndGenericBlocks()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  extension pen
  var score = 0
  cloud var highScore = 0
  list inventory = ["key", "coin"]
  broadcast gameOver = "game over"
  costume "Backdrop" 480x360 center 240,180 {
    rect 0,0 480,360 fill="#ffffff" stroke="#000000" width=2
    line 0,0 480,360 stroke="#ff0000" width=4
    text 20,50 "Hello" size=28 fill="#111111"
  }

  @greenflag:
    block "data_setvariableto" field VARIABLE=score input VALUE=10
    block "event_broadcast" input BROADCAST_INPUT=gameOver
}

sprite "Player" {
  state x=10 y=-20 direction=45 size=80 visible=false layer=3
  rotationStyle "left-right"
  costume "Player" 100x100 center 50,50 {
    circle 50,50 r=40 fill="none" stroke="#00ff00" width=6
  }

  @greenflag:
    block "control_repeat" input TIMES=3 {
      motion.move 10
    }
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement root = document.RootElement;
        Assert.AreEqual("pen", root.GetProperty("extensions")[0].GetString());

        JsonElement stage = GetTarget(root, "Stage");
        JsonElement variables = stage.GetProperty("variables");
        JsonProperty score = AssertSinglePropertyWithPrefix(variables, "stage_var_score");
        Assert.AreEqual("score", score.Value[0].GetString());
        Assert.AreEqual("0", score.Value[1].GetString());

        JsonProperty highScore = AssertSinglePropertyWithPrefix(variables, "stage_var_highscore");
        Assert.AreEqual("highScore", highScore.Value[0].GetString());
        Assert.AreEqual("0", highScore.Value[1].GetString());
        Assert.IsTrue(highScore.Value[2].GetBoolean());

        JsonProperty inventory = AssertSinglePropertyWithPrefix(stage.GetProperty("lists"), "stage_list_inventory");
        Assert.AreEqual("inventory", inventory.Value[0].GetString());
        Assert.AreEqual("key", inventory.Value[1][0].GetString());
        Assert.AreEqual("coin", inventory.Value[1][1].GetString());

        JsonProperty gameOver = AssertSinglePropertyWithPrefix(stage.GetProperty("broadcasts"), "stage_broadcast_gameover");
        Assert.AreEqual("game over", gameOver.Value.GetString());

        JsonElement dataSet = FindBlockByOpcode(stage.GetProperty("blocks"), "data_setvariableto");
        JsonElement variableField = dataSet.GetProperty("fields").GetProperty("VARIABLE");
        Assert.AreEqual("score", variableField[0].GetString());
        Assert.AreEqual(score.Name, variableField[1].GetString());

        JsonElement stageCostume = stage.GetProperty("costumes")[0];
        string stageMd5Ext = stageCostume.GetProperty("md5ext").GetString()!;
        Assert.AreEqual(stageCostume.GetProperty("assetId").GetString() + ".svg", stageMd5Ext);
        Assert.IsTrue(result.Assets.ContainsKey(stageMd5Ext));
        byte[] stageAsset = result.Assets[stageMd5Ext];
        string stageSvg = System.Text.Encoding.UTF8.GetString(stageAsset);
        string stageHash = Convert.ToHexString(MD5.HashData(stageAsset)).ToLowerInvariant();
        Assert.AreEqual(stageCostume.GetProperty("assetId").GetString(), stageHash);
        StringAssert.Contains(stageSvg, "<rect");
        StringAssert.Contains(stageSvg, "<text");
        Assert.IsFalse(stageSvg.Contains("<script", StringComparison.OrdinalIgnoreCase));

        JsonElement sprite = GetTarget(root, "Player");
        Assert.AreEqual(10, sprite.GetProperty("x").GetDouble());
        Assert.AreEqual(-20, sprite.GetProperty("y").GetDouble());
        Assert.AreEqual(45, sprite.GetProperty("direction").GetDouble());
        Assert.AreEqual(80, sprite.GetProperty("size").GetDouble());
        Assert.IsFalse(sprite.GetProperty("visible").GetBoolean());
        Assert.AreEqual(3, sprite.GetProperty("layerOrder").GetInt32());
        Assert.AreEqual("left-right", sprite.GetProperty("rotationStyle").GetString());

        JsonElement repeat = FindBlockByOpcode(sprite.GetProperty("blocks"), "control_repeat");
        string substackId = repeat.GetProperty("inputs").GetProperty("SUBSTACK")[1].GetString()!;
        JsonElement move = sprite.GetProperty("blocks").GetProperty(substackId);
        Assert.AreEqual("motion_movesteps", move.GetProperty("opcode").GetString());
        Assert.AreEqual(GetBlockId(sprite.GetProperty("blocks"), repeat), move.GetProperty("parent").GetString());
    }

    [TestMethod]
    public void CompilesGenericHatAndNestedReporterBlocks()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var score = 0
  broadcast start = "start"

  @block "event_whenbroadcastreceived" field BROADCAST_OPTION=start:
    block "control_if" input CONDITION=["operator_gt" input OPERAND1=["data_variable" field VARIABLE=score] input OPERAND2=10] {
      block "data_changevariableby" field VARIABLE=score input VALUE=["operator_add" input NUM1=1 input NUM2=2]
    }
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        JsonElement hat = FindBlockByOpcode(blocks, "event_whenbroadcastreceived");
        Assert.IsTrue(hat.GetProperty("topLevel").GetBoolean());
        Assert.AreEqual("start", hat.GetProperty("fields").GetProperty("BROADCAST_OPTION")[0].GetString());

        JsonElement condition = FindBlockByOpcode(blocks, "operator_gt");
        JsonElement variable = FindBlockByOpcode(blocks, "data_variable");
        Assert.AreEqual(GetBlockId(blocks, condition), variable.GetProperty("parent").GetString());

        JsonElement add = FindBlockByOpcode(blocks, "operator_add");
        JsonElement change = FindBlockByOpcode(blocks, "data_changevariableby");
        Assert.AreEqual(GetBlockId(blocks, change), add.GetProperty("parent").GetString());
    }

    [TestMethod]
    public void CompilesBooleanCustomProcedureWithDisplaySignature()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  proc check(enabled:bool=false) as "check %b now":
    looks.say "checked"

  @greenflag:
    call check(["operator_equals" input OPERAND1=1 input OPERAND2=1])
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement blocks = GetStageBlocks(document);
        JsonElement prototype = FindBlockByOpcode(blocks, "procedures_prototype");
        Assert.AreEqual("check %b now", prototype.GetProperty("mutation").GetProperty("proccode").GetString());
        _ = FindBlockByOpcode(blocks, "argument_reporter_boolean");

        JsonElement call = FindBlockByOpcode(blocks, "procedures_call");
        string reporterId = call.GetProperty("inputs").EnumerateObject().Single().Value[1].GetString()!;
        Assert.AreEqual("operator_equals", blocks.GetProperty(reporterId).GetProperty("opcode").GetString());
    }

    [TestMethod]
    public void CompilesNamedSubstacksAndResolvesStageDataFromSprites()
    {
        CtsCompileResult result = CtsCompiler.Compile("""
stage {
  var score = 0
  list items = []
  broadcast start = "start"

  proc reset():
    block "data_setvariableto" field VARIABLE=score input VALUE=0
}

sprite "Player" {
  proc reset():
    block "data_setvariableto" field VARIABLE=score input VALUE=1

  @block "event_whenbroadcastreceived" field BROADCAST_OPTION=start:
    block "control_if_else" input CONDITION=["operator_gt" input OPERAND1=["data_variable" field VARIABLE=score] input OPERAND2=0] {
      substack SUBSTACK:
        block "data_changevariableby" field VARIABLE=score input VALUE=1
      substack SUBSTACK2:
        block "data_addtolist" field LIST=items input ITEM="ready"
    }
}
""");

        AssertNoErrors(result.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement stage = GetTarget(document.RootElement, "Stage");
        string scoreId = AssertSinglePropertyWithPrefix(stage.GetProperty("variables"), "stage_var_score").Name;
        string listId = AssertSinglePropertyWithPrefix(stage.GetProperty("lists"), "stage_list_items").Name;
        string broadcastId = AssertSinglePropertyWithPrefix(stage.GetProperty("broadcasts"), "stage_broadcast_start").Name;

        JsonElement spriteBlocks = GetTarget(document.RootElement, "Player").GetProperty("blocks");
        JsonElement hat = FindBlockByOpcode(spriteBlocks, "event_whenbroadcastreceived");
        Assert.AreEqual(broadcastId, hat.GetProperty("fields").GetProperty("BROADCAST_OPTION")[1].GetString());

        JsonElement ifElse = FindBlockByOpcode(spriteBlocks, "control_if_else");
        Assert.IsTrue(ifElse.GetProperty("inputs").TryGetProperty("SUBSTACK", out _));
        Assert.IsTrue(ifElse.GetProperty("inputs").TryGetProperty("SUBSTACK2", out _));

        JsonElement change = FindBlockByOpcode(spriteBlocks, "data_changevariableby");
        Assert.AreEqual(scoreId, change.GetProperty("fields").GetProperty("VARIABLE")[1].GetString());
        JsonElement add = FindBlockByOpcode(spriteBlocks, "data_addtolist");
        Assert.AreEqual(listId, add.GetProperty("fields").GetProperty("LIST")[1].GetString());
    }

    [TestMethod]
    public void SampleHelloMonoCompilesAndCreatesGeneratedAsset()
    {
        string samplePath = FindRepositoryFile(Path.Combine("samples", "hello.sasm"));
        string source = File.ReadAllText(samplePath);

        CtsCompileResult result = CtsCompiler.Compile(source, samplePath);

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement stage = GetTarget(document.RootElement, "Stage");
        string md5Ext = stage.GetProperty("costumes")[0].GetProperty("md5ext").GetString()!;
        Assert.IsTrue(result.Assets.ContainsKey(md5Ext));
    }

    [TestMethod]
    public void ConverterAllowsWarningsButBlocksErrorsForScratchAsmInput()
    {
        string validSource = """
stage {
  @greenflag:
    % "motion_movesteps" input STEPS=10
}
""";
        string validPath = WriteTempMono(validSource);
        string validOutput = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        string invalidPath = WriteTempMono("""
stage {
  @greenflag
    motion.move 10
}
""");
        string invalidOutput = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        try
        {
            ScratchProjectConverter converter = new();
            ConversionResult warningResult = converter.ConvertToSb3(validPath, validOutput);

            Assert.IsTrue(warningResult.Success, FormatIssues(warningResult.Issues));
            Assert.IsTrue(File.Exists(validOutput));
            Assert.HasCount(2, warningResult.Issues, FormatIssues(warningResult.Issues));
            Assert.IsTrue(warningResult.Issues.All(issue => issue.Severity == DiagnosticSeverity.Warning));
            CollectionAssert.AreEquivalent(
                new[] { "CTS2001", "CTS2003" },
                warningResult.Issues.Select(issue => issue.Code).ToArray());

            ConversionResult errorResult = converter.ConvertToSb3(invalidPath, invalidOutput);
            Assert.IsFalse(errorResult.Success);
            Assert.IsFalse(File.Exists(invalidOutput));
            Assert.AreEqual(DiagnosticSeverity.Error, errorResult.Issues[0].Severity);
            Assert.AreEqual("CTS1001", errorResult.Issues[0].Code);
        }
        finally
        {
            DeleteIfExists(validPath);
            DeleteIfExists(validOutput);
            DeleteIfExists(invalidPath);
            DeleteIfExists(invalidOutput);
        }
    }

    [TestMethod]
    public void ClassifierReturnsScratchCategoryColors()
    {
        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify("""
stage {
  @greenflag:
    motion.move 10
    looks.say "Hello"
}
""");

        Assert.IsTrue(spans.Any(span => span.Color == ScratchCategoryColors.Events));
        Assert.IsTrue(spans.Any(span => span.Color == ScratchCategoryColors.Motion));
        Assert.IsTrue(spans.Any(span => span.Color == ScratchCategoryColors.Looks));
    }

    [TestMethod]
    public void ClassifierColorsEveryCatalogAliasWithItsScratchCategory()
    {
        foreach (CtsAliasDefinition definition in CtsBlockRegistry.Definitions)
        {
            string source = definition.Shape == CtsBlockShape.Hat ? "@" + definition.Name : definition.Name;
            IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);
            Assert.IsTrue(
                spans.Any(span => span.Color == definition.CategoryColor),
                $"Alias '{definition.Name}' was not colored {definition.CategoryColor}.");
        }
    }

    [TestMethod]
    public void ClassifierColorsNativeControlDataAndExpressionOperators()
    {
        const string source = """
stage {
  var score = 0
  list items = []
  @greenflag:
    score += 2 ^ 3
    repeat 2:
      items.add "coin"
    if score >= 2 and not items.contains("x"):
      score = sin(score) + 1
}
""";

        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);
        AssertColorForText(source, spans, "repeat", ScratchCategoryColors.Control);
        AssertColorForText(source, spans, "score", ScratchCategoryColors.Variables);
        AssertColorForText(source, spans, "items.add", ScratchCategoryColors.Lists);
        AssertColorForText(source, spans, "^", ScratchCategoryColors.Operators);
        AssertColorForText(source, spans, ">=", ScratchCategoryColors.Operators);
        AssertColorForText(source, spans, "and", ScratchCategoryColors.Operators);
        AssertColorForText(source, spans, "not", ScratchCategoryColors.Operators);
        AssertColorForText(source, spans, "sin", ScratchCategoryColors.Operators);
    }

    [TestMethod]
    public void ClassifierColorsGenericOpcodesByScratchCategory()
    {
        const string source = """
stage {
  @greenflag:
    block "motion_movesteps" input STEPS=10
    block "looks_say" input MESSAGE="Hello"
}
""";

        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);
        CtsColorSpan motion = spans.Single(span => source.Substring(span.Start, span.Length) == "\"motion_movesteps\"");
        CtsColorSpan looks = spans.Single(span => source.Substring(span.Start, span.Length) == "\"looks_say\"");

        Assert.AreEqual(ScratchCategoryColors.Motion, motion.Color);
        Assert.AreEqual(ScratchCategoryColors.Looks, looks.Color);
    }

    [TestMethod]
    public void ClassifierColorsContextualScratchAsmSyntax()
    {
        const string source = """
stage {
  broadcast start = "start"
  proc announce(message:str, enabled:bool=false):
    event.broadcast start
    looks.say message

  costume "Badge" 32x32 center 16,16 {
    rect 0,0 32,32 fill="#9966ff"
  }

  @block "control_if" input CONDITION=1 {
    substack SUBSTACK:
      looks.show
  }
}
""";

        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);
        AssertColorForText(source, spans, "start", ScratchCategoryColors.Events);
        AssertColorForText(source, spans, "announce", ScratchCategoryColors.MyBlocks);
        AssertColorForText(source, spans, "message", ScratchCategoryColors.MyBlocks);
        AssertColorForText(source, spans, "rect", ScratchCategoryColors.Looks);
        AssertColorForText(source, spans, "fill", ScratchCategoryColors.Looks);
        AssertColorForText(source, spans, "substack", ScratchCategoryColors.Control);
        AssertColorForText(source, spans, "@block", "#455A64");
        AssertColorForText(source, spans, "input", "#455A64");

        int declarationEquals = source.IndexOf('=');
        Assert.IsTrue(spans.Any(span => span.Start == declarationEquals && span.Color == ScratchCategoryColors.Events));
        int parameterDefaultEquals = source.IndexOf("=false", StringComparison.Ordinal);
        Assert.IsTrue(spans.Any(span => span.Start == parameterDefaultEquals && span.Color == ScratchCategoryColors.MyBlocks));
        int rawEquals = source.LastIndexOf('=');
        Assert.IsTrue(spans.Any(span => span.Start == rawEquals && span.Color == ScratchCategoryColors.RawSyntax));
    }

    [TestMethod]
    public void ClassifierScopesProcedureParameterColorsToTheirProcedure()
    {
        const string source = """
stage {
  var value = 0
  proc consume(value:num):
    looks.say value
  @greenflag:
    value = 1
}
""";

        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);
        int parameterUse = source.IndexOf("value\n", source.IndexOf("looks.say", StringComparison.Ordinal), StringComparison.Ordinal);
        int variableUse = source.LastIndexOf("value", StringComparison.Ordinal);

        Assert.IsTrue(spans.Any(span => span.Start == parameterUse && span.Color == ScratchCategoryColors.MyBlocks));
        Assert.IsTrue(spans.Any(span => span.Start == variableUse && span.Color == ScratchCategoryColors.Variables));
    }

    [TestMethod]
    public void RegistryUsesScratchExtensionColorWithFallback()
    {
        Assert.AreEqual(ScratchCategoryColors.Extensions, CtsBlockRegistry.GetCategoryColor("music"));
        Assert.AreEqual(ScratchCategoryColors.Extensions, CtsBlockRegistry.GetCategoryColor("videoSensing"));
        Assert.AreEqual(ScratchCategoryColors.Extensions, CtsBlockRegistry.GetCategoryColor("text2speech"));
        Assert.AreEqual(ScratchCategoryColors.Extensions, CtsBlockRegistry.GetCategoryColor("translate"));
        Assert.AreEqual(ScratchCategoryColors.Extensions, CtsBlockRegistry.GetCategoryColor("makeymakey"));
        Assert.AreEqual(ScratchCategoryColors.Extensions, ScratchCategoryColors.GetExtensionColor("third-party"));
    }

    [TestMethod]
    public void CatalogExporterIsDeterministicCompleteAndCompileSafe()
    {
        string firstJson = ScratchAsmCatalogExporter.GenerateJson();
        string firstSource = ScratchAsmCatalogExporter.GenerateScratchAsm();

        Assert.AreEqual(firstJson, ScratchAsmCatalogExporter.GenerateJson());
        Assert.AreEqual(firstSource, ScratchAsmCatalogExporter.GenerateScratchAsm());

        using JsonDocument catalog = JsonDocument.Parse(firstJson);
        Assert.AreEqual(1, catalog.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement aliases = catalog.RootElement.GetProperty("aliases");
        Assert.HasCount(258, CtsBlockRegistry.Definitions);
        Assert.HasCount(CtsBlockRegistry.Definitions.Count, aliases.EnumerateArray().ToArray());

        string[] signatures = aliases.EnumerateArray()
            .Select(alias => $"{alias.GetProperty("alias").GetString()}/{alias.GetProperty("bindings").GetArrayLength()}")
            .ToArray();
        Assert.HasCount(signatures.Length, signatures.Distinct(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEquivalent(
            CtsBlockRegistry.Definitions.Select(definition => $"{definition.Name}/{definition.ArgumentCount}").ToArray(),
            signatures);
        Assert.IsTrue(aliases.EnumerateArray().All(alias =>
            alias.TryGetProperty("opcode", out _) &&
            alias.TryGetProperty("category", out _) &&
            alias.TryGetProperty("color", out _) &&
            alias.TryGetProperty("shape", out _) &&
            alias.TryGetProperty("extension", out _) &&
            alias.TryGetProperty("substacks", out _) &&
            alias.TryGetProperty("terminal", out _) &&
            alias.TryGetProperty("legacy", out _) &&
            alias.TryGetProperty("fixedFields", out _) &&
            alias.TryGetProperty("sampleTarget", out _) &&
            !alias.TryGetProperty("target", out _)));
        Assert.IsTrue(catalog.RootElement.TryGetProperty("categoryPalette", out _));
        Assert.IsTrue(catalog.RootElement.TryGetProperty("syntaxPalette", out _));

        StringAssert.Contains(firstSource, "proc demonstrate_custom_block");
        StringAssert.Contains(firstSource, "call demonstrate_custom_block");
        string[] sourceMarkers = firstSource.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("# alias ", StringComparison.Ordinal))
            .ToArray();
        CollectionAssert.AreEquivalent(
            CtsBlockRegistry.Definitions.Select(definition => $"# alias {definition.Name}/{definition.ArgumentCount}").ToArray(),
            sourceMarkers);

        CtsCompileResult result = CtsCompiler.Compile(firstSource, "all-aliases.sasm");
        Assert.HasCount(0, result.Diagnostics, string.Join(Environment.NewLine, result.Diagnostics));
        using JsonDocument project = JsonDocument.Parse(result.ProjectJsonBytes);
        string[] emittedOpcodes = project.RootElement.GetProperty("targets").EnumerateArray()
            .SelectMany(target => target.GetProperty("blocks").EnumerateObject())
            .Select(block => block.Value.GetProperty("opcode").GetString()!)
            .ToArray();
        foreach (IGrouping<string, CtsAliasDefinition> opcodeGroup in CtsBlockRegistry.Definitions.GroupBy(definition => definition.Opcode, StringComparer.Ordinal))
        {
            Assert.IsGreaterThanOrEqualTo(
                opcodeGroup.Count(),
                emittedOpcodes.Count(opcode => opcode == opcodeGroup.Key),
                opcodeGroup.Key);
        }

        Assert.AreEqual(
            File.ReadAllText(FindRepositoryFile(Path.Combine("samples", "all-aliases.sasm"))),
            firstSource);
        Assert.AreEqual(
            File.ReadAllText(FindRepositoryFile(Path.Combine("samples", "all-aliases.json"))),
            firstJson);
    }

    [TestMethod]
    public void SourceLocationConvertsToEditorOffset()
    {
        const string source = "first\r\nsecond\nthird";

        Assert.AreEqual(0, CtsSourcePosition.GetOffset(source, new SourceLocation(1, 1)));
        Assert.AreEqual(8, CtsSourcePosition.GetOffset(source, new SourceLocation(2, 2)));
        Assert.AreEqual(source.Length, CtsSourcePosition.GetOffset(source, new SourceLocation(99, 99)));
    }

    [TestMethod]
    public void ConvertsMonoFileToScratchReadableSb3()
    {
        string inputPath = WriteTempMono("""
stage {
  @greenflag:
    motion.move 10
    looks.say "Hello" 2s
}
""");
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(inputPath, outputPath);

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            Assert.IsNotNull(archive.GetEntry("project.json"));

            using Stream projectStream = archive.GetEntry("project.json")!.Open();
            using JsonDocument projectJson = JsonDocument.Parse(projectStream);
            JsonElement costume = projectJson.RootElement.GetProperty("targets")[0].GetProperty("costumes")[0];
            using Stream asset = archive.GetEntry(costume.GetProperty("md5ext").GetString()!)!.Open();
            Assert.AreEqual(costume.GetProperty("assetId").GetString(), Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(asset)));
            JsonElement blocks = GetStageBlocks(projectJson);
            _ = FindBlockByOpcode(blocks, "event_whenflagclicked");
            _ = FindBlockByOpcode(blocks, "motion_movesteps");
            _ = FindBlockByOpcode(blocks, "looks_sayforsecs");
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    private static JsonElement GetStageBlocks(JsonDocument document)
    {
        return document.RootElement.GetProperty("targets")[0].GetProperty("blocks");
    }

    private static void AssertColorForText(
        string source,
        IReadOnlyList<CtsColorSpan> spans,
        string text,
        string expectedColor)
    {
        Assert.IsTrue(
            spans.Any(span => source.Substring(span.Start, span.Length) == text && span.Color == expectedColor),
            $"Expected '{text}' to use {expectedColor}.");
    }

    private static string CreateAliasSmokeSource(CtsAliasDefinition definition)
    {
        string[] arguments = definition.Bindings.Select(binding => binding.Name switch
        {
            "VARIABLE" => "score",
            "LIST" => "items",
            "BROADCAST_OPTION" or "BROADCAST_INPUT" => "start",
            _ => "1"
        }).ToArray();
        string spacedArguments = arguments.Length == 0 ? string.Empty : " " + string.Join(' ', arguments);
        string functionArguments = string.Join(", ", arguments);
        string script = definition.Shape switch
        {
            CtsBlockShape.Hat => $"@{definition.Name}{spacedArguments}:\n    looks.show",
            CtsBlockShape.Reporter or CtsBlockShape.Boolean =>
                $"@greenflag:\n    output = {definition.Name}({functionArguments})",
            CtsBlockShape.CBlock when definition.Name == "forever" =>
                "@greenflag:\n    forever:\n      looks.show",
            CtsBlockShape.CBlock when definition.Name == "ifelse" =>
                "@greenflag:\n    if 1:\n      looks.show\n    else:\n      looks.hide",
            CtsBlockShape.CBlock =>
                $"@greenflag:\n    {definition.Name}{spacedArguments}:\n      looks.show",
            _ => $"@greenflag:\n    {definition.Name}{spacedArguments}"
        };

        return $$"""
stage {
  var output = 0
  var score = 0
  list items = []
  broadcast start = "start"
  {{script}}
}
""";
    }

    private static JsonElement GetTarget(JsonElement root, string name)
    {
        foreach (JsonElement target in root.GetProperty("targets").EnumerateArray())
        {
            if (target.GetProperty("name").GetString() == name)
            {
                return target;
            }
        }

        Assert.Fail($"Target was not found: {name}");
        throw new InvalidOperationException();
    }

    private static JsonElement FindBlockByOpcode(JsonElement blocks, string opcode)
    {
        foreach (JsonProperty blockProperty in blocks.EnumerateObject())
        {
            if (blockProperty.Value.GetProperty("opcode").GetString() == opcode)
            {
                return blockProperty.Value;
            }
        }

        Assert.Fail($"Block opcode was not found: {opcode}");
        throw new InvalidOperationException();
    }

    private static string GetBlockId(JsonElement blocks, JsonElement block)
    {
        foreach (JsonProperty blockProperty in blocks.EnumerateObject())
        {
            if (JsonElement.DeepEquals(blockProperty.Value, block))
            {
                return blockProperty.Name;
            }
        }

        Assert.Fail("Block id was not found.");
        throw new InvalidOperationException();
    }

    private static JsonProperty AssertSinglePropertyWithPrefix(JsonElement element, string prefix)
    {
        JsonProperty[] properties = element.EnumerateObject()
            .Where(property => property.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(1, properties);
        return properties[0];
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Repository file was not found: {relativePath}");
        throw new InvalidOperationException();
    }

    private static CtsDiagnostic AssertSingleDiagnostic(IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        return diagnostics[0];
    }

    private static void AssertNoErrors(IReadOnlyList<CtsDiagnostic> diagnostics, string? context = null)
    {
        Assert.IsFalse(
            diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            (context is null ? string.Empty : context + Environment.NewLine) +
            string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
    }

    private static string WriteTempMono(string source)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mono");
        File.WriteAllText(path, source);
        return path;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string FormatIssues(IReadOnlyList<ValidationIssue> issues)
    {
        return string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Severity} {issue.Code}: {issue.Message}"));
    }
}
