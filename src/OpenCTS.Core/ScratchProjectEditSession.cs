using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenCTS.Core;

public sealed class ScratchProjectEditSession
{
    private readonly ScratchArchiveSnapshot _baseline;
    private readonly ScratchAsmOriginMap _originMap;
    private readonly Lazy<string> _normalizedSource;

    private ScratchProjectEditSession(
        ScratchArchiveSnapshot baseline,
        string sourceText,
        IReadOnlyList<ValidationIssue> issues,
        ScratchAsmOriginMap originMap)
    {
        _baseline = baseline;
        SourceText = sourceText;
        Issues = issues;
        _originMap = originMap;
        _normalizedSource = new Lazy<string>(() => WithoutProjectReference(sourceText));
    }

    public string InputPath => _baseline.SourcePath;

    public string SourceText { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool CanEdit => !Issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error);

    public ScratchProjectDocument Materialize(string source)
    {
        if (!CanEdit) throw new InvalidDataException("Repair the project before editing assets.");
        if (SourceUnchanged(source)) return new ScratchProjectDocument(_baseline.Project, _baseline.Entries);
        CtsCompileResult compiled = CtsCompiler.Compile(source, "editor.sasm");
        List<ValidationIssue> issues = compiled.Diagnostics.Select(ToIssue).ToList();
        if (issues.Any(issue => issue.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
        ScratchMergeOutput? merged = ScratchProjectMerger.Merge(_baseline, compiled, source, _originMap, issues);
        if (merged is null || issues.Any(issue => issue.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
        return new ScratchProjectDocument(merged.Project, merged.Entries);
    }

    internal static ScratchProjectEditSession FromDocument(ScratchProjectDocument document)
    {
        ScratchAssetCollection entries = new(document.Assets) { ["project.json"] = document.JsonBytes };
        var validation = Validate(entries["project.json"], entries);
        if (validation.Any(issue => issue.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Where(issue => issue.Severity == DiagnosticSeverity.Error).Select(issue => issue.Message)));
        ScratchArchiveSnapshot snapshot = new(Path.Combine(Path.GetTempPath(), $"scratchasm-{Guid.NewGuid():N}.sb3"), document.Project.DeepClone().AsObject(), entries);
        var decompilation = ScratchProjectDecompiler.Decompile(snapshot.Project);
        return new ScratchProjectEditSession(snapshot, ScratchProvenance.StampSource(decompilation.SourceText, "generated"), decompilation.Issues, decompilation.OriginMap);
    }

    public static ScratchProjectEditSession Open(string inputPath, IProgress<ScratchLoadProgress>? progress = null)
    {
        ScratchArchiveSnapshot snapshot = ScratchPackageSnapshotReader.Read(inputPath, progress);
        List<ValidationIssue> validation = Validate(snapshot.Entries["project.json"], snapshot.Entries).ToList();
        if (snapshot.PerformanceWarning is not null) validation.Add(snapshot.PerformanceWarning);
        if (validation.Any(issue => issue.Severity == DiagnosticSeverity.Error))
            return new ScratchProjectEditSession(snapshot, "", validation, new ScratchAsmOriginMap());
        ScratchProjectDecompilation decompilation = ScratchProjectDecompiler.Decompile(snapshot.Project);
        return new ScratchProjectEditSession(snapshot, ScratchProvenance.StampSource(decompilation.SourceText, "imported"), validation.Concat(decompilation.Issues).ToArray(), decompilation.OriginMap);
    }

    public ConversionResult WriteEdited(string sourceText, string outputPath, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        List<ValidationIssue> issues = [.. Issues];
        if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
        {
            return Failure(issues);
        }

        try
        {
            if (string.Equals(Path.GetFullPath(outputPath), InputPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output must be different from the imported .sb3 or project companion.");

            if (SourceUnchanged(sourceText))
            {
                ScratchProjectMerger.WriteArchive(new ScratchMergeOutput(_baseline.Project, _baseline.Entries,
                    _baseline.Entries["project.json"], ["imported", "exported"]), outputPath, overwrite);
                return new ConversionResult { Success = true, OutputPath = Path.GetFullPath(outputPath), Issues = issues };
            }
            CtsCompileResult compiled = CtsCompiler.Compile(sourceText, "edited.sasm");
            issues.AddRange(compiled.Diagnostics.Select(ToIssue));
            if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error)) return Failure(issues);
            ScratchMergeOutput? merged = ScratchProjectMerger.Merge(_baseline, compiled, sourceText, _originMap, issues);
            if (merged is null || issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
            {
                return Failure(issues);
            }

            issues.AddRange(Validate(JsonSerializer.SerializeToUtf8Bytes(merged.Project), merged.Entries));
            if (issues.Any(issue => issue.Severity == DiagnosticSeverity.Error)) return Failure(issues);

            ScratchProjectMerger.WriteArchive(merged with { Operations = ["imported", "edited", "exported"] }, outputPath, overwrite);
            return new ConversionResult
            {
                Success = true,
                OutputPath = Path.GetFullPath(outputPath),
                Issues = issues
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException)
        {
            issues.Add(new ValidationIssue(ex.Message, "$", null));
            return Failure(issues);
        }
    }

    public ConversionResult SaveSource(string sourceText, string outputPath, bool overwrite = false)
    {
        try
        {
            if (!CanEdit) return Failure(Issues);
            string fullPath = Path.GetFullPath(outputPath);
            if (!fullPath.EndsWith(".sasm", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source output path must end with .sasm.");
            if (File.Exists(fullPath) && !overwrite) throw new IOException($"Output file already exists: {fullPath}");
            string directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            string fingerprint = Fingerprint(_baseline.Entries);
            string companionName = $"{Path.GetFileNameWithoutExtension(fullPath)}.{fingerprint[..16]}.assets.sb3";
            string companionPath = Path.Combine(directory, companionName);
            if (!File.Exists(companionPath))
                ScratchProjectMerger.WriteArchive(new ScratchMergeOutput(_baseline.Project, _baseline.Entries,
                    _baseline.Entries["project.json"], ["imported", "exported"]), companionPath, false);
            else
            {
                ScratchArchiveSnapshot existing = ScratchPackageSnapshotReader.Read(companionPath);
                if (existing.Entries.Count != _baseline.Entries.Count || Fingerprint(existing.Entries) != fingerprint)
                    throw new IOException($"Project companion exists with different content: {companionPath}");
            }
            string exported = $"project {JsonSerializer.Serialize(companionName)}\n\n{WithoutProjectReference(sourceText)}\n";
            WriteSourceFile(fullPath, ScratchProvenance.StampSource(exported,
                SourceUnchanged(sourceText) ? ["imported", "source-saved"] : ["imported", "edited", "source-saved"]), overwrite);
            return new ConversionResult { Success = true, OutputPath = fullPath, Issues = Issues };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException)
        {
            return Failure([new ValidationIssue(ex.Message, "$", null)]);
        }
    }

    public static ScratchProjectEditSession? OpenSourceCompanion(string sourceText, string sourcePath)
    {
        CtsProjectReference? reference = CtsParser.Parse(sourceText).CompilationUnit.FileDeclarations.OfType<CtsProjectReference>().FirstOrDefault();
        if (reference is null) return null;
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, reference.FileName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Project companion cannot be a symbolic link or reparse point.");
        return Open(path);
    }

    public static void WriteSourceFile(string outputPath, string sourceText, bool overwrite = false)
    {
        string path = Path.GetFullPath(outputPath);
        if (ScratchAsmLanguage.IsSupportedSourceName(path)) sourceText = ScratchProvenance.StampSource(sourceText, "source-saved");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, sourceText, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string WithoutProjectReference(string source)
    {
        source = ScratchProvenance.WithoutMarker(source);
        HashSet<int> lines = CtsParser.Parse(source).CompilationUnit.FileDeclarations.OfType<CtsProjectReference>()
            .Select(reference => reference.Span.Start.Line).ToHashSet();
        return string.Join('\n', source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n').Where((_, index) => !lines.Contains(index + 1))).Trim();
    }

    private bool SourceUnchanged(string source) => source == SourceText || WithoutProjectReference(source) == _normalizedSource.Value;

    private static string Fingerprint(IReadOnlyDictionary<string, byte[]> entries)
    {
        using SHA256 hash = SHA256.Create();
        using CryptoStream sink = new(Stream.Null, hash, CryptoStreamMode.Write);
        foreach (string name in entries.Keys.Order(StringComparer.Ordinal))
        {
            long length = entries is ScratchAssetCollection indexed ? indexed.Length(name) : entries[name].LongLength;
            sink.Write(Encoding.UTF8.GetBytes(name + "\0" + length + "\0"));
            if (entries is ScratchAssetCollection assets) assets.CopyTo(name, sink);
            else sink.Write(entries[name]);
        }
        sink.FlushFinalBlock();
        return Convert.ToHexStringLower(hash.Hash!);
    }

    private static IReadOnlyList<ValidationIssue> Validate(byte[] json, IReadOnlyDictionary<string, byte[]> entries)
    {
        using ScratchInputPackage package = ScratchInputPackage.FromGenerated(json, entries, null);
        using JsonDocument document = JsonDocument.Parse(json);
        List<ValidationIssue> issues = [];
        ScratchProjectValidator.Validate(document.RootElement, JsonSourceMap.Create(json), package, issues);
        return issues;
    }

    private static ValidationIssue ToIssue(CtsDiagnostic diagnostic) => new(
        diagnostic.Message,
        "$",
        diagnostic.Span.Start,
        diagnostic.Severity,
        diagnostic.Code,
        diagnostic.Span);

    private static ConversionResult Failure(IReadOnlyList<ValidationIssue> issues) => new()
    {
        Success = false,
        Issues = issues
    };
}
