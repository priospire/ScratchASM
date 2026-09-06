using System.IO.Compression;
using System.Text.Json;

namespace OpenCTS.Core;

public sealed class ScratchProjectConverter
{
    public ConversionResult ConvertToScratchAsm(string inputPath, string outputPath, bool overwrite = false)
    {
        try
        {
            ScratchProjectEditSession session = ScratchProjectEditSession.Open(inputPath);
            return session.SaveSource(session.SourceText, outputPath, overwrite);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException)
        {
            return Failure([new ValidationIssue($"Could not import project: {ex.Message}", "$", null)]);
        }
    }

    public ConversionResult ConvertToSb3(string inputPath, string outputPath, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();

        List<ValidationIssue> issues = [];
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            issues.Add(new ValidationIssue("Input path is required.", "$", null));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            issues.Add(new ValidationIssue("Output .sb3 path is required.", "$", null));
        }

        if (issues.Count > 0)
        {
            return Failure(issues);
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure([new ValidationIssue($"Output path is invalid: {ex.Message}", "$", null)]);
        }

        if (!string.Equals(Path.GetExtension(fullOutputPath), ".sb3", StringComparison.OrdinalIgnoreCase))
        {
            return Failure([new ValidationIssue("Output path must end with .sb3.", "$", null)]);
        }

        try
        {
            if (IsScratchAsmInput(inputPath))
            {
                return ConvertScratchAsmToSb3(inputPath, fullOutputPath, options);
            }

            using ScratchInputPackage package = ScratchInputPackage.Open(inputPath, options.AttemptSafeRepair);
            if (package.SourceFilePath is not null &&
                string.Equals(Path.GetFullPath(package.SourceFilePath), fullOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                return Failure([new ValidationIssue("Output path must be different from the input .sb3 file.", "$", null)]);
            }

            if (options.AttemptSafeRepair && TryRemoveUtf8Bom(package.ProjectJsonBytes, out byte[] normalizedJson))
            {
                package.ApplyRepair(normalizedJson, new Dictionary<string, byte[]>());
                issues.Add(new ValidationIssue(
                    "Removed UTF-8 byte order mark from project.json.",
                    "$",
                    new SourceLocation(1, 1),
                    DiagnosticSeverity.Warning,
                    "REPAIR100"));
            }

            JsonSourceMap sourceMap;
            using JsonDocument document = ParseProjectJson(package.ProjectJsonBytes, out sourceMap, out ValidationIssue? syntaxIssue);
            if (syntaxIssue is not null)
            {
                if (options.AttemptSafeRepair)
                {
                    return Failure([new ValidationIssue(
                        "Safe repair is impossible because project.json contains malformed JSON.",
                        "$",
                        syntaxIssue.Location,
                        DiagnosticSeverity.Error,
                        "REPAIR001")]);
                }

                return Failure([syntaxIssue]);
            }

            IReadOnlyList<ScratchAssetReference> assetReferences;
            if (options.AttemptSafeRepair)
            {
                ScratchRepairResult repair = ScratchProjectRepairer.Repair(package);
                package.ApplyRepair(repair.ProjectJsonBytes, repair.GeneratedAssets);
                issues.AddRange(repair.Issues);

                using JsonDocument repairedDocument = ParseProjectJson(
                    package.ProjectJsonBytes,
                    out JsonSourceMap repairedSourceMap,
                    out ValidationIssue? repairedSyntaxIssue);
                if (repairedSyntaxIssue is not null)
                {
                    return Failure([.. issues, repairedSyntaxIssue]);
                }

                assetReferences = ScratchProjectValidator.Validate(
                    repairedDocument.RootElement,
                    repairedSourceMap,
                    package,
                    issues);
            }
            else
            {
                assetReferences = ScratchProjectValidator.Validate(
                    document.RootElement,
                    sourceMap,
                    package,
                    issues);
            }

            if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
            {
                return Failure(issues);
            }

            WriteSb3(package, assetReferences, fullOutputPath, options.Overwrite);
            return new ConversionResult
            {
                Success = true,
                OutputPath = fullOutputPath,
                Issues = issues
            };
        }
        catch (JsonException ex)
        {
            if (options.AttemptSafeRepair)
            {
                return Failure([new ValidationIssue(
                    "Safe repair is impossible because project.json contains malformed JSON.",
                    "$",
                    CreateSyntaxIssue(ex).Location,
                    DiagnosticSeverity.Error,
                    "REPAIR001")]);
            }

            return Failure([CreateSyntaxIssue(ex)]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return Failure([new ValidationIssue(ex.Message, "$", null)]);
        }
    }

    private static ConversionResult ConvertScratchAsmToSb3(string inputPath, string fullOutputPath, ConversionOptions options)
    {
        string fullInputPath = Path.GetFullPath(inputPath);
        string? sourceOverride = options.ScratchAsmSourceText ?? options.MonocodeSourceText;
        if (!File.Exists(fullInputPath) && sourceOverride is null)
        {
            return Failure([new ValidationIssue($"Input path was not found: {fullInputPath}", "$", null)]);
        }

        string sourceText = sourceOverride ?? File.ReadAllText(fullInputPath);
        List<ValidationIssue> issues = [];
        if (options.AttemptSafeRepair)
        {
            ScratchAsmSourceRepairResult repair = ScratchAsmSourceRepairer.Repair(sourceText);
            sourceText = repair.SourceText;
            issues.AddRange(repair.Issues);
        }

        CtsCompileResult compileResult = CtsCompiler.Compile(sourceText, fullInputPath);
        issues.AddRange(compileResult.Diagnostics.Select(ToValidationIssue));
        if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
        {
            return Failure(issues);
        }

        ScratchProjectEditSession? companion = ScratchProjectEditSession.OpenSourceCompanion(sourceText, fullInputPath);
        if (companion is not null)
        {
            ConversionResult merged = companion.WriteEdited(sourceText, fullOutputPath, options.Overwrite);
            return new ConversionResult { Success = merged.Success, OutputPath = merged.OutputPath, Issues = [.. issues, .. merged.Issues] };
        }

        using ScratchInputPackage package = ScratchInputPackage.FromGenerated(
            compileResult.ProjectJsonBytes,
            compileResult.Assets,
            fullInputPath);

        JsonSourceMap sourceMap;
        using JsonDocument document = ParseProjectJson(package.ProjectJsonBytes, out sourceMap, out ValidationIssue? syntaxIssue);
        if (syntaxIssue is not null)
        {
            issues.Add(syntaxIssue);
            return Failure(issues);
        }

        IReadOnlyList<ScratchAssetReference> assetReferences = ScratchProjectValidator.Validate(
            document.RootElement,
            sourceMap,
            package,
            issues);

        if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
        {
            return Failure(issues);
        }

        WriteSb3(package, assetReferences, fullOutputPath, options.Overwrite);
        return new ConversionResult
        {
            Success = true,
            OutputPath = fullOutputPath,
            Issues = issues
        };
    }

    private static bool IsScratchAsmInput(string inputPath)
    {
        return ScratchAsmLanguage.IsSupportedSourceName(inputPath);
    }

    private static ValidationIssue ToValidationIssue(CtsDiagnostic diagnostic)
    {
        return new ValidationIssue(
            diagnostic.Message,
            "$",
            diagnostic.Span.Start,
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Span);
    }

    private static JsonDocument ParseProjectJson(byte[] projectJsonBytes, out JsonSourceMap sourceMap, out ValidationIssue? syntaxIssue)
    {
        sourceMap = JsonSourceMap.Empty;
        syntaxIssue = null;

        try
        {
            sourceMap = JsonSourceMap.Create(projectJsonBytes);
            return JsonDocument.Parse(projectJsonBytes);
        }
        catch (JsonException ex)
        {
            syntaxIssue = CreateSyntaxIssue(ex);
            return JsonDocument.Parse("{}");
        }
    }

    private static ValidationIssue CreateSyntaxIssue(JsonException ex)
    {
        SourceLocation? location = null;
        if (ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue)
        {
            location = new SourceLocation(
                checked((int)ex.LineNumber.Value + 1),
                checked((int)ex.BytePositionInLine.Value + 1));
        }

        return new ValidationIssue($"JSON syntax error: {ex.Message}", "$", location);
    }

    private static bool TryRemoveUtf8Bom(byte[] bytes, out byte[] normalized)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            normalized = bytes[3..];
            return true;
        }

