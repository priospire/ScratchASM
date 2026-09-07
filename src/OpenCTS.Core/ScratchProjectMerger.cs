using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal sealed record ScratchMergeOutput(JsonObject Project, IReadOnlyDictionary<string, byte[]> Entries,
    byte[]? OriginalProjectBytes = null, string[]? Operations = null);

internal static class ScratchProjectMerger
{
    private static readonly HashSet<string> StandardBlockProperties = new(StringComparer.Ordinal)
    {
        "opcode", "next", "parent", "inputs", "fields", "shadow", "topLevel", "x", "y", "mutation"
    };

    private static readonly string[] StateProperties =
    [
        "volume", "layerOrder", "tempo", "videoTransparency", "videoState", "textToSpeechLanguage",
        "visible", "x", "y", "size", "direction", "draggable", "rotationStyle"
    ];

    public static ScratchMergeOutput? Merge(
        ScratchArchiveSnapshot baseline,
        CtsCompileResult compiled,
        string source,
        ScratchAsmOriginMap originMap,
        List<ValidationIssue> issues)
    {
        if (JsonNode.Parse(compiled.ProjectJsonBytes) is not JsonObject compiledProject)
        {
            issues.Add(Error("CTS3010", "Compiled ScratchASM did not produce a project object."));
            return null;
        }

        JsonObject merged = baseline.Project.DeepClone().AsObject();
        JsonArray baselineTargets = baseline.Project["targets"] as JsonArray ?? [];
        JsonArray compiledTargets = compiledProject["targets"] as JsonArray ?? [];
        JsonArray outputTargets = [];
        CtsCompilationUnit unit = CtsParser.Parse(source).CompilationUnit;
        Dictionary<string, string> globalDataIds = new(StringComparer.Ordinal);
        Dictionary<string, string> dataNames = new(StringComparer.Ordinal);
        foreach (JsonObject target in compiledTargets.OfType<JsonObject>())
        {
            bool stage = target["isStage"]?.GetValue<bool>() == true;
            string originalName = unit.Targets.First(item => item.IsStage == stage && (stage || item.Name == target["name"]?.GetValue<string>()))
                .Members.OfType<CtsTargetOriginDeclaration>().FirstOrDefault()?.Name ?? target["name"]!.GetValue<string>();
            ScratchTargetOrigin? origin = originMap.Targets.FirstOrDefault(item => item.IsStage == stage &&
                (stage || item.Name == originalName));
            if (origin is null) continue;
            foreach (var (kind, origins) in new[] { ("variables", origin.Variables), ("lists", origin.Lists), ("broadcasts", origin.Broadcasts) })
            {
                foreach ((string id, JsonNode? value) in target[kind] as JsonObject ?? [])
                {
                    string? alias = value is JsonArray tuple ? NodeString(tuple[0]) :
                        origins.Values.FirstOrDefault(item => item.Name == NodeString(value))?.Alias;
                    if (alias is not null && origins.TryGetValue(alias, out ScratchDataOrigin? data))
                    {
                        globalDataIds[id] = data.Id;
                        dataNames[data.Id] = data.Name;
                    }
                }
            }
        }

        foreach (JsonNode? targetNode in compiledTargets)
        {
            if (targetNode is not JsonObject compiledTarget)
            {
                continue;
            }

            bool isStage = compiledTarget["isStage"]?.GetValue<bool>() == true;
            string name = compiledTarget["name"]?.GetValue<string>() ?? (isStage ? "Stage" : string.Empty);
            CtsTargetDeclaration declaration = unit.Targets.First(target => target.IsStage == isStage && (isStage || target.Name == name));
            string originalName = declaration.Members.OfType<CtsTargetOriginDeclaration>().FirstOrDefault()?.Name ?? name;
            JsonObject? baselineTarget = baselineTargets.OfType<JsonObject>().FirstOrDefault(target =>
                target["isStage"]?.GetValue<bool>() == isStage &&
                (isStage || string.Equals(target["name"]?.GetValue<string>(), originalName, StringComparison.Ordinal)));
            ScratchTargetOrigin? origin = originMap.Targets.FirstOrDefault(target =>
                target.IsStage == isStage && (isStage || target.Name == originalName));

            outputTargets.Add(baselineTarget is null || origin is null
                ? compiledTarget.DeepClone()
                : MergeTarget(baselineTarget, compiledTarget, origin, globalDataIds, dataNames,
                    declaration));
        }

        merged["targets"] = outputTargets;
        if (merged["monitors"] is JsonArray monitors)
        {
            HashSet<string> ids = outputTargets.OfType<JsonObject>().SelectMany(target =>
                (target["variables"] as JsonObject ?? []).Concat(target["lists"] as JsonObject ?? []))
                .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            foreach (JsonObject monitor in monitors.OfType<JsonObject>().ToArray())
            {
                if (NodeString(monitor["opcode"]) is "data_variable" or "data_listcontents" &&
                    NodeString(monitor["id"]) is string id && !ids.Contains(id))
                {
                    // Keep unrelated unknown monitor IDs, but remove monitors for explicitly deleted data.
                    bool wasData = baselineTargets.OfType<JsonObject>().Any(target =>
                        (target["variables"] as JsonObject)?.ContainsKey(id) == true || (target["lists"] as JsonObject)?.ContainsKey(id) == true);
                    if (wasData) { monitors.Remove(monitor); continue; }
                }
                foreach (CtsTargetDeclaration target in unit.Targets)
                    if (target.Members.OfType<CtsTargetOriginDeclaration>().FirstOrDefault() is CtsTargetOriginDeclaration previous &&
                        NodeString(monitor["spriteName"]) == previous.Name) monitor["spriteName"] = target.Name;
            }
        }
        merged["extensions"] = MergeExtensions(baseline.Project["extensions"] as JsonArray, compiledProject["extensions"] as JsonArray);
        foreach (string property in new[] { "extensionURLs", "extensionColors" })
        {
            if (compiledProject[property] is not JsonObject values) continue;
            JsonObject combined = merged[property] as JsonObject ?? new JsonObject();
            foreach ((string key, JsonNode? value) in values) combined[key] = value?.DeepClone();
            merged[property] = combined;
        }
        JsonObject meta = merged["meta"] as JsonObject ?? [];
        meta["agent"] = ScratchAsmLanguage.DisplayName;
        meta["scratchasm"] = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["languageVersion"] = "0.2.0",
            ["sourceSha256"] = Sha256(Encoding.UTF8.GetBytes(source)),
            ["graphSha256"] = Sha256(JsonSerializer.SerializeToUtf8Bytes(outputTargets))
        };
        merged["meta"] = meta;

