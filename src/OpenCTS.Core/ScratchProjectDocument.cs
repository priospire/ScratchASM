using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

public sealed class ScratchProjectDocument
{
    public JsonObject Project { get; }
    public Dictionary<string, byte[]> Assets { get; }
    public bool Compact { get; set; }
    public byte[] JsonBytes => JsonSerializer.SerializeToUtf8Bytes(Project, new JsonSerializerOptions { WriteIndented = !Compact });

    public ScratchProjectDocument(JsonObject project, IReadOnlyDictionary<string, byte[]> assets)
    {
        Project = project.DeepClone().AsObject();
        Assets = assets.Where(pair => pair.Key != "project.json").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
    public static ScratchProjectDocument Compile(string source)
    {
        CtsCompileResult result = CtsCompiler.Compile(source);
        CtsDiagnostic[] errors = result.Diagnostics.Where(issue => issue.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => issue.ToString())));
        return new ScratchProjectDocument(JsonNode.Parse(result.ProjectJsonBytes)!.AsObject(), result.Assets);
    }
    public ScratchProjectDocument Copy() => new(Project, Assets) { Compact = Compact };
    public ScratchProjectEditSession CreateSession() => ScratchProjectEditSession.FromDocument(this);
    public void Write(string outputPath, bool overwrite = false)
    {
        byte[] json = JsonBytes;
        using ScratchInputPackage package = ScratchInputPackage.FromGenerated(json, Assets, null);
        using JsonDocument document = JsonDocument.Parse(json);
        List<ValidationIssue> issues = [];
        ScratchProjectValidator.Validate(document.RootElement, JsonSourceMap.Create(json), package, issues);
        if (issues.Any(issue => issue.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, issues.Where(issue => issue.Severity == DiagnosticSeverity.Error).Select(issue => issue.Message)));
        ScratchProjectMerger.WriteArchive(new ScratchMergeOutput(Project, Assets, json), outputPath, overwrite);
    }
    public int AddSprite(string name)
    {
        JsonArray targets = Project["targets"]!.AsArray();
        name = UniqueName(name, targets.OfType<JsonObject>().Select(target => target["name"]!.ToString()));
        ScratchProjectDocument template = Compile("stage {\n}\nsprite Sprite {\n  costume \"costume1\" 80x80 center 40,40 {\n    circle 40,40 r=30 fill=\"#4C97FF\"\n  }\n}\n");
        JsonObject sprite = template.Project["targets"]![1]!.DeepClone().AsObject();
        sprite["name"] = name;
        sprite["layerOrder"] = targets.Count;
        targets.Add(sprite);
        foreach ((string key, byte[] bytes) in template.Assets) AddAsset(key, bytes);
        return targets.Count - 1;
    }
    public int ImportSprite(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 4096 || zip.Entries.Sum(entry => entry.Length) > 128L * 1024 * 1024)
            throw new InvalidDataException("Sprite archive exceeds the supported size limit.");
        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        long totalRead = 0;
        byte[] chunk = new byte[65536];
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!ScratchArchivePath.IsSafe(entry.FullName) || entries.ContainsKey(entry.FullName)) throw new InvalidDataException("Unsafe or duplicate sprite entry.");
            using Stream stream = entry.Open();
            using MemoryStream buffer = new();
            int read;
            while ((read = stream.Read(chunk)) > 0)
            {
                totalRead += read;
                if (totalRead > 128L * 1024 * 1024 || buffer.Length + read > entry.Length)
                    throw new InvalidDataException("Sprite data exceeds its declared or supported size.");
                buffer.Write(chunk, 0, read);
            }
            if (buffer.Length != entry.Length) throw new InvalidDataException("Truncated sprite archive entry.");
            entries.Add(entry.FullName, buffer.ToArray());
        }
        if (!entries.Remove("sprite.json", out byte[]? json) || JsonNode.Parse(json) is not JsonObject sprite || sprite["isStage"]?.ToString() != "false")
            throw new InvalidDataException("Expected a Scratch .sprite3 archive containing a sprite.json object.");
        JsonArray targets = Project["targets"]!.AsArray();
        sprite["name"] = UniqueName(sprite["name"]?.ToString() ?? "Sprite", targets.OfType<JsonObject>().Select(target => target["name"]!.ToString()));
        foreach ((string key, byte[] bytes) in entries) AddAsset(key, bytes);
        targets.Add(sprite);
        return targets.Count - 1;
    }
    public void AddCostume(int target, string name, byte[] bytes, string format, double centerX, double centerY)
    {
        format = format.ToLowerInvariant();
        if (format is not ("svg" or "png" or "jpg" or "jpeg")) throw new InvalidDataException("Choose an SVG, PNG, or JPEG costume.");
        JsonArray costumes = Project["targets"]![target]!["costumes"]!.AsArray();
        string hash = Convert.ToHexStringLower(MD5.HashData(bytes));
        AddAsset(hash + "." + format, bytes);
        costumes.Add(new JsonObject
        {
            ["name"] = UniqueName(name, costumes.OfType<JsonObject>().Select(item => item["name"]!.ToString())),
            ["assetId"] = hash, ["md5ext"] = hash + "." + format, ["dataFormat"] = format,
            ["rotationCenterX"] = centerX, ["rotationCenterY"] = centerY, ["bitmapResolution"] = 1
        });
        Project["targets"]![target]!["currentCostume"] = costumes.Count - 1;
    }
    public void AddSound(int target, string name, byte[] wav, int sampleRate, long sampleCount)
    {
        if (sampleRate <= 0 || sampleCount < 0 || wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Expected a valid decoded WAV sound.");
        JsonArray sounds = Project["targets"]![target]!["sounds"]!.AsArray();
        string hash = Convert.ToHexStringLower(MD5.HashData(wav));
        AddAsset(hash + ".wav", wav);
        sounds.Add(new JsonObject
        {
            ["name"] = UniqueName(name, sounds.OfType<JsonObject>().Select(item => item["name"]!.ToString())),
            ["assetId"] = hash, ["md5ext"] = hash + ".wav", ["dataFormat"] = "wav",
            ["rate"] = sampleRate, ["sampleCount"] = sampleCount
        });
    }
    private void AddAsset(string key, byte[] bytes)
    {
        if (Assets.TryGetValue(key, out byte[]? existing) && !existing.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Conflicting asset content.");
        Assets[key] = bytes;
    }
    private static string UniqueName(string name, IEnumerable<string> existing)
    {
        HashSet<string> names = existing.ToHashSet(StringComparer.Ordinal);
        string candidate = string.IsNullOrWhiteSpace(name) ? "Sprite" : name;
        for (int i = 2; names.Contains(candidate); i++) candidate = name + i;
        return candidate;
    }
}
