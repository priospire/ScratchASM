using System.IO.Compression;
using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ScratchProjectConverterTests
{
    [TestMethod]
    public void ConvertsFolderProjectToScratchReadableSb3()
    {
        string projectDir = CreateValidProjectFolder();
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(projectDir, outputPath);

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(File.Exists(outputPath));

            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            Assert.IsNotNull(archive.GetEntry("project.json"));
            Assert.IsNotNull(archive.GetEntry("cd21514d0531fdffb22204e0ec5ed84a.svg"));

            using Stream projectStream = archive.GetEntry("project.json")!.Open();
            using JsonDocument projectJson = JsonDocument.Parse(projectStream);
            Assert.IsTrue(projectJson.RootElement.TryGetProperty("targets", out JsonElement targets));
            Assert.AreEqual(JsonValueKind.Array, targets.ValueKind);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [TestMethod]
    public void ConvertsMonoFileToScratchReadableSb3()
    {
        string inputPath = WriteTempSource(
            ".mono",
            """
stage {
  var score = 0
  costume "Backdrop" 480x360 center 240,180 {
    rect 0,0 480,360 fill="#ffffff"
  }

  @greenflag:
    block "data_setvariableto" field VARIABLE=score input VALUE=1
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
            JsonElement costume = projectJson.RootElement
                .GetProperty("targets")[0]
                .GetProperty("costumes")[0];
            string md5Ext = costume.GetProperty("md5ext").GetString()!;
            Assert.IsNotNull(archive.GetEntry(md5Ext));
            Assert.AreEqual(costume.GetProperty("assetId").GetString() + ".svg", md5Ext);
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void CtsExtensionIsAcceptedAsLegacyScratchAsm()
    {
        string inputPath = WriteTempSource(".cts", "stage {\n}\n");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(inputPath, outputPath);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(File.Exists(outputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void MalformedProjectJsonReportsExactLineAndColumn()
    {
        string projectDir = CreateProjectFolder("""
{
  "targets": [
}
""");
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        ConversionResult result = new ScratchProjectConverter().ConvertToSb3(projectDir, outputPath);

        Assert.IsFalse(result.Success);
        ValidationIssue issue = AssertSingleIssue(result);
        Assert.AreEqual("$", issue.JsonPath);
        Assert.AreEqual(3, issue.Location?.Line);
        Assert.AreEqual(1, issue.Location?.Column);
    }

    [TestMethod]
    public void MissingCostumeAssetReportsJsonPathAndLocation()
    {
        string projectDir = CreateProjectFolder(ValidProjectJson("missing", "missing.svg"));
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");

        ConversionResult result = new ScratchProjectConverter().ConvertToSb3(projectDir, outputPath);

        Assert.IsFalse(result.Success);
        ValidationIssue issue = AssertSingleIssue(result);
        Assert.AreEqual("$.targets[0].costumes[0].md5ext", issue.JsonPath);
        Assert.IsTrue(issue.Message.Contains("missing.svg", StringComparison.Ordinal));
        Assert.AreEqual(15, issue.Location?.Line);
    }

    [TestMethod]
    public void SafeRepairNormalizesRecoverableStructureAndAddsStage()
    {
        string inputPath = CreateSb3(
            """
            {
              "targets": [
                {
                  "name": "Sprite1"
                }
              ]
            }
            """);
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Severity == DiagnosticSeverity.Warning));

            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            using JsonDocument project = JsonDocument.Parse(archive.GetEntry("project.json")!.Open());
            JsonElement root = project.RootElement;
            Assert.AreEqual(JsonValueKind.Array, root.GetProperty("monitors").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, root.GetProperty("extensions").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, root.GetProperty("meta").ValueKind);

            JsonElement stage = root.GetProperty("targets")[0];
            Assert.IsTrue(stage.GetProperty("isStage").GetBoolean());
            Assert.AreEqual("Stage", stage.GetProperty("name").GetString());
            JsonElement stageCostume = stage.GetProperty("costumes")[0];
            Assert.IsNotNull(archive.GetEntry(stageCostume.GetProperty("md5ext").GetString()!));

            JsonElement sprite = root.GetProperty("targets")[1];
            Assert.IsFalse(sprite.GetProperty("isStage").GetBoolean());
            Assert.AreEqual(JsonValueKind.Object, sprite.GetProperty("variables").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, sprite.GetProperty("costumes").ValueKind);
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairReplacesMissingCostumeAssetWithGeneratedSvg()
    {
        string inputPath = CreateSb3(ValidProjectJson("missing", "missing.svg"));
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("generated default SVG", StringComparison.Ordinal)));

            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            using JsonDocument project = JsonDocument.Parse(archive.GetEntry("project.json")!.Open());
            JsonElement costume = project.RootElement.GetProperty("targets")[0].GetProperty("costumes")[0];
            string md5Ext = costume.GetProperty("md5ext").GetString()!;
            Assert.AreNotEqual("missing.svg", md5Ext);
            Assert.IsNotNull(archive.GetEntry(md5Ext));
            Assert.AreEqual("svg", costume.GetProperty("dataFormat").GetString());
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairRemovesUnusableBlocksAndNormalizesRecoverableBlocks()
    {
        string projectJson = ValidProjectJson("cd21514d0531fdffb22204e0ec5ed84a", "cd21514d0531fdffb22204e0ec5ed84a.svg")
            .Replace(
                "\"blocks\": {}",
                "\"blocks\": {\"broken\": 5, \"missingOpcode\": {\"inputs\": {}}, \"show\": {\"opcode\": \"looks_show\"}}",
                StringComparison.Ordinal);
        string inputPath = CreateSb3WithEntries(
            ("project.json", projectJson),
            ("cd21514d0531fdffb22204e0ec5ed84a.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("block", StringComparison.OrdinalIgnoreCase)));

            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            using JsonDocument project = JsonDocument.Parse(archive.GetEntry("project.json")!.Open());
            JsonElement blocks = project.RootElement.GetProperty("targets")[0].GetProperty("blocks");
            Assert.IsFalse(blocks.TryGetProperty("broken", out _));
            Assert.IsFalse(blocks.TryGetProperty("missingOpcode", out _));
            JsonElement show = blocks.GetProperty("show");
            Assert.AreEqual(JsonValueKind.Null, show.GetProperty("next").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, show.GetProperty("parent").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, show.GetProperty("inputs").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, show.GetProperty("fields").ValueKind);
            Assert.IsFalse(show.GetProperty("shadow").GetBoolean());
            Assert.IsTrue(show.GetProperty("topLevel").GetBoolean());
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairRejectsMalformedProjectJsonWithExactDiagnostic()
    {
        string inputPath = CreateSb3("{\"targets\":[}");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsFalse(result.Success);
            Assert.AreEqual(
                "Safe repair is impossible because project.json contains malformed JSON.",
                AssertSingleIssue(result).Message);
            Assert.IsFalse(File.Exists(outputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairRejectsNonZipInputWithExactDiagnostic()
    {
        string inputPath = TempSb3Path();
        string outputPath = TempSb3Path();
        File.WriteAllText(inputPath, "not a zip archive");

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsFalse(result.Success);
            Assert.AreEqual(
                "Safe repair is impossible because the input is not a readable ZIP archive.",
                AssertSingleIssue(result).Message);
            Assert.IsFalse(File.Exists(outputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairRejectsDuplicateZipPaths()
    {
        string inputPath = CreateSb3WithEntries(
            ("project.json", "{}"),
            ("project.json", "{}"));
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(AssertSingleIssue(result).Message.Contains("duplicate ZIP entry path", StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(outputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairRejectsUnsafeZipPaths()
    {
        string inputPath = CreateSb3WithEntries(
            ("project.json", "{}"),
            ("../unsafe.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(AssertSingleIssue(result).Message.Contains("unsafe ZIP entry path", StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(outputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairNeverMutatesSourceArchive()
    {
        string inputPath = CreateSb3("{}");
        string outputPath = TempSb3Path();
        byte[] sourceBefore = File.ReadAllBytes(inputPath);

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            CollectionAssert.AreEqual(sourceBefore, File.ReadAllBytes(inputPath));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairAppliesScratchAsmSourceFixupsBeforeCompile()
    {
        string inputPath = WriteTempSource(".sasm", """
global var score = 0
@greenflag:
  set score to 5
  change score by 3
""");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Code == "REPAIR200"));

            using ZipArchive archive = ZipFile.OpenRead(outputPath);
            using JsonDocument project = JsonDocument.Parse(archive.GetEntry("project.json")!.Open());
            JsonElement blocks = project.RootElement.GetProperty("targets")[0].GetProperty("blocks");
            Assert.IsTrue(blocks.EnumerateObject().Any(block =>
                block.Value.GetProperty("opcode").GetString() == "data_setvariableto"));
            Assert.IsTrue(blocks.EnumerateObject().Any(block =>
                block.Value.GetProperty("opcode").GetString() == "data_changevariableby"));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairAcceptsProjectJsonInput()
    {
        string projectDir = CreateProjectFolder("{}");
        string inputPath = Path.Combine(projectDir, "project.json");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                inputPath,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Code == "REPAIR100"));
            Assert.IsTrue(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void SafeRepairAcceptsProjectFolderWithUtf8BomProjectJson()
    {
        string projectDir = CreateProjectFolder("\uFEFF{}");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(
                projectDir,
                outputPath,
                new ConversionOptions { AttemptSafeRepair = true });

            Assert.IsTrue(result.Success, FormatIssues(result.Issues));
            Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("byte order mark", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            DeleteIfExists(outputPath);
        }
    }

    [TestMethod]
    public void RecoverableProjectStillFailsWhenSafeRepairIsDisabled()
    {
        string inputPath = CreateSb3("{}");
        string outputPath = TempSb3Path();

        try
        {
            ConversionResult result = new ScratchProjectConverter().ConvertToSb3(inputPath, outputPath);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(File.Exists(outputPath));
            Assert.IsTrue(result.Issues.Any(issue => issue.JsonPath == "$.targets"));
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    private static string CreateValidProjectFolder()
    {
        string projectDir = CreateProjectFolder(ValidProjectJson("cd21514d0531fdffb22204e0ec5ed84a", "cd21514d0531fdffb22204e0ec5ed84a.svg"));
        File.WriteAllText(
            Path.Combine(projectDir, "cd21514d0531fdffb22204e0ec5ed84a.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"360\"><rect width=\"480\" height=\"360\" fill=\"#ffffff\"/></svg>");
        return projectDir;
    }

    private static string WriteTempSource(string extension, string source)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllText(path, source);
        return path;
    }

    private static string CreateSb3(string projectJson)
    {
        return CreateSb3WithEntries(("project.json", projectJson));
    }

    private static string CreateSb3WithEntries(params (string Name, string Content)[] entries)
    {
        string path = TempSb3Path();
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private static string TempSb3Path()
    {
        return Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sb3");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string CreateProjectFolder(string projectJson)
    {
        string projectDir = Path.Combine(Path.GetTempPath(), "opencts-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "project.json"), projectJson);
        return projectDir;
    }

    private static string ValidProjectJson(string assetId, string md5Ext)
    {
        return $$"""
{
  "targets": [
    {
      "isStage": true,
      "name": "Stage",
      "variables": {},
      "lists": {},
      "broadcasts": {},
      "blocks": {},
      "comments": {},
      "costumes": [
        {
          "assetId": "{{assetId}}",
          "name": "backdrop1",
          "md5ext": "{{md5Ext}}",
          "dataFormat": "svg",
          "bitmapResolution": 1,
          "rotationCenterX": 0,
          "rotationCenterY": 0
        }
      ],
      "sounds": []
    }
  ],
  "monitors": [],
  "extensions": [],
  "meta": {
    "semver": "3.0.0",
    "vm": "0.2.0",
    "agent": "OpenCTS"
  }
}
""";
    }

    private static ValidationIssue AssertSingleIssue(ConversionResult result)
    {
        Assert.HasCount(1, result.Issues, FormatIssues(result.Issues));
        return result.Issues[0];
    }

    private static string FormatIssues(IReadOnlyList<ValidationIssue> issues)
    {
        return string.Join(Environment.NewLine, issues.Select(issue => $"{issue.JsonPath}: {issue.Message}"));
    }
}
