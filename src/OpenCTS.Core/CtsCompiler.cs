using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenCTS.Core;

public sealed class CtsCompileResult
{
    public byte[] ProjectJsonBytes { get; init; } = [];

    public IReadOnlyDictionary<string, byte[]> Assets { get; init; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    public IReadOnlyList<CtsDiagnostic> Diagnostics { get; init; } = [];
}

public static class CtsCompiler
{
    private static readonly byte[] DefaultSvgBytes = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"360\" viewBox=\"0 0 480 360\"><rect width=\"480\" height=\"360\" fill=\"#ffffff\"/></svg>");
    private static readonly string DefaultAssetId = Convert.ToHexStringLower(MD5.HashData(DefaultSvgBytes));
    private static readonly string DefaultAssetFileName = DefaultAssetId + ".svg";

    public static CtsCompileResult Compile(string source, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 8 * 1024 * 1024)
        {
            return LimitError("Source exceeds the 8 MiB supported size limit.");
        }
        int nesting = 0;
        foreach (CtsToken token in CtsLexer.Lex(source))
        {
            if (token.Text is "(" or "[" or "{") nesting++;
            if (token.Text is ")" or "]" or "}") nesting--;
            if (nesting > 128) return LimitError("Source nesting exceeds the supported limit of 128.");
        }
        CtsProjectBuilder builder = new(sourceName);
        try
        {
            return builder.Compile(source);
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or JsonException or InvalidOperationException)
        {
            return LimitError($"Improper syntax: {ex.Message}");
        }
    }

    private static CtsCompileResult LimitError(string message) => new()
    {
        Diagnostics = [new CtsDiagnostic("CTS1030", DiagnosticSeverity.Error, message,
            new SourceSpan(new SourceLocation(1, 1), new SourceLocation(1, 1)))]
    };

    internal static IReadOnlyDictionary<string, byte[]> CreateDefaultAssets()
    {
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [DefaultAssetFileName] = DefaultSvgBytes
        };
    }

    private sealed class CtsProjectBuilder
    {
        private readonly string? _sourceName;
        private readonly List<CtsDiagnostic> _diagnostics = [];
        private readonly Dictionary<string, ProcedureInfo> _procedures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _assets = new(CreateDefaultAssets(), StringComparer.Ordinal);
        private readonly List<string> _extensions = [];
        private readonly HashSet<string> _extensionSet = new(StringComparer.Ordinal);
        private readonly HashSet<string> _terminalBlocks = new(StringComparer.Ordinal);
        private readonly ScratchIdAllocator _dataIdAllocator = new();
        private readonly ScratchIdAllocator _argumentIdAllocator = new();
        private readonly ScratchIdAllocator _blockIdAllocator = new();
        private JsonObject _currentBlocks = [];
        private TargetBuildData _currentTargetData = TargetBuildData.Empty;
        private TargetBuildData _stageTargetData = TargetBuildData.Empty;
        private int _nextBlockId;

        public CtsProjectBuilder(string? sourceName)
        {
            _sourceName = sourceName;
        }

        public CtsCompileResult Compile(string source)
        {
            CtsParseResult parseResult = CtsParser.Parse(source);
            _diagnostics.AddRange(parseResult.Diagnostics);
            if (ScratchAsmLanguage.IsCompatibilitySourceName(_sourceName))
            {
                _diagnostics.Add(new CtsDiagnostic(
                    "CTS2003",
                    DiagnosticSeverity.Warning,
                    "The .mono extension is supported for compatibility; use the canonical ScratchASM .sasm extension.",
                    parseResult.CompilationUnit.Span));
            }

            if (HasErrors())
            {
                return CreateResult([]);
            }

            CtsSemanticModel semanticModel = CtsBinder.Bind(parseResult.CompilationUnit);
            _diagnostics.AddRange(semanticModel.Diagnostics);
            if (HasErrors())
            {
                return CreateResult([]);
            }

            CtsLocalLoweringResult loweringResult = CtsLocalLowerer.Lower(semanticModel);
            _diagnostics.AddRange(loweringResult.Diagnostics);
            if (HasErrors())
            {
                return CreateResult([]);
            }

            CtsCompilationUnit compilationUnit = loweringResult.CompilationUnit;

            int stageCount = compilationUnit.Targets.Count(static target => target.IsStage);
            if (stageCount != 1)
            {
                AddError("CTS1004", "A ScratchASM project must declare exactly one stage target.", compilationUnit.Span);
                return CreateResult([]);
            }

            ValidateProcedures(compilationUnit);
            if (HasErrors())
            {
                return CreateResult([]);
            }

            CtsTargetDeclaration[] orderedTargets = compilationUnit.Targets
                .OrderByDescending(static target => target.IsStage)
                .ToArray();
            List<TargetBuildData> targetData = [];
            for (int i = 0; i < orderedTargets.Length; i++)
            {
                targetData.Add(BuildTargetData(orderedTargets[i], i));
            }

            _stageTargetData = targetData[0];

            JsonArray targets = [];
            int targetIndex = 0;
            foreach (CtsTargetDeclaration target in orderedTargets)
            {
                targets.Add(CompileTarget(target, targetIndex, targetData[targetIndex]));
                targetIndex++;
            }

            JsonArray extensions = [];
            foreach (string extension in _extensions)
            {
                extensions.Add(extension);
            }

            JsonObject project = new()
            {
                ["targets"] = targets,
                ["monitors"] = new JsonArray(),
                ["extensions"] = extensions,
                ["meta"] = new JsonObject
                {
                    ["semver"] = "3.0.0",
                    ["vm"] = "0.2.0",
                    ["agent"] = "OpenCTS",
                    ["scratchasm-ready"] = true
                }
            };

            byte[] projectJsonBytes = JsonSerializer.SerializeToUtf8Bytes(
                project,
                new JsonSerializerOptions { WriteIndented = true });

            return CreateResult(projectJsonBytes);
        }

        private JsonObject CompileTarget(CtsTargetDeclaration target, int targetIndex, TargetBuildData targetData)
        {
            _currentTargetData = targetData;
            _currentBlocks = [];
            _terminalBlocks.Clear();
            _nextBlockId = 0;
            RegisterTargetProcedures(target);

            int scriptIndex = 0;
            foreach (CtsScript script in target.Scripts)
            {
                switch (script)
                {
                    case CtsHatScript hat:
                        CompileHatScript(hat, scriptIndex);
                        break;
                    case CtsGenericHatScript genericHat:
                        CompileGenericHatScript(genericHat, scriptIndex);
                        break;
                    case CtsAliasHatScript aliasHat:
                        CompileAliasHatScript(aliasHat, scriptIndex);
                        break;
                    case CtsProcedureDefinition procedure:
                        CompileProcedureDefinition(procedure, scriptIndex);
                        break;
                }

                scriptIndex++;
            }

            foreach (CtsRawBlocksDeclaration raw in target.Members.OfType<CtsRawBlocksDeclaration>())
            {
                foreach ((string id, JsonNode? block) in JsonNode.Parse(raw.Json)!.AsObject())
                {
                    if (_currentBlocks.ContainsKey(id))
                        AddError("CTS1031", $"Duplicate raw block ID '{id}'.", raw.Span);
                    else
                        _currentBlocks[id] = block?.DeepClone();
                }
            }

            if (target.Members.OfType<CtsRawBlocksDeclaration>().FirstOrDefault() is CtsRawBlocksDeclaration rawSource)
            {
                using JsonDocument rawDocument = JsonDocument.Parse(_currentBlocks.ToJsonString());
                List<ValidationIssue> issues = [];
                ScratchProjectValidator.ValidateBlocks(rawDocument.RootElement, "$", JsonSourceMap.Empty, issues);
                foreach (ValidationIssue issue in issues)
                {
                    CtsRawBlocksDeclaration owner = target.Members.OfType<CtsRawBlocksDeclaration>().FirstOrDefault(raw =>
                        JsonNode.Parse(raw.Json)!.AsObject().ContainsKey(issue.JsonPath[2..])) ?? rawSource;
                    SourceLocation location = JsonSourceMap.Create(Encoding.UTF8.GetBytes(owner.Json)).GetLocation(issue.JsonPath) ?? new SourceLocation(1, 1);
                    SourceLocation start = new(owner.Span.Start.Line + location.Line - 1,
                        location.Column + (location.Line == 1 ? owner.Span.Start.Column + 8 : 0));
                    _diagnostics.Add(new CtsDiagnostic(issue.Code ?? "CTS1031", issue.Severity, issue.Message, new SourceSpan(start, start)));
                }
            }

            return target.IsStage
                ? CreateStageTarget(target.Name, _currentBlocks, _currentTargetData)
                : CreateSpriteTarget(target.Name, _currentBlocks, _currentTargetData, targetIndex);
        }

        private void ValidateProcedures(CtsCompilationUnit unit)
        {
            foreach (CtsTargetDeclaration target in unit.Targets)
            {
                HashSet<string> names = new(StringComparer.Ordinal);
                foreach (CtsProcedureDefinition procedure in target.Scripts.OfType<CtsProcedureDefinition>())
                {
                    if (!names.Add(procedure.Name))
                    {
                        AddError("CTS1005", $"Procedure '{procedure.Name}' is already defined.", procedure.Span);
                        continue;
                    }

                    if (!HasValidProcedureSignature(procedure))
                    {
                        AddError(
                            "CTS1011",
                            $"Procedure '{procedure.Name}' display signature placeholders do not match its parameter types.",
                            procedure.Span);
                        continue;
                    }

                }
            }
        }

        private void RegisterTargetProcedures(CtsTargetDeclaration target)
        {
            _procedures.Clear();
            foreach (CtsProcedureDefinition procedure in target.Scripts.OfType<CtsProcedureDefinition>())
            {
                string safeName = SanitizeIdPart(procedure.Name);
                string[] argumentIds = procedure.Parameters
                    .Select(parameter => _argumentIdAllocator.Allocate($"{safeName}_{SanitizeIdPart(parameter.Name)}"))
                    .ToArray();
                _procedures[procedure.Name] = new ProcedureInfo(procedure, argumentIds);
            }
        }

        private TargetBuildData BuildTargetData(CtsTargetDeclaration target, int targetIndex)
        {
            string targetPrefix = target.IsStage ? "stage" : $"sprite_{SanitizeIdPart(target.Name)}";
            TargetBuildData data = new();

            foreach (CtsTargetMember member in target.Members)
            {
                switch (member)
                {
                    case CtsVariableDeclaration variable:
                    {
                        string id = _dataIdAllocator.Allocate($"{targetPrefix}_var_{SanitizeIdPart(variable.Name)}");
                        JsonArray values = JsonArrayOf(variable.Name, ValueToScratchText(variable.InitialValue));
                        if (variable.IsCloud)
                        {
                            values.Add(true);
                        }

                        data.Variables[id] = values;
                        data.VariableIds[variable.Name] = id;
                        break;
                    }

                    case CtsListDeclaration list:
                    {
                        string id = _dataIdAllocator.Allocate($"{targetPrefix}_list_{SanitizeIdPart(list.Name)}");
                        JsonArray items = [];
                        foreach (CtsValue item in list.Items)
                        {
                            items.Add(ValueToScratchText(item));
                        }

                        data.Lists[id] = JsonArrayOf(list.Name, items);
                        data.ListIds[list.Name] = id;
                        break;
                    }

                    case CtsBroadcastDeclaration broadcast:
                    {
                        string id = _dataIdAllocator.Allocate($"{targetPrefix}_broadcast_{SanitizeIdPart(broadcast.Name)}");
                        string message = ValueToScratchText(broadcast.Message);
                        data.Broadcasts[id] = message;
                        data.BroadcastsByName[broadcast.Name] = new BroadcastInfo(id, message);
                        break;
                    }

                    case CtsExtensionDeclaration extension:
                        AddExtension(extension.Name);
                        break;

                    case CtsStateDeclaration state:
                        ApplyState(data, state);
                        break;

                    case CtsRotationStyleDeclaration rotationStyle:
                        data.RotationStyle = rotationStyle.Value;
                        break;

                    case CtsCostumeDeclaration costume:
                        data.Costumes.Add(CreateGeneratedCostume(costume));
                        break;
                }
            }

            if (data.Costumes.Count == 0)
            {
                data.Costumes.Add(DefaultCostume(target.IsStage ? "backdrop1" : "costume1"));
            }

            return data;
        }

        private void AddExtension(string name)
        {
            if (_extensionSet.Add(name))
            {
                _extensions.Add(name);
            }
        }

        private static void ApplyState(TargetBuildData data, CtsStateDeclaration state)
        {
            foreach (KeyValuePair<string, CtsValue> property in state.Properties)
            {
                switch (property.Key)
                {
                    case "x":
                        data.X = ValueToDouble(property.Value);
                        break;
                    case "y":
                        data.Y = ValueToDouble(property.Value);
                        break;
                    case "direction":
                        data.Direction = ValueToDouble(property.Value);
                        break;
                    case "size":
                        data.Size = ValueToDouble(property.Value);
                        break;
                    case "visible":
                        data.Visible = ValueToBoolean(property.Value);
                        break;
                    case "layer":
                    case "layerOrder":
                        data.LayerOrder = checked((int)Math.Round(ValueToDouble(property.Value)));
                        break;
                }
            }
        }

        private void CompileHatScript(CtsHatScript script, int scriptIndex)
        {
            string opcode;
            JsonObject fields = [];
            switch (script.HatName)
            {
                case "greenflag":
                    opcode = "event_whenflagclicked";
                    break;
                case "key":
                    opcode = "event_whenkeypressed";
                    fields["KEY_OPTION"] = JsonArrayOf(script.HatArgument ?? "space", null);
                    break;
                default:
                    AddError("CTS1006", $"Unsupported hat '@{script.HatName}'. Use @greenflag or @key.", script.Span);
                    return;
            }

            string hatId = NewBlockId("hat");
            AddBlock(hatId, opcode, null, null, [], fields, shadow: false, topLevel: true, 48, 48 + scriptIndex * 112, null);
            string? firstStatement = CompileStack(script.Statements, hatId, null);
            SetNext(hatId, firstStatement);
        }

        private void CompileGenericHatScript(CtsGenericHatScript script, int scriptIndex)
        {
            string hatId = NewBlockId("hat");
            JsonObject inputs = [];
            foreach (CtsRawInput input in script.Inputs)
            {
                inputs[input.Name] = BuildInput(input.Name, input.Value, hatId, null);
            }

            JsonObject fields = [];
            foreach (CtsRawField field in script.Fields)
            {
                fields[field.Name] = BuildField(field);
            }

            AddBlock(
                hatId,
                script.Opcode,
                null,
                null,
                inputs,
                fields,
                shadow: false,
                topLevel: true,
                48,
                48 + scriptIndex * 112,
                BuildMutation(script.Mutation));
            string? firstStatement = CompileStack(script.Statements, hatId, null);
            SetNext(hatId, firstStatement);
        }

        private void CompileAliasHatScript(CtsAliasHatScript script, int scriptIndex)
        {
            if (!CtsBlockRegistry.TryResolve(script.HatName, script.Arguments.Count, out CtsAliasDefinition definition) ||
                definition.Shape != CtsBlockShape.Hat)
            {
                AddError("CTS1006", $"Unsupported hat '@{script.HatName}'.", script.Span);
                return;
            }

            string hatId = NewBlockId("hat");
            BuildAliasBindings(definition, script.Arguments, hatId, null, out JsonObject inputs, out JsonObject fields);
            RegisterExtension(definition);
            AddBlock(
                hatId,
                definition.Opcode,
                null,
                null,
                inputs,
                fields,
                shadow: false,
                topLevel: true,
                48,
                48 + scriptIndex * 112,
                null);
            string? firstStatement = CompileStack(script.Statements, hatId, null);
            SetNext(hatId, firstStatement);
        }

        private void CompileProcedureDefinition(CtsProcedureDefinition procedure, int scriptIndex)
        {
            ProcedureInfo info = _procedures[procedure.Name];
            string definitionId = NewBlockId("procdef");
            string prototypeId = NewBlockId("proto");

            JsonObject definitionInputs = new()
            {
                ["custom_block"] = JsonArrayOf(1, prototypeId)
            };

            AddBlock(
                definitionId,
                "procedures_definition",
                null,
                null,
                definitionInputs,
                [],
                shadow: false,
                topLevel: true,
                320,
                48 + scriptIndex * 136,
                null);

            JsonObject prototypeInputs = [];
            for (int i = 0; i < procedure.Parameters.Count; i++)
            {
                CtsProcedureParameter parameter = procedure.Parameters[i];
                string argumentBlockId = NewBlockId("arg");
                AddArgumentReporter(argumentBlockId, parameter, prototypeId, shadow: true);
                prototypeInputs[info.ArgumentIds[i]] = JsonArrayOf(1, argumentBlockId);
            }

            AddBlock(
                prototypeId,
                "procedures_prototype",
                null,
                definitionId,
                prototypeInputs,
                [],
                shadow: true,
                topLevel: false,
                null,
                null,
                CreateProcedureMutation(info, includeNamesAndDefaults: true, warp: procedure.Warp));

            string? firstStatement = CompileStack(procedure.Statements, definitionId, info);
            SetNext(definitionId, firstStatement);
        }

        private string? CompileStack(
            IReadOnlyList<CtsStatement> statements,
            string parentId,
            ProcedureInfo? procedure)
        {
            string? firstId = null;
            string? previousId = null;

            foreach (CtsStatement statement in statements)
            {
                if (previousId is not null && _terminalBlocks.Contains(previousId))
                {
                    _diagnostics.Add(new CtsDiagnostic(
                        "CTS2002",
                        DiagnosticSeverity.Warning,
                        "Statement is unreachable because the previous block never continues.",
                        statement.Span));
                    break;
                }

                string? blockId = CompileStatement(statement, previousId ?? parentId, procedure);
                if (blockId is null)
                {
                    continue;
                }

                if (firstId is null)
                {
                    firstId = blockId;
                }

                if (previousId is not null)
                {
                    SetNext(previousId, blockId);
                }

                previousId = blockId;
            }

            return firstId;
        }

        private string? CompileStatement(CtsStatement statement, string parentId, ProcedureInfo? procedure)
        {
            return statement switch
            {
                CtsAliasStatement alias => CompileAliasStatement(alias, parentId, procedure),
                CtsStructuredStatement structured => CompileStructuredStatement(structured, parentId, procedure),
                CtsVariableOperationStatement variable => CompileVariableOperation(variable, parentId, procedure),
                CtsListOperationStatement list => CompileListOperation(list, parentId, procedure),
                CtsCallStatement call => CompileCallStatement(call, parentId, procedure),
                CtsRawStatement raw => CompileRawStatement(raw, parentId, procedure),
                CtsBlockStatement block => CompileBlockStatement(block, parentId, procedure),
                _ => null
            };
        }

        private string? CompileAliasStatement(CtsAliasStatement statement, string parentId, ProcedureInfo? procedure)
        {
            if (!CtsBlockRegistry.TryResolve(statement.CommandName, statement.Arguments.Count, out CtsAliasDefinition alias))
            {
                if (CtsBlockRegistry.HasAlias(statement.CommandName))
                {
                    string counts = string.Join(", ", CtsBlockRegistry.GetSupportedArgumentCounts(statement.CommandName));
                    AddError("CTS1007", $"Command '{statement.CommandName}' expects argument count {counts}.", statement.Span);
                }
                else
                {
                    AddError("CTS1008", $"Unknown command alias '{statement.CommandName}'. Use raw '%' for unsupported opcodes.", statement.Span);
                }

                return null;
            }

            if (alias.Shape is CtsBlockShape.Reporter or CtsBlockShape.Boolean or CtsBlockShape.Hat or CtsBlockShape.CBlock)
            {
                AddError("CTS1008", $"Alias '{statement.CommandName}' cannot be used as a stack command.", statement.Span);
                return null;
            }

            return CompileAliasDefinition(alias, statement.Arguments, parentId, procedure);
        }

        private string? CompileStructuredStatement(CtsStructuredStatement statement, string parentId, ProcedureInfo? procedure)
        {
            if (!CtsBlockRegistry.TryResolve(statement.CommandName, statement.Arguments.Count, out CtsAliasDefinition definition) ||
                definition.Shape != CtsBlockShape.CBlock)
            {
                AddError("CTS1008", $"Unknown structured block '{statement.CommandName}'.", statement.Span);
                return null;
            }

            string blockId = CompileAliasDefinition(definition, statement.Arguments, parentId, procedure);
            JsonObject inputs = (JsonObject)_currentBlocks[blockId]!["inputs"]!;
            foreach (string substackName in definition.SubstackNames)
            {
                if (!statement.Substacks.TryGetValue(substackName, out IReadOnlyList<CtsStatement>? substack))
                {
                    continue;
                }

                string? first = CompileStack(substack, blockId, procedure);
                if (first is not null)
                {
                    inputs[substackName] = JsonArrayOf(2, first);
                }
            }

            return blockId;
        }

        private string? CompileVariableOperation(CtsVariableOperationStatement statement, string parentId, ProcedureInfo? procedure)
        {
            if (!TryGetVariableId(statement.VariableName, out string? variableId))
            {
                AddError("CTS1016", $"Variable '{statement.VariableName}' is not declared in this target or the stage.", statement.Span);
                return null;
            }

            string blockId = NewBlockId("data");
            JsonObject inputs = new()
            {
                ["VALUE"] = BuildInput("VALUE", statement.Value, blockId, procedure)
            };
            JsonObject fields = new()
            {
                ["VARIABLE"] = JsonArrayOf(statement.VariableName, variableId)
            };
            AddBlock(
                blockId,
                statement.IsChange ? "data_changevariableby" : "data_setvariableto",
                null,
                parentId,
                inputs,
                fields,
                shadow: false,
                topLevel: false,
                null,
                null,
                null);
            return blockId;
        }

        private string? CompileListOperation(CtsListOperationStatement statement, string parentId, ProcedureInfo? procedure)
        {
            if (!TryGetListId(statement.ListName, out string? listId))
            {
                AddError("CTS1017", $"List '{statement.ListName}' is not declared in this target or the stage.", statement.Span);
                return null;
            }

            string opcode = statement.Operation switch
            {
                "add" => "data_addtolist",
                "delete" => "data_deleteoflist",
                "deleteall" => "data_deletealloflist",
                "insert" => "data_insertatlist",
                "replace" => "data_replaceitemoflist",
                "show" => "data_showlist",
                "hide" => "data_hidelist",
                _ => string.Empty
            };
            string blockId = NewBlockId("list");
            JsonObject inputs = [];
            if (statement.Operation == "add")
            {
                inputs["ITEM"] = BuildInput("ITEM", statement.Arguments[0], blockId, procedure);
            }
            else if (statement.Operation == "delete")
            {
                inputs["INDEX"] = BuildInput("INDEX", statement.Arguments[0], blockId, procedure);
            }
            else if (statement.Operation is "insert" or "replace")
            {
                inputs["INDEX"] = BuildInput("INDEX", statement.Arguments[0], blockId, procedure);
                inputs["ITEM"] = BuildInput("ITEM", statement.Arguments[1], blockId, procedure);
            }

            JsonObject fields = new()
            {
                ["LIST"] = JsonArrayOf(statement.ListName, listId)
            };
            AddBlock(blockId, opcode, null, parentId, inputs, fields, false, false, null, null, null);
            return blockId;
        }

        private string CompileAliasDefinition(
            CtsAliasDefinition definition,
            IReadOnlyList<CtsValue> arguments,
            string parentId,
            ProcedureInfo? procedure)
        {
            string blockId = NewBlockId("block");
            BuildAliasBindings(definition, arguments, blockId, procedure, out JsonObject inputs, out JsonObject fields);
            RegisterExtension(definition);
            AddBlock(blockId, definition.Opcode, null, parentId, inputs, fields, false, false, null, null, null);

            bool terminal = definition.TerminalPolicy == CtsTerminalPolicy.AlwaysCaps ||
                definition.TerminalPolicy == CtsTerminalPolicy.CapsUnlessOtherScripts &&
                arguments.Count > 0 &&
                !string.Equals(ValueToScratchText(arguments[0]), "other scripts in sprite", StringComparison.OrdinalIgnoreCase);
            if (terminal)
            {
                _terminalBlocks.Add(blockId);
            }

            return blockId;
        }

        private void BuildAliasBindings(
            CtsAliasDefinition definition,
            IReadOnlyList<CtsValue> arguments,
            string blockId,
            ProcedureInfo? procedure,
            out JsonObject inputs,
            out JsonObject fields)
        {
            inputs = [];
            fields = [];
            foreach (KeyValuePair<string, string> fixedField in definition.ConstantFields)
            {
                fields[fixedField.Key] = JsonArrayOf(fixedField.Value, null);
            }

            for (int i = 0; i < definition.Bindings.Count; i++)
            {
                CtsArgumentBinding binding = definition.Bindings[i];
                CtsValue value = arguments[i];
                switch (binding.Kind)
                {
                    case CtsBindingKind.Input:
                        inputs[binding.Name] = BuildInput(binding.Name, value, blockId, procedure);
                        break;
                    case CtsBindingKind.Field:
                        fields[binding.Name] = BuildField(new CtsRawField(binding.Name, value, null));
                        break;
                    case CtsBindingKind.Menu:
                        inputs[binding.Name] = BuildMenuInput(binding, value, blockId, procedure);
                        break;
                }
            }
        }

        private JsonArray BuildMenuInput(CtsArgumentBinding binding, CtsValue value, string parentId, ProcedureInfo? procedure)
        {
            if (binding.Name == "BROADCAST_INPUT" && TryGetBroadcast(ValueToScratchText(value), out BroadcastInfo broadcast))
            {
                string broadcastId = NewBlockId("menu");
                AddBlock(
                    broadcastId,
                    binding.MenuOpcode!,
                    null,
                    parentId,
                    [],
                    new JsonObject { [binding.MenuField!] = JsonArrayOf(broadcast.Message, broadcast.Id) },
                    true,
                    false,
                    null,
                    null,
                    null);
                return JsonArrayOf(1, broadcastId);
            }

            if (value is CtsUnaryValue or CtsBinaryValue or CtsFunctionValue ||
                value is CtsIdentifierValue identifier &&
                (TryGetVariableId(identifier.Name, out _) || procedure?.TryGetParameter(identifier.Name, out _) == true))
            {
                return BuildInput(binding.Name, value, parentId, procedure);
            }

            string menuId = NewBlockId("menu");
            string display = ValueToScratchText(value);
            string? fieldId = null;
            string fieldName = binding.MenuField ?? binding.Name;
            fieldId = ResolveFieldId(fieldName, value, ref display);
            AddBlock(
                menuId,
                binding.MenuOpcode!,
                null,
                parentId,
                [],
                new JsonObject { [fieldName] = JsonArrayOf(display, fieldId) },
                true,
                false,
                null,
                null,
                null);
            return JsonArrayOf(1, menuId);
        }

        private void RegisterExtension(CtsAliasDefinition definition)
        {
            if (definition.ExtensionId is not null)
            {
                AddExtension(definition.ExtensionId);
            }
        }

        private string? CompileCallStatement(CtsCallStatement statement, string parentId, ProcedureInfo? currentProcedure)
        {
            if (!_procedures.TryGetValue(statement.ProcedureName, out ProcedureInfo? procedure))
            {
                AddError("CTS1009", $"Unknown procedure '{statement.ProcedureName}'.", statement.Span);
                return null;
            }

            if (statement.Arguments.Count != procedure.Definition.Parameters.Count)
            {
                AddError(
                    "CTS1010",
                    $"Procedure '{statement.ProcedureName}' expects {procedure.Definition.Parameters.Count} argument(s).",
                    statement.Span);
                return null;
            }

            string blockId = NewBlockId("call");
            JsonObject inputs = [];
            for (int i = 0; i < procedure.Definition.Parameters.Count; i++)
            {
                inputs[procedure.ArgumentIds[i]] = BuildInput(null, statement.Arguments[i], blockId, currentProcedure);
            }

            AddBlock(
                blockId,
                "procedures_call",
                null,
                parentId,
                inputs,
                [],
                shadow: false,
                topLevel: false,
                null,
                null,
                CreateProcedureMutation(procedure, includeNamesAndDefaults: true, warp: false));
            return blockId;
        }

        private string CompileRawStatement(CtsRawStatement statement, string parentId, ProcedureInfo? procedure)
        {
            _diagnostics.Add(new CtsDiagnostic(
                "CTS2001",
                DiagnosticSeverity.Warning,
                $"Raw opcode '{statement.Opcode}' bypasses alias metadata and is emitted as-is.",
                statement.Span));

            string blockId = NewBlockId("raw");
            JsonObject inputs = [];
            foreach (CtsRawInput input in statement.Inputs)
            {
                inputs[input.Name] = BuildInput(input.Name, input.Value, blockId, procedure);
            }

            JsonObject fields = [];
            foreach (CtsRawField field in statement.Fields)
            {
                fields[field.Name] = BuildField(field);
            }

            JsonObject? mutation = BuildMutation(statement.Mutation);

            AddBlock(blockId, statement.Opcode, null, parentId, inputs, fields, shadow: false, topLevel: false, null, null, mutation);
            return blockId;
        }

        private string CompileBlockStatement(CtsBlockStatement statement, string parentId, ProcedureInfo? procedure)
        {
            string blockId = NewBlockId("block");
            JsonObject inputs = [];
            foreach (CtsRawInput input in statement.Inputs)
            {
                inputs[input.Name] = BuildInput(input.Name, input.Value, blockId, procedure);
            }

            JsonObject fields = [];
            foreach (CtsRawField field in statement.Fields)
            {
                fields[field.Name] = BuildField(field);
            }

            AddBlock(blockId, statement.Opcode, null, parentId, inputs, fields, shadow: false, topLevel: false, null, null, BuildMutation(statement.Mutation));

            if (statement.Statements.Count > 0)
            {
                string? firstStatement = CompileStack(statement.Statements, blockId, procedure);
                if (firstStatement is not null)
                {
                    inputs["SUBSTACK"] = JsonArrayOf(2, firstStatement);
                }
            }

            foreach (KeyValuePair<string, IReadOnlyList<CtsStatement>> substack in statement.NamedSubstacks)
            {
                string? firstStatement = CompileStack(substack.Value, blockId, procedure);
                if (firstStatement is not null)
                {
                    inputs[substack.Key] = JsonArrayOf(2, firstStatement);
                }
            }

            return blockId;
        }

        private JsonArray BuildField(CtsRawField field)
        {
            string valueText = ValueToScratchText(field.Value);
            string? id = field.Id is null ? ResolveFieldId(field.Name, field.Value, ref valueText) : ValueToScratchText(field.Id);
            return JsonArrayOf(valueText, id);
        }

        private string? ResolveFieldId(string fieldName, CtsValue value, ref string displayName)
        {
            string lookupName = ValueToScratchText(value);
            if (fieldName is "VARIABLE" && TryGetVariableId(lookupName, out string? variableId))
            {
                displayName = lookupName;
                return variableId;
            }

            if (fieldName is "LIST" && TryGetListId(lookupName, out string? listId))
            {
                displayName = lookupName;
                return listId;
            }

            if (fieldName is "BROADCAST_OPTION" && TryGetBroadcast(lookupName, out BroadcastInfo broadcast))
            {
                displayName = broadcast.Message;
                return broadcast.Id;
            }

            return null;
        }

        private static JsonObject? BuildMutation(IReadOnlyDictionary<string, CtsValue> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            JsonObject mutation = new()
            {
                ["tagName"] = "mutation",
                ["children"] = new JsonArray()
            };

            foreach (KeyValuePair<string, CtsValue> item in values)
            {
                mutation[item.Key] = ValueToScratchText(item.Value);
            }

            return mutation;
        }

        private JsonArray BuildInput(string? inputName, CtsValue value, string parentId, ProcedureInfo? procedure)
        {
            if (value is CtsBlockValue blockValue)
            {
                string reporterId = CompileBlockValue(blockValue, parentId, procedure);
                return JsonArrayOf(blockValue.IsShadow ? 1 : 2, reporterId);
            }

            if (inputName is "BROADCAST_INPUT" &&
                TryGetBroadcast(ValueToScratchText(value), out BroadcastInfo broadcast))
            {
                string menuId = NewBlockId("broadcast");
                JsonObject fields = new()
                {
                    ["BROADCAST_OPTION"] = JsonArrayOf(broadcast.Message, broadcast.Id)
                };
                AddBlock(menuId, "event_broadcast_menu", null, parentId, [], fields, shadow: true, topLevel: false, null, null, null);
                return JsonArrayOf(1, menuId);
            }

            if (value is CtsIdentifierValue identifier &&
                procedure is not null &&
                procedure.TryGetParameter(identifier.Name, out CtsProcedureParameter? parameter) &&
                parameter is not null)
            {
                string reporterId = NewBlockId("arg");
                AddArgumentReporter(reporterId, parameter, parentId, shadow: false);
                return parameter.Type == CtsParameterType.Boolean
                    ? JsonArrayOf(2, reporterId)
                    : JsonArrayOf(3, reporterId, DefaultShadow(parameter));
            }

            if (value is CtsUnaryValue or CtsBinaryValue or CtsFunctionValue ||
                value is CtsIdentifierValue variableIdentifier && TryGetVariableId(variableIdentifier.Name, out _))
            {
                string? reporterId = CompileExpression(value, parentId, procedure, out bool isBoolean);
                if (reporterId is not null)
                {
                    return isBoolean
                        ? JsonArrayOf(2, reporterId)
                        : JsonArrayOf(3, reporterId, DefaultInputShadow(inputName));
                }
            }

            return JsonArrayOf(1, LiteralValue(value));
        }

        private string? CompileExpression(CtsValue value, string parentId, ProcedureInfo? procedure, out bool isBoolean)
        {
            isBoolean = false;
            if (value is CtsIdentifierValue identifier)
            {
                if (!TryGetVariableId(identifier.Name, out string? variableId))
                {
                    AddError("CTS1016", $"Variable '{identifier.Name}' is not declared.", identifier.Span);
                    return null;
                }

                string variableBlockId = NewBlockId("reporter");
                AddBlock(
                    variableBlockId,
                    "data_variable",
                    null,
                    parentId,
                    [],
                    new JsonObject { ["VARIABLE"] = JsonArrayOf(identifier.Name, variableId) },
                    false,
                    false,
                    null,
                    null,
                    null);
                return variableBlockId;
            }

            if (value is CtsUnaryValue unary)
            {
                if (unary.Operator == "not")
                {
                    isBoolean = true;
                    return CompileReporterDefinition(
                        CtsBlockRegistry.Definitions.Single(definition => definition.Opcode == "operator_not"),
                        [unary.Operand],
                        parentId,
                        procedure);
                }

                return CompileReporterDefinition(
                    CtsBlockRegistry.Definitions.Single(definition => definition.Opcode == "operator_subtract"),
                    [new CtsNumberValue(0, "0", unary.Span), unary.Operand],
                    parentId,
                    procedure);
            }

            if (value is CtsBinaryValue binary)
            {
                if (binary.Operator == "^")
                {
                    return CompileLiteralPower(binary, parentId, procedure);
                }

                if (binary.Operator is "<=" or ">=" or "!=")
                {
                    string comparison = binary.Operator switch
                    {
                        "<=" => ">",
                        ">=" => "<",
                        _ => "=="
                    };
                    CtsValue inverted = new CtsUnaryValue(
                        "not",
                        new CtsBinaryValue(comparison, binary.Left, binary.Right, binary.Span),
                        binary.Span);
                    return CompileExpression(inverted, parentId, procedure, out isBoolean);
                }

                string opcode = binary.Operator switch
                {
                    "+" => "operator_add",
                    "-" => "operator_subtract",
                    "*" => "operator_multiply",
                    "/" => "operator_divide",
                    "%" => "operator_mod",
                    ">" => "operator_gt",
                    "<" => "operator_lt",
                    "==" => "operator_equals",
                    "and" => "operator_and",
                    "or" => "operator_or",
                    _ => string.Empty
                };
                CtsAliasDefinition? definition = CtsBlockRegistry.Definitions.FirstOrDefault(candidate => candidate.Opcode == opcode);
                if (definition is null)
                {
                    AddError("CTS1018", $"Unsupported operator '{binary.Operator}'.", binary.Span);
                    return null;
                }

                isBoolean = definition.Shape == CtsBlockShape.Boolean;
                return CompileReporterDefinition(definition, [binary.Left, binary.Right], parentId, procedure);
            }

            if (value is CtsFunctionValue function)
            {
                if (TryCompileListReporter(function, parentId, procedure, out string? listReporterId, out isBoolean))
                {
                    return listReporterId;
                }

                string functionName = function.Name;
                string? mathOperation = functionName switch
                {
                    "abs" => "abs",
                    "floor" => "floor",
                    "ceil" or "ceiling" => "ceiling",
                    "sqrt" => "sqrt",
                    "sin" => "sin",
                    "cos" => "cos",
                    "tan" => "tan",
                    "asin" => "asin",
                    "acos" => "acos",
                    "atan" => "atan",
                    "ln" => "ln",
                    "log10" or "log" => "log",
                    "exp" => "e ^",
                    "pow10" => "10 ^",
                    _ => null
                };
                if (mathOperation is not null)
                {
                    if (function.Arguments.Count != 1)
                    {
                        AddError("CTS1019", $"Function '{function.Name}' expects 1 argument.", function.Span);
                        return null;
                    }

                    string blockId = NewBlockId("reporter");
                    JsonObject inputs = new()
                    {
                        ["NUM"] = BuildInput("NUM", function.Arguments[0], blockId, procedure)
                    };
                    JsonObject fields = new()
                    {
                        ["OPERATOR"] = JsonArrayOf(mathOperation, null)
                    };
                    AddBlock(blockId, "operator_mathop", null, parentId, inputs, fields, false, false, null, null, null);
                    return blockId;
                }

                string aliasName = functionName switch
                {
                    "letter_of" => "letter",
                    "index_of" => "list.index",
                    _ => functionName
                };
                if (!CtsBlockRegistry.TryResolveExpression(aliasName, function.Arguments.Count, out CtsAliasDefinition definition))
                {
                    AddError("CTS1018", $"Unknown reporter function '{function.Name}'.", function.Span);
                    return null;
                }

                isBoolean = definition.Shape == CtsBlockShape.Boolean;
                RegisterExtension(definition);
                return CompileReporterDefinition(definition, function.Arguments, parentId, procedure);
            }

            return null;
        }

        private string CompileReporterDefinition(
            CtsAliasDefinition definition,
            IReadOnlyList<CtsValue> arguments,
            string parentId,
            ProcedureInfo? procedure)
        {
            string blockId = NewBlockId("reporter");
            BuildAliasBindings(definition, arguments, blockId, procedure, out JsonObject inputs, out JsonObject fields);
            RegisterExtension(definition);
            AddBlock(blockId, definition.Opcode, null, parentId, inputs, fields, false, false, null, null, null);
            return blockId;
        }

        private bool TryCompileListReporter(
            CtsFunctionValue function,
            string parentId,
            ProcedureInfo? procedure,
            out string? blockId,
            out bool isBoolean)
        {
            blockId = null;
            isBoolean = false;
            int dot = function.Name.LastIndexOf('.');
            if (dot <= 0)
            {
                return false;
            }

            string listName = function.Name[..dot];
            string operation = function.Name[(dot + 1)..];
            string opcode = operation switch
            {
                "item" => "data_itemoflist",
                "index" or "index_of" => "data_itemnumoflist",
                "length" => "data_lengthoflist",
                "contains" => "data_listcontainsitem",
                "contents" => "data_listcontents",
                _ => string.Empty
            };
            if (opcode.Length == 0)
            {
                return false;
            }

            if (!TryGetListId(listName, out string? listId))
            {
                return false;
            }

            int expected = operation is "length" or "contents" ? 0 : 1;
            if (function.Arguments.Count != expected)
            {
                AddError("CTS1019", $"List reporter '{operation}' expects {expected} argument(s).", function.Span);
                return true;
            }

            blockId = NewBlockId("reporter");
            JsonObject inputs = [];
            if (expected == 1)
            {
                string inputName = operation == "item" ? "INDEX" : "ITEM";
                inputs[inputName] = BuildInput(inputName, function.Arguments[0], blockId, procedure);
            }

            AddBlock(
                blockId,
                opcode,
                null,
                parentId,
                inputs,
                new JsonObject { ["LIST"] = JsonArrayOf(listName, listId) },
                false,
                false,
                null,
                null,
                null);
            isBoolean = opcode == "data_listcontainsitem";
            return true;
        }

        private string? CompileLiteralPower(CtsBinaryValue power, string parentId, ProcedureInfo? procedure)
        {
            if (!IsStablePowerBase(power.Left))
            {
                AddError(
                    "CTS1020",
                    "The '^' base must be a stable variable, literal, or pure expression so repeated multiplication preserves its value.",
                    power.Left.Span);
                return null;
            }

            int exponent;
            if (power.Right is CtsNumberValue number && number.Number == Math.Truncate(number.Number))
            {
                exponent = checked((int)number.Number);
            }
            else if (power.Right is CtsUnaryValue { Operator: "-", Operand: CtsNumberValue negative } &&
                negative.Number == Math.Truncate(negative.Number))
            {
                exponent = checked(-(int)negative.Number);
            }
            else
            {
                AddError("CTS1020", "The '^' operator requires an integer literal exponent. Use exp() or pow10() for Scratch math operations.", power.Right.Span);
                return null;
            }

            if (Math.Abs((long)exponent) > 64)
            {
                AddError("CTS1020", "The '^' exponent must be between -64 and 64.", power.Right.Span);
                return null;
            }

            CtsValue expanded = ExpandPositivePower(power.Left, Math.Abs(exponent), power.Span);
            if (exponent < 0)
            {
                expanded = new CtsBinaryValue(
                    "/",
                    new CtsNumberValue(1, "1", power.Span),
                    expanded,
                    power.Span);
            }

            return CompileExpression(expanded, parentId, procedure, out _);
        }

        private static bool IsStablePowerBase(CtsValue value)
        {
            if (value is CtsNumberValue or CtsStringValue or CtsIdentifierValue)
            {
                return true;
            }

            if (value is CtsUnaryValue unary)
            {
                return IsStablePowerBase(unary.Operand);
            }

            if (value is CtsBinaryValue binary)
            {
                return IsStablePowerBase(binary.Left) && IsStablePowerBase(binary.Right);
            }

            if (value is not CtsFunctionValue function)
            {
                return false;
            }

            string[] pureFunctions =
            [
                "join", "letter", "length", "contains", "round", "abs", "floor", "ceil", "ceiling",
                "sqrt", "sin", "cos", "tan", "asin", "acos", "atan", "ln", "log", "log10", "exp", "pow10"
            ];
            bool isPure = pureFunctions.Contains(function.Name, StringComparer.Ordinal) ||
                function.Name.EndsWith(".item", StringComparison.Ordinal) ||
                function.Name.EndsWith(".index", StringComparison.Ordinal) ||
                function.Name.EndsWith(".length", StringComparison.Ordinal) ||
                function.Name.EndsWith(".contains", StringComparison.Ordinal) ||
                function.Name.EndsWith(".contents", StringComparison.Ordinal);
            return isPure && function.Arguments.All(IsStablePowerBase);
        }

        private static CtsValue ExpandPositivePower(CtsValue value, int exponent, SourceSpan span)
        {
            if (exponent == 0)
            {
                return new CtsBinaryValue(
                    "+",
                    new CtsNumberValue(1, "1", span),
                    new CtsNumberValue(0, "0", span),
                    span);
            }

            CtsValue result = value;
            for (int i = 1; i < exponent; i++)
            {
                result = new CtsBinaryValue("*", result, value, span);
            }

            return result;
        }

        private static JsonArray DefaultInputShadow(string? inputName)
        {
            return inputName is "MESSAGE" or "STRING" or "STRING1" or "STRING2" or "QUESTION" or "ITEM" or "WORDS" or "TEXT"
                ? JsonArrayOf(10, string.Empty)
                : JsonArrayOf(4, string.Empty);
        }

        private bool TryGetVariableId(string name, out string? id)
        {
            return _currentTargetData.VariableIds.TryGetValue(name, out id) ||
                _stageTargetData.VariableIds.TryGetValue(name, out id);
        }

        private bool TryGetListId(string name, out string? id)
        {
            return _currentTargetData.ListIds.TryGetValue(name, out id) ||
                _stageTargetData.ListIds.TryGetValue(name, out id);
        }

        private bool TryGetBroadcast(string name, out BroadcastInfo broadcast)
        {
            if (_currentTargetData.BroadcastsByName.TryGetValue(name, out BroadcastInfo? localBroadcast))
            {
                broadcast = localBroadcast;
                return true;
            }

            if (_stageTargetData.BroadcastsByName.TryGetValue(name, out BroadcastInfo? stageBroadcast))
            {
                broadcast = stageBroadcast;
                return true;
            }

            broadcast = null!;
            return false;
        }

        private string CompileBlockValue(CtsBlockValue value, string parentId, ProcedureInfo? procedure)
        {
            string blockId = NewBlockId(value.IsShadow ? "shadow" : "reporter");
            JsonObject inputs = [];
            foreach (CtsRawInput input in value.Inputs)
            {
                inputs[input.Name] = BuildInput(input.Name, input.Value, blockId, procedure);
            }

            JsonObject fields = [];
            foreach (CtsRawField field in value.Fields)
            {
                fields[field.Name] = BuildField(field);
            }

            AddBlock(
                blockId,
                value.Opcode,
                null,
                parentId,
                inputs,
                fields,
                value.IsShadow,
                topLevel: false,
                null,
                null,
                BuildMutation(value.Mutation));
            return blockId;
        }

        private void AddArgumentReporter(string blockId, CtsProcedureParameter parameter, string parentId, bool shadow)
        {
            JsonObject fields = new()
            {
                ["VALUE"] = JsonArrayOf(parameter.Name, null)
            };

            AddBlock(
                blockId,
                parameter.Type == CtsParameterType.Boolean
                    ? "argument_reporter_boolean"
                    : "argument_reporter_string_number",
                null,
                parentId,
                [],
                fields,
                shadow,
                topLevel: false,
                null,
                null,
                null);
        }

        private static JsonArray DefaultShadow(CtsProcedureParameter parameter)
        {
            return parameter.Type == CtsParameterType.Number
                ? JsonArrayOf(4, "0")
                : JsonArrayOf(10, string.Empty);
        }

        private static JsonArray LiteralValue(CtsValue value)
        {
            return value switch
            {
                CtsNumberValue number => JsonArrayOf(4, number.Text),
                CtsStringValue text => JsonArrayOf(10, text.Text),
                CtsIdentifierValue identifier => JsonArrayOf(10, identifier.Name),
                _ => JsonArrayOf(10, string.Empty)
            };
        }

        private static string ValueToScratchText(CtsValue value)
        {
            return value switch
            {
                CtsNumberValue number => number.Text,
                CtsStringValue text => text.Text,
                CtsIdentifierValue identifier => identifier.Name,
                _ => string.Empty
            };
        }

        private static double ValueToDouble(CtsValue value)
        {
            if (value is CtsNumberValue number)
            {
                return number.Number;
            }

            return double.TryParse(ValueToScratchText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 0;
        }

        private static bool ValueToBoolean(CtsValue value)
        {
            string text = ValueToScratchText(value);
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                text == "1";
        }

        private JsonObject CreateProcedureMutation(ProcedureInfo procedure, bool includeNamesAndDefaults, bool warp)
        {
            JsonObject mutation = new()
            {
                ["tagName"] = "mutation",
                ["children"] = new JsonArray(),
                ["proccode"] = procedure.Proccode,
                ["argumentids"] = JsonSerializer.Serialize(procedure.ArgumentIds),
                ["warp"] = warp ? "true" : "false"
            };

            if (includeNamesAndDefaults)
            {
                mutation["argumentnames"] = JsonSerializer.Serialize(procedure.Definition.Parameters.Select(static parameter => parameter.Name).ToArray());
                mutation["argumentdefaults"] = JsonSerializer.Serialize(procedure.Definition.Parameters.Select(GetDefaultText).ToArray());
            }

            return mutation;
        }

        private static string GetDefaultText(CtsProcedureParameter parameter)
        {
            if (parameter.DefaultValue is not null)
            {
                return ValueToScratchText(parameter.DefaultValue);
            }

            return parameter.Type switch
            {
                CtsParameterType.Number => "0",
                CtsParameterType.Boolean => "false",
                _ => string.Empty
            };
        }

        private static bool HasValidProcedureSignature(CtsProcedureDefinition procedure)
        {
            if (procedure.DisplaySignature is null)
            {
                return true;
            }

            string[] actual = Regex.Matches(procedure.DisplaySignature, "%[nsb]")
                .Select(static match => match.Value)
                .ToArray();
            string[] expected = procedure.Parameters
                .Select(static parameter => parameter.Type switch
                {
                    CtsParameterType.Number => "%n",
                    CtsParameterType.Boolean => "%b",
                    _ => "%s"
                })
                .ToArray();
            return actual.SequenceEqual(expected, StringComparer.Ordinal);
        }

        private void AddBlock(
            string id,
            string opcode,
            string? next,
            string? parent,
            JsonObject inputs,
            JsonObject fields,
            bool shadow,
            bool topLevel,
            int? x,
            int? y,
            JsonObject? mutation)
        {
            JsonObject block = new()
            {
                ["opcode"] = opcode,
                ["next"] = next,
                ["parent"] = parent,
                ["inputs"] = inputs,
                ["fields"] = fields,
                ["shadow"] = shadow,
                ["topLevel"] = topLevel
            };

            if (topLevel)
            {
                block["x"] = x ?? 48;
                block["y"] = y ?? 48;
            }

            if (mutation is not null)
            {
                block["mutation"] = mutation;
            }

            _currentBlocks[id] = block;
        }

        private void SetNext(string blockId, string? nextBlockId)
        {
            if (_currentBlocks[blockId] is JsonObject block)
            {
                block["next"] = nextBlockId;
            }
        }

        private static JsonObject CreateStageTarget(string name, JsonObject blocks, TargetBuildData data)
        {
            return new JsonObject
            {
                ["isStage"] = true,
                ["name"] = name,
                ["variables"] = data.Variables,
                ["lists"] = data.Lists,
                ["broadcasts"] = data.Broadcasts,
                ["blocks"] = blocks,
                ["comments"] = new JsonObject(),
                ["currentCostume"] = 0,
                ["costumes"] = data.Costumes,
                ["sounds"] = new JsonArray(),
                ["volume"] = 100,
                ["layerOrder"] = data.LayerOrder ?? 0,
                ["tempo"] = 60,
                ["videoTransparency"] = 50,
                ["videoState"] = "on",
                ["textToSpeechLanguage"] = null
            };
        }

        private static JsonObject CreateSpriteTarget(string name, JsonObject blocks, TargetBuildData data, int layerOrder)
        {
            return new JsonObject
            {
                ["isStage"] = false,
                ["name"] = name,
                ["variables"] = data.Variables,
                ["lists"] = data.Lists,
                ["broadcasts"] = data.Broadcasts,
                ["blocks"] = blocks,
                ["comments"] = new JsonObject(),
                ["currentCostume"] = 0,
                ["costumes"] = data.Costumes,
                ["sounds"] = new JsonArray(),
                ["volume"] = 100,
                ["layerOrder"] = data.LayerOrder ?? layerOrder,
                ["visible"] = data.Visible ?? true,
                ["x"] = data.X ?? 0,
                ["y"] = data.Y ?? 0,
                ["size"] = data.Size ?? 100,
                ["direction"] = data.Direction ?? 90,
                ["draggable"] = false,
                ["rotationStyle"] = data.RotationStyle ?? "all around"
            };
        }

        private static JsonObject DefaultCostume(string name)
        {
            return new JsonObject
            {
                ["assetId"] = DefaultAssetId,
                ["name"] = name,
                ["md5ext"] = DefaultAssetFileName,
                ["dataFormat"] = "svg",
                ["bitmapResolution"] = 1,
                ["rotationCenterX"] = 0,
                ["rotationCenterY"] = 0
            };
        }

        private JsonObject CreateGeneratedCostume(CtsCostumeDeclaration costume)
        {
            string svg = CreateSvg(costume);
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);
            string assetId = Convert.ToHexString(MD5.HashData(svgBytes)).ToLowerInvariant();
            string fileName = assetId + ".svg";
            _assets[fileName] = svgBytes;

            return new JsonObject
            {
                ["assetId"] = assetId,
                ["name"] = costume.Name,
                ["md5ext"] = fileName,
                ["dataFormat"] = "svg",
                ["bitmapResolution"] = 1,
                ["rotationCenterX"] = costume.RotationCenterX,
                ["rotationCenterY"] = costume.RotationCenterY
            };
        }

        private static string CreateSvg(CtsCostumeDeclaration costume)
        {
            StringBuilder builder = new();
            builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"");
            builder.Append(FormatNumber(costume.Width));
            builder.Append("\" height=\"");
            builder.Append(FormatNumber(costume.Height));
            builder.Append("\" viewBox=\"0 0 ");
            builder.Append(FormatNumber(costume.Width));
            builder.Append(' ');
            builder.Append(FormatNumber(costume.Height));
            builder.Append("\">");

            foreach (CtsSvgShape shape in costume.Shapes)
            {
                AppendSvgShape(builder, shape);
            }

            builder.Append("</svg>");
            return builder.ToString();
        }

        private static void AppendSvgShape(StringBuilder builder, CtsSvgShape shape)
        {
            switch (shape.Kind)
            {
                case "line":
                    builder.Append("<line x1=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[0])));
                    builder.Append("\" y1=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[1])));
                    builder.Append("\" x2=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[2])));
                    builder.Append("\" y2=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[3])));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity");
                    builder.Append("/>");
                    break;

                case "rect":
                    builder.Append("<rect x=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[0])));
                    builder.Append("\" y=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[1])));
                    builder.Append("\" width=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[2])));
                    builder.Append("\" height=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[3])));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity");
                    builder.Append("/>");
                    break;

                case "circle":
                    builder.Append("<circle cx=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[0])));
                    builder.Append("\" cy=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[1])));
                    builder.Append("\" r=\"");
                    builder.Append(FormatNumber(GetAttributeNumber(shape.Attributes, "r")));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity");
                    builder.Append("/>");
                    break;

                case "ellipse":
                    builder.Append("<ellipse cx=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[0])));
                    builder.Append("\" cy=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[1])));
                    builder.Append("\" rx=\"");
                    builder.Append(FormatNumber(GetAttributeNumber(shape.Attributes, "rx")));
                    builder.Append("\" ry=\"");
                    builder.Append(FormatNumber(GetAttributeNumber(shape.Attributes, "ry")));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity");
                    builder.Append("/>");
                    break;

                case "path":
                    builder.Append("<path d=\"");
                    builder.Append(EscapeXmlAttribute(ValueToScratchText(shape.Arguments[0])));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity");
                    builder.Append("/>");
                    break;

                case "text":
                    builder.Append("<text x=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[0])));
                    builder.Append("\" y=\"");
                    builder.Append(FormatNumber(ValueToDouble(shape.Arguments[1])));
                    builder.Append('"');
                    AppendSvgAttributes(builder, shape.Attributes, "fill", "stroke", "width", "opacity", "size");
                    builder.Append('>');
                    builder.Append(EscapeXmlText(ValueToScratchText(shape.Arguments[2])));
                    builder.Append("</text>");
                    break;
            }
        }

        private static void AppendSvgAttributes(StringBuilder builder, IReadOnlyDictionary<string, CtsValue> attributes, params string[] allowed)
        {
            foreach (KeyValuePair<string, CtsValue> attribute in attributes)
            {
                if (!allowed.Contains(attribute.Key))
                {
                    continue;
                }

                string svgName = attribute.Key switch
                {
                    "width" => "stroke-width",
                    "size" => "font-size",
                    _ => attribute.Key
                };

                builder.Append(' ');
                builder.Append(svgName);
                builder.Append("=\"");
                builder.Append(EscapeXmlAttribute(ValueToScratchText(attribute.Value)));
                builder.Append('"');
            }
        }

        private static double GetAttributeNumber(IReadOnlyDictionary<string, CtsValue> attributes, string name)
        {
            return attributes.TryGetValue(name, out CtsValue? value) ? ValueToDouble(value) : 0;
        }

        private static string EscapeXmlAttribute(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        private static string EscapeXmlText(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private CtsCompileResult CreateResult(byte[] projectJsonBytes)
        {
            return new CtsCompileResult
            {
                ProjectJsonBytes = projectJsonBytes,
                Assets = _assets,
                Diagnostics = _diagnostics.ToArray()
            };
        }

        private bool HasErrors()
        {
            return _diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        private void AddError(string code, string message, SourceSpan span)
        {
            _diagnostics.Add(new CtsDiagnostic(code, DiagnosticSeverity.Error, message, span));
        }

        private string NewBlockId(string prefix)
        {
            _nextBlockId++;
            return _blockIdAllocator.Allocate($"{prefix}{_nextBlockId}");
        }

        private static string SanitizeIdPart(string value)
        {
            StringBuilder builder = new();
            foreach (char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
            }

            return builder.Length == 0 ? "arg" : builder.ToString();
        }

        private static JsonArray JsonArrayOf(params object?[] values)
        {
            JsonArray array = [];
            foreach (object? value in values)
            {
                array.Add(ToJsonNode(value));
            }

            return array;
        }

        private static JsonNode? ToJsonNode(object? value)
        {
            return value switch
            {
                null => null,
                JsonNode node => node,
                string text => JsonValue.Create(text),
                bool boolean => JsonValue.Create(boolean),
                int integer => JsonValue.Create(integer),
                double number => JsonValue.Create(number),
                float number => JsonValue.Create(number),
                decimal number => JsonValue.Create(number),
                _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
            };
        }
    }

    private sealed class TargetBuildData
    {
        public static TargetBuildData Empty { get; } = new();

        public JsonObject Variables { get; } = [];

        public JsonObject Lists { get; } = [];

        public JsonObject Broadcasts { get; } = [];

        public JsonArray Costumes { get; } = [];

        public Dictionary<string, string> VariableIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ListIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, BroadcastInfo> BroadcastsByName { get; } = new(StringComparer.Ordinal);

        public double? X { get; set; }

        public double? Y { get; set; }

        public double? Direction { get; set; }

        public double? Size { get; set; }

        public bool? Visible { get; set; }

        public int? LayerOrder { get; set; }

        public string? RotationStyle { get; set; }
    }

    private sealed record BroadcastInfo(string Id, string Message);

    private sealed class ProcedureInfo
    {
        public ProcedureInfo(CtsProcedureDefinition definition, string[] argumentIds)
        {
            Definition = definition;
            ArgumentIds = argumentIds;
            Proccode = CreateProccode(definition);
        }

        public CtsProcedureDefinition Definition { get; }

        public string[] ArgumentIds { get; }

        public string Proccode { get; }

        public bool TryGetParameter(string name, out CtsProcedureParameter? parameter)
        {
            parameter = Definition.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            return parameter is not null;
        }

        private static string CreateProccode(CtsProcedureDefinition definition)
        {
            if (definition.DisplaySignature is not null)
            {
                return definition.DisplaySignature;
            }

            if (definition.Parameters.Count == 0)
            {
                return definition.Name;
            }

            string[] parts = new string[definition.Parameters.Count + 1];
            parts[0] = definition.Name;
            for (int i = 0; i < definition.Parameters.Count; i++)
            {
                parts[i + 1] = definition.Parameters[i].Type switch
                {
                    CtsParameterType.Number => "%n",
                    CtsParameterType.Boolean => "%b",
                    _ => "%s"
                };
            }

            return string.Join(' ', parts);
        }
    }
}