        ScratchAssetCollection entries = new(baseline.Entries);
        entries.Remove("project.json");
        HashSet<string> referencedAssets = outputTargets.OfType<JsonObject>()
            .SelectMany(target => (target["costumes"] as JsonArray ?? []).Concat(target["sounds"] as JsonArray ?? []))
            .OfType<JsonObject>().Select(asset => NodeString(asset["md5ext"]) ?? "").ToHashSet(StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in compiled.Assets)
        {
            if (!referencedAssets.Contains(name)) continue;
            if (entries.TryGetValue(name, out byte[]? existing) && !existing.AsSpan().SequenceEqual(bytes))
            {
                issues.Add(Error("CTS3011", $"Generated asset collides with a different baseline archive entry: {name}"));
                return null;
            }

            entries[name] = bytes;
        }

        return new ScratchMergeOutput(merged, entries);
    }

    public static void WriteArchive(ScratchMergeOutput output, string outputPath, bool overwrite)
    {
        string fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".sb3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Edited output path must end with .sb3.");
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"Output file already exists: {fullPath}");
        }

        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                using ScratchProvenance.ArchiveHash hash = new();
                hash.WriteEntry(archive, "project.json", output.OriginalProjectBytes ?? Encoding.UTF8.GetBytes(output.Project.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                })));
                foreach (string name in output.Entries.Keys.Order(StringComparer.Ordinal))
                {
                    if (name == "project.json") continue;
                    if (output.Entries is ScratchAssetCollection indexed)
                        hash.WriteEntry(archive, name, indexed.Length(name), destination => indexed.CopyTo(name, destination));
                    else hash.WriteEntry(archive, name, output.Entries[name]);
                }
                archive.Comment = ScratchProvenance.ArchiveComment(hash.Finish(), output.Operations ?? ["exported"]);
            }

            File.Move(tempPath, fullPath, overwrite);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static JsonObject MergeTarget(JsonObject baseline, JsonObject compiled, ScratchTargetOrigin origin,
        Dictionary<string, string> dataIds, Dictionary<string, string> dataNames, CtsTargetDeclaration declaration)
    {
        JsonObject output = baseline.DeepClone().AsObject();
        JsonObject variables = RemapData(compiled["variables"] as JsonObject ?? [], origin.Variables, dataIds, baseline["variables"] as JsonObject ?? []);
        JsonObject lists = RemapData(compiled["lists"] as JsonObject ?? [], origin.Lists, dataIds, baseline["lists"] as JsonObject ?? []);
        JsonObject broadcasts = RemapData(compiled["broadcasts"] as JsonObject ?? [], origin.Broadcasts, dataIds, baseline["broadcasts"] as JsonObject ?? []);
        JsonObject compiledBlocks = compiled["blocks"] as JsonObject ?? [];
        RemapDataReferences(compiledBlocks, dataIds, dataNames);
        HashSet<string> rawIds = declaration.Members.OfType<CtsRawBlocksDeclaration>()
            .SelectMany(raw => JsonNode.Parse(raw.Json)!.AsObject().Select(pair => pair.Key)).ToHashSet(StringComparer.Ordinal);
        JsonObject blocks = PreserveBlockIdentity(baseline["blocks"] as JsonObject ?? [], compiledBlocks, origin.BlockOrder, rawIds);

        output["isStage"] = compiled["isStage"]?.DeepClone();
        output["name"] = compiled["name"]?.DeepClone();
        output["variables"] = variables;
        output["lists"] = lists;
        output["broadcasts"] = broadcasts;
        output["blocks"] = blocks;
        HashSet<string> explicitState = declaration.Members.OfType<CtsStateDeclaration>()
            .SelectMany(state => state.Properties.Keys).Select(key => key == "layer" ? "layerOrder" : key).ToHashSet();
        if (declaration.Members.OfType<CtsRotationStyleDeclaration>().Any()) explicitState.Add("rotationStyle");
        foreach (string property in StateProperties.Where(explicitState.Contains))
        {
            if (compiled[property] is JsonNode value)
            {
                output[property] = value.DeepClone();
            }
        }

        if (declaration.Members.OfType<CtsCostumeDeclaration>().Any())
        {
            output["costumes"] = compiled["costumes"]?.DeepClone();
            output["currentCostume"] = 0;
        }
        if (output["comments"] is JsonObject comments)
        {
            foreach (JsonObject comment in comments.Select(pair => pair.Value).OfType<JsonObject>())
                if (NodeString(comment["blockId"]) is string id && !blocks.ContainsKey(id)) comment["blockId"] = null;
        }

        return output;
    }

    private static JsonObject RemapData(
        JsonObject compiled,
        IReadOnlyDictionary<string, ScratchDataOrigin> origins,
        Dictionary<string, string> idMap, JsonObject baseline)
    {
        JsonObject output = [];
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            JsonNode? value = node?.DeepClone();
            string? alias = value is JsonArray tuple && tuple.Count > 0 ? NodeString(tuple[0]) :
                origins.Values.FirstOrDefault(origin => origin.Name == NodeString(value))?.Alias;
            if (alias is not null && origins.TryGetValue(alias, out ScratchDataOrigin? origin))
            {
                idMap[compiledId] = origin.Id;
                if (value is JsonArray mappedTuple)
                {
                    mappedTuple[0] = origin.Name;
                    if (baseline[origin.Id] is JsonArray original && original.Count >= 2 && mappedTuple.Count >= 2 &&
                        EquivalentValue(original[1], mappedTuple[1])) mappedTuple[1] = original[1]?.DeepClone();
                }

                output[origin.Id] = value;
            }
            else
            {
                output[compiledId] = value;
            }
        }

        return output;
    }

    private static bool EquivalentValue(JsonNode? first, JsonNode? second)
    {
        if (first is JsonArray firstItems && second is JsonArray secondItems)
            return firstItems.Count == secondItems.Count && firstItems.Zip(secondItems).All(pair => EquivalentValue(pair.First, pair.Second));
        return first?.ToString() == second?.ToString();
    }

    private static void RemapDataReferences(JsonObject blocks, IReadOnlyDictionary<string, string> dataIds,
        IReadOnlyDictionary<string, string> dataNames)
    {
        foreach (JsonObject block in blocks.Select(static pair => pair.Value).OfType<JsonObject>())
        {
            if (block["fields"] is JsonObject fields)
            {
                foreach (string name in fields.Select(static pair => pair.Key).ToArray())
                {
                    if (fields[name] is JsonArray field && field.Count >= 2 && NodeString(field[1]) is string id &&
                        dataIds.TryGetValue(id, out string? mapped))
                    {
                        field[1] = mapped;
                        if (dataNames.TryGetValue(mapped, out string? display)) field[0] = display;
                    }
                }
            }

            if (block["inputs"] is JsonObject inputs)
            {
                foreach (JsonNode? input in inputs.Select(static pair => pair.Value))
                {
                    RemapPrimitiveDataId(input, dataIds, dataNames);
                }
            }
        }
    }

    private static void RemapPrimitiveDataId(JsonNode? node, IReadOnlyDictionary<string, string> dataIds,
        IReadOnlyDictionary<string, string> dataNames)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        if (array.Count >= 3 && Number(array[0]) is 11 or 12 or 13 && NodeString(array[2]) is string id &&
            dataIds.TryGetValue(id, out string? mapped))
        {
            array[2] = mapped;
            if (dataNames.TryGetValue(mapped, out string? display)) array[1] = display;
            return;
        }

        foreach (JsonNode? child in array)
        {
            RemapPrimitiveDataId(child, dataIds, dataNames);
        }
    }

    private static JsonObject PreserveBlockIdentity(JsonObject baseline, JsonObject compiled, IReadOnlyList<string> baselineOrder, HashSet<string> rawIds)
    {
        Dictionary<string, Queue<string>> baselineByOpcode = new(StringComparer.Ordinal);
        foreach (string id in baselineOrder)
        {
            if (baseline[id] is not JsonObject block || NodeString(block["opcode"]) is not string opcode)
            {
                continue;
            }

            if (!baselineByOpcode.TryGetValue(opcode, out Queue<string>? queue))
            {
                queue = new Queue<string>();
                baselineByOpcode[opcode] = queue;
            }

            queue.Enqueue(id);
        }

        Dictionary<string, string> idMap = new(StringComparer.Ordinal);
        HashSet<string> reserved = compiled.Where(pair => baseline.ContainsKey(pair.Key)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string id in reserved) idMap[id] = id;
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            if (reserved.Contains(compiledId)) continue;
            if (node is JsonObject block && NodeString(block["opcode"]) is string opcode &&
                baselineByOpcode.TryGetValue(opcode, out Queue<string>? queue))
            {
                while (queue.Count > 0 && reserved.Contains(queue.Peek())) queue.Dequeue();
                if (queue.Count > 0)
                {
                    idMap[compiledId] = queue.Dequeue();
                    reserved.Add(idMap[compiledId]);
                }
            }
        }
        ScratchIdAllocator allocator = new(reserved);
        foreach (string id in compiled.Select(pair => pair.Key))
            if (!idMap.ContainsKey(id)) idMap[id] = allocator.Allocate(id);

        JsonObject output = [];
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            if (node is not JsonObject source)
            {
                output[idMap[compiledId]] = node?.DeepClone();
                continue;
            }

            JsonObject block = source.DeepClone().AsObject();
            RemapBlockReferences(block, idMap);
            string outputId = idMap.GetValueOrDefault(compiledId, compiledId);
            if (baseline[outputId] is JsonObject original)
            {
                foreach ((string name, JsonNode? value) in original)
                {
                    if (!StandardBlockProperties.Contains(name) && !block.ContainsKey(name))
                    {
                        block[name] = value?.DeepClone();
                    }
                }
                if (block["topLevel"]?.GetValue<bool>() == true && !rawIds.Contains(compiledId))
                {
                    block["x"] = original["x"]?.DeepClone() ?? block["x"]?.DeepClone();
                    block["y"] = original["y"]?.DeepClone() ?? block["y"]?.DeepClone();
                }
            }

            output[outputId] = block;
        }

        return output;
    }

    private static void RemapBlockReferences(JsonObject block, IReadOnlyDictionary<string, string> idMap)
    {
        foreach (string property in new[] { "next", "parent" })
        {
            if (NodeString(block[property]) is string id && idMap.TryGetValue(id, out string? mapped))
            {
                block[property] = mapped;
            }
        }

        if (block["inputs"] is not JsonObject inputs)
        {
            return;
        }

        foreach (JsonNode? input in inputs.Select(static pair => pair.Value))
        {
            if (input is not JsonArray tuple)
            {
                continue;
            }

            for (int index = 1; index < Math.Min(tuple.Count, 3); index++)
            {
                if (NodeString(tuple[index]) is string id && idMap.TryGetValue(id, out string? mapped))
                {
                    tuple[index] = mapped;
                }
            }
        }
    }

    private static JsonArray MergeExtensions(JsonArray? baseline, JsonArray? compiled)
    {
        SortedSet<string> values = new(StringComparer.Ordinal);
        foreach (JsonNode? node in (baseline ?? []).Concat(compiled ?? []))
        {
            if (NodeString(node) is string value)
            {
                values.Add(value);
            }
        }

        return new JsonArray(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? NodeString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static double Number(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out int number))
        {
            return number;
        }

        return -1;
    }

    private static ValidationIssue Error(string code, string message) =>
        new(message, "$", null, DiagnosticSeverity.Error, code);
}
