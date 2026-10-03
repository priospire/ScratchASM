using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenCTS.Core;

internal sealed partial class ScratchProjectDecompiler
{
    private static readonly Dictionary<string, CtsAliasDefinition[]> AliasesByOpcode = CtsBlockRegistry.Definitions
        .GroupBy(alias => alias.Opcode).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedProcedure> _procedures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedProcedure> _procedureDefinitions = new(StringComparer.Ordinal);
    private ImportedProcedure? _activeProcedure;

    private sealed record ImportedProcedure(string Name, string Code, string[] Ids, string[] Names,
        string[] Aliases, string[] Types, JsonArray Defaults, bool Warp);

    private void RegisterProcedures(IReadOnlyDictionary<string, JsonObject> blocks)
    {
        _procedures.Clear();
        _procedureDefinitions.Clear();
        _activeProcedure = null;
        foreach ((string id, JsonObject definition) in blocks)
        {
            if (NodeString(definition["opcode"]) != "procedures_definition" ||
                definition["inputs"]?["custom_block"] is not JsonArray input || input.Count < 2 ||
                NodeString(input[1]) is not string prototypeId || !blocks.TryGetValue(prototypeId, out JsonObject? prototype) ||
                prototype["mutation"] is not JsonObject mutation || NodeString(mutation["proccode"]) is not string code)
                continue;
            try
            {
                string[] ids = JsonSerializer.Deserialize<string[]>(NodeString(mutation["argumentids"]) ?? "[]") ?? [];
                string[] names = JsonSerializer.Deserialize<string[]>(NodeString(mutation["argumentnames"]) ?? "[]") ?? [];
                JsonArray defaults = JsonNode.Parse(NodeString(mutation["argumentdefaults"]) ?? "[]")!.AsArray();
                string[] types = Regex.Matches(code, "%[nsb]").Select(match => match.Value switch
                { "%b" => "bool", "%n" => "num", _ => "str" }).ToArray();
                if (ids.Length != names.Length || types.Length != ids.Length || _procedures.ContainsKey(code)) continue;
                HashSet<string> used = new(_usedAliases, StringComparer.Ordinal);
                string[] aliases = names.Select(name =>
                {
                    string candidate = ToIdentifier(name, "arg"), alias = candidate;
                    int suffix = 2;
                    while (!used.Add(alias)) alias = candidate + "_" + suffix++;
                    return alias;
                }).ToArray();
                string label = Regex.Replace(code, "%[nsb]", "").Trim();
                ImportedProcedure procedure = new(CreateAlias(label, "procedure"), code, ids, names, aliases, types, defaults,
                    string.Equals(mutation["warp"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase));
                _procedures[code] = procedure;
                _procedureDefinitions[id] = procedure;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                AddWarning("CTS3012", $"Custom block '{code}' has an unfamiliar signature; its generic form is retained.", "$.targets[*].blocks");
            }
        }
    }

    private bool TryEmitReadableRoot(StringBuilder source, IReadOnlyDictionary<string, JsonObject> blocks,
        string id, JsonObject block, int indent, HashSet<string> emitted, ScratchTargetOrigin origin)
    {
        if (_procedureDefinitions.TryGetValue(id, out ImportedProcedure? procedure))
        {
            MarkReporterInputs(block, blocks, emitted, origin);
            AppendIndent(source, indent).Append("proc ").Append(procedure.Name).Append('(');
            for (int i = 0; i < procedure.Ids.Length; i++)
            {
                if (i > 0) source.Append(", ");
                source.Append(procedure.Aliases[i]).Append(": ").Append(procedure.Types[i]);
                if (i < procedure.Defaults.Count) source.Append(" = ").Append(EmitDataValue(procedure.Defaults[i]));
            }
            source.Append(") as ").Append(Quote(procedure.Code));
            if (procedure.Warp) source.Append(" warp");
            source.AppendLine(":");
            _activeProcedure = procedure;
            if (NodeString(block["next"]) is string next) EmitStatementChain(source, blocks, next, indent + 2, emitted, origin);
            else AppendIndent(source, indent + 2).AppendLine("# empty custom block");
            _activeProcedure = null;
            return true;
        }
        if (TryMatchAlias(block, CtsBlockShape.Reporter, CtsBlockShape.Boolean, out _) ||
            NodeString(block["opcode"]) is "data_variable" or "data_listcontents")
        {
            AppendIndent(source, indent).Append("reporter ").AppendLine(EmitReporter(id, blocks));
            MarkReporterInputs(block, blocks, emitted, origin);
            return true;
        }
        if (TryMatchAlias(block, CtsBlockShape.Stack, CtsBlockShape.Cap, out _) ||
            TryMatchAlias(block, CtsBlockShape.CBlock) || NodeString(block["opcode"]) == "procedures_call")
        {
            AppendIndent(source, indent).AppendLine("stack:");
            EmitStatementChain(source, blocks, id, indent + 2, emitted, origin);
            return true;
        }
        return false;
    }

    private bool TryEmitReadableStatement(StringBuilder source, IReadOnlyDictionary<string, JsonObject> blocks,
        JsonObject block, int indent, HashSet<string> emitted, ScratchTargetOrigin origin)
    {
        string? opcode = NodeString(block["opcode"]);
        string Input(string name) => EmitInput(block["inputs"]?[name], blocks);
        string? Data(string name) => block["fields"]?[name] is JsonArray field && field.Count > 1 &&
            NodeString(field[1]) is string id && _dataById.TryGetValue(id, out ScratchDataOrigin? data) ? data.Alias : null;
        if (opcode == "procedures_call" && NodeString(block["mutation"]?["proccode"]) is string code &&
            _procedures.TryGetValue(code, out ImportedProcedure? procedure))
        {
            AppendIndent(source, indent).Append("call ").Append(procedure.Name).Append('(')
                .Append(string.Join(", ", procedure.Ids.Select((id, i) => block["inputs"]?[id] is null && i < procedure.Defaults.Count
                    ? EmitDataValue(procedure.Defaults[i]) : Input(id)))).AppendLine(")");
            MarkReporterInputs(block, blocks, emitted, origin);
            return true;
        }
        if (opcode is "data_setvariableto" or "data_changevariableby" && Data("VARIABLE") is string variable)
        {
            AppendIndent(source, indent).Append(variable).Append(opcode == "data_setvariableto" ? " = " : " += ")
                .AppendLine(Input("VALUE"));
            MarkReporterInputs(block, blocks, emitted, origin);
            return true;
        }
        if (Data("LIST") is string list)
        {
            string? operation = opcode switch
            {
                "data_addtolist" => ".add " + Input("ITEM"),
                "data_deleteoflist" => ".delete " + Input("INDEX"),
                "data_deletealloflist" => ".delete_all",
                "data_insertatlist" => ".insert " + Input("INDEX") + ", " + Input("ITEM"),
                "data_replaceitemoflist" => ".replace " + Input("INDEX") + ", " + Input("ITEM"),
                "data_showlist" => ".show", "data_hidelist" => ".hide", _ => null
            };
            if (operation is not null)
            {
                AppendIndent(source, indent).Append(list).AppendLine(operation);
                MarkReporterInputs(block, blocks, emitted, origin);
                return true;
            }
        }
        if (!TryMatchAlias(block, CtsBlockShape.CBlock, out CtsAliasDefinition? definition)) return false;
        AppendIndent(source, indent).Append(definition!.Name == "ifelse" ? "if" : definition.Name)
            .Append(EmitAliasArguments(block, definition, blocks)).AppendLine(":");
        for (int i = 0; i < definition.SubstackNames.Count; i++)
        {
            if (i > 0) AppendIndent(source, indent).AppendLine("else:");
            JsonNode? substack = block["inputs"]?[definition.SubstackNames[i]];
            if (substack is JsonArray tuple && tuple.Count > 1 && NodeString(tuple[1]) is string child)
                EmitStatementChain(source, blocks, child, indent + 2, emitted, origin);
            else AppendIndent(source, indent + 2).AppendLine("# empty branch");
        }
        MarkReporterInputs(block, blocks, emitted, origin);
        return true;
    }

    private bool TryEmitReadableReporter(JsonObject block, IReadOnlyDictionary<string, JsonObject> blocks, out string? expression)
    {
        expression = null;
        string? opcode = NodeString(block["opcode"]);
        if (opcode is "argument_reporter_string_number" or "argument_reporter_boolean" && _activeProcedure is not null)
        {
            int index = Array.LastIndexOf(_activeProcedure.Names, FieldDisplay(block["fields"]?["VALUE"]));
            if (index >= 0) { expression = _activeProcedure.Aliases[index]; return true; }
        }
        (string? symbol, string left, string right) = opcode switch
        {
            "operator_add" => ("+", "NUM1", "NUM2"), "operator_subtract" => ("-", "NUM1", "NUM2"),
            "operator_multiply" => ("*", "NUM1", "NUM2"), "operator_divide" => ("/", "NUM1", "NUM2"),
            "operator_mod" => ("%", "NUM1", "NUM2"), "operator_equals" => ("==", "OPERAND1", "OPERAND2"),
            "operator_gt" => (">", "OPERAND1", "OPERAND2"), "operator_lt" => ("<", "OPERAND1", "OPERAND2"),
            "operator_and" => ("and", "OPERAND1", "OPERAND2"), "operator_or" => ("or", "OPERAND1", "OPERAND2"),
            _ => ((string?)null, "", "")
        };
        if (symbol is not null)
            expression = "(" + EmitInput(block["inputs"]?[left], blocks) + " " + symbol + " " + EmitInput(block["inputs"]?[right], blocks) + ")";
        else if (opcode == "operator_not") expression = "(not " + EmitInput(block["inputs"]?["OPERAND"], blocks) + ")";
        return expression is not null;
    }
}
