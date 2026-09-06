using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed record ScratchAsmProject(
    string RootPath,
    string? ManifestPath,
    string EntryPath,
    string OutputPath,
    string? BaselinePath);

public static class ScratchAsmProjectLoader
{
    public static ScratchAsmProject Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string manifestPath = Directory.Exists(fullPath) ? Path.Combine(root, "scratchasm.json") : fullPath;
        WorkspacePathPolicy policy = new(root);
        if (File.Exists(fullPath) && ScratchAsmLanguage.IsSupportedSourceName(fullPath))
        {
            CtsProjectReference? reference = CtsParser.Parse(File.ReadAllText(fullPath)).CompilationUnit.FileDeclarations.OfType<CtsProjectReference>().FirstOrDefault();
            return new ScratchAsmProject(root, null, fullPath, Path.Combine(root, "build", Path.GetFileNameWithoutExtension(fullPath) + ".sb3"),
                reference is null ? null : policy.Resolve(reference.FileName));
        }

        if (File.Exists(manifestPath))
        {
            ScratchAsmManifest manifest = JsonSerializer.Deserialize<ScratchAsmManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidDataException("scratchasm.json is empty.");
            string entry = policy.Resolve(Require(manifest.Entry, "entry"));
            string output = policy.Resolve(Require(manifest.Output, "output"));
            string? baseline = string.IsNullOrWhiteSpace(manifest.Baseline) ? null : policy.Resolve(manifest.Baseline);
            return new ScratchAsmProject(root, manifestPath, entry, output, baseline);
        }

        string? defaultEntry = new WorkspaceService(root).FindSourceFiles()
            .FirstOrDefault(file => file.EndsWith(".sasm", StringComparison.OrdinalIgnoreCase));
        defaultEntry ??= Path.Combine(root, "main.sasm");
        string defaultOutput = Path.Combine(root, "build", Path.GetFileNameWithoutExtension(defaultEntry) + ".sb3");
        return new ScratchAsmProject(root, null, defaultEntry, defaultOutput, null);
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"scratchasm.json requires '{name}'.") : value;

    private sealed class ScratchAsmManifest
    {
        public string? Entry { get; init; }
        public string? Output { get; init; }
        public string? Baseline { get; init; }
    }
}
