using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ScratchAsmRoundTripTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("scratchasm-roundtrip-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void ExportedSourceSurvivesOriginalDeletionAndPreservesEveryArchiveEntry()
    {
        string input = MakeProject("stage {\n  var score = 1\n  @greenflag:\n    score += 2\n}\n");
        Dictionary<string, byte[]> original = ReadArchive(input);
        original["custom.bin"] = [1, 2, 3, 4];
        WriteArchive(input, original);
        string source = Path.Combine(_directory, "portable.sasm");
        AssertSuccess(new ScratchProjectConverter().ConvertToScratchAsm(input, source));
        File.Delete(input);
        string moved = Path.Combine(_directory, "moved");
        Directory.CreateDirectory(moved);
        foreach (string file in Directory.GetFiles(_directory)) File.Move(file, Path.Combine(moved, Path.GetFileName(file)));
        string output = Path.Combine(moved, "rebuilt.sb3");
        AssertSuccess(new ScratchProjectConverter().ConvertToSb3(Path.Combine(moved, "portable.sasm"), output));
        Dictionary<string, byte[]> rebuilt = ReadArchive(output);
        Assert.HasCount(original.Count, rebuilt);
        foreach ((string name, byte[] bytes) in original) CollectionAssert.AreEqual(bytes, rebuilt[name], name);
    }

    [TestMethod]
    public void EditedSourcePreservesSpriteStateGlobalIdsAndDisplayNames()
    {
        string input = MakeProject("""
stage {
  var score = 1
  broadcast changed = "changed"
}
sprite "Player" {
  state x=71 y=-23 direction=-45 size=82 visible=false layer=7
  @greenflag:
    score += 2
    event.broadcast changed
}
""");
        Rewrite(input, project =>
        {
            JsonObject stage = project["targets"]![0]!.AsObject();
            JsonObject sprite = project["targets"]![1]!.AsObject();
            string variableId = stage["variables"]!.AsObject().First().Key;
            JsonNode tuple = stage["variables"]![variableId]!;
            stage["variables"]!.AsObject().Remove(variableId);
            tuple[0] = "global score";
            stage["variables"]!["original-global-id"] = tuple;
            foreach (JsonObject block in sprite["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>())
                if (block["fields"]?["VARIABLE"] is JsonArray field)
                { field[0] = "global score"; field[1] = "original-global-id"; }
            sprite["volume"] = 31;
            sprite["draggable"] = true;
        });
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(input);
        string edited = session.SourceText.Replace("data.change global_score 2", "data.change global_score 9", StringComparison.Ordinal);
        if (edited == session.SourceText) edited += "\n# edited\n";
        string output = Path.Combine(_directory, "edited.sb3");
        AssertSuccess(session.WriteEdited(edited, output));
        JsonNode project = JsonNode.Parse(ReadArchive(output)["project.json"])!;
        JsonNode player = project["targets"]![1]!;
        Assert.AreEqual(71, player["x"]!.GetValue<int>());
        Assert.AreEqual(31, player["volume"]!.GetValue<int>());
        Assert.IsTrue(player["draggable"]!.GetValue<bool>());
        JsonObject change = player["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>()
            .Single(block => block["opcode"]!.GetValue<string>() == "data_changevariableby");
        Assert.AreEqual("original-global-id", change["fields"]!["VARIABLE"]![1]!.GetValue<string>());
        Assert.AreEqual("global score", change["fields"]!["VARIABLE"]![0]!.GetValue<string>());
    }

    [TestMethod]
    public void CustomBlocksUnicodeAndFloatingReportersSurviveAnEditedRoundTrip()
    {
        string input = MakeProject("""
stage {
  var value = "001"
  list items = ["001", "1e3", "\u4f60\u597d"]
  proc add(n: num):
    value += n
  @greenflag:
    call add(2)
}
""");
        Rewrite(input, project => project["targets"]![0]!["blocks"]!["floating"] =
            new JsonArray(12, "value", "stage_var_value", 41, 52));
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(input);
        Assert.IsTrue(session.CanEdit, string.Join(";", session.Issues));
        StringAssert.Contains(session.SourceText, "rawblocks {");
        string output = Path.Combine(_directory, "edited.sb3");
        AssertSuccess(session.WriteEdited(session.SourceText + "\n# edit\n", output));
        JsonNode original = JsonNode.Parse(ReadArchive(input)["project.json"])!;
        JsonNode rebuilt = JsonNode.Parse(ReadArchive(output)["project.json"])!;
        Assert.IsTrue(JsonNode.DeepEquals(original["targets"]![0]!["blocks"], rebuilt["targets"]![0]!["blocks"]));
        Assert.IsTrue(JsonNode.DeepEquals(original["targets"]![0]!["lists"], rebuilt["targets"]![0]!["lists"]));
        Assert.AreEqual("001", rebuilt["targets"]![0]!["variables"]!["stage_var_value"]![1]!.GetValue<string>());
    }

    [TestMethod]
    public void InvalidGraphDoesNotOverwriteExistingOutput()
    {
        string input = MakeProject("stage {\n  @greenflag:\n    looks.say \"hello\"\n}\n");
        Rewrite(input, project =>
        {
            JsonObject blocks = project["targets"]![0]!["blocks"]!.AsObject();
            JsonObject block = blocks.First().Value!.AsObject();
            block["inputs"]!["missing"] = new JsonArray(2, "absent");
        });
        string output = Path.Combine(_directory, "existing.sasm");
        File.WriteAllText(output, "keep");
        ConversionResult result = new ScratchProjectConverter().ConvertToScratchAsm(input, output, true);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("missing block", StringComparison.Ordinal)));
        Assert.AreEqual("keep", File.ReadAllText(output));
    }

    [TestMethod]
    public void RawJsonErrorsHaveSourceLocationsAndGraphErrorsPreventPackaging()
    {
        CtsCompileResult invalid = CtsCompiler.Compile("stage {\n  rawblocks {\n    \"bad\": invalid\n  }\n}\n");
        Assert.IsTrue(invalid.Diagnostics.Any(diagnostic => diagnostic.Span.Start.Line == 3));
        string source = Path.Combine(_directory, "invalid.sasm");
        File.WriteAllText(source, "stage {\n  rawblocks {\"a\": {\"opcode\": \"event_whenflagclicked\", \"next\": \"a\", \"parent\": null, \"inputs\": {}, \"fields\": {}, \"shadow\": false, \"topLevel\": true}}\n}\n");
        ConversionResult result = new ScratchProjectConverter().ConvertToSb3(source, Path.Combine(_directory, "bad.sb3"));
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("cycle", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void MissingCompanionAndPathEscapesProduceErrors()
    {
        string source = Path.Combine(_directory, "missing.sasm");
        File.WriteAllText(source, "project \"missing.assets.sb3\"\nstage {\n}\n");
        Assert.IsFalse(new ScratchProjectConverter().ConvertToSb3(source, Path.Combine(_directory, "missing.sb3")).Success);
        CtsCompileResult escaped = CtsCompiler.Compile("project \"../outside.sb3\"\nstage {\n}\n");
        Assert.IsTrue(escaped.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void StringEscapesDecodeUnicodeAndRejectInvalidEscapes()
    {
        CtsCompileResult compiled = CtsCompiler.Compile("stage {\n  var value = \"\\u4f60\\u597d\\b\\f\"\n}\n");
        Assert.IsFalse(compiled.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        JsonNode project = JsonNode.Parse(compiled.ProjectJsonBytes)!;
        Assert.AreEqual("\u4f60\u597d\b\f", project["targets"]![0]!["variables"]!["stage_var_value"]![1]!.GetValue<string>());
        Assert.IsTrue(CtsCompiler.Compile("stage {\n  var value = \"\\q\"\n}\n").Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private string MakeProject(string source)
    {
        string input = Path.Combine(_directory, "original.sasm");
        string output = Path.Combine(_directory, "original.sb3");
        File.WriteAllText(input, source);
        AssertSuccess(new ScratchProjectConverter().ConvertToSb3(input, output));
        return output;
    }

    [TestMethod]
    public void RenamingImportedSpritePreservesItsCostumesAndState()
    {
        string input = MakeProject("stage {\n}\nsprite \"Before\" {\n  state x=1e-4 y=7 size=81\n  costume \"Shape\" 40x40 center 20,20 {\n    circle 20,20 r=10 fill=\"#ff0000\"\n  }\n}\n");
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(input);
        string changed = session.SourceText.Replace("sprite \"Before\"", "sprite \"After\"", StringComparison.Ordinal);
        string output = Path.Combine(_directory, "renamed.sb3");
        AssertSuccess(session.WriteEdited(changed, output));
        JsonNode before = JsonNode.Parse(ReadArchive(input)["project.json"])!["targets"]![1]!;
        JsonNode after = JsonNode.Parse(ReadArchive(output)["project.json"])!["targets"]![1]!;
        Assert.AreEqual("After", after["name"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(before["costumes"], after["costumes"]));
        Assert.AreEqual(0.0001, after["x"]!.GetValue<double>());
    }

    [TestMethod]
    public void RepairDoesNotRewriteCharactersInsideStringsOrComments()
    {
        const string source = "stage {\n  var text = \"\u201chello\u201d\u00a0world\"\n  # \u201ccomment\u201d\u00a0\n}\n";
        Assert.AreEqual(source, ScratchAsmSourceRepairer.Repair(source).SourceText);
    }

    [TestMethod]
    public void RawBlockWorkspaceCoordinatesRemainEditable()
    {
        string input = MakeProject("stage {\n  var value = 0\n  proc example():\n    value += 1\n}\n");
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(input);
        string edited = session.SourceText.Replace("\"x\": 320", "\"x\": 999", StringComparison.Ordinal);
        Assert.AreNotEqual(session.SourceText, edited);
        string output = Path.Combine(_directory, "layout.sb3");
        AssertSuccess(session.WriteEdited(edited, output));
        JsonNode project = JsonNode.Parse(ReadArchive(output)["project.json"])!;
        JsonObject definition = project["targets"]![0]!["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>()
            .Single(block => block["opcode"]!.GetValue<string>() == "procedures_definition");
        Assert.AreEqual(999, definition["x"]!.GetValue<int>());
    }

    [TestMethod]
    public void CorruptArchivesReturnDiagnosticsWithoutWritingOutput()
    {
        string input = Path.Combine(_directory, "broken.sb3");
        string output = Path.Combine(_directory, "broken.sasm");
        File.WriteAllText(input, "not a zip");
        Assert.IsFalse(new ScratchProjectConverter().ConvertToScratchAsm(input, output).Success);
        Assert.IsFalse(File.Exists(output));
    }
    private static void AssertSuccess(ConversionResult result) => Assert.IsTrue(result.Success, string.Join("; ", result.Issues));
    private static Dictionary<string, byte[]> ReadArchive(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using Stream stream = entry.Open();
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }
    private static void WriteArchive(string path, Dictionary<string, byte[]> entries)
    {
        File.Delete(path);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, byte[] bytes) in entries)
        {
            using Stream stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }
    private static void Rewrite(string path, Action<JsonNode> rewrite)
    {
        Dictionary<string, byte[]> entries = ReadArchive(path);
        JsonNode project = JsonNode.Parse(entries["project.json"])!;
        rewrite(project);
        entries["project.json"] = Encoding.UTF8.GetBytes(project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        WriteArchive(path, entries);
    }
}
