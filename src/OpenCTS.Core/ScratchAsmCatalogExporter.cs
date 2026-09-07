using System.Text;
using System.Text.Json;

namespace OpenCTS.Core;

public static class ScratchAsmCatalogExporter
{
    public const string ScratchAsmFileName = "all-aliases.sasm";
    public const string JsonFileName = "all-aliases.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GenerateJson()
    {
        object[] aliases = OrderedDefinitions()
            .Select(definition => new
            {
                alias = definition.Name,
                opcode = definition.Opcode,
                category = CtsBlockRegistry.GetCategoryName(definition),
                color = definition.CategoryColor,
                shape = ToCamelCase(definition.Shape),
                bindings = definition.Bindings.Select(binding => new
                {
                    name = binding.Name,
                    kind = ToCamelCase(binding.Kind),
                    menuOpcode = binding.MenuOpcode,
                    menuField = binding.MenuField,
                    defaultValue = binding.DefaultValue,
                    sampleValue = GetSampleValue(binding)
                }).ToArray(),
                extension = definition.ExtensionId,
                substacks = definition.SubstackNames.ToArray(),
                terminal = ToCamelCase(definition.TerminalPolicy),
                legacy = definition.IsLegacy,
                fixedFields = definition.ConstantFields
                    .OrderBy(field => field.Key, StringComparer.Ordinal)
                    .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
                sampleTarget = IsStageOnly(definition) ? "stage" : "sprite"
            })
            .ToArray();

        var document = new
        {
            schemaVersion = 1,
            categoryPalette = OrderedPalette(ScratchCategoryColors.CategoryPalette),
            extensionPalette = OrderedPalette(ScratchCategoryColors.ExtensionPalette),
            syntaxPalette = OrderedPalette(ScratchCategoryColors.SyntaxPalette),
            aliases
        };

        return NormalizeNewlines(JsonSerializer.Serialize(document, JsonOptions)) + "\n";
    }

    public static string GenerateScratchAsm()
    {
        StringBuilder source = new();
        source.AppendLine("# Generated from CtsBlockRegistry. Regenerate with --emit-aliases.");
        source.AppendLine("# Every catalog signature has one '# alias name/argument-count' marker.");
        source.AppendLine();
        source.AppendLine("stage {");
        source.AppendLine("  var output = 0");
        source.AppendLine("  var score = 0");
        source.AppendLine("  list items = []");
        source.AppendLine("  broadcast start = \"start\"");

        foreach (CtsAliasDefinition definition in OrderedDefinitions().Where(IsStageOnly))
        {
            source.AppendLine();
            AppendHat(source, definition, 2);
        }

        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("sprite \"Alias Sprite\" {");
        source.AppendLine("  proc consume(value:num):");
        source.AppendLine("    control.stop \"this script\"");
        source.AppendLine();
        source.AppendLine("  proc consume_boolean(value:bool):");
        source.AppendLine("    control.stop \"this script\"");
        source.AppendLine();
        source.AppendLine("  proc demonstrate_custom_block(value:num, label:str, enabled:bool=false) as \"demonstrate %n %s %b\":");
        source.AppendLine("    control.stop \"this script\"");
        source.AppendLine();
        source.AppendLine("  proc demonstrate_custom_block_call():");
        source.AppendLine("    call demonstrate_custom_block(1, \"sample\", 1)");

        int sampleIndex = 0;
        foreach (CtsAliasDefinition definition in OrderedDefinitions().Where(definition => !IsStageOnly(definition)))
        {
            source.AppendLine();
            if (definition.Shape == CtsBlockShape.Hat)
            {
                AppendHat(source, definition, 2);
                continue;
            }

            source.AppendLine($"  proc alias_sample_{sampleIndex++:D3}():");
            AppendAliasMarker(source, definition, 4);
            AppendStatement(source, definition, 4);
        }

        source.AppendLine("}");
        return NormalizeNewlines(source.ToString());
    }

