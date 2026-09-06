using System.Text.Json;

namespace OpenCTS.Core;

internal static class ScratchGraphValidator
{
    public static void Validate(JsonElement blocks, string path, JsonSourceMap map, List<ValidationIssue> issues)
    {
        Dictionary<string, JsonElement> nodes = new(StringComparer.Ordinal);
        foreach (JsonProperty property in blocks.EnumerateObject())
            if (!nodes.TryAdd(property.Name, property.Value)) Error($"Duplicate block ID '{property.Name}'.", property.Name);
        Dictionary<string, List<string>> edges = new(StringComparer.Ordinal);
        foreach ((string id, JsonElement block) in nodes)
        {
            List<string> children = [];
            edges[id] = children;
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (block.TryGetProperty("next", out JsonElement next) && next.ValueKind == JsonValueKind.String)
                Reference(next.GetString()!, id, children);
            if (block.TryGetProperty("parent", out JsonElement parent) && parent.ValueKind == JsonValueKind.String && !nodes.ContainsKey(parent.GetString()!))
                Error($"Block references missing parent '{parent.GetString()}'.", id);
            if (block.TryGetProperty("inputs", out JsonElement inputs) && inputs.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty input in inputs.EnumerateObject())
                {
                    JsonElement tuple = input.Value;
                    if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 2 ||
                        tuple[0].ValueKind != JsonValueKind.Number || !tuple[0].TryGetInt32(out int mode) || mode is < 1 or > 3 ||
                        tuple.GetArrayLength() != (mode == 3 ? 3 : 2))
                    {
                        Error($"Input '{input.Name}' must be a Scratch input tuple [1|2, value] or [3, value, shadow].", id);
                        continue;
                    }
                    foreach (JsonElement value in tuple.EnumerateArray().Skip(1))
                    {
                        if (value.ValueKind == JsonValueKind.String) Reference(value.GetString()!, id, children);
                        else if (value.ValueKind != JsonValueKind.Null && !IsPrimitive(value, false))
                            Error($"Input '{input.Name}' contains an invalid Scratch primitive.", id);
                    }
                }
            }
        }

        Dictionary<string, byte> colors = new(StringComparer.Ordinal);
        foreach (string root in nodes.Keys)
        {
            if (colors.ContainsKey(root)) continue;
            Stack<(string Id, bool Exit)> pending = new();
            pending.Push((root, false));
            while (pending.TryPop(out var visit))
            {
                if (visit.Exit) { colors[visit.Id] = 2; continue; }
                if (colors.TryGetValue(visit.Id, out byte color))
                {
                    if (color == 1) Error($"Block graph contains a cycle at '{visit.Id}'.", visit.Id);
                    continue;
                }
                colors[visit.Id] = 1;
                pending.Push((visit.Id, true));
                foreach (string child in edges[visit.Id].AsEnumerable().Reverse()) pending.Push((child, false));
            }
        }

        void Reference(string child, string owner, List<string> children)
        {
            if (!nodes.ContainsKey(child)) Error($"Block graph references missing block '{child}'.", owner);
            else children.Add(child);
        }
        void Error(string message, string id)
        {
            string location = $"{path}.{id}";
            issues.Add(new ValidationIssue(message, location, map.GetLocation(location), DiagnosticSeverity.Error, "SB3101"));
        }
    }

    public static bool IsPrimitive(JsonElement value, bool topLevel)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2 || value[0].ValueKind != JsonValueKind.Number ||
            !value[0].TryGetInt32(out int type) || type is < 4 or > 13) return false;
        int length = value.GetArrayLength();
        if (topLevel) return type is 12 or 13 && length == 5 && value[2].ValueKind == JsonValueKind.String;
        return type >= 11 ? length == 3 && value[2].ValueKind == JsonValueKind.String : length == 2;
    }
}
