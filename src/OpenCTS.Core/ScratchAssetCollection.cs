using System.Collections;
using System.IO.Compression;

namespace OpenCTS.Core;

/// <summary>Copyable asset index. Imported media stays compressed until requested.</summary>
public sealed class ScratchAssetCollection : IReadOnlyDictionary<string, byte[]>
{
    private readonly Dictionary<string, Asset> _assets = new(StringComparer.Ordinal);
    public ScratchAssetCollection(IReadOnlyDictionary<string, byte[]> assets)
    {
        if (assets is ScratchAssetCollection indexed)
            foreach (var entry in indexed._assets) _assets.Add(entry.Key, entry.Value);
        else
            foreach (var entry in assets) this[entry.Key] = entry.Value;
    }
    internal ScratchAssetCollection(ArchiveBacking backing)
    {
        foreach (var entry in backing.Entries) _assets.Add(entry.Key, new Asset(null, backing, entry.Value));
    }
    public byte[] this[string key]
    {
        get => _assets[key].Read();
        set => _assets[key] = new Asset(value, null, null);
    }
    public IEnumerable<string> Keys => _assets.Keys;
    public IEnumerable<byte[]> Values => _assets.Values.Select(asset => asset.Read());
    public int Count => _assets.Count;
    public bool ContainsKey(string key) => _assets.ContainsKey(key);
    public bool Remove(string key) => _assets.Remove(key);
    public bool TryGetValue(string key, out byte[] value)
    {
        if (_assets.TryGetValue(key, out Asset? asset)) { value = asset.Read(); return true; }
        value = null!; return false;
    }
    public IEnumerator<KeyValuePair<string, byte[]>> GetEnumerator() =>
        _assets.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value.Read())).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    internal long Length(string key) => _assets[key].Length;
    internal void CopyTo(string key, Stream output) => _assets[key].CopyTo(output);

    private sealed record Asset(byte[]? Bytes, ArchiveBacking? Backing, ZipArchiveEntry? Entry)
    {
        public long Length => Bytes?.LongLength ?? Entry!.Length;
        public byte[] Read()
        {
            if (Bytes is not null) return Bytes;
            lock (Backing!) return ScratchArchiveResources.ReadBytes(Entry!, ScratchArchiveResources.MaximumBufferedAssetBytes);
        }
        public void CopyTo(Stream output)
        {
            if (Bytes is not null) output.Write(Bytes);
            else lock (Backing!) ScratchArchiveResources.CopyEntry(Entry!, output);
        }
    }
}

internal sealed class ArchiveBacking : IDisposable
{
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    public Dictionary<string, ZipArchiveEntry> Entries { get; }
    public ValidationIssue? Warning { get; }

    public ArchiveBacking(string path, IProgress<ScratchLoadProgress>? progress)
    {
        using FileStream input = File.OpenRead(path);
        bool largeFile = input.Length >= 64L * 1024 * 1024;
        progress?.Report(new ScratchLoadProgress(largeFile
            ? "Large archive: creating a compressed snapshot on disk. Loading may take longer."
            : "Indexing archive...", largeFile));
        // A private compressed snapshot keeps later edits/deletion of the input from changing this session.
        string temporary = Path.Combine(Path.GetTempPath(), $"scratchasm-assets-{Guid.NewGuid():N}.tmp");
        _stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            65536, FileOptions.DeleteOnClose | FileOptions.RandomAccess);
        try
        {
            input.CopyTo(_stream);
            _stream.Position = 0;
            _archive = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: true);
            (Entries, Warning) = ScratchArchiveResources.Index(_archive);
            if (Warning is not null) progress?.Report(new ScratchLoadProgress(Warning.Message, true));
        }
        catch { _stream.Dispose(); throw; }
    }
    public void Dispose() { _archive?.Dispose(); _stream?.Dispose(); GC.SuppressFinalize(this); }
}
