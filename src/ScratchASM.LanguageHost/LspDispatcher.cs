using System.Text.Json.Nodes;
using OpenCTS.Core;
using OpenCTS.LanguageServices;

namespace ScratchASM.LanguageHost;

public sealed class LspDispatcher
{
    private readonly ScratchAsmLanguageService _languageService = new();
    private readonly Dictionary<string, TextDocumentState> _documents = new(StringComparer.Ordinal);

    public IReadOnlyList<JsonObject> Handle(JsonObject message)
    {
        try { return HandleCore(message); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or InvalidDataException)
        {
            return message.ContainsKey("id") ? [new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = message["id"]?.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32602, ["message"] = ex.Message }
            }] : [];
        }
    }

    private IReadOnlyList<JsonObject> HandleCore(JsonObject message)
    {
        string? method = message["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
        {
            return [];
        }

        return method switch
        {
            "initialize" => [Response(message, InitializeResult())],
            "shutdown" => [Response(message, null)],
            "textDocument/didOpen" => HandleDidOpen(message),
            "textDocument/didChange" => HandleDidChange(message),
            "textDocument/didClose" => HandleDidClose(message),
            "textDocument/completion" => [Response(message, CompletionResult(message))],
            "textDocument/signatureHelp" => [Response(message, SignatureHelpResult(message))],
            "textDocument/documentSymbol" => [Response(message, DocumentSymbolResult(message))],
            _ when message.ContainsKey("id") => [Response(message, new JsonObject())],
            _ => []
        };
    }

    private IReadOnlyList<JsonObject> HandleDidClose(JsonObject message)
    {
        string? uri = message["params"]?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri is null) return [];
        _documents.Remove(uri);
        return [new JsonObject
        {
            ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() }
        }];
    }

    private IReadOnlyList<JsonObject> HandleDidOpen(JsonObject message)
    {
        JsonObject? document = message["params"]?["textDocument"] as JsonObject;
        string? uri = document?["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
        {
            return [];
        }

        string text = document?["text"]?.GetValue<string>() ?? string.Empty;
        int version = document?["version"]?.GetValue<int>() ?? 0;
        _documents[uri] = new TextDocumentState(uri, text, version);
        return [PublishDiagnostics(uri, text, version)];
    }

    private IReadOnlyList<JsonObject> HandleDidChange(JsonObject message)
    {
        string? uri = message["params"]?["textDocument"]?["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
        {
            return [];
        }

        JsonArray? changes = message["params"]?["contentChanges"] as JsonArray;
        string text = changes?.LastOrDefault()?["text"]?.GetValue<string>() ??
            (_documents.TryGetValue(uri, out TextDocumentState? existing) ? existing.Text : string.Empty);
        int version = message["params"]?["textDocument"]?["version"]?.GetValue<int>() ?? 0;
        _documents[uri] = new TextDocumentState(uri, text, version);
        return [PublishDiagnostics(uri, text, version)];
    }

    private JsonObject InitializeResult()
    {
        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["textDocumentSync"] = 1,
                ["completionProvider"] = new JsonObject
                {
                    ["resolveProvider"] = false,
                    ["triggerCharacters"] = new JsonArray(".", "(", "\"", "`")
                },
                ["signatureHelpProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray("(", ",")
                },
                ["documentSymbolProvider"] = true
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ScratchASM Language Host",
                ["version"] = "0.2.0"
            }
        };
    }

    private JsonArray CompletionResult(JsonObject message)
    {
        (string source, int offset) = GetDocumentAndOffset(message);
        JsonArray items = [];
        foreach (ScratchAsmCompletion item in _languageService.GetCompletions(source, offset))
        {
            items.Add(new JsonObject
            {
                ["label"] = item.Label,
                ["insertText"] = item.InsertText,
                ["kind"] = CompletionKind(item.Kind),
                ["detail"] = item.Detail ?? item.Kind
            });
        }

        return items;
    }

    private JsonObject SignatureHelpResult(JsonObject message)
    {
        (string source, int offset) = GetDocumentAndOffset(message);
        JsonArray signatures = [];
        foreach (ScratchAsmSignature signature in _languageService.GetSignatures(source, offset))
        {
            JsonArray parameters = [];
            foreach (string parameter in signature.Parameters)
            {
                parameters.Add(new JsonObject { ["label"] = parameter });
            }

            signatures.Add(new JsonObject
            {
                ["label"] = signature.Label,
                ["documentation"] = signature.Documentation,
                ["parameters"] = parameters
            });
        }

        return new JsonObject
        {
            ["signatures"] = signatures,
            ["activeSignature"] = signatures.Count == 0 ? null : 0,
            ["activeParameter"] = 0
        };
    }

    private JsonArray DocumentSymbolResult(JsonObject message)
    {
        string source = GetDocument(message).Text;
        JsonArray symbols = [];
        foreach (ScratchAsmSymbol symbol in _languageService.Analyze(source).Symbols)
        {
            JsonObject range = ToLspRange(symbol.Range.Start, symbol.Range.End);
            symbols.Add(new JsonObject
            {
                ["name"] = symbol.Name,
                ["kind"] = SymbolKind(symbol.Kind),
                ["detail"] = symbol.Detail,
                ["range"] = range.DeepClone(),
                ["selectionRange"] = range
            });
        }

        return symbols;
    }

    private JsonObject PublishDiagnostics(string uri, string text, int version)
    {
        DocumentAnalysis analysis = _languageService.Analyze(text, UriToSourceName(uri), version);
        JsonArray diagnostics = [];
        foreach (StructuredDiagnostic diagnostic in analysis.Diagnostics)
        {
            diagnostics.Add(new JsonObject
            {
                ["range"] = ToLspRange(diagnostic.Range.Start, diagnostic.Range.End),
                ["severity"] = diagnostic.Severity == "error" ? 1 : 2,
                ["source"] = ScratchAsmLanguage.DisplayName,
                ["code"] = diagnostic.Code,
                ["message"] = diagnostic.Message
            });
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject
            {
                ["uri"] = uri,
                ["version"] = version,
                ["diagnostics"] = diagnostics
            }
        };
    }

    private (string Source, int Offset) GetDocumentAndOffset(JsonObject message)
    {
        TextDocumentState document = GetDocument(message);
        int line = message["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int character = message["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;
        return (document.Text, PositionToOffset(document.Text, line, character));
    }

    private TextDocumentState GetDocument(JsonObject message)
    {
        string? uri = message["params"]?["textDocument"]?["uri"]?.GetValue<string>();
        return uri is not null && _documents.TryGetValue(uri, out TextDocumentState? document)
            ? document
            : new TextDocumentState(uri ?? "untitled:scratchasm", string.Empty, 0);
    }

    private static JsonObject Response(JsonObject request, JsonNode? result)
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

    private static JsonObject ToLspRange(SourceLocation start, SourceLocation end)
    {
        int startLine = Math.Max(0, start.Line - 1);
        int startColumn = Math.Max(0, start.Column - 1);
        int endLine = Math.Max(startLine, end.Line - 1);
        int endColumn = endLine == startLine
            ? Math.Max(startColumn, end.Column - 1)
            : Math.Max(0, end.Column - 1);
        return new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startColumn },
            ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endColumn }
        };
    }

    private static int PositionToOffset(string source, int zeroBasedLine, int zeroBasedCharacter)
    {
        return CtsSourcePosition.GetOffset(
            source,
            new SourceLocation(Math.Max(0, zeroBasedLine) + 1, Math.Max(0, zeroBasedCharacter) + 1));
    }

    private static string UriToSourceName(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.IsFile
            ? parsed.LocalPath
            : uri;
    }

    private static int CompletionKind(string kind)
    {
        return kind switch
        {
            "keyword" => 14,
            "variable" or "local" or "parameter" => 6,
            "procedure" or "stack" or "reporter" or "boolean" or "hat" => 3,
            "enum" => 13,
            "struct" => 7,
            _ => 1
        };
    }

    private static int SymbolKind(ScratchAsmSymbolKind kind)
    {
        return kind switch
        {
            ScratchAsmSymbolKind.Enum => 10,
            ScratchAsmSymbolKind.EnumMember => 22,
            ScratchAsmSymbolKind.Struct => 23,
            ScratchAsmSymbolKind.Target => 2,
            ScratchAsmSymbolKind.Procedure => 12,
            ScratchAsmSymbolKind.Variable or ScratchAsmSymbolKind.Local or ScratchAsmSymbolKind.Parameter => 13,
            ScratchAsmSymbolKind.List => 18,
            _ => 13
        };
    }

    private sealed record TextDocumentState(string Uri, string Text, int Version);
}
