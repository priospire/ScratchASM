using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCTS.Core;
using OpenCTS.LanguageServices;

namespace ScratchASM.LanguageHost;

public sealed class McpDispatcher
{
    private readonly ScratchAsmLanguageService _languageService = new();
    private readonly ScratchProjectConverter _converter = new();
    private readonly WorkspacePathPolicy _policy;
    private readonly WorkspaceService _workspace;

    public McpDispatcher(string workspaceRoot)
    {
        _policy = new WorkspacePathPolicy(workspaceRoot);
        _workspace = new WorkspaceService(workspaceRoot);
    }

    public JsonObject? Handle(JsonObject request)
    {
        try { return HandleCore(request); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = request["id"]?.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32602, ["message"] = ex.Message }
            };
        }
    }

    private JsonObject? HandleCore(JsonObject request)
    {
        string? method = request["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
        {
            return null;
        }

        try
        {
            return method switch
            {
                "initialize" => Response(request, Initialize()),
                "tools/list" => Response(request, new JsonObject { ["tools"] = Tools() }),
                "tools/call" => Response(request, CallTool(request["params"] as JsonObject ?? [])),
                "resources/list" => Response(request, ListResources()),
                "resources/read" => Response(request, ReadResource(request["params"] as JsonObject ?? [])),
                _ when request.ContainsKey("id") => Response(request, ToolError($"Unsupported MCP method '{method}'.")),
                _ => null
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return Response(request, ToolError(ex.Message));
        }
    }

    private JsonObject Initialize()
    {
        return new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ScratchASM Language Host",
                ["version"] = "0.2.0"
            }
        };
    }

    private JsonObject CallTool(JsonObject parameters)
    {
        string name = parameters["name"]?.GetValue<string>() ?? string.Empty;
        JsonObject arguments = parameters["arguments"] as JsonObject ?? [];
        try
        {
            return name switch
            {
                "analyze_source" => AnalyzeSource(arguments),
                "compile_to_sb3" => CompileToSb3(arguments),
                "decompile_sb3" => DecompileSb3(arguments),
                "merge_edited_source" => MergeEditedSource(arguments),
                "repair_input" => RepairInput(arguments),
                "lookup_catalog" => LookupCatalog(arguments),
                "get_project_info" => GetProjectInfo(arguments),
                _ => ToolError($"Unknown tool '{name}'.")
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return ToolError(ex.Message);
        }
    }

    private JsonObject AnalyzeSource(JsonObject arguments)
    {
        string source;
        string sourceName;
        if (arguments["source"]?.GetValue<string>() is string inlineSource)
        {
            source = inlineSource;
            sourceName = arguments["sourceName"]?.GetValue<string>() ?? "inline.sasm";
        }
        else
        {
            string path = ResolveRequiredPath(arguments, "path");
            source = File.ReadAllText(path);
            sourceName = path;
        }

        DocumentAnalysis analysis = _languageService.Analyze(source, sourceName);
        return ToolText(JsonSerializer.Serialize(new
        {
            diagnostics = analysis.Diagnostics,
            symbols = analysis.Symbols,
            colors = analysis.ColorSpans
        }, PrettyJson));
    }

    private JsonObject CompileToSb3(JsonObject arguments)
    {
        string input = ResolveRequiredPath(arguments, "path");
        string output = ResolveRequiredPath(arguments, "output");
        string? source = arguments["source"]?.GetValue<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? _policy.RootPath);

        ConversionResult result = _converter.ConvertToSb3(input, output, new ConversionOptions
        {
            Overwrite = arguments["overwrite"]?.GetValue<bool>() ?? true,
            ScratchAsmSourceText = source,
            AttemptSafeRepair = arguments["repair"]?.GetValue<bool>() ?? false
        });

        return ConversionToolResult(result);
    }

    private JsonObject DecompileSb3(JsonObject arguments)
    {
        string path = ResolveRequiredPath(arguments, "path");
        if (arguments["output"] is not null)
            return ConversionToolResult(_converter.ConvertToScratchAsm(path, ResolveRequiredPath(arguments, "output"),
                arguments["overwrite"]?.GetValue<bool>() ?? false));
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(path);
        return ToolText(session.SourceText, session.Issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error));
    }

    private JsonObject MergeEditedSource(JsonObject arguments)
    {
        string path = ResolveRequiredPath(arguments, "path");
        string output = ResolveRequiredPath(arguments, "output");
        string source = arguments["source"]?.GetValue<string>() ??
            throw new InvalidDataException("merge_edited_source requires a 'source' string argument.");
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(path);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? _policy.RootPath);
        ConversionResult result = session.WriteEdited(source, output, arguments["overwrite"]?.GetValue<bool>() ?? true);
        return ConversionToolResult(result);
    }

    private JsonObject RepairInput(JsonObject arguments)
    {
        string input = ResolveRequiredPath(arguments, "path");
        string output = ResolveRequiredPath(arguments, "output");
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? _policy.RootPath);
        ConversionResult result = _converter.ConvertToSb3(input, output, new ConversionOptions
        {
            Overwrite = arguments["overwrite"]?.GetValue<bool>() ?? true,
            AttemptSafeRepair = true
        });
        return ConversionToolResult(result);
    }

    private JsonObject LookupCatalog(JsonObject arguments)
    {
        string query = arguments["query"]?.GetValue<string>() ?? string.Empty;
        string? category = arguments["category"]?.GetValue<string>();
        string? shape = arguments["shape"]?.GetValue<string>();
        int limit = arguments["limit"]?.GetValue<int>() ?? 20;
        return ToolText(JsonSerializer.Serialize(
            _languageService.SearchCatalog(query, category, shape, limit),
            PrettyJson));
    }

    private JsonObject GetProjectInfo(JsonObject arguments)
    {
        string root = arguments["path"]?.GetValue<string>() is string relativePath
            ? ResolveRequiredPath(arguments, "path")
            : _policy.RootPath;
        ScratchAsmProject project = ScratchAsmProjectLoader.Load(root);
        return ToolText(JsonSerializer.Serialize(new
        {
            project,
            sourceFiles = _workspace.FindSourceFiles()
                .Select(path => Path.GetRelativePath(_policy.RootPath, path))
                .ToArray()
        }, PrettyJson));
    }

    private JsonObject ListResources()
    {
        JsonArray resources = [];
        foreach (string path in _workspace.FindSourceFiles())
        {
            string relative = Path.GetRelativePath(_policy.RootPath, path);
            resources.Add(new JsonObject
            {
                ["uri"] = "scratchasm://workspace/" + relative.Replace('\\', '/'),
                ["name"] = relative,
                ["mimeType"] = "text/x-scratchasm"
            });
        }

        return new JsonObject { ["resources"] = resources };
    }

    private JsonObject ReadResource(JsonObject arguments)
    {
        string uri = arguments["uri"]?.GetValue<string>() ?? string.Empty;
        const string prefix = "scratchasm://workspace/";
        if (!uri.StartsWith(prefix, StringComparison.Ordinal))
        {
            return ToolError("Only scratchasm://workspace resources are supported.");
        }

        string path = _policy.Resolve(uri[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = uri,
                ["mimeType"] = "text/x-scratchasm",
                ["text"] = File.ReadAllText(path)
            })
        };
    }

    private JsonArray Tools()
    {
        return
        [
            Tool("analyze_source", "Analyze ScratchASM source and return diagnostics, symbols, and color spans."),
            Tool("compile_to_sb3", "Compile a ScratchASM, .sb3, project.json, or project folder input to a Scratch-readable .sb3."),
            Tool("decompile_sb3", "Decompile a .sb3 archive to editable ScratchASM. Optional output writes portable .sasm source and its project companion."),
            Tool("merge_edited_source", "Merge edited ScratchASM source back into an existing .sb3 while preserving unknown assets and JSON."),
            Tool("repair_input", "Attempt safe repair for any supported input and write a repaired .sb3 when possible."),
            Tool("lookup_catalog", "Search the ScratchASM alias catalog by alias, opcode, category, or shape."),
            Tool("get_project_info", "Return project manifest/defaults and ScratchASM source files in the workspace.")
        ];
    }

    private static JsonObject Tool(string name, string description)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true,
                ["properties"] = name == "decompile_sb3" ? new JsonObject
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Workspace-relative input .sb3 path." },
                    ["output"] = new JsonObject { ["type"] = "string", ["description"] = "Optional workspace-relative .sasm output path." },
                    ["overwrite"] = new JsonObject { ["type"] = "boolean", ["default"] = false }
                } : new JsonObject()
            }
        };
    }

    private string ResolveRequiredPath(JsonObject arguments, string name)
    {
        string value = arguments[name]?.GetValue<string>() ??
            throw new InvalidDataException($"Tool argument '{name}' is required.");
        try
        {
            return _policy.Resolve(value);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Path '{value}' is outside or invalid for this workspace: {ex.Message}", ex);
        }
    }

    private static JsonObject ConversionToolResult(ConversionResult result)
    {
        string text = JsonSerializer.Serialize(new
        {
            result.Success,
            result.OutputPath,
            result.Issues
        }, PrettyJson);
        return ToolText(text, !result.Success);
    }

    private static JsonObject ToolText(string text, bool isError = false)
    {
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            }),
            ["isError"] = isError
        };
    }

    private static JsonObject ToolError(string message) => ToolText(message, isError: true);

    private static JsonObject Response(JsonObject request, JsonNode result)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (request.TryGetPropertyValue("id", out JsonNode? id))
        {
            response["id"] = id?.DeepClone();
        }

        return response;
    }

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true
    };
}
