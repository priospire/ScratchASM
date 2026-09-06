using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScratchASM.LanguageHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.FirstOrDefault(arg =>
            string.Equals(arg, "--lsp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "lsp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "mcp", StringComparison.OrdinalIgnoreCase)) ?? "--lsp";
        string workspace = GetOption(args, "--workspace") ?? Directory.GetCurrentDirectory();

        return mode.Contains("mcp", StringComparison.OrdinalIgnoreCase)
            ? RunMcp(workspace)
            : RunLsp();
    }

    private static int RunLsp()
    {
        LspDispatcher dispatcher = new();
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        while (true)
        {
            JsonObject? message;
            try { if (!TryReadLspMessage(input, out message) || message is null) break; }
            catch (JsonException ex)
            {
                WriteLspMessage(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = null,
                    ["error"] = new JsonObject { ["code"] = -32700, ["message"] = ex.Message }
                });
                continue;
            }
            catch (InvalidDataException ex) { Console.Error.WriteLine(ex.Message); return 1; }
            foreach (JsonObject response in dispatcher.Handle(message))
            {
                WriteLspMessage(output, response);
            }
        }

        return 0;
    }

    private static int RunMcp(string workspace)
    {
        McpDispatcher dispatcher = new(workspace);
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            line = line.TrimStart('\uFEFF');
            JsonObject? request;
            try
            {
                request = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException ex)
            {
                Console.WriteLine(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32700,
                        ["message"] = ex.Message
                    }
                }.ToJsonString());
                continue;
            }

            if (request is null)
            {
                continue;
            }

            JsonObject? response = dispatcher.Handle(request);
            if (response is not null)
            {
                Console.WriteLine(response.ToJsonString());
            }
        }

        return 0;
    }

    private static bool TryReadLspMessage(Stream input, out JsonObject? message)
    {
        message = null;
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? line = ReadAsciiLine(input);
            if (line is null)
            {
                return false;
            }

            if (line.Length == 0)
            {
                break;
            }

            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        if (!headers.TryGetValue("Content-Length", out string? lengthText) ||
            !int.TryParse(lengthText, out int length) ||
            length < 0 || length > 16 * 1024 * 1024)
        {
            return false;
        }

        byte[] payload = new byte[length];
        int read = 0;
        while (read < payload.Length)
        {
            int count = input.Read(payload, read, payload.Length - read);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        message = JsonNode.Parse(payload) as JsonObject;
        return message is not null;
    }

    private static string? ReadAsciiLine(Stream input)
    {
        List<byte> bytes = [];
        while (true)
        {
            int value = input.ReadByte();
            if (value < 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (value == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add((byte)value);
            if (bytes.Count > 8192) throw new InvalidDataException("LSP header exceeds the supported size limit.");
        }
    }

    private static void WriteLspMessage(Stream output, JsonObject response)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(response);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        output.Write(header);
        output.Write(payload);
        output.Flush();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
