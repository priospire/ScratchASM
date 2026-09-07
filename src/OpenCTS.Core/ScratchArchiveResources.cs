using System.IO.Compression;
using System.Buffers;
using System.IO.Hashing;

namespace OpenCTS.Core;

public sealed record ScratchLoadProgress(string Message, bool IsLargeProject);

internal static class ScratchArchiveResources
{
    public const long MaximumJsonBytes = 128L * 1024 * 1024;
    public const long MaximumBufferedAssetBytes = 128L * 1024 * 1024;

    public static (Dictionary<string, ZipArchiveEntry> Entries, ValidationIssue? Warning) Index(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        long expanded = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!ScratchArchivePath.IsSafe(entry.FullName)) throw new InvalidDataException($"Input .sb3 contains an unsafe ZIP entry path: {entry.FullName}");
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException($"Input .sb3 contains a duplicate ZIP entry path: {entry.FullName}");
            if (entry.Length < 0 || entry.Length > long.MaxValue - expanded) throw new InvalidDataException("Invalid ZIP expanded size.");
            expanded += entry.Length;
        }
        long jsonBytes = entries.GetValueOrDefault("project.json")?.Length ?? 0;
        if (jsonBytes > MaximumJsonBytes)
            throw new InvalidDataException("project.json exceeds the 128 MiB in-memory JSON safety limit. Asset count and total archive size are not limited.");
        ValidationIssue? warning = entries.Count > 4096 || expanded >= 256L * 1024 * 1024 || jsonBytes >= 2L * 1024 * 1024
            ? new ValidationIssue($"Large project: {entries.Count:N0} ZIP entries, {expanded / 1048576d:N1} MiB expanded. Loading and checking may take longer; editing may lag. Media is loaded on demand.",
                "$", null, DiagnosticSeverity.Warning, "SASM4005") : null;
        return (entries, warning);
    }

    public static byte[] ReadBytes(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length > limit) throw new InvalidDataException($"Entry is too large for this in-memory operation: {entry.FullName}. Streaming archive export is still supported.");
        byte[] bytes = new byte[checked((int)entry.Length)];
        using Stream stream = entry.Open();
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"ZIP data exceeds its declared size: {entry.FullName}");
        if (Crc32.HashToUInt32(bytes) != entry.Crc32) throw new InvalidDataException($"ZIP checksum mismatch: {entry.FullName}");
        return bytes;
    }

    public static void CopyEntry(ZipArchiveEntry entry, Stream output)
    {
        using Stream input = entry.Open();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long count = 0;
            Crc32 crc = new();
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (read > entry.Length - count) throw new InvalidDataException($"ZIP data exceeds its declared size: {entry.FullName}");
                output.Write(buffer, 0, read);
                crc.Append(buffer.AsSpan(0, read));
                count += read;
            }
            if (count != entry.Length) throw new InvalidDataException($"Truncated ZIP entry: {entry.FullName}");
            if (crc.GetCurrentHashAsUInt32() != entry.Crc32) throw new InvalidDataException($"ZIP checksum mismatch: {entry.FullName}");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static Stream OpenEntry(ZipArchiveEntry entry) => new CheckedEntryStream(entry.Open(), entry.Length, entry.FullName, entry.Crc32);

    private sealed class CheckedEntryStream(Stream inner, long expected, string name, uint expectedCrc) : Stream
    {
        private long _read;
        private readonly Crc32 _crc = new();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0;
            int read = inner.Read(buffer);
            if (read > expected - _read || read == 0 && _read != expected)
                throw new InvalidDataException($"Invalid expanded ZIP length: {name}");
            _read += read;
            _crc.Append(buffer[..read]);
            if (read == 0 && _crc.GetCurrentHashAsUInt32() != expectedCrc) throw new InvalidDataException($"ZIP checksum mismatch: {name}");
            return read;
        }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => expected;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
