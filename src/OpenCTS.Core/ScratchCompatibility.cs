using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

public sealed record ProjectToolReport(ScratchProjectDocument Document, int OriginalBytes, int OutputBytes,
    int FoldedReporters, IReadOnlyList<string> Changes, IReadOnlyList<ValidationIssue> Issues)
{
    public bool CanExportVanilla => !Issues.Any(issue => issue.Code is "SASM4001" or "SASM4003" or "SASM4004");
}

public static class ScratchCompatibility
{
    public const int SaveLimit = 5 * 1024 * 1024;
    public const int WarningThreshold = 9 * SaveLimit / 10;
    private static readonly HashSet<string> NativeOpcodes = CtsBlockRegistry.Definitions.Select(item => item.Opcode)
        .Concat(CtsBlockRegistry.Definitions.SelectMany(item => item.Bindings).Select(item => item.MenuOpcode).OfType<string>())
        .Concat(new[] { "procedures_definition", "procedures_prototype", "procedures_call", "argument_reporter_string_number", "argument_reporter_boolean" })
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<ValidationIssue> Inspect(JsonObject project, int jsonBytes)
    {
        List<ValidationIssue> issues = [];
        HashSet<string> custom = [];
        foreach (JsonObject target in Targets(project))
            foreach (JsonObject block in Blocks(target).Select(pair => pair.Value).OfType<JsonObject>())
            {
                string opcode = block["opcode"]?.ToString() ?? "";
                if (!NativeOpcodes.Contains(opcode)) custom.Add(opcode);
            }
        foreach (JsonObject monitor in (project["monitors"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string opcode = monitor["opcode"]?.ToString() ?? "";
            if (!NativeOpcodes.Contains(opcode) && opcode is not "data_variable" and not "data_listcontents") custom.Add(opcode);
        }
        foreach (JsonNode? extension in project["extensions"] as JsonArray ?? [])
        {
            string id = extension?.ToString() ?? "";
            if (!ScratchCategoryColors.ExtensionPalette.ContainsKey(id)) custom.Add(id);
        }
        if (project["extensionURLs"] is JsonObject urls)
            foreach (string id in urls.Select(pair => pair.Key)) custom.Add(id);
        if (custom.Count > 0) issues.Add(Warning("SASM4001",
            $"TurboWarp/custom extensions are not compatible with vanilla Scratch: {string.Join(", ", custom.Order().Take(12))}{(custom.Count > 12 ? ", ..." : "")}. Extension code is not run by this IDE."));
        if (jsonBytes >= WarningThreshold) issues.Add(Warning(jsonBytes > SaveLimit ? "SASM4003" : "SASM4002",
            $"project.json is {jsonBytes / 1048576d:F2} MiB ({jsonBytes:N0} bytes). {(jsonBytes > SaveLimit ? "Exceeds" : "Approaching")} the 5 MiB Scratch save budget. Try Compact / Optimize."));
        if (project["meta"]?["tw"] is JsonObject tw && tw.Count > 0 ||
            project["stageWidth"] is JsonValue width && width.ToString() != "480" ||
            project["stageHeight"] is JsonValue height && height.ToString() != "360")
            issues.Add(Warning("SASM4004", "TurboWarp runtime settings require manual review. Vanilla export cannot promise the same behavior."));
        return issues;
    }

    public static ProjectToolReport Optimize(ScratchProjectDocument document, bool vanilla)
    {
        ScratchProjectDocument output = document.Copy();
        int originalBytes = document.JsonBytes.Length;
        List<string> changes = [];
        int converted = 0, folded = 0;
        bool custom = Inspect(output.Project, 0).Any(issue => issue.Code == "SASM4001");
        if (vanilla)
        {
            // Only finite, integral literal operands are lowered: no coercion or extension side effects.
            foreach (JsonObject target in Targets(output.Project))
                foreach (JsonObject block in Blocks(target).Select(pair => pair.Value).OfType<JsonObject>())
                {
                    string opcode = block["opcode"]?.ToString() ?? "";
                    if (!opcode.StartsWith("Bitwise_", StringComparison.Ordinal) || block["inputs"] is not JsonObject inputs ||
                        (output.Project["monitors"] as JsonArray ?? []).Any(monitor => (monitor?["opcode"]?.ToString() ?? "").StartsWith("Bitwise_", StringComparison.Ordinal)) ||
                        output.Project["extensionURLs"]?["Bitwise"]?.ToString() != "https://extensions.turbowarp.org/bitwise.js" ||
                        block["mutation"] is not null || block["fields"] is JsonObject { Count: > 0 }) continue;
                    bool unary = opcode == "Bitwise_bitwiseNot";
                    if (inputs.Count != (unary ? 1 : 2) || block["next"] is not null) continue;
                    if (!Literal(inputs[unary ? "CENTRAL" : "LEFT"], out double left) || !Int32Value(left) ||
                        !unary && (!Literal(inputs["RIGHT"], out double rightValue) || !Int32Value(rightValue))) continue;
                    int a = (int)left;
                    int b = !unary && Literal(inputs["RIGHT"], out double right) ? (int)right : 0;
                    double? value = opcode switch
                    {
                        "Bitwise_bitwiseAnd" => a & b, "Bitwise_bitwiseOr" => a | b,
                        "Bitwise_bitwiseXor" => a ^ b, "Bitwise_bitwiseNot" => ~a,
                        "Bitwise_bitwiseLeftShift" => a << b, "Bitwise_bitwiseRightShift" => a >> b,
                        "Bitwise_bitwiseLogicalRightShift" => (uint)a >> b, _ => null
                    };
                    if (value is null) continue;
                    block["opcode"] = "operator_add";
                    block["inputs"] = new JsonObject { ["NUM1"] = Input(value.Value), ["NUM2"] = Input(0) };
                    block["fields"] = new JsonObject();
                    converted++;
                }
            if (converted > 0 && !Targets(output.Project).Any(target => Blocks(target).Any(pair =>
                pair.Value is JsonObject block && (block["opcode"]?.ToString() ?? "").StartsWith("Bitwise_", StringComparison.Ordinal))))
            {
                if (output.Project["extensions"] is JsonArray extensions)
                    for (int i = extensions.Count - 1; i >= 0; i--) if (extensions[i]?.ToString() == "Bitwise") extensions.RemoveAt(i);
                (output.Project["extensionURLs"] as JsonObject)?.Remove("Bitwise");
                (output.Project["extensionColors"] as JsonObject)?.Remove("Bitwise");
            }
            if (converted > 0) changes.Add($"Lowered {converted} literal Bitwise reporters to native Scratch arithmetic.");
            custom = Inspect(output.Project, 0).Any(issue => issue.Code == "SASM4001");
        }
        if (!custom)
        {
            foreach (JsonObject target in Targets(output.Project))
            {
                JsonObject blocks = Blocks(target);
                foreach ((string id, JsonNode? node) in blocks.ToArray())
                {
                    if (node is not JsonObject block || block["inputs"] is not JsonObject inputs || block["parent"] is not JsonValue parentId ||
                        inputs.Count != 2 || block["mutation"] is not null || block["next"] is not null ||
                        block["comment"] is not null || (target["comments"] as JsonObject ?? []).Any(pair => pair.Value?["blockId"]?.ToString() == id) ||
                        (output.Project["monitors"] as JsonArray ?? []).Any(monitor => monitor?["id"]?.ToString() == id)) continue;
                    if (!Literal(inputs["NUM1"], out double a) || !Literal(inputs["NUM2"], out double b)) continue;
                    double value = block["opcode"]?.ToString() switch
                    {
                        "operator_add" => a + b, "operator_subtract" => a - b,
                        "operator_multiply" => a * b, "operator_divide" when b != 0 => a / b, _ => double.NaN
                    };
                    if (!double.IsFinite(value) || value == 0 && double.IsNegative(value)) continue;
                    if (blocks[parentId.ToString()] is not JsonObject parent || parent["inputs"] is not JsonObject parentInputs) continue;
                    JsonArray[] references = parentInputs.Select(pair => pair.Value).OfType<JsonArray>()
                        .Where(input => input.Skip(1).Any(item => item is JsonValue && item.ToString() == id)).ToArray();
                    if (references.Length != 1) continue;
                    JsonArray reference = references[0];
                    for (int i = 1; i < reference.Count; i++)
                        if (reference[i] is JsonValue && reference[i]!.ToString() == id) reference[i] = Number(value);
                    blocks.Remove(id);
                    folded++;
                }
            }
        }
        if (folded > 0) changes.Add($"Folded {folded} constant arithmetic reporters, including variable values and list indices.");
        output.Compact = true;
        int outputBytes = output.JsonBytes.Length;
        changes.Add($"Compacted JSON without deleting variables, lists, scripts, assets, or comments: {originalBytes:N0} -> {outputBytes:N0} bytes.");
        if (custom) changes.Add("Runtime optimizations skipped: custom extensions may inspect the block graph.");
        return new ProjectToolReport(output, originalBytes, outputBytes, folded, changes, Inspect(output.Project, outputBytes));
    }
    internal static IEnumerable<JsonObject> Targets(JsonObject project) => (project["targets"] as JsonArray ?? []).OfType<JsonObject>();
    internal static JsonObject Blocks(JsonObject target) => target["blocks"] as JsonObject ?? [];
    private static bool Int32Value(double value) => value >= int.MinValue && value <= int.MaxValue && Math.Truncate(value) == value;
    private static JsonArray Number(double value) => new(4, JsonValue.Create(value));
    private static JsonArray Input(double value) => new(1, Number(value));
    private static bool Literal(JsonNode? input, out double value)
    {
        value = 0;
        return input is JsonArray { Count: 2 } tuple && tuple[0]?.ToString() == "1" && tuple[1] is JsonArray { Count: 2 } primitive &&
            primitive[0]?.ToString() is "4" or "5" or "6" or "7" or "8" &&
            double.TryParse(primitive[1]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
    private static ValidationIssue Warning(string code, string message) => new(message, "$", null, DiagnosticSeverity.Warning, code);
}
