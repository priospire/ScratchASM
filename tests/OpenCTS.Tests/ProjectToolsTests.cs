using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ProjectToolsTests
{
    [TestMethod]
    public void VisibleLineClassificationKeepsDeclarationContextAndOriginalOffsets()
    {
        string source = "stage {\n  extension custom \"https://example.com/custom.js\" \"#123456\"\n  list items = [" + new string(' ', 50000) + "]\n  @greenflag:\n    % \"custom_test\"\n    items.add 1\n}\n";
        List<(int Start, int Length, int Line)> slices = [];
        int offset = 0, number = 1;
        foreach (ReadOnlySpan<char> line in source.AsSpan().EnumerateLines())
        {
            slices.Add((offset, Math.Min(line.Length, 150), number++));
            offset += line.Length + 1;
        }
        var colors = CtsSyntaxClassifier.ClassifyLines(source, slices);
        int opcode = source.IndexOf("\"custom_test\"", StringComparison.Ordinal);
        Assert.IsTrue(colors.Any(span => span.Start == opcode && span.Color == "#123456"));
        Assert.IsTrue(colors.Any(span => span.Start == source.IndexOf("@greenflag", StringComparison.Ordinal) && span.Color == ScratchCategoryColors.Events));
        Assert.IsLessThan(100, colors.Count);
    }

    [TestMethod]
    public void SpriteArchiveImportPreservesAssetsAndRejectsUnsafeEntries()
    {
        string directory = Directory.CreateTempSubdirectory("sprite-import-").FullName;
        try
        {
            var document = ScratchProjectDocument.Compile("stage {\n}\n");
            document.AddSprite("Player");
            string path = Path.Combine(directory, "player.sprite3");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (StreamWriter writer = new(archive.CreateEntry("sprite.json").Open()))
                    writer.Write(document.Project["targets"]![1]!.ToJsonString());
                foreach ((string name, byte[] bytes) in document.Assets)
                {
                    using Stream stream = archive.CreateEntry(name).Open();
                    stream.Write(bytes);
                }
            }
            int imported = document.ImportSprite(path);
            Assert.AreEqual("Player2", document.Project["targets"]![imported]!["name"]!.ToString());
            Assert.IsTrue(document.CreateSession().CanEdit);
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
                archive.CreateEntry("../outside.svg");
            Assert.Throws<InvalidDataException>(() => document.ImportSprite(path));
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void NoGalleryDeclarationIsMistakenForAUniversalVanillaWorkaround()
    {
        foreach (TurboWarpExtension extension in TurboWarpExtensionCatalog.Entries)
        {
            var document = ScratchProjectDocument.Compile($"stage {{\n  extension \"{extension.Id}\" \"{extension.Url}\"\n}}\n");
            var report = ScratchCompatibility.Optimize(document, true);
            Assert.IsFalse(report.CanExportVanilla, extension.Name);
            Assert.IsTrue(JsonNode.DeepEquals(document.Project["extensionURLs"], report.Document.Project["extensionURLs"]));
        }
    }

    [TestMethod]
    public void BitwiseConversionRejectsUnprovenShapesWithoutChangingInput()
    {
        const string source = "stage {\n  extension Bitwise \"https://extensions.turbowarp.org/bitwise.js\"\n  var result = 0\n  @greenflag:\n    result = [Bitwise_bitwiseAnd input LEFT=6 input RIGHT=3]\n}\n";
        Action<ScratchProjectDocument, JsonObject>[] mutations =
        [
            (doc, block) => doc.Project["extensionURLs"]!["Bitwise"] = "https://example.com/bitwise.js",
            (_, block) => block["opcode"] = "Bitwise_unknown",
            (_, block) => block["mutation"] = new JsonObject(),
            (_, block) => block["fields"]!["MODE"] = new JsonArray("custom", null),
            (_, block) => block["inputs"]!["EXTRA"] = new JsonArray(1, new JsonArray(4, 1)),
            (_, block) => block["inputs"]!.AsObject().Remove("RIGHT"),
            (_, block) => block["inputs"]!["LEFT"] = new JsonArray(1, new JsonArray(4, 0.5)),
            (_, block) => block["inputs"]!["LEFT"] = new JsonArray(1, new JsonArray(4, 2147483648d)),
            (_, block) => block["inputs"]!["RIGHT"] = new JsonArray(1, new JsonArray(4, -2147483649d)),
            (_, block) => block["inputs"]!["LEFT"] = new JsonArray(1, new JsonArray(10, "6")),
            (_, block) => block["inputs"]!["LEFT"] = new JsonArray(1, new JsonArray(4, "NaN")),
            (_, block) => block["inputs"]!["LEFT"] = new JsonArray(1, new JsonArray(4, "Infinity")),
            (_, block) => block["next"] = "unexpected"
        ];
        foreach (var mutate in mutations)
        {
            var document = ScratchProjectDocument.Compile(source);
            JsonObject block = document.Project["targets"]![0]!["blocks"]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>()
                .Single(item => item["opcode"]?.ToString() == "Bitwise_bitwiseAnd");
            mutate(document, block);
            string before = document.Project.ToJsonString();
            var report = ScratchCompatibility.Optimize(document, true);
            Assert.IsFalse(report.CanExportVanilla);
            Assert.AreEqual(before, document.Project.ToJsonString());
            Assert.AreEqual(before, report.Document.Project.ToJsonString());
        }
    }

    [TestMethod]
    public void EveryBundledGalleryEntryCanBeDeclaredAndRoundTripped()
    {
        Assert.IsGreaterThan(100, TurboWarpExtensionCatalog.Entries.Count);
        foreach (TurboWarpExtension extension in TurboWarpExtensionCatalog.Entries)
        {
            string declaration = $"stage {{\n  extension \"{extension.Id}\" \"{extension.Url}\" \"{extension.Color}\"\n}}\n";
            var document = ScratchProjectDocument.Compile(declaration);
            var session = document.CreateSession();
            var restored = session.Materialize(session.SourceText);
            Assert.AreEqual(extension.Url, restored.Project["extensionURLs"]![extension.Id]!.ToString(), extension.Name);
        }
    }
    [TestMethod]
    public void GalleryExtensionDeclarationsPreserveUrlsColorsAndNumericIds()
    {
        var compiled = CtsCompiler.Compile("""
stage {
  extension "0832rxfs2" "https://extensions.turbowarp.org/0832/rxFS2.js" "#123456"
  @greenflag:
    % "0832rxfs2_example"
}
""");
        Assert.IsFalse(compiled.Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error), string.Join("\n", compiled.Diagnostics));
        Assert.IsTrue(compiled.Diagnostics.Any(issue => issue.Code == "SASM4001"));
        JsonObject project = JsonNode.Parse(compiled.ProjectJsonBytes)!.AsObject();
        Assert.AreEqual("https://extensions.turbowarp.org/0832/rxFS2.js", project["extensionURLs"]!["0832rxfs2"]!.ToString());
        var document = new ScratchProjectDocument(project, compiled.Assets);
        var session = document.CreateSession();
        var restored = session.Materialize(session.SourceText + "\n# edited\n");
        Assert.IsTrue(JsonNode.DeepEquals(project["extensionURLs"], restored.Project["extensionURLs"]));
        var spans = CtsSyntaxClassifier.Classify(session.SourceText);
        Assert.IsTrue(spans.Any(span => span.Color == "#123456"));
    }

    [TestMethod]
    public void UnsafeExtensionUrlsAndMalformedColorsAreErrors()
    {
        foreach (string declaration in new[] { "extension test \"file:///secret\"", "extension test \"https://example.com/ext.js\" \"red\"", "extension test \"https://example.com\" \"#123456\" garbage" })
            Assert.IsTrue(CtsCompiler.Compile("stage {\n  " + declaration + "\n}\n").Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void SizeWarningsUseUncompressedBytesAndDoNotDeleteLargeData()
    {
        ScratchProjectDocument document = ScratchProjectDocument.Compile("stage {\n  list data = []\n}\n");
        Assert.HasCount(0, ScratchCompatibility.Inspect(document.Project, ScratchCompatibility.WarningThreshold - 1));
        Assert.IsTrue(ScratchCompatibility.Inspect(document.Project, ScratchCompatibility.WarningThreshold).Any(issue => issue.Code == "SASM4002"));
        Assert.IsFalse(ScratchCompatibility.Inspect(document.Project, ScratchCompatibility.SaveLimit).Any(issue => issue.Code == "SASM4003"));
        string large = new('x', ScratchCompatibility.SaveLimit);
        document.Project["targets"]![0]!["lists"]!.AsObject().First().Value![1]!.AsArray().Add(large);
        ProjectToolReport report = ScratchCompatibility.Optimize(document, true);
        Assert.IsFalse(report.CanExportVanilla);
        Assert.AreEqual(large, report.Document.Project["targets"]![0]!["lists"]!.AsObject().First().Value![1]![0]!.ToString());
    }

    [TestMethod]
    public void BitwiseLiteralWorkaroundIsNativeAndVariableOperandsAreBlocked()
    {
        string source = """
stage {
  extension Bitwise "https://extensions.turbowarp.org/bitwise.js"
  var result = 0
  @greenflag:
    result = [Bitwise_bitwiseAnd input LEFT=6 input RIGHT=3]
}
""";
        var document = ScratchProjectDocument.Compile(source);
        var report = ScratchCompatibility.Optimize(document, true);
        Assert.IsTrue(report.CanExportVanilla, string.Join("\n", report.Issues));
        Assert.IsFalse(report.Document.Project.ToJsonString().Contains("Bitwise", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, report.FoldedReporters);
        Assert.IsTrue(document.Project.ToJsonString().Contains("Bitwise", StringComparison.Ordinal));
        var variable = ScratchProjectDocument.Compile(source.Replace("LEFT=6", "LEFT=result", StringComparison.Ordinal));
        Assert.IsFalse(ScratchCompatibility.Optimize(variable, true).CanExportVanilla);
        document.Project["monitors"]!.AsArray().Add(new JsonObject { ["id"] = "bitwise-monitor", ["opcode"] = "Bitwise_bitwiseAnd" });
        var monitored = ScratchCompatibility.Optimize(document, true);
        Assert.IsFalse(monitored.CanExportVanilla, "Extension monitors must not be silently left behind.");
        Assert.IsTrue(monitored.Document.Project["extensionURLs"]!.AsObject().ContainsKey("Bitwise"));
    }

    [TestMethod]
    public void OptimizerFoldsArithmeticWithoutChangingDataOrAssets()
    {
        var document = ScratchProjectDocument.Compile("""
stage {
  var total = 0
  list items = [10,20,30]
  @greenflag:
    total = 4 * 5
    items.replace (1 + 1) 40
}
""");
        var report = ScratchCompatibility.Optimize(document, false);
        Assert.IsGreaterThan(0, report.FoldedReporters);
        Assert.IsLessThan(report.OriginalBytes, report.OutputBytes);
        Assert.IsTrue(JsonNode.DeepEquals(document.Project["targets"]![0]!["variables"], report.Document.Project["targets"]![0]!["variables"]));
        Assert.IsTrue(JsonNode.DeepEquals(document.Project["targets"]![0]!["lists"], report.Document.Project["targets"]![0]!["lists"]));
        CollectionAssert.AreEqual(document.Assets.Keys.ToArray(), report.Document.Assets.Keys.ToArray());
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string output = Path.Combine(directory, "optimized.sb3");
            report.Document.Write(output);
            Assert.IsTrue(ScratchProjectEditSession.Open(output).CanEdit);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void SpriteCostumeAndSoundEditsSurviveSourceRoundTrip()
    {
        var document = ScratchProjectDocument.Compile("stage {\n}\n");
        int sprite = document.AddSprite("Player");
        Assert.AreEqual(1, sprite);
        byte[] svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="red"/></svg>""");
        document.AddCostume(sprite, "Red", svg, "svg", 10, 10);
        using MemoryStream wav = new();
        using (BinaryWriter writer = new(wav, Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(38); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(2); writer.Write((short)0);
        }
        document.AddSound(sprite, "Click", wav.ToArray(), 8000, 1);
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var session = document.CreateSession();
            string path = Path.Combine(directory, "assets.sasm");
            Assert.IsTrue(session.SaveSource(session.SourceText, path).Success);
            string source = File.ReadAllText(path);
            var restored = ScratchProjectEditSession.OpenSourceCompanion(source, path)!.Materialize(source);
            Assert.AreEqual(2, restored.Project["targets"]![1]!["costumes"]!.AsArray().Count);
            Assert.AreEqual(1, restored.Project["targets"]![1]!["sounds"]!.AsArray().Count);
            restored.Write(Path.Combine(directory, "assets.sb3"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void LargeAssignmentHighlightingScalesAndKeepsCategoryColors()
    {
        string source = "stage {\n  var count = 0\n  @greenflag:\n" + string.Concat(Enumerable.Repeat("    count += 1\n", 18000)) + "}\n";
        Stopwatch timer = Stopwatch.StartNew();
        var spans = CtsSyntaxClassifier.Classify(source);
        Assert.IsGreaterThan(36000, spans.Count);
        Assert.IsTrue(timer.Elapsed < TimeSpan.FromSeconds(5), $"Classification took {timer.Elapsed}.");
        Assert.IsTrue(spans.Any(span => span.Color == ScratchCategoryColors.Variables));
    }

    [TestMethod]
    public void EveryQuickStartSnippetCompiles()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenCTS.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        foreach (string file in new[] { "quick-start.md", "project-tools.md" })
            foreach (Match match in Regex.Matches(File.ReadAllText(Path.Combine(root.FullName, "docs", file)), @"~~~scratchasm\r?\n([\s\S]*?)~~~"))
            {
                var result = CtsCompiler.Compile(match.Groups[1].Value);
                Assert.IsFalse(result.Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error), file + "\n" + string.Join("\n", result.Diagnostics));
            }
    }
}
