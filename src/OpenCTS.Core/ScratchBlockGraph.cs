using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal static class ScratchBlockGraph
{
    public static bool Equivalent(JsonObject original, JsonObject compiled, ScratchTargetOrigin origin,
        IEnumerable<ScratchTargetOrigin> allOrigins)
    {
        Dictionary<string, string> originalNames = allOrigins.SelectMany(target =>
            target.Variables.Values.Concat(target.Lists.Values).Concat(target.Broadcasts.Values))
            .GroupBy(data => data.Id).ToDictionary(group => group.Key, group => group.First().Alias);
        Dictionary<string, string> compiledNames = new(StringComparer.Ordinal);
        foreach (string kind in new[] { "variables", "lists", "broadcasts" })
        {
            foreach ((string id, JsonNode? value) in compiled[kind] as JsonObject ?? [])
                compiledNames[id] = value is JsonArray tuple ? Text(tuple[0]) : Text(value);
        }
        // Generated global IDs used by sprites are resolved by their display names below.
        return Signature(original["blocks"] as JsonObject ?? [], originalNames) ==
            Signature(compiled["blocks"] as JsonObject ?? [], compiledNames);
    }

    private static string Signature(JsonObject blocks, IReadOnlyDictionary<string, string> names)
    {
        if (blocks.Any(pair => pair.Value is not JsonObject)) return "primitive-workspace";
        HashSet<string> seen = new(StringComparer.Ordinal);
        JsonArray roots = [];
        foreach ((string id, JsonNode? node) in blocks.Where(pair => (pair.Value as JsonObject)?["topLevel"]?.ToString() == "true")
            .OrderBy(pair => Number(pair.Value?["y"])).ThenBy(pair => Number(pair.Value?["x"])))
        {
            roots.Add(Block(id, 0));
        }
        return seen.Count == blocks.Count ? roots.ToJsonString() : "unreachable:" + blocks.ToJsonString();

        JsonNode? Block(string id, int depth)
        {
            if (depth > 128 || !seen.Add(id) || blocks[id] is not JsonObject block)
                return JsonValue.Create("invalid:" + id);
            JsonObject result = new() { ["opcode"] = block["opcode"]?.DeepClone() };
            JsonObject fields = [];
            foreach ((string key, JsonNode? value) in (block["fields"] as JsonObject ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (value is JsonArray field && field.Count > 0)
                    fields[key] = field.Count > 1 && field[1] is not null
                        ? names.GetValueOrDefault(Text(field[1]), Text(field[0])) : Text(field[0]);
            }
            result["fields"] = fields;
            JsonObject inputs = [];
            foreach ((string key, JsonNode? value) in (block["inputs"] as JsonObject ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (value is not JsonArray input) return JsonValue.Create("invalid-input");
                JsonArray normalized = [];
                foreach (JsonNode? part in input)
                {
                    if (part is JsonValue scalar && scalar.TryGetValue(out string? reference))
                        normalized.Add(Block(reference!, depth + 1));
                    else if (part is JsonArray primitive && primitive.Count >= 3 && Number(primitive[0]) is 11 or 12 or 13)
                        normalized.Add(new JsonArray(primitive[0]?.DeepClone(), JsonValue.Create(names.GetValueOrDefault(Text(primitive[2]), Text(primitive[1])))));
                    else
                        normalized.Add(part?.DeepClone());
                }
                inputs[key] = normalized;
            }
            result["inputs"] = inputs;
            if (block["mutation"] is not null) result["mutation"] = block["mutation"]!.DeepClone();
            result["next"] = block["next"] is JsonValue next ? Block(Text(next), depth + 1) : null;
            return result;
        }
    }

    private static string Text(JsonNode? value) => value?.ToString() ?? "";
    private static double Number(JsonNode? value) => double.TryParse(Text(value), CultureInfo.InvariantCulture, out double number) ? number : 0;
}
