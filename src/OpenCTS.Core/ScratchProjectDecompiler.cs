using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal sealed class ScratchProjectDecompiler
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "stage", "sprite", "proc", "call", "block", "input", "field", "mutation", "var", "global",
        "sprite", "local", "cloud", "list", "broadcast", "extension", "state", "costume", "struct",
        "enum", "const", "repeat", "forever", "if", "else", "repeatuntil", "waituntil", "substack",
        "project", "rawblocks", "origin", "true", "false", "and", "or", "not", "as", "warp", "shadow", "num", "str", "bool"
    };

    private readonly JsonObject _project;
    private readonly List<ValidationIssue> _issues = [];
    private readonly ScratchAsmOriginMap _originMap = new();
    private readonly Dictionary<string, ScratchDataOrigin> _dataById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedAliases = new(StringComparer.Ordinal);
    private readonly HashSet<int> _rawTargets = [];

    private ScratchProjectDecompiler(JsonObject project)
    {
        _project = project;
        if (project["targets"] is JsonArray targets)
        {
            for (int index = 0; index < targets.Count; index++)
                if (targets[index]?["blocks"] is JsonObject blocks &&
                    (blocks.Count > 100 || blocks.Any(pair => pair.Value is not JsonObject))) _rawTargets.Add(index);
        }
    }

    public static ScratchProjectDecompilation Decompile(JsonObject project)
    {
        ScratchProjectDecompiler decompiler = new(project);
        ScratchProjectDecompilation candidate = decompiler.Run();
        if (candidate.Issues.Any(issue => issue.Severity == DiagnosticSeverity.Error)) return candidate;
        CtsCompileResult compiled = CtsCompiler.Compile(candidate.SourceText, "import.sasm");
        JsonArray originalTargets = project["targets"]!.AsArray();
        JsonArray? compiledTargets = compiled.ProjectJsonBytes.Length > 0
            ? JsonNode.Parse(compiled.ProjectJsonBytes)?["targets"] as JsonArray : null;
        HashSet<int> rawTargets = [];
        for (int i = 0; i < originalTargets.Count; i++)
        {
            if (compiledTargets is null || i >= compiledTargets.Count ||
                !ScratchBlockGraph.Equivalent(originalTargets[i]!.AsObject(), compiledTargets[i]!.AsObject(),
                    candidate.OriginMap.Targets[i], candidate.OriginMap.Targets)) rawTargets.Add(i);
        }
        if (rawTargets.Count == 0) return candidate;
        ScratchProjectDecompiler exact = new(project);
        exact._rawTargets.UnionWith(rawTargets);
        return exact.Run();
    }

    private ScratchProjectDecompilation Run()
    {
        StringBuilder source = new();
        source.AppendLine("# Decompiled by ScratchASM. Unsupported blocks use exact generic syntax.");

        if (_project["targets"] is not JsonArray targets)
        {
            AddError("CTS3001", "project.json does not contain a targets array.", "$.targets");
            return new ScratchProjectDecompilation(source.ToString(), _issues, _originMap);
        }

        JsonArray extensions = _project["extensions"] as JsonArray ?? [];
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            if (targets[targetIndex] is not JsonObject target)
            {
                AddError("CTS3002", "Target entry must be an object.", $"$.targets[{targetIndex}]");
                continue;
            }

            if (targetIndex > 0)
            {
                source.AppendLine();
            }

            EmitTarget(source, target, targetIndex, extensions);
        }

        return new ScratchProjectDecompilation(NormalizeNewlines(source.ToString()), _issues, _originMap);
    }

    private void EmitTarget(StringBuilder source, JsonObject target, int targetIndex, JsonArray extensions)
    {
        bool isStage = target["isStage"]?.GetValue<bool>() == true;
        string name = target["name"]?.GetValue<string>() ?? (isStage ? "Stage" : $"Sprite {targetIndex}");
        ScratchTargetOrigin origin = new() { IsStage = isStage, Name = name };
        _originMap.Targets.Add(origin);

        source.Append(isStage ? "stage" : $"sprite {Quote(name)}").AppendLine(" {");
        if (!isStage) source.Append("  origin ").AppendLine(Quote(name));
        EmitDataDeclarations(source, target, origin, isStage);
        EmitState(source, target);

        if (isStage)
        {
            foreach (JsonNode? extension in extensions)
            {
                if (extension is JsonValue value && value.TryGetValue(out string? extensionId) &&
                    !string.IsNullOrWhiteSpace(extensionId))
                {
                    source.Append("  extension ").AppendLine(ToIdentifier(extensionId, "extension"));
                }
            }
        }

        if (target["blocks"] is JsonObject blocks && blocks.Count > 0)
        {
            if (_rawTargets.Contains(targetIndex)) EmitRawBlocks(source, blocks, origin);
            else EmitScripts(source, blocks, origin, targetIndex);
        }

        source.AppendLine("}");
    }

    private static void EmitState(StringBuilder source, JsonObject target)
    {
        string[] properties = ["x", "y", "direction", "size", "visible", "layerOrder"];
        List<string> values = properties.Where(property => target[property] is not null)
            .Select(property => $"{property}={EmitScalar(target[property])}").ToList();
        if (values.Count > 0) source.Append("  state ").AppendLine(string.Join(' ', values));
        if (NodeString(target["rotationStyle"]) is string rotation)
            source.Append("  rotationStyle ").AppendLine(Quote(rotation));
    }

    private static void EmitRawBlocks(StringBuilder source, JsonObject blocks, ScratchTargetOrigin origin)
    {
        source.AppendLine();
        source.AppendLine("  # Exact Scratch graph: IDs, inputs, shadows, and custom block mutations.");
        string json = blocks.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string[] lines = NormalizeNewlines(json).Split('\n');
        source.Append("  rawblocks ").AppendLine(lines[0]);
        foreach (string line in lines.Skip(1)) source.Append("  ").AppendLine(line);
        origin.BlockOrder.AddRange(blocks.Select(pair => pair.Key));
    }

    private void EmitDataDeclarations(StringBuilder source, JsonObject target, ScratchTargetOrigin origin, bool isStage)
    {
        EmitVariables(source, target["variables"] as JsonObject ?? [], origin, isStage);
        EmitLists(source, target["lists"] as JsonObject ?? [], origin);
        EmitBroadcasts(source, target["broadcasts"] as JsonObject ?? [], origin);
    }

    private void EmitVariables(StringBuilder source, JsonObject variables, ScratchTargetOrigin origin, bool isStage)
    {
        foreach ((string id, JsonNode? node) in variables.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (node is not JsonArray value || value.Count < 2)
            {
                AddError("CTS3003", $"Variable '{id}' has an invalid value tuple.", "$.targets[*].variables");
                continue;
            }

            string name = NodeString(value[0]) ?? id;
            string alias = CreateAlias(name, "variable");
            bool cloud = value.Count >= 3 && value[2]?.GetValue<bool>() == true;
            ScratchDataOrigin data = new(alias, id, name);
            origin.Variables[alias] = data;
            _dataById[id] = data;

            source.Append("  ");
            if (cloud)
            {
                source.Append("cloud global var ");
            }
            else
            {
                source.Append(isStage ? "global var " : "sprite var ");
            }

            source.Append(alias).Append(" = ").AppendLine(EmitDataValue(value[1]));
        }
    }

    private void EmitLists(StringBuilder source, JsonObject lists, ScratchTargetOrigin origin)
    {
        foreach ((string id, JsonNode? node) in lists.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (node is not JsonArray value || value.Count < 2 || value[1] is not JsonArray items)
            {
                AddError("CTS3004", $"List '{id}' has an invalid value tuple.", "$.targets[*].lists");
                continue;
            }

            string name = NodeString(value[0]) ?? id;
            string alias = CreateAlias(name, "list");
            ScratchDataOrigin data = new(alias, id, name);
            origin.Lists[alias] = data;
            _dataById[id] = data;

            source.Append("  list ").Append(alias).Append(" = [")
                .Append(string.Join(", ", items.Select(EmitDataValue))).AppendLine("]");
        }
    }

    private void EmitBroadcasts(StringBuilder source, JsonObject broadcasts, ScratchTargetOrigin origin)
    {
        foreach ((string id, JsonNode? node) in broadcasts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            string name = NodeString(node) ?? id;
            string alias = CreateAlias(name, "broadcast");
            ScratchDataOrigin data = new(alias, id, name);
            origin.Broadcasts[alias] = data;
            _dataById[id] = data;
            source.Append("  broadcast ").Append(alias).Append(" = ").AppendLine(Quote(name));
        }
    }

    private void EmitScripts(StringBuilder source, JsonObject blocks, ScratchTargetOrigin origin, int targetIndex)
    {
        Dictionary<string, JsonObject> byId = blocks
            .Where(static pair => pair.Value is JsonObject)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value!.AsObject(), StringComparer.Ordinal);

        ValidateGraph(byId, targetIndex);
        if (_issues.Any(issue => issue.Severity == DiagnosticSeverity.Error))
        {
            EmitRawBlocks(source, blocks, origin);
            return;
        }
        List<string> roots = byId
            .Where(static pair => pair.Value["topLevel"]?.GetValue<bool>() == true)
            .OrderBy(static pair => Number(pair.Value["y"]))
            .ThenBy(static pair => Number(pair.Value["x"]))
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key)
            .ToList();

        HashSet<string> emitted = new(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            source.AppendLine();
            EmitTopLevel(source, byId, root, 2, emitted, origin);
        }

        foreach (string orphan in byId.Keys.Where(id => !emitted.Contains(id)).Order(StringComparer.Ordinal))
        {
            AddWarning("CTS3005", $"Disconnected block '{orphan}' was emitted as an additional top-level generic script.",
                $"$.targets[{targetIndex}].blocks.{orphan}");
            source.AppendLine();
            EmitTopLevel(source, byId, orphan, 2, emitted, origin);
        }
    }

    private void EmitTopLevel(
        StringBuilder source,
        IReadOnlyDictionary<string, JsonObject> blocks,
        string id,
        int indent,
        HashSet<string> emitted,
        ScratchTargetOrigin origin)
    {
        if (!blocks.TryGetValue(id, out JsonObject? block))
        {
            return;
        }

        emitted.Add(id);
        origin.BlockOrder.Add(id);
        string opcode = NodeString(block["opcode"]) ?? "unknown_opcode";
        MarkReporterInputs(block, blocks, emitted, origin);
        if (TryMatchAlias(block, CtsBlockShape.Hat, out CtsAliasDefinition? hat))
        {
            AppendIndent(source, indent).Append('@').Append(hat!.Name)
                .Append(EmitAliasArguments(block, hat, blocks)).AppendLine(":");
        }
        else
        {
            AppendIndent(source, indent).Append("@block ").Append(Quote(opcode))
                .Append(EmitClauses(block, blocks, includeSubstacks: false)).AppendLine(":");
        }

        string? next = NodeString(block["next"]);
        if (next is not null)
        {
            EmitStatementChain(source, blocks, next, indent + 2, emitted, origin);
        }
        else
        {
            AppendIndent(source, indent + 2).AppendLine("# empty script");
        }

        foreach (string substack in SubstackIds(block).Where(idValue => !emitted.Contains(idValue)))
        {
            AddWarning("CTS3006", $"A top-level generic block substack was emitted separately: {opcode}", "$.targets[*].blocks");
            source.AppendLine();
            EmitTopLevel(source, blocks, substack, indent, emitted, origin);
        }
    }

    private void EmitStatementChain(
        StringBuilder source,
        IReadOnlyDictionary<string, JsonObject> blocks,
        string startId,
        int indent,
        HashSet<string> emitted,
        ScratchTargetOrigin origin)
    {
        string? current = startId;
        HashSet<string> chain = new(StringComparer.Ordinal);
        while (current is not null && blocks.TryGetValue(current, out JsonObject? block))
        {
            string opcode = NodeString(block["opcode"]) ?? "unknown_opcode";
            if (!chain.Add(current))
            {
                AppendIndent(source, indent).Append("# cycle detected at ").AppendLine(opcode);
                return;
            }

            if (emitted.Add(current))
            {
                origin.BlockOrder.Add(current);
            }

            EmitStatement(source, blocks, block, indent, emitted, origin);
            current = NodeString(block["next"]);
        }
    }

    private void EmitStatement(
        StringBuilder source,
        IReadOnlyDictionary<string, JsonObject> blocks,
        JsonObject block,
        int indent,
        HashSet<string> emitted,
        ScratchTargetOrigin origin)
    {
        if (TryMatchAlias(block, CtsBlockShape.Stack, CtsBlockShape.Cap, out CtsAliasDefinition? definition) &&
            !HasSubstacks(block))
        {
            AppendIndent(source, indent).Append(definition!.Name)
                .Append(EmitAliasArguments(block, definition, blocks)).AppendLine();
            MarkReporterInputs(block, blocks, emitted, origin);
            return;
        }

        string opcode = NodeString(block["opcode"]) ?? "unknown_opcode";
        string clauses = EmitClauses(block, blocks, includeSubstacks: false);
        string[] substacks = SubstackPairs(block).Select(static pair => pair.Value).ToArray();
        AppendIndent(source, indent).Append("block ").Append(Quote(opcode)).Append(clauses);
        if (substacks.Length == 0)
        {
            source.AppendLine();
            MarkReporterInputs(block, blocks, emitted, origin);
            return;
        }

        source.AppendLine(" {");
        foreach ((string name, string substackId) in SubstackPairs(block))
        {
            AppendIndent(source, indent + 2).Append("substack ").Append(name).AppendLine(":");
            EmitStatementChain(source, blocks, substackId, indent + 4, emitted, origin);
        }

        AppendIndent(source, indent).AppendLine("}");
        MarkReporterInputs(block, blocks, emitted, origin);
    }

    private void MarkReporterInputs(
        JsonObject block,
        IReadOnlyDictionary<string, JsonObject> blocks,
        HashSet<string> emitted,
        ScratchTargetOrigin origin)
    {
        foreach (string child in InputBlockIds(block).Where(id => !IsSubstackReference(block, id)))
        {
            CollectInputOrder(child, blocks, emitted, origin);
        }
    }

    private void CollectInputOrder(
        string id,
        IReadOnlyDictionary<string, JsonObject> blocks,
        HashSet<string> emitted,
        ScratchTargetOrigin origin)
    {
        if (!blocks.TryGetValue(id, out JsonObject? block) || !emitted.Add(id))
        {
            return;
        }

        origin.BlockOrder.Add(id);
        foreach (string child in InputBlockIds(block))
        {
            CollectInputOrder(child, blocks, emitted, origin);
        }
    }

    private string EmitClauses(JsonObject block, IReadOnlyDictionary<string, JsonObject> blocks, bool includeSubstacks)
    {
        StringBuilder result = new();
        if (block["inputs"] is JsonObject inputs)
        {
            foreach ((string name, JsonNode? value) in inputs.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (!includeSubstacks && name.StartsWith("SUBSTACK", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Append(" input ").Append(name).Append('=').Append(EmitInput(value, blocks));
            }
        }

        if (block["fields"] is JsonObject fields)
        {
            foreach ((string name, JsonNode? value) in fields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                result.Append(" field ").Append(name).Append('=').Append(EmitField(value));
            }
        }

        if (block["mutation"] is JsonObject mutation)
        {
            foreach ((string name, JsonNode? value) in mutation
                .Where(static pair => pair.Key is not "tagName" and not "children")
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                result.Append(" mutation ").Append(name).Append('=').Append(EmitScalar(value));
            }
        }

        return result.ToString();
    }

    private string EmitAliasArguments(JsonObject block, CtsAliasDefinition definition, IReadOnlyDictionary<string, JsonObject> blocks)
    {
        if (definition.Bindings.Count == 0)
        {
            return string.Empty;
        }

        List<string> arguments = [];
        foreach (CtsArgumentBinding binding in definition.Bindings)
        {
            JsonNode? value = binding.Kind switch
            {
                CtsBindingKind.Field => (block["fields"] as JsonObject)?[binding.Name],
                CtsBindingKind.Input => (block["inputs"] as JsonObject)?[binding.Name],
                CtsBindingKind.Menu => (block["fields"] as JsonObject)?[binding.Name] ??
                    (block["inputs"] as JsonObject)?[binding.Name],
                _ => null
            };

            arguments.Add(binding.Kind is CtsBindingKind.Input or CtsBindingKind.Menu && value is JsonArray input && IsInputTuple(input)
                ? EmitInput(value, blocks)
                : EmitFieldArgument(value));
        }

        return " " + string.Join(' ', arguments);
    }

    private string EmitReporter(string id, IReadOnlyDictionary<string, JsonObject> blocks)
    {
        if (!blocks.TryGetValue(id, out JsonObject? block))
        {
            return Quote(id);
        }

        string opcode = NodeString(block["opcode"]) ?? "unknown_opcode";
        if (opcode is "data_variable" or "data_listcontents" &&
            block["fields"] is JsonObject fields)
        {
            JsonNode? field = fields[opcode == "data_variable" ? "VARIABLE" : "LIST"];
            string? dataId = field is JsonArray tuple && tuple.Count >= 2 ? NodeString(tuple[1]) : null;
            if (dataId is not null && _dataById.TryGetValue(dataId, out ScratchDataOrigin? origin))
            {
                return origin.Alias;
            }
        }

        if (TryMatchAlias(block, CtsBlockShape.Reporter, CtsBlockShape.Boolean, out CtsAliasDefinition? definition))
        {
            string arguments = EmitAliasArguments(block, definition!, blocks).TrimStart();
            return $"{definition!.Name}({arguments})";
        }

        return "[" + (block["shadow"]?.GetValue<bool>() == true ? "shadow " : string.Empty) +
            Quote(opcode) + EmitClauses(block, blocks, includeSubstacks: false) + "]";
    }

    private string EmitInput(JsonNode? node, IReadOnlyDictionary<string, JsonObject> blocks)
    {
        if (node is not JsonArray input || !IsInputTuple(input) || input.Count < 2)
        {
            return EmitScalar(node);
        }

        JsonNode? active = input[1];
        string? blockId = NodeString(active);
        if (blockId is not null && blocks.ContainsKey(blockId))
        {
            return EmitReporter(blockId, blocks);
        }

        return EmitPrimitive(active);
    }

    private string EmitPrimitive(JsonNode? node)
    {
        if (node is not JsonArray primitive || primitive.Count < 2)
        {
            return EmitScalar(node);
        }

        int type = (int)Number(primitive[0]);
        if (type is 12 or 13 && primitive.Count >= 3)
        {
            string? id = NodeString(primitive[2]);
            if (id is not null && _dataById.TryGetValue(id, out ScratchDataOrigin? origin))
            {
                return origin.Alias;
            }
        }

        return type is >= 4 and <= 8
            ? NumericOrQuoted(NodeString(primitive[1]) ?? primitive[1]?.ToJsonString() ?? "0")
            : EmitScalar(primitive[1]);
    }

    private string EmitField(JsonNode? node)
    {
        if (node is not JsonArray tuple || tuple.Count == 0)
        {
            return EmitScalar(node);
        }

        string display = NodeString(tuple[0]) ?? tuple[0]?.ToJsonString() ?? string.Empty;
        string? id = tuple.Count >= 2 ? NodeString(tuple[1]) : null;
        if (id is not null)
        {
            return $"({Quote(display)}, {Quote(id)})";
        }

        return Quote(display);
    }

    private string EmitFieldArgument(JsonNode? node)
    {
        if (node is JsonArray tuple && tuple.Count > 0)
        {
            string? id = tuple.Count >= 2 ? NodeString(tuple[1]) : null;
            if (id is not null && _dataById.TryGetValue(id, out ScratchDataOrigin? origin))
            {
                return origin.Alias;
            }

            return EmitScalar(tuple[0]);
        }

        return EmitScalar(node);
    }

    private bool TryMatchAlias(JsonObject block, params CtsBlockShape[] allowedShapes)
    {
        return TryMatchAlias(block, allowedShapes, out _);
    }

    private bool TryMatchAlias(JsonObject block, CtsBlockShape shape, out CtsAliasDefinition? definition)
    {
        return TryMatchAlias(block, [shape], out definition);
    }

    private bool TryMatchAlias(JsonObject block, CtsBlockShape first, CtsBlockShape second, out CtsAliasDefinition? definition)
    {
        return TryMatchAlias(block, [first, second], out definition);
    }

    private bool TryMatchAlias(JsonObject block, IReadOnlyCollection<CtsBlockShape> allowedShapes, out CtsAliasDefinition? definition)
    {
        string? opcode = NodeString(block["opcode"]);
        JsonObject fields = block["fields"] as JsonObject ?? [];
        JsonObject inputs = block["inputs"] as JsonObject ?? [];
        JsonObject mutation = block["mutation"] as JsonObject ?? [];
        if (opcode is null || mutation.Count > 0)
        {
            definition = null;
            return false;
        }

        CtsAliasDefinition[] matches = CtsBlockRegistry.Definitions.Where(candidate =>
            candidate.Opcode == opcode &&
            allowedShapes.Contains(candidate.Shape) &&
            candidate.ConstantFields.All(field => FieldDisplay(fields[field.Key]) == field.Value) &&
            candidate.Bindings.All(binding => binding.Kind switch
            {
                CtsBindingKind.Field => fields.ContainsKey(binding.Name),
                CtsBindingKind.Input => inputs.ContainsKey(binding.Name),
                CtsBindingKind.Menu => fields.ContainsKey(binding.Name) || inputs.ContainsKey(binding.Name),
                _ => false
            }) &&
            fields.All(field => candidate.ConstantFields.ContainsKey(field.Key) ||
                candidate.Bindings.Any(binding => binding.Name == field.Key)) &&
            inputs.All(input => candidate.SubstackNames.Contains(input.Key) ||
                candidate.Bindings.Any(binding => binding.Name == input.Key)))
            .ToArray();

        definition = matches.Length == 1 ? matches[0] : null;
        return definition is not null;
    }

    private void ValidateGraph(IReadOnlyDictionary<string, JsonObject> blocks, int targetIndex)
    {
        Dictionary<string, string> owners = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        foreach (string root in blocks.Keys)
        {
            if (!visited.Contains(root)) Visit(root, null, 0);
        }

        void Visit(string id, string? owner, int depth)
        {
            if (depth > 128)
            {
                AddError("CTS3009", "Block graph nesting exceeds the supported limit of 128.", $"$.targets[{targetIndex}].blocks");
                return;
            }
            if (!blocks.TryGetValue(id, out JsonObject? block))
            {
                AddError("CTS3007", $"Block graph references missing block '{id}'.", $"$.targets[{targetIndex}].blocks");
                return;
            }

            if (owner is not null && owners.TryGetValue(id, out string? priorOwner) && priorOwner != owner)
            {
                AddError("CTS3008", $"Block '{id}' is shared by multiple graph parents.", $"$.targets[{targetIndex}].blocks.{id}");
            }
            else if (owner is not null)
            {
                owners[id] = owner;
            }

            if (!visiting.Add(id))
            {
                AddError("CTS3009", $"Block graph contains a cycle at '{id}'.", $"$.targets[{targetIndex}].blocks.{id}");
                return;
            }

            if (visited.Add(id))
            {
                foreach (string child in References(block, blocks))
                {
                    Visit(child, id, depth + 1);
                }
            }

            visiting.Remove(id);
        }
    }

    private static IEnumerable<string> References(JsonObject block, IReadOnlyDictionary<string, JsonObject> blocks)
    {
        string? next = NodeString(block["next"]);
        if (next is not null)
        {
            yield return next;
        }

        foreach (string child in InputBlockIds(block))
        {
            yield return child;
        }
    }

    private static IEnumerable<string> InputBlockIds(JsonObject block)
    {
        if (block["inputs"] is not JsonObject inputs)
        {
            yield break;
        }

        foreach (JsonNode? value in inputs.Select(static pair => pair.Value))
        {
            if (value is not JsonArray tuple || !IsInputTuple(tuple))
            {
                continue;
            }

            for (int index = 1; index < Math.Min(tuple.Count, 3); index++)
            {
                if (tuple[index] is JsonValue item && item.TryGetValue(out string? id) && id is not null)
                {
                    yield return id;
                }
            }
        }
    }

    private static IEnumerable<(string Name, string Value)> SubstackPairs(JsonObject block)
    {
        if (block["inputs"] is not JsonObject inputs)
        {
            yield break;
        }

        foreach ((string name, JsonNode? node) in inputs.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!name.StartsWith("SUBSTACK", StringComparison.Ordinal) || node is not JsonArray tuple || tuple.Count < 2)
            {
                continue;
            }

            string? id = NodeString(tuple[1]);
            if (id is not null)
            {
                yield return (name, id);
            }
        }
    }

    private static IEnumerable<string> SubstackIds(JsonObject block) => SubstackPairs(block).Select(static pair => pair.Value);

    private static bool HasSubstacks(JsonObject block) => SubstackPairs(block).Any();

    private static bool IsSubstackReference(JsonObject block, string id) => SubstackIds(block).Contains(id, StringComparer.Ordinal);

    private static bool IsInputTuple(JsonArray value)
    {
        return value.Count > 0 && Number(value[0]) is >= 1 and <= 3;
    }

    private string CreateAlias(string name, string prefix)
    {
        string candidate = ToIdentifier(name, prefix);
        string unique = candidate;
        int suffix = 2;
        while (!_usedAliases.Add(unique))
        {
            unique = $"{candidate}_{suffix++}";
        }

        return unique;
    }

    private static string ToIdentifier(string value, string prefix)
    {
        StringBuilder result = new();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                result.Append(character);
            }
            else
            {
                result.Append('_');
            }
        }

        if (result.Length == 0 || !(char.IsLetter(result[0]) || result[0] == '_'))
        {
            result.Insert(0, prefix + "_");
        }

        string identifier = result.ToString();
        return ReservedWords.Contains(identifier) ? prefix + "_" + identifier : identifier;
    }

    private static string EmitScalar(JsonNode? node)
    {
        if (node is null)
        {
            return "0";
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue(out string? text))
            {
                return Quote(text ?? string.Empty);
            }

            if (value.TryGetValue(out bool boolean))
            {
                return boolean ? "1" : "0";
            }

            return value.ToJsonString();
        }

        return Quote(node.ToJsonString());
    }

    private static string EmitDataValue(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool boolean)
        ? Quote(boolean ? "true" : "false") : EmitScalar(node);

    private static string NumericOrQuoted(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
            ? value
            : Quote(value);
    }

    private static string? FieldDisplay(JsonNode? node)
    {
        return node is JsonArray tuple && tuple.Count > 0 ? NodeString(tuple[0]) : NodeString(node);
    }

    private static string? NodeString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    private static double Number(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double number))
            {
                return number;
            }

            if (value.TryGetValue(out int integer))
            {
                return integer;
            }
        }

        return 0;
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);

    private static StringBuilder AppendIndent(StringBuilder builder, int indent) => builder.Append(' ', indent);

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private void AddError(string code, string message, string path) =>
        _issues.Add(new ValidationIssue(message, path, null, DiagnosticSeverity.Error, code));

    private void AddWarning(string code, string message, string path) =>
        _issues.Add(new ValidationIssue(message, path, null, DiagnosticSeverity.Warning, code));
}
