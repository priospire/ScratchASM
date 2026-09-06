using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace OpenCTS.Core;

internal static class ScratchPackageSnapshotReader
{
    private const int MaximumEntries = 4096;
    private const long MaximumEntryBytes = 128L * 1024 * 1024;
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;

    public static ScratchArchiveSnapshot Read(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Input .sb3 was not found: {fullPath}", fullPath);
        }

        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        long totalBytes = 0;
        using (ZipArchive archive = ZipFile.OpenRead(fullPath))
        {
            if (archive.Entries.Count > MaximumEntries)
            {
                throw new InvalidDataException($"Input .sb3 contains more than {MaximumEntries} ZIP entries.");
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!IsSafeRootEntry(entry))
                {
                    throw new InvalidDataException($"Input .sb3 contains an unsafe ZIP entry path: {entry.FullName}");
                }

                if (entry.Length > MaximumEntryBytes)
                {
                    throw new InvalidDataException($"ZIP entry is too large: {entry.FullName}");
                }

                totalBytes += entry.Length;
                if (totalBytes > MaximumArchiveBytes)
                {
                    throw new InvalidDataException("Input .sb3 expands beyond the supported size limit.");
                }

                if (!entries.TryAdd(entry.FullName, ReadEntry(entry)))
                {
                    throw new InvalidDataException($"Input .sb3 contains a duplicate ZIP entry: {entry.FullName}");
                }
            }
        }

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

        return new ScratchArchiveSnapshot(fullPath, project, entries);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool IsSafeRootEntry(ZipArchiveEntry entry)
    {
        return ScratchArchivePath.IsSafe(entry.FullName);
    }
}
