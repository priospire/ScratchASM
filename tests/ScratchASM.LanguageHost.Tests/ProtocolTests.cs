using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScratchASM.LanguageHost.Tests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void MalformedProtocolParametersDoNotCrashDispatchers()
    {
        JsonObject response = new LspDispatcher().Handle(new JsonObject { ["id"] = 1, ["method"] = 7 }).Single();
        Assert.AreEqual(-32602, response["error"]!["code"]!.GetValue<int>());
        string root = CreateTemporaryDirectory();
        try
        {
            JsonObject result = new McpDispatcher(root).Handle(new JsonObject { ["id"] = 1, ["method"] = true })!;
            Assert.AreEqual(-32602, result["error"]!["code"]!.GetValue<int>());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void McpExportsPortableSourceThatCanBeRecompiled()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "main.sasm"), "stage {\n  var score = 1\n}\n");
            McpDispatcher dispatcher = new(root);
            JsonObject Call(string name, string input, string output) => dispatcher.Handle(Request(1, "tools/call", new JsonObject
            {
                ["name"] = name, ["arguments"] = new JsonObject { ["path"] = input, ["output"] = output }
            }))!;
            Assert.IsFalse(Call("compile_to_sb3", "main.sasm", "original.sb3")["result"]!["isError"]!.GetValue<bool>());
            Assert.IsFalse(Call("decompile_sb3", "original.sb3", "exported.sasm")["result"]!["isError"]!.GetValue<bool>());
            File.Delete(Path.Combine(root, "original.sb3"));
            Assert.IsFalse(Call("compile_to_sb3", "exported.sasm", "rebuilt.sb3")["result"]!["isError"]!.GetValue<bool>());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void LspInitializesAndPublishesScratchAsmDiagnosticsAndCompletions()
    {
        LspDispatcher dispatcher = new();
        IReadOnlyList<JsonObject> initialize = dispatcher.Handle(Request(1, "initialize", new JsonObject()));
        IReadOnlyList<JsonObject> opened = dispatcher.Handle(Notification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///workspace/main.sasm",
                ["version"] = 3,
                ["text"] = "stage {\n  @greenflag:\n    motion.move(\n}\n"
            }
        }));
        IReadOnlyList<JsonObject> completion = dispatcher.Handle(Request(2, "textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///workspace/main.sasm" },
            ["position"] = new JsonObject { ["line"] = 2, ["character"] = 14 }
        }));

        Assert.IsNotNull(initialize[0]["result"]?["capabilities"]?["completionProvider"]);
        Assert.AreEqual("textDocument/publishDiagnostics", opened.Single()["method"]?.GetValue<string>());
        Assert.IsGreaterThan(0, opened.Single()["params"]?["diagnostics"]?.AsArray().Count ?? 0);
        Assert.IsTrue(completion[0]["result"]?.AsArray().Any(item => item?["label"]?.GetValue<string>() == "motion.move"));
    }

    [TestMethod]
    public void McpListsRequiredToolsAndRejectsEscapingPaths()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            McpDispatcher dispatcher = new(root);
            JsonObject initialized = dispatcher.Handle(Request(1, "initialize", new JsonObject()))!;
            JsonObject listed = dispatcher.Handle(Request(2, "tools/list", new JsonObject()))!;
            JsonObject rejected = dispatcher.Handle(Request(3, "tools/call", new JsonObject
            {
                ["name"] = "analyze_source",
                ["arguments"] = new JsonObject { ["path"] = "../outside.sasm" }
            }))!;

            Assert.AreEqual("ScratchASM Language Host", initialized["result"]?["serverInfo"]?["name"]?.GetValue<string>());
            string[] names = listed["result"]?["tools"]?.AsArray()
                .Select(tool => tool?["name"]?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
            CollectionAssert.IsSubsetOf(
                new[] { "analyze_source", "compile_to_sb3", "decompile_sb3", "merge_edited_source", "repair_input", "lookup_catalog", "get_project_info" },
                names);
            Assert.IsTrue(rejected["result"]?["isError"]?.GetValue<bool>());
            StringAssert.Contains(rejected["result"]?["content"]?[0]?["text"]?.GetValue<string>(), "workspace");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void McpRepairsScratchAsmSourceWithinWorkspace()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "broken.sasm"), "global var score = 0\n@greenflag:\n  set score to 5\n");
            McpDispatcher dispatcher = new(root);

            JsonObject repaired = dispatcher.Handle(Request(1, "tools/call", new JsonObject
            {
                ["name"] = "repair_input",
                ["arguments"] = new JsonObject { ["path"] = "broken.sasm", ["output"] = "build/repaired.sb3" }
            }))!;

            Assert.IsFalse(repaired["result"]?["isError"]?.GetValue<bool>() ?? true);
            Assert.IsTrue(File.Exists(Path.Combine(root, "build", "repaired.sb3")));
            StringAssert.Contains(repaired["result"]?["content"]?[0]?["text"]?.GetValue<string>(), "REPAIR200");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void McpCompilesAndDecompilesWithinWorkspace()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "main.sasm"), "stage {\n  global var score = 0\n}\n");
            McpDispatcher dispatcher = new(root);

            JsonObject compiled = dispatcher.Handle(Request(1, "tools/call", new JsonObject
            {
                ["name"] = "compile_to_sb3",
                ["arguments"] = new JsonObject { ["path"] = "main.sasm", ["output"] = "build/main.sb3" }
            }))!;
            JsonObject decompiled = dispatcher.Handle(Request(2, "tools/call", new JsonObject
            {
                ["name"] = "decompile_sb3",
                ["arguments"] = new JsonObject { ["path"] = "build/main.sb3" }
            }))!;

            Assert.IsFalse(compiled["result"]?["isError"]?.GetValue<bool>() ?? true);
            Assert.IsTrue(File.Exists(Path.Combine(root, "build", "main.sb3")));
            using ZipArchive archive = ZipFile.OpenRead(Path.Combine(root, "build", "main.sb3"));
            Assert.IsNotNull(archive.GetEntry("project.json"));
            StringAssert.Contains(decompiled["result"]?["content"]?[0]?["text"]?.GetValue<string>(), "global var score");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonObject Request(int id, string method, JsonObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters
    };

    private static JsonObject Notification(string method, JsonObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["method"] = method,
        ["params"] = parameters
    };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scratchasm-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
