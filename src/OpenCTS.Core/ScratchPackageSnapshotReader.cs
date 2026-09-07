using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace OpenCTS.Core;

internal static class ScratchPackageSnapshotReader
{
    public static ScratchArchiveSnapshot Read(string inputPath, IProgress<ScratchLoadProgress>? progress = null)
    {
        string fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Input .sb3 was not found: {fullPath}", fullPath);
        }

        ArchiveBacking backing = new(fullPath, progress);
        try
        {
            ScratchAssetCollection entries = new(backing);

            if (!entries.TryGetValue("project.json", out byte[]? projectBytes))
            {
                throw new InvalidDataException("Input .sb3 does not contain project.json at the archive root.");
            }

            JsonNode? parsed;
            try { parsed = JsonNode.Parse(projectBytes); }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Improper syntax in project.json at line {ex.LineNumber + 1}, column {ex.BytePositionInLine + 1}: {ex.Message}", ex);
            }
            if (parsed is not JsonObject project)
            {
                throw new InvalidDataException("project.json must contain a JSON object.");
            }

            entries["project.json"] = projectBytes;
            return new ScratchArchiveSnapshot(fullPath, project, entries) { PerformanceWarning = backing.Warning };
        }
        catch { backing.Dispose(); throw; }
    }
}
