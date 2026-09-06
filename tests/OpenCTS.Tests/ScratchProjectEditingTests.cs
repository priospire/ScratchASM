using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ScratchProjectEditingTests
{
    [TestMethod]
    public void ConverterAcceptsCanonicalScratchAsmFilesWithoutCompatibilityWarning()
    {
        string sourcePath = TemporaryPath(".sasm");
        string outputPath = TemporaryPath(".sb3");
        File.WriteAllText(sourcePath, "stage {\n  @greenflag:\n    looks.say \"ScratchASM\"\n}\n");

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(sourcePath, outputPath);

            Assert.IsTrue(result.Success, Format(result.Issues));
            Assert.IsFalse(result.Issues.Any(issue => issue.Code == "CTS2003"));
            Assert.IsTrue(File.Exists(outputPath));
        }
        finally
        {
            Delete(sourcePath);
            Delete(outputPath);
        }
    }

    [TestMethod]
    public void DecompilesScratchProjectToDeterministicCompilableScratchAsm()
    {
        string inputPath = CreateBaselineSb3();
        string outputPath = TemporaryPath(".sb3");

        try
        {
            ScratchProjectEditSession first = ScratchProjectEditSession.Open(inputPath);
            ScratchProjectEditSession second = ScratchProjectEditSession.Open(inputPath);

            Assert.IsFalse(first.Issues.Any(issue => issue.Severity == DiagnosticSeverity.Error), Format(first.Issues));
            Assert.AreEqual(first.SourceText, second.SourceText);
            StringAssert.Contains(first.SourceText, "global var score = \"1\"");
            StringAssert.Contains(first.SourceText, "@event.greenflag:");
            StringAssert.Contains(first.SourceText, "motion.move 10");

            CtsCompileResult compile = CtsCompiler.Compile(first.SourceText, "decompiled.sasm");
            Assert.IsFalse(compile.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join(Environment.NewLine, compile.Diagnostics));

            ConversionResult write = first.WriteEdited(first.SourceText, outputPath);
            Assert.IsTrue(write.Success, Format(write.Issues));
            Assert.IsTrue(File.Exists(outputPath));
        }
        finally
        {
            Delete(inputPath);
            Delete(outputPath);
        }
    }

    [TestMethod]
    public void EditedMergePreservesUnknownJsonCommentsMonitorsAndArchiveBytes()
    {
        byte[] extraBytes = [0, 1, 2, 3, 254, 255];
        string inputPath = CreateBaselineSb3(extraBytes);
        string outputPath = TemporaryPath(".sb3");

        try
        {
            ScratchProjectEditSession session = ScratchProjectEditSession.Open(inputPath);
            string edited = session.SourceText.Replace("motion.move 10", "motion.move 20", StringComparison.Ordinal);

            ConversionResult result = session.WriteEdited(edited, outputPath);

            Assert.IsTrue(result.Success, Format(result.Issues));
            CollectionAssert.AreEqual(extraBytes, ReadEntry(outputPath, "custom.bin"));

            byte[] inputCostume = ReadFirstCostume(inputPath);
            CollectionAssert.AreEqual(inputCostume, ReadFirstCostume(outputPath));

            JsonNode output = ReadProject(outputPath);
            Assert.AreEqual("preserve", output["customRoot"]!.GetValue<string>());
            Assert.AreEqual("target-value", output["targets"]![0]!["customTarget"]!.GetValue<string>());
            Assert.AreEqual(1, output["monitors"]!.AsArray().Count);
            Assert.AreEqual(1, output["targets"]![0]!["comments"]!.AsObject().Count);
            Assert.AreEqual("block-value", FindBlock(output, "motion_movesteps")["customBlock"]!.GetValue<string>());
            Assert.AreEqual(2, output["meta"]!["scratchasm"]!["schemaVersion"]!.GetValue<int>());
        }
        finally
        {
            Delete(inputPath);
            Delete(outputPath);
        }
    }

    [TestMethod]
    public void ReportsMalformedBlockGraphWithoutDroppingTheProblem()
    {
        string inputPath = CreateBaselineSb3();

        try
        {
            RewriteProject(inputPath, project =>
            {
                JsonObject block = FindBlock(project, "motion_movesteps");
                string id = FindBlockId(project, block);
                block["next"] = id;
            });

            ScratchProjectEditSession session = ScratchProjectEditSession.Open(inputPath);

            Assert.IsTrue(session.Issues.Any(issue =>
                issue.Severity == DiagnosticSeverity.Error &&
                issue.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)), Format(session.Issues));
            Assert.IsFalse(session.CanEdit);
        }
        finally
        {
            Delete(inputPath);
        }
    }

    private static string CreateBaselineSb3(byte[]? extraBytes = null)
    {
        const string source = """
stage {
  global var score = 1
  @greenflag:
    motion.move 10
    looks.say score
}
""";

        CtsCompileResult compile = CtsCompiler.Compile(source, "baseline.sasm");
        Assert.IsFalse(compile.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            string.Join(Environment.NewLine, compile.Diagnostics));

        JsonNode project = JsonNode.Parse(compile.ProjectJsonBytes)!;
        project["customRoot"] = "preserve";
        project["monitors"] = new JsonArray(new JsonObject
        {
            ["id"] = "monitor-1",
            ["mode"] = "default",
            ["opcode"] = "data_variable",
            ["params"] = new JsonObject(),
            ["spriteName"] = null,
            ["value"] = "1",
            ["width"] = 0,
            ["height"] = 0,
            ["x"] = 0,
            ["y"] = 0,
            ["visible"] = false
        });

        JsonObject target = project["targets"]![0]!.AsObject();
        target["customTarget"] = "target-value";
        JsonObject motion = FindBlock(project, "motion_movesteps");
        motion["customBlock"] = "block-value";
        target["comments"] = new JsonObject
        {
            ["comment-1"] = new JsonObject
            {
                ["blockId"] = FindBlockId(project, motion),
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 160,
                ["height"] = 80,
                ["minimized"] = false,
                ["text"] = "keep"
            }
        };

        string path = TemporaryPath(".sb3");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "project.json", Encoding.UTF8.GetBytes(project.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        })));
        foreach ((string name, byte[] bytes) in compile.Assets)
        {
            WriteEntry(archive, name, bytes);
        }

        WriteEntry(archive, "custom.bin", extraBytes ?? [9, 8, 7]);
        return path;
    }

    private static void RewriteProject(string path, Action<JsonNode> rewrite)
    {
        Dictionary<string, byte[]> entries;
        using (ZipArchive archive = ZipFile.OpenRead(path))
        {
            entries = archive.Entries.ToDictionary(entry => entry.FullName, ReadEntry, StringComparer.Ordinal);
        }

        JsonNode project = JsonNode.Parse(entries["project.json"])!;
        rewrite(project);
        entries["project.json"] = Encoding.UTF8.GetBytes(project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        File.Delete(path);
        using ZipArchive output = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, byte[] bytes) in entries)
        {
            WriteEntry(output, name, bytes);
        }
    }

    private static JsonNode ReadProject(string path)
    {
        return JsonNode.Parse(ReadEntry(path, "project.json"))!;
    }

    private static byte[] ReadFirstCostume(string path)
    {
        JsonNode project = ReadProject(path);
        string fileName = project["targets"]![0]!["costumes"]![0]!["md5ext"]!.GetValue<string>();
        return ReadEntry(path, fileName);
    }

    private static byte[] ReadEntry(string path, string name)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = archive.GetEntry(name)!;
        return ReadEntry(entry);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static JsonObject FindBlock(JsonNode project, string opcode)
    {
        return project["targets"]![0]!["blocks"]!.AsObject()
            .Select(property => property.Value!.AsObject())
            .Single(block => block["opcode"]!.GetValue<string>() == opcode);
    }

    private static string FindBlockId(JsonNode project, JsonObject block)
    {
        return project["targets"]![0]!["blocks"]!.AsObject()
            .Single(property => ReferenceEquals(property.Value, block)).Key;
    }

    private static string TemporaryPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Format(IEnumerable<ValidationIssue> issues)
    {
        return string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Severity} {issue.Code}: {issue.Message}"));
    }
}
