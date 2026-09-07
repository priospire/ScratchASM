using System.IO.Compression;

namespace OpenCTS.Core;

internal sealed class ScratchInputPackage : IDisposable
{
    private readonly ZipArchive? _zipArchive;
    private readonly FileStream? _zipStream;
    private readonly Dictionary<string, ZipArchiveEntry>? _zipEntries;
    private readonly string? _assetDirectory;
    private readonly IReadOnlyDictionary<string, byte[]>? _assetBytes;
    private IReadOnlyDictionary<string, byte[]> _generatedAssetBytes = new Dictionary<string, byte[]>();

    private ScratchInputPackage(
        byte[] projectJsonBytes,
        string? sourceFilePath,
        string? assetDirectory,
        FileStream? zipStream,
        ZipArchive? zipArchive,
        Dictionary<string, ZipArchiveEntry>? zipEntries,
        IReadOnlyDictionary<string, byte[]>? assetBytes)
    {
        ProjectJsonBytes = projectJsonBytes;
        SourceFilePath = sourceFilePath;
        _assetDirectory = assetDirectory;
        _zipStream = zipStream;
        _zipArchive = zipArchive;
        _zipEntries = zipEntries;
        _assetBytes = assetBytes;
    }

    public byte[] ProjectJsonBytes { get; private set; }

    public string? SourceFilePath { get; }
    public ValidationIssue? PerformanceWarning { get; private init; }

    public static ScratchInputPackage Open(string inputPath, bool requireSafeArchive = false)
    {
        string fullInputPath = Path.GetFullPath(inputPath);

        if (Directory.Exists(fullInputPath))
        {
            string projectJsonPath = Path.Combine(fullInputPath, "project.json");
            if (!File.Exists(projectJsonPath))
            {
                throw new FileNotFoundException($"Folder does not contain project.json: {fullInputPath}", projectJsonPath);
            }

            return new ScratchInputPackage(
                File.ReadAllBytes(projectJsonPath),
                projectJsonPath,
                fullInputPath,
                null,
                null,
                null,
                null);
        }

        if (!File.Exists(fullInputPath))
        {
            throw new FileNotFoundException($"Input path was not found: {fullInputPath}", fullInputPath);
        }

        if (string.Equals(Path.GetExtension(fullInputPath), ".sb3", StringComparison.OrdinalIgnoreCase))
        {
            return OpenSb3(fullInputPath, requireSafeArchive);
        }

        if (string.Equals(Path.GetFileName(fullInputPath), "project.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(fullInputPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return new ScratchInputPackage(
                File.ReadAllBytes(fullInputPath),
                fullInputPath,
                Path.GetDirectoryName(fullInputPath) ?? Directory.GetCurrentDirectory(),
                null,
                null,
                null,
                null);
        }

        throw new InvalidDataException("Input must be a .sasm/.mono source file, a .sb3 file, a project.json file, or a folder containing project.json.");
    }

    public static ScratchInputPackage FromGenerated(
        byte[] projectJsonBytes,
        IReadOnlyDictionary<string, byte[]> assetBytes,
        string? sourceFilePath)
    {
        return new ScratchInputPackage(
            projectJsonBytes,
            sourceFilePath,
            null,
            null,
            null,
            null,
            assetBytes);
    }

    public bool HasAsset(string fileName)
    {
        if (_generatedAssetBytes.ContainsKey(fileName))
        {
            return true;
        }

        if (_zipEntries is not null)
        {
            return _zipEntries.ContainsKey(fileName);
        }

        if (_assetBytes is not null)
        {
            return _assetBytes.ContainsKey(fileName);
        }

        return _assetDirectory is not null && File.Exists(Path.Combine(_assetDirectory, fileName));
    }

    public Stream OpenAsset(string fileName)
    {
        if (_generatedAssetBytes.TryGetValue(fileName, out byte[]? generatedBytes))
        {
            return new MemoryStream(generatedBytes, writable: false);
        }

        if (_zipEntries is not null)
        {
            if (_zipEntries.TryGetValue(fileName, out ZipArchiveEntry? entry))
            {
                return ScratchArchiveResources.OpenEntry(entry);
            }

            throw new FileNotFoundException($"Asset is missing from input .sb3: {fileName}", fileName);
        }

        if (_assetBytes is not null)
        {
            if (_assetBytes.TryGetValue(fileName, out byte[]? assetBytes))
            {
                return new MemoryStream(assetBytes, writable: false);
            }

            throw new FileNotFoundException($"Generated asset is missing: {fileName}", fileName);
        }

        if (_assetDirectory is null)
        {
            throw new FileNotFoundException($"Asset is unavailable: {fileName}", fileName);
        }

        string assetPath = Path.Combine(_assetDirectory, fileName);
        return File.OpenRead(assetPath);
    }

    public void ApplyRepair(byte[] projectJsonBytes, IReadOnlyDictionary<string, byte[]> generatedAssetBytes)
    {
        ProjectJsonBytes = projectJsonBytes;
        _generatedAssetBytes = generatedAssetBytes;
    }

    public void Dispose()
    {
        _zipArchive?.Dispose();
        _zipStream?.Dispose();
    }

    private static ScratchInputPackage OpenSb3(string fullInputPath, bool requireSafeArchive)
    {
        FileStream? zipStream = null;
        try
        {
            zipStream = File.OpenRead(fullInputPath);
            ZipArchive zipArchive = new(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            Dictionary<string, ZipArchiveEntry> entries;
            ValidationIssue? warning;
            try { (entries, warning) = ScratchArchiveResources.Index(zipArchive); }
            catch (InvalidDataException ex) { throw new ScratchPackageException(ex.Message, ex); }

            ZipArchiveEntry? projectJsonEntry = entries.GetValueOrDefault("project.json");
            if (projectJsonEntry is null)
            {
                throw requireSafeArchive
                    ? new ScratchPackageException("Safe repair is impossible because the ZIP does not contain project.json at the root.")
                    : new InvalidDataException("Input .sb3 does not contain project.json at the zip root.");
            }

            byte[] projectJsonBytes = ScratchArchiveResources.ReadBytes(projectJsonEntry, ScratchArchiveResources.MaximumJsonBytes);

            return new ScratchInputPackage(
                projectJsonBytes,
                fullInputPath,
                null,
                zipStream,
                zipArchive,
                entries,
                null) { PerformanceWarning = warning };
        }
        catch (ScratchPackageException)
        {
            zipStream?.Dispose();
            throw;
        }
        catch (Exception ex) when (requireSafeArchive &&
            ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            zipStream?.Dispose();
            throw new ScratchPackageException(
                "Safe repair is impossible because the input is not a readable ZIP archive.",
                ex);
        }
        catch
        {
            zipStream?.Dispose();
            throw;
        }
    }

}

internal sealed class ScratchPackageException : IOException
{
    public ScratchPackageException(string message)
        : base(message)
    {
    }

    public ScratchPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
