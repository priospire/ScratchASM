using System.Text.Json.Nodes;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ReadableImportTests
{
    [TestMethod]
    public void NumericSocketTextKeepsSignsAndWhitespaceWhenRecompiled()
    {
        foreach (string value in new[] { "+3", "-03", " 03 ", "001", "1e3", ".5", "Infinity" })
        {
            var document = ScratchProjectDocument.Compile("stage {\n  @greenflag:\n    looks.say 1\n}\n");
            var block = document.Project["targets"]![0]!["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>()
                .Single(item => item["opcode"]!.ToString() == "looks_say");
            block["inputs"]!["MESSAGE"] = new JsonArray(1, new JsonArray(4, value));
            var session = document.CreateSession();
            AssertCompiles(session.SourceText, value);
            var rebuilt = ScratchProjectDocument.Compile(session.SourceText);
            var say = rebuilt.Project["targets"]![0]!["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>()
                .Single(item => item["opcode"]!.ToString() == "looks_say");
            Assert.AreEqual(value, say["inputs"]!["MESSAGE"]![1]![1]!.GetValue<string>());
        }
    }

    [TestMethod]
    public void GenericSignedInputsRemainLiteralsWithoutChangingExpressions()
    {
        const string source = """
stage {
  var value = 2
  @greenflag:
    block "custom_operation" input MIN=-2147483648 input ZERO=-0 input DECIMAL=-1.25 input EXP=-1e3 input PLUS=+3 input MIXED=-(+3) input EXPR=-(value + 3)
}
""";
        AssertCompiles(source);
        var blocks = ScratchProjectDocument.Compile(source).Project["targets"]![0]!["blocks"]!.AsObject();
        var custom = blocks.Select(pair => pair.Value).OfType<JsonObject>()
            .Single(block => block["opcode"]!.ToString() == "custom_operation");
        foreach ((string name, string value) in new[] { ("MIN", "-2147483648"), ("ZERO", "-0"), ("DECIMAL", "-1.25"), ("EXP", "-1e3"), ("PLUS", "+3"), ("MIXED", "-3") })
        {
            JsonNode input = custom["inputs"]![name]!;
            Assert.AreEqual(1, input[0]!.GetValue<int>(), name);
            Assert.AreEqual(4, input[1]![0]!.GetValue<int>(), name);
            Assert.AreEqual(value, input[1]![1]!.GetValue<string>(), name);
        }

        string expressionId = custom["inputs"]!["EXPR"]![1]!.GetValue<string>();
        Assert.AreEqual("operator_subtract", blocks[expressionId]!["opcode"]!.GetValue<string>());
    }

    [TestMethod]
    public void GenericExtensionInputsAcceptExpressionsAndQuotedSlotNames()
    {
        const string source = "stage {\n  var value = 2\n  @greenflag:\n    block \"custom_operation\" input \"slot/1\"=(value + 3) input OTHER=join(\"a\", \"b\") field MODE=\"test\"\n}\n";
        var document = ScratchProjectDocument.Compile(source);
        var session = document.CreateSession();
        AssertCompiles(session.SourceText);
        StringAssert.Contains(session.SourceText, "input \"slot/1\"=(value + 3)");
        var blocks = JsonNode.Parse(CtsCompiler.Compile(session.SourceText).ProjectJsonBytes)!["targets"]![0]!["blocks"]!.AsObject();
        var custom = blocks.Select(pair => pair.Value).OfType<JsonObject>().Single(block => block["opcode"]!.ToString() == "custom_operation");
        Assert.IsTrue(custom["inputs"]!.AsObject().ContainsKey("slot/1"));
    }

    [TestMethod]
    public void OptimizedArchivesCanOmitFalseShadowFlagsButCannotUseInvalidTypes()
    {
        var document = ScratchProjectDocument.Compile("stage {\n  var result = 0\n  @greenflag:\n    result = 7\n}\n");
        var blocks = document.Project["targets"]![0]!["blocks"]!.AsObject();
        foreach (JsonObject block in blocks.Select(pair => pair.Value).OfType<JsonObject>()) block.Remove("shadow");
        var session = document.CreateSession();
        Assert.IsTrue(session.CanEdit);
        StringAssert.Contains(session.SourceText, "result = 7");
        AssertCompiles(session.SourceText);
        Assert.IsTrue(JsonNode.DeepEquals(blocks, session.Materialize(session.SourceText + "\n# edit\n").Project["targets"]![0]!["blocks"]));
        blocks.First().Value!["shadow"] = "false";
        Assert.Throws<InvalidDataException>(() => document.CreateSession());
    }

    [TestMethod]
    public void StructuredCodeProceduresMenusAndExpressionsRemainReadable()
    {
        const string source = """
stage {
  var score = 0
  list items = ["one"]
}
sprite "Player" {
  proc calculate(amount: num, enabled: bool) as "calculate %n if %b" warp:
    if enabled:
      repeat 3:
        score += (amount * 2)
        items.add score
    else:
      items.delete_all
  @greenflag:
    call calculate(4, (1 == 1))
    motion.goto "_mouse_"
    looks.say join("answer", sin(score))
  stack:
    looks.show
  reporter score
}
""";
        var document = ScratchProjectDocument.Compile(source);
        var session = document.CreateSession();
        string text = session.SourceText;
        Assert.IsTrue(session.CanEdit, string.Join(";", session.Issues));
        foreach (string expected in new[] { "proc calculate", "call calculate", "if enabled", "repeat 3:", "score += (amount * 2)",
            "items.add score", "items.delete_all", "motion.goto \"_mouse_\"", "join(\"answer\", sin(score))", "stack:", "reporter score" })
            StringAssert.Contains(text, expected);
        foreach (string forbidden in new[] { "rawblocks", "@block", "\"opcode\"", "procedures_", "operator_", "[shadow" })
            Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), forbidden);
        AssertCompiles(text);
        var rebuilt = session.Materialize(text + "\n# formatted\n");
        Assert.IsTrue(JsonNode.DeepEquals(document.Project["targets"]![1]!["blocks"], rebuilt.Project["targets"]![1]!["blocks"]));
    }

    [TestMethod]
    public void EditingOneScriptPreservesTheOthersAndKeepsProcedureBindingsConsistent()
    {
        var document = ScratchProjectDocument.Compile("stage {\n  var result = 0\n  proc calculate(n: num):\n    result = n\n  @greenflag:\n    call calculate(7)\n  @key \"space\":\n    looks.say \"untouched\"\n}\n");
        var native = document.Project["targets"]![0]!["blocks"]!.AsObject();
        var prototype = native.Select(pair => pair.Value).OfType<JsonObject>().Single(block => block["opcode"]!.ToString() == "procedures_prototype");
        string oldId = JsonNode.Parse(prototype["mutation"]!["argumentids"]!.GetValue<string>())![0]!.GetValue<string>();
        foreach (JsonObject block in native.Select(pair => pair.Value).OfType<JsonObject>())
        {
            if (block["inputs"] is JsonObject inputs && inputs.Remove(oldId, out JsonNode? value)) inputs["native-parameter"] = value;
            if (block["mutation"]?["argumentids"] is not null) block["mutation"]!["argumentids"] = "[\"native-parameter\"]";
        }
        var session = document.CreateSession();
        var output = session.Materialize(session.SourceText.Replace("result = n", "result = (n * 2)", StringComparison.Ordinal));
        var blocks = output.Project["targets"]![0]!["blocks"]!.AsObject();
        foreach (JsonObject block in blocks.Select(pair => pair.Value).OfType<JsonObject>().Where(block => block["opcode"]!.ToString() is "procedures_call" or "procedures_prototype"))
        {
            Assert.AreEqual("[\"native-parameter\"]", block["mutation"]!["argumentids"]!.GetValue<string>());
            Assert.IsTrue(block["inputs"]!.AsObject().ContainsKey("native-parameter"));
        }
        foreach ((string id, JsonNode? block) in native.Where(pair => pair.Value?["opcode"]?.ToString() is "event_whenkeypressed" or "looks_say"))
            Assert.IsTrue(JsonNode.DeepEquals(block, blocks[id]), id);
    }

    [TestMethod]
    public void EveryCatalogedAliasReimportsAsCompilableSource()
    {
        foreach (CtsAliasDefinition definition in CtsBlockRegistry.Definitions)
        {
            string[] args = definition.Bindings.Select(binding => binding.Name switch
            { "VARIABLE" => "score", "LIST" => "items", "BROADCAST_OPTION" or "BROADCAST_INPUT" => "start", _ => "1" }).ToArray();
            string arguments = args.Length == 0 ? "" : " " + string.Join(", ", args);
            string script = definition.Shape switch
            {
                CtsBlockShape.Hat => $"@{definition.Name}{arguments}:\n    looks.show",
                CtsBlockShape.Reporter or CtsBlockShape.Boolean => $"reporter {definition.Name}({string.Join(", ", args)})",
                CtsBlockShape.CBlock when definition.Name == "ifelse" => "@greenflag:\n    if 1:\n      looks.show\n    else:\n      looks.hide",
                CtsBlockShape.CBlock => $"@greenflag:\n    {definition.Name}{arguments}:\n      looks.show",
                _ => $"@greenflag:\n    {definition.Name}{arguments}"
            };
            string source = "stage {\n  var score = 0\n  list items = []\n  broadcast start = \"start\"\n  " + script + "\n}\n";
            var session = ScratchProjectDocument.Compile(source).CreateSession();
            Assert.IsTrue(session.CanEdit, definition.Name + ": " + string.Join(";", session.Issues));
            Assert.IsFalse(session.SourceText.Contains("rawblocks", StringComparison.Ordinal), definition.Name);
            AssertCompiles(session.SourceText, definition.Name);
        }
    }

    private static void AssertCompiles(string source, string name = "import")
    {
        var errors = CtsCompiler.Compile(source).Diagnostics.Where(issue => issue.Severity == DiagnosticSeverity.Error);
        Assert.IsFalse(errors.Any(), name + ": " + string.Join(";", errors) + "\n" + source[..Math.Min(1800, source.Length)]);
    }
}
