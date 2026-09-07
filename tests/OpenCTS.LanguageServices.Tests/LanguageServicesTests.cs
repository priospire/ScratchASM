using OpenCTS.LanguageServices;

namespace OpenCTS.LanguageServices.Tests;

[TestClass]
public sealed class LanguageServicesTests
{
    [TestMethod]
    public void RawGraphsHaveInfoAndCompletionCanReuseTheBackgroundSymbolIndex()
    {
        var service = new ScratchAsmLanguageService();
        var analysis = service.Analyze("stage {\n  var score = 0\n  rawblocks {}\n}\n");
        Assert.IsTrue(analysis.Diagnostics.Any(issue => issue.Code == "SASM5001" && issue.Severity == "info"));
        Assert.IsTrue(service.GetCompletions("sco", 3, analysis.Symbols).Any(item => item.Label == "score"));
    }
    [TestMethod]
    public void ProjectLoaderAcceptsSourcePathsWithoutParsingThemAsJson()
    {
        string directory = Directory.CreateTempSubdirectory("scratchasm-project-").FullName;
        try
        {
            string source = Path.Combine(directory, "entry.sasm");
            File.WriteAllText(source, "stage {\n}\n");
            ScratchAsmProject project = ScratchAsmProjectLoader.Load(source);
            Assert.AreEqual(source, project.EntryPath);
            Assert.IsNull(project.ManifestPath);
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void AnalysisReturnsVersionColorsSymbolsAndStructuredDiagnostics()
    {
        const string source = """
const Limit = 3
enum Mode { Idle = 0 }
stage {
  global var score = Limit
  proc run(amount:num):
    local var step = amount
    unknown.command 1
}
""";

        DocumentAnalysis analysis = new DocumentAnalyzer().Analyze(source, "main.sasm", 17);

        Assert.AreEqual(17, analysis.Version);
        Assert.IsNotEmpty(analysis.ColorSpans);
        Assert.IsTrue(analysis.Diagnostics.Any(diagnostic => diagnostic.Code == "CTS1008"));
        Assert.IsTrue(analysis.Symbols.Any(symbol => symbol.Name == "Limit" && symbol.Kind == ScratchAsmSymbolKind.Constant));
        Assert.IsTrue(analysis.Symbols.Any(symbol => symbol.Name == "Mode.Idle" && symbol.Kind == ScratchAsmSymbolKind.EnumMember));
        Assert.IsTrue(analysis.Symbols.Any(symbol => symbol.Name == "step" && symbol.Kind == ScratchAsmSymbolKind.Local));
        Assert.IsTrue(analysis.Diagnostics.All(diagnostic => diagnostic.Range.Start.Line >= 1));
    }

    [TestMethod]
    public void CompletionAndCatalogUseCompilerRegistryAndDocumentSymbols()
    {
        const string source = """
stage {
  global var score = 0
  @greenflag:
    mot
}
""";

        ScratchAsmLanguageService service = new();
        IReadOnlyList<ScratchAsmCompletion> aliasCompletions = service.GetCompletions(source, source.IndexOf("mot", StringComparison.Ordinal) + 3);
        IReadOnlyList<ScratchAsmCompletion> symbolCompletions = service.GetCompletions(source, source.IndexOf("@greenflag", StringComparison.Ordinal));
        IReadOnlyList<ScratchAsmCatalogItem> catalog = service.SearchCatalog("motion.move", limit: 5);

        Assert.IsTrue(aliasCompletions.Any(item => item.Label == "motion.move"));
        Assert.IsTrue(symbolCompletions.Any(item => item.Label == "score"));
        Assert.AreEqual("motion.move", catalog[0].Alias);
        Assert.AreEqual("motion_movesteps", catalog[0].Opcode);
    }

    [TestMethod]
    public void ProjectLoaderUsesManifestAndWorkspacePathPolicyRejectsEscapes()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "main.sasm"), "stage {}\n");
            File.WriteAllText(Path.Combine(root, "scratchasm.json"), """
{
  "entry": "main.sasm",
  "output": "build/game.sb3",
  "baseline": "baseline.sb3"
}
""");

            ScratchAsmProject project = ScratchAsmProjectLoader.Load(root);
            WorkspacePathPolicy policy = new(root);

            Assert.AreEqual(Path.Combine(root, "main.sasm"), project.EntryPath);
            Assert.AreEqual(Path.Combine(root, "build", "game.sb3"), project.OutputPath);
            Assert.Throws<InvalidDataException>(() => policy.Resolve("../outside.sasm"));
            Assert.Throws<InvalidDataException>(() => policy.Resolve(Path.Combine(root, "main.sasm")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void WorkspaceServiceFindsCanonicalAndCompatibilitySourcesOnly()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "one.sasm"), string.Empty);
            File.WriteAllText(Path.Combine(root, "nested", "two.mono"), string.Empty);
            File.WriteAllText(Path.Combine(root, "nested", "ignore.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "bin", "generated.sasm"), string.Empty);

            IReadOnlyList<string> files = new WorkspaceService(root).FindSourceFiles();

            Assert.HasCount(2, files);
            Assert.IsTrue(files.Any(path => path.EndsWith("one.sasm", StringComparison.Ordinal)));
            Assert.IsTrue(files.Any(path => path.EndsWith("two.mono", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scratchasm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
