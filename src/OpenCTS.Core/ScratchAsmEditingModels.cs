using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal sealed record ScratchArchiveSnapshot(
    string SourcePath,
    JsonObject Project,
    IReadOnlyDictionary<string, byte[]> Entries)
{
    public ValidationIssue? PerformanceWarning { get; init; }
}

internal sealed record ScratchDataOrigin(string Alias, string Id, string Name);

internal sealed class ScratchTargetOrigin
{
    public required bool IsStage { get; init; }

    public required string Name { get; init; }

    public Dictionary<string, ScratchDataOrigin> Variables { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScratchDataOrigin> Lists { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScratchDataOrigin> Broadcasts { get; } = new(StringComparer.Ordinal);

    public List<string> BlockOrder { get; } = [];
}

internal sealed class ScratchAsmOriginMap
{
    public List<ScratchTargetOrigin> Targets { get; } = [];
}

internal sealed record ScratchProjectDecompilation(
    string SourceText,
    IReadOnlyList<ValidationIssue> Issues,
    ScratchAsmOriginMap OriginMap);
