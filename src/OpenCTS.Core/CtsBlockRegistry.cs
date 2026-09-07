namespace OpenCTS.Core;

public enum CtsBlockShape
{
    Stack,
    Reporter,
    Boolean,
    Hat,
    CBlock,
    Cap
}

public enum CtsBindingKind
{
    Input,
    Field,
    Menu
}

public enum CtsTerminalPolicy
{
    LinksNext,
    AlwaysCaps,
    CapsUnlessOtherScripts
}

public sealed record CtsArgumentBinding(
    string Name,
    CtsBindingKind Kind,
    string? MenuOpcode = null,
    string? MenuField = null,
    string? DefaultValue = null);

public sealed record CtsAliasDefinition(
    string Name,
    string Opcode,
    string CategoryColor,
    CtsBlockShape Shape,
    IReadOnlyList<CtsArgumentBinding> Bindings,
    string? ExtensionId = null,
    IReadOnlyList<string>? Substacks = null,
    CtsTerminalPolicy TerminalPolicy = CtsTerminalPolicy.LinksNext,
    bool IsLegacy = false,
    IReadOnlyDictionary<string, string>? FixedFields = null)
{
    public int ArgumentCount => Bindings.Count;

    public IReadOnlyList<string> Inputs => Bindings
        .Where(static binding => binding.Kind is CtsBindingKind.Input or CtsBindingKind.Menu)
        .Select(static binding => binding.Name)
        .ToArray();

    public IReadOnlyList<string> SubstackNames => Substacks ?? [];

    public IReadOnlyDictionary<string, string> ConstantFields => FixedFields ??
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public static class CtsBlockRegistry
{
    private static readonly IReadOnlyList<CtsAliasDefinition> Aliases = CtsBlockCatalog.Create();
    private static readonly IReadOnlyDictionary<string, string> OpcodeColors = Aliases.GroupBy(alias => alias.Opcode)
        .ToDictionary(group => group.Key, group => group.First().CategoryColor, StringComparer.Ordinal);

    public static IReadOnlyList<CtsAliasDefinition> Definitions => Aliases;

    public static bool TryResolve(string name, int argumentCount, out CtsAliasDefinition definition)
    {
        definition = Aliases.FirstOrDefault(alias =>
            string.Equals(alias.Name, name, StringComparison.Ordinal) &&
            alias.ArgumentCount == argumentCount)!;
        return definition is not null;
    }

    public static bool TryResolveExpression(string name, int argumentCount, out CtsAliasDefinition definition)
    {
        definition = Aliases.FirstOrDefault(alias =>
            string.Equals(alias.Name, name, StringComparison.Ordinal) &&
            alias.ArgumentCount == argumentCount &&
            alias.Shape is CtsBlockShape.Reporter or CtsBlockShape.Boolean)!;
        return definition is not null;
    }

    public static bool HasAlias(string name)
    {
        return Aliases.Any(alias => string.Equals(alias.Name, name, StringComparison.Ordinal));
    }

    public static IReadOnlyList<int> GetSupportedArgumentCounts(string name)
    {
        return Aliases
            .Where(alias => string.Equals(alias.Name, name, StringComparison.Ordinal))
            .Select(alias => alias.ArgumentCount)
            .Distinct()
            .Order()
            .ToArray();
    }

    public static string? GetCategoryColor(string category)
    {
        return category switch
        {
            "motion" => ScratchCategoryColors.Motion,
            "looks" => ScratchCategoryColors.Looks,
            "sound" => ScratchCategoryColors.Sound,
            "events" or "event" => ScratchCategoryColors.Events,
            "control" => ScratchCategoryColors.Control,
            "sensing" => ScratchCategoryColors.Sensing,
            "operators" or "operator" => ScratchCategoryColors.Operators,
            "variables" or "variable" or "data" => ScratchCategoryColors.Variables,
            "lists" or "list" => ScratchCategoryColors.Lists,
            "proc" or "call" => ScratchCategoryColors.MyBlocks,
            "extensions" or "extension" or "video" or "legacy" => ScratchCategoryColors.Extensions,
            "pen" or "music" or "videoSensing" or "text2speech" or "translate" or "speech2text" or
            "faceSensing" or "makeymakey" or "microbit" or "ev3" or "wedo2" or "gdxfor" or "boost" =>
                ScratchCategoryColors.GetExtensionColor(category),
            _ => Aliases.FirstOrDefault(alias => string.Equals(alias.Name, category, StringComparison.Ordinal))?.CategoryColor
        };
    }

    public static string GetCategoryName(CtsAliasDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.ExtensionId is not null)
        {
            return "extension." + definition.ExtensionId;
        }

        string opcode = definition.Opcode;
        if (opcode.StartsWith("motion_", StringComparison.Ordinal))
        {
            return "motion";
        }

        if (opcode.StartsWith("looks_", StringComparison.Ordinal))
        {
            return "looks";
        }

        if (opcode.StartsWith("sound_", StringComparison.Ordinal))
        {
            return "sound";
        }

        if (opcode.StartsWith("event_", StringComparison.Ordinal))
        {
            return "events";
        }

        if (opcode.StartsWith("control_", StringComparison.Ordinal))
        {
            return "control";
        }

        if (opcode.StartsWith("sensing_", StringComparison.Ordinal))
        {
            return "sensing";
        }

        if (opcode.StartsWith("operator_", StringComparison.Ordinal))
        {
            return "operators";
        }

        if (opcode.StartsWith("data_", StringComparison.Ordinal))
        {
            return IsListOpcode(opcode) ? "lists" : "variables";
        }

        if (opcode.StartsWith("procedures_", StringComparison.Ordinal) ||
            opcode.StartsWith("argument_", StringComparison.Ordinal))
        {
            return "myBlocks";
        }

        return "extensions";
    }

    public static string? GetOpcodeColor(string opcode)
    {
        if (OpcodeColors.TryGetValue(opcode, out string? color))
        {
            return color;
        }

        return opcode switch
        {
            _ when opcode.StartsWith("motion_", StringComparison.Ordinal) => ScratchCategoryColors.Motion,
            _ when opcode.StartsWith("looks_", StringComparison.Ordinal) => ScratchCategoryColors.Looks,
            _ when opcode.StartsWith("sound_", StringComparison.Ordinal) => ScratchCategoryColors.Sound,
            _ when opcode.StartsWith("event_", StringComparison.Ordinal) => ScratchCategoryColors.Events,
            _ when opcode.StartsWith("control_", StringComparison.Ordinal) => ScratchCategoryColors.Control,
            _ when opcode.StartsWith("sensing_", StringComparison.Ordinal) => ScratchCategoryColors.Sensing,
            _ when opcode.StartsWith("operator_", StringComparison.Ordinal) => ScratchCategoryColors.Operators,
            _ when opcode.StartsWith("data_", StringComparison.Ordinal) => IsListOpcode(opcode)
                ? ScratchCategoryColors.Lists
                : ScratchCategoryColors.Variables,
            _ when opcode.StartsWith("procedures_", StringComparison.Ordinal) ||
                opcode.StartsWith("argument_", StringComparison.Ordinal) => ScratchCategoryColors.MyBlocks,
            _ when CtsBlockCatalog.ExtensionPrefixes.Any(prefix => opcode.StartsWith(prefix, StringComparison.Ordinal)) => ScratchCategoryColors.Extensions,
            _ => null
        };
    }

    private static bool IsListOpcode(string opcode)
    {
        return opcode is
            "data_addtolist" or "data_deleteoflist" or "data_deletealloflist" or
            "data_insertatlist" or "data_replaceitemoflist" or "data_itemoflist" or
            "data_itemnumoflist" or "data_lengthoflist" or "data_listcontainsitem" or
            "data_showlist" or "data_hidelist" or "data_listcontents";
    }
}