        normalized = bytes;
        return false;
    }

    private static void WriteSb3(
        ScratchInputPackage package,
        IReadOnlyList<ScratchAssetReference> assetReferences,
        string outputPath,
        bool overwrite)
    {
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(outputPath) && !overwrite)
        {
            throw new IOException($"Output file already exists: {outputPath}");
        }

        string tempPath = Path.Combine(
            string.IsNullOrEmpty(outputDirectory) ? Directory.GetCurrentDirectory() : outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (ZipArchive output = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry projectEntry = output.CreateEntry("project.json", CompressionLevel.Optimal);
                using (Stream projectStream = projectEntry.Open())
                {
                    projectStream.Write(package.ProjectJsonBytes);
                }

                HashSet<string> writtenAssets = new(StringComparer.Ordinal);
                foreach (ScratchAssetReference assetReference in assetReferences)
                {
                    if (!writtenAssets.Add(assetReference.FileName))
                    {
                        continue;
                    }

                    ZipArchiveEntry assetEntry = output.CreateEntry(assetReference.FileName, CompressionLevel.Optimal);
                    using Stream assetInput = package.OpenAsset(assetReference.FileName);
                    using Stream assetOutput = assetEntry.Open();
                    assetInput.CopyTo(assetOutput);
                }
            }

            File.Move(tempPath, outputPath, overwrite);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static ConversionResult Failure(IReadOnlyList<ValidationIssue> issues)
    {
        return new ConversionResult
        {
            Success = false,
            Issues = issues
        };
    }
}