    public static IReadOnlyList<string> WriteArtifacts(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);

        string scratchAsmPath = Path.Combine(fullDirectory, ScratchAsmFileName);
        string jsonPath = Path.Combine(fullDirectory, JsonFileName);
        ScratchProjectEditSession.WriteSourceFile(scratchAsmPath, ScratchProvenance.StampSource(GenerateScratchAsm(), "generated"), true);
        File.WriteAllText(jsonPath, GenerateJson(), new UTF8Encoding(false));
        return [scratchAsmPath, jsonPath];
    }

    private static IEnumerable<CtsAliasDefinition> OrderedDefinitions()
    {
        return CtsBlockRegistry.Definitions
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .ThenBy(definition => definition.ArgumentCount)
            .ThenBy(definition => definition.Opcode, StringComparer.Ordinal);
    }

    private static void AppendHat(StringBuilder source, CtsAliasDefinition definition, int indent)
    {
        AppendAliasMarker(source, definition, indent);
        source.Append(' ', indent)
            .Append('@')
            .Append(definition.Name)
            .Append(FormatArguments(definition))
            .AppendLine(":");
        source.Append(' ', indent + 2).AppendLine("control.stop \"this script\"");
    }

    private static void AppendAliasMarker(StringBuilder source, CtsAliasDefinition definition, int indent)
    {
        source.Append(' ', indent)
            .Append("# alias ")
            .Append(definition.Name)
            .Append('/')
            .Append(definition.ArgumentCount)
            .AppendLine();
    }

    private static void AppendStatement(StringBuilder source, CtsAliasDefinition definition, int indent)
    {
        string arguments = FormatArguments(definition);
        string functionArguments = string.Join(", ", definition.Bindings.Select(GetSampleValue));
        string padding = new(' ', indent);
        switch (definition.Shape)
        {
            case CtsBlockShape.Reporter:
            case CtsBlockShape.Boolean:
                source.Append(padding).Append("output = ").Append(definition.Name).Append('(')
                    .Append(functionArguments).AppendLine(")");
                break;
            case CtsBlockShape.CBlock when definition.Name == "ifelse":
                source.Append(padding).Append("if").Append(arguments).AppendLine(":");
                source.Append(' ', indent + 2).AppendLine("call consume(1)");
                source.Append(padding).AppendLine("else:");
                source.Append(' ', indent + 2).AppendLine("call consume(1)");
                break;
            case CtsBlockShape.CBlock:
                source.Append(padding).Append(definition.Name).Append(arguments).AppendLine(":");
                source.Append(' ', indent + 2).AppendLine("call consume(1)");
                break;
            default:
                source.Append(padding).Append(definition.Name).Append(arguments).AppendLine();
                break;
        }
    }

    private static string FormatArguments(CtsAliasDefinition definition)
    {
        return definition.Bindings.Count == 0
            ? string.Empty
            : " " + string.Join(' ', definition.Bindings.Select(GetSampleValue));
    }

    private static string GetSampleValue(CtsArgumentBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.DefaultValue))
        {
            return binding.DefaultValue;
        }

        return binding.Name switch
        {
            "VARIABLE" => "score",
            "LIST" => "items",
            "BROADCAST_OPTION" or "BROADCAST_INPUT" => "start",
            "STOP_OPTION" => "\"this script\"",
            _ => "1"
        };
    }

    private static bool IsStageOnly(CtsAliasDefinition definition)
    {
        return definition.Opcode == "event_whenstageclicked";
    }

    private static string ToCamelCase<T>(T value) where T : struct, Enum
    {
        return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    }

    private static SortedDictionary<string, string> OrderedPalette(IReadOnlyDictionary<string, string> palette)
    {
        SortedDictionary<string, string> ordered = new(StringComparer.Ordinal);
        foreach ((string key, string value) in palette)
        {
            ordered.Add(key, value);
        }

        return ordered;
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
