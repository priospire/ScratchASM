using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal static class ScratchBlockGraph
{
    internal sealed record PreservedScripts(HashSet<string> OriginalIds, HashSet<string> CompiledIds);

    // Compare compilations of source, not native JSON representations. The companion
    // retains hidden shadows, parameter IDs and workspace metadata for unchanged scripts.
    public static PreservedScripts FindUnchangedScripts(JsonObject original, JsonObject baseline, JsonObject edited,
        IReadOnlyList<string> originalRoots, HashSet<string> rawIds)
    {
        PreservedScripts result = new([], []);
        string[] roots = Roots(baseline).ToArray();
        if (roots.Length != originalRoots.Count) return result;
        Dictionary<string, Queue<string>> matches = new(StringComparer.Ordinal);
        for (int index = 0; index < roots.Length; index++)
        {
            string signature = Signature(baseline, roots[index]);
            if (!matches.TryGetValue(signature, out Queue<string>? candidates)) matches[signature] = candidates = new();
            candidates.Enqueue(originalRoots[index]);
        }
        foreach (string root in Roots(edited))
        {
            if (rawIds.Contains(root)) continue;
            if (!matches.TryGetValue(Signature(edited, root), out Queue<string>? candidates) || !candidates.TryDequeue(out string? previous)) continue;
            result.OriginalIds.UnionWith(Component(original, previous));
            result.CompiledIds.UnionWith(Component(edited, root));
        }
        return result;
    }

    private static IEnumerable<string> Roots(JsonObject blocks) => blocks.Where(pair =>
        pair.Value is JsonArray || pair.Value is JsonObject block && block["topLevel"]?.ToString() == "true").Select(pair => pair.Key);

    internal static string[] Component(JsonObject blocks, string root)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> order = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? id))
        {
            if (!blocks.ContainsKey(id) || !seen.Add(id)) continue;
            order.Add(id);
            if (blocks[id] is not JsonObject block) continue;
            if (block["next"] is JsonValue next && next.TryGetValue<string>(out string? nextId)) pending.Push(nextId);
            foreach ((string _, JsonNode? value) in (block["inputs"] as JsonObject ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                if (value is JsonArray input)
                    foreach (JsonNode? part in input.Skip(1).Take(2))
                        if (part is JsonValue reference && reference.TryGetValue<string>(out string? child)) pending.Push(child);
        }
        return order.ToArray();
    }

    private static string Signature(JsonObject blocks, string root)
    {
        string[] ids = Component(blocks, root);
        Dictionary<string, int> positions = ids.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string id in ids)
        {
            JsonNode? node = blocks[id]?.DeepClone();
            if (node is JsonObject block)
            {
                block.Remove("x"); block.Remove("y");
                foreach (string property in new[] { "next", "parent" })
                    if (block[property] is JsonValue link && link.TryGetValue<string>(out string? reference) && positions.TryGetValue(reference, out int index))
                        block[property] = index;
                foreach (JsonArray input in (block["inputs"] as JsonObject ?? []).Select(pair => pair.Value).OfType<JsonArray>())
                    for (int i = 1; i < Math.Min(input.Count, 3); i++)
                        if (input[i] is JsonValue link && link.TryGetValue<string>(out string? reference) && positions.TryGetValue(reference, out int index))
                            input[i] = index;
            }
            hash.AppendData(Encoding.UTF8.GetBytes((node?.ToJsonString() ?? "null") + "\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
