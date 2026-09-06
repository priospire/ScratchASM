using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ScratchAsmLanguageTests
{
    [TestMethod]
    public void ExposesScratchAsmLanguageIdentityAndExtensionPolicy()
    {
        Assert.AreEqual("ScratchASM", GetPublicConstant(nameof(ScratchAsmLanguage.DisplayName)));
        Assert.AreEqual(".sasm", GetPublicConstant(nameof(ScratchAsmLanguage.CanonicalExtension)));
        Assert.AreEqual(".mono", GetPublicConstant(nameof(ScratchAsmLanguage.CompatibilityExtension)));
        Assert.IsTrue(ScratchAsmLanguage.IsSupportedSourceName("game.sasm"));
        Assert.IsTrue(ScratchAsmLanguage.IsSupportedSourceName("GAME.MONO"));
        Assert.IsTrue(ScratchAsmLanguage.IsSupportedSourceName("game.cts"));
    }

    private static string GetPublicConstant(string name)
    {
        return (string)typeof(ScratchAsmLanguage).GetField(name)!.GetRawConstantValue()!;
    }

    [TestMethod]
    public void MonoSourceProducesOneCompatibilityWarningPerCompile()
    {
        CtsCompileResult result = CtsCompiler.Compile(MinimalProject, "legacy.mono");

        AssertNoErrors(result.Diagnostics);
        CtsDiagnostic[] warnings = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning &&
                diagnostic.Message.Contains(".mono", StringComparison.Ordinal))
            .ToArray();
        Assert.HasCount(1, warnings);

        CtsCompileResult canonical = CtsCompiler.Compile(MinimalProject, "current.sasm");
        Assert.IsFalse(canonical.Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains(".mono", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CompilesConstantsEnumsStructsAndContextualVariables()
    {
        const string source = """
const Base = 2
const Start = Base ^ 3
enum Mode {
  Idle = 1
  Run = Start + 1
}
struct Stats {
  score: num = Start
  label: str = "ready"
}
stage {
  global var total = Mode.Run
  cloud global var shared = 0
  var contextual = Base
  game: Stats
  @greenflag:
    game.score += 1
    contextual = game.score
}
sprite "Hero" {
  sprite var energy = 10
  var contextual = 1
  stats: Stats
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "types.sasm");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement stage = GetTarget(document.RootElement, "Stage");
        JsonElement sprite = GetTarget(document.RootElement, "Hero");

        AssertVariable(stage, "total", "9", cloud: false);
        AssertVariable(stage, "shared", "0", cloud: true);
        AssertVariable(stage, "contextual", "2", cloud: false);
        AssertVariable(stage, "game.score", "8", cloud: false);
        AssertVariable(stage, "game.label", "ready", cloud: false);
        AssertVariable(sprite, "energy", "10", cloud: false);
        AssertVariable(sprite, "contextual", "1", cloud: false);
        AssertVariable(sprite, "stats.score", "8", cloud: false);

        JsonElement meta = document.RootElement.GetProperty("meta");
        Assert.IsTrue(meta.GetProperty("scratchasm-ready").GetBoolean());
    }

    [TestMethod]
    public void RejectsInvalidVariableScopePlacement()
    {
        const string source = """
stage {
  sprite var wrong = 0
}
sprite "Hero" {
  global var wrong = 0
  cloud global var shared = 0
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "scope.sasm");

        Assert.IsGreaterThanOrEqualTo(3, result.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("stage", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("sprite", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ConstantsRequirePriorDeterministicValuesAndEnumsRequireExplicitValues()
    {
        const string source = """
const Forward = Later + 1
const Random = random(1, 10)
const Later = 2
enum Mode {
  Missing
}
stage {
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "constants.sasm");

        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("prior", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("deterministic", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("explicit", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void RejectsNestedStructsStructSignaturesAndWholeInstanceAssignment()
    {
        const string source = """
struct Inner {
  value: num = 0
}
struct Outer {
  nested: Inner
}
stage {
  left: Inner
  right: Inner
  proc invalid(value: Inner):
    left = right
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "invalid-structs.sasm");

        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("nested", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("parameter", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("whole-instance", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void GeneratedIdsAreStableAndCollisionSafe()
    {
        const string source = """
stage {
  var a-b = 1
  var a_b = 2
  @greenflag:
    a-b += a_b
}
""";

        CtsCompileResult first = CtsCompiler.Compile(source, "ids.sasm");
        CtsCompileResult second = CtsCompiler.Compile(source, "ids.sasm");

        AssertNoErrors(first.Diagnostics);
        AssertNoErrors(second.Diagnostics);
        CollectionAssert.AreEqual(first.ProjectJsonBytes, second.ProjectJsonBytes);

        using JsonDocument document = JsonDocument.Parse(first.ProjectJsonBytes);
        JsonElement variables = GetTarget(document.RootElement, "Stage").GetProperty("variables");
        string[] ids = variables.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.HasCount(2, ids);
        Assert.AreNotEqual(ids[0], ids[1]);
    }

    [TestMethod]
    public void ClassifierColorsScratchAsmDeclarationsAsScratchCategories()
    {
        const string source = """
const Limit = 3
enum Mode { Idle = 0 }
struct Stats { score: num = 0 }
stage {
  global var total = Limit
  game: Stats
  proc run():
    local var count = game.score
    count += 1
}
""";

        IReadOnlyList<CtsColorSpan> spans = CtsSyntaxClassifier.Classify(source);

        AssertColor(source, spans, "const", ScratchCategoryColors.Operators);
        AssertColor(source, spans, "enum", ScratchCategoryColors.Operators);
        AssertColor(source, spans, "struct", ScratchCategoryColors.Variables);
        AssertColor(source, spans, "global", ScratchCategoryColors.Variables);
        AssertColor(source, spans, "local", ScratchCategoryColors.Variables);
        AssertColor(source, spans, "Stats", ScratchCategoryColors.Variables);
        AssertColor(source, spans, "game.score", ScratchCategoryColors.Variables);
        AssertColor(source, spans, "count", ScratchCategoryColors.Variables);
    }

    private const string MinimalProject = """
stage {
  @greenflag:
    looks.say "hello"
}
""";

    private static void AssertNoErrors(IEnumerable<CtsDiagnostic> diagnostics)
    {
        CtsDiagnostic[] errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }

    private static JsonElement GetTarget(JsonElement root, string name)
    {
        return root.GetProperty("targets").EnumerateArray()
            .Single(target => target.GetProperty("name").GetString() == name);
    }

    private static void AssertVariable(JsonElement target, string name, string value, bool cloud)
    {
        JsonElement variable = target.GetProperty("variables").EnumerateObject()
            .Single(property => property.Value[0].GetString() == name).Value;
        Assert.AreEqual(value, variable[1].ToString());
        Assert.AreEqual(cloud, variable.GetArrayLength() == 3 && variable[2].GetBoolean());
    }

    private static void AssertColor(string source, IReadOnlyList<CtsColorSpan> spans, string text, string color)
    {
        int index = source.IndexOf(text, StringComparison.Ordinal);
        Assert.IsTrue(spans.Any(span => span.Start == index && span.Length == text.Length && span.Color == color),
            $"Expected '{text}' to have color {color}.");
    }
}
