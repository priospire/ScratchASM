using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenCTS.Core;

public sealed record ScratchProvenanceRecord(int Schema, string Tool, string[] Operations, string Sha256);

public sealed record ScratchProvenanceReport(bool MarkerPresent, bool Recognized, bool? ContentHashMatches,
    ScratchProvenanceRecord? Record, string Notice);

public static class ScratchProvenance
{
    public const string ArchivePrefix = "ScratchASM-Provenance: ";
    public const string SourcePrefix = "# " + ArchivePrefix;
    private const string Notice = "Self-declared metadata, not a signature. Markers can be removed or forged; absence does not prove ScratchASM was never used.";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
        { "imported", "compiled", "edited", "exported", "source-saved", "generated", "repaired" };

    public static ScratchProvenanceReport Inspect(string path)
    {
        if (ScratchAsmLanguage.IsSupportedSourceName(path)) return InspectSource(File.ReadAllText(path));
        if (!Path.GetExtension(path).Equals(".sb3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Provenance inspection accepts .sb3 or ScratchASM source files.");
        using ZipArchive archive = ZipFile.OpenRead(path);
        string comment = archive.Comment;
        ScratchProvenanceRecord? record = Parse(comment, ArchivePrefix);
        if (record is null) return Report(comment.StartsWith(ArchivePrefix, StringComparison.Ordinal), null, null);
        var (entries, _) = ScratchArchiveResources.Index(archive);
        using ArchiveHash hash = new();
        foreach (string name in entries.Keys.OrderBy(name => name == "project.json" ? 0 : 1).ThenBy(name => name, StringComparer.Ordinal))
            hash.Append(name, entries[name].Length, Stream.Null, output => ScratchArchiveResources.CopyEntry(entries[name], output));
        return Report(true, record, hash.Finish() == record.Sha256);
    }

    public static ScratchProvenanceReport InspectSource(string source)
    {
        ScratchProvenanceRecord? record = Parse(FirstLine(source), SourcePrefix);
        return Report(source.StartsWith(SourcePrefix, StringComparison.Ordinal), record,
            record is null ? null : SourceHash(WithoutMarker(source)) == record.Sha256);
    }

    public static string StampSource(string source, params string[] operations)
    {
        ScratchProvenanceRecord? previous = Parse(FirstLine(source), SourcePrefix);
        string body = WithoutMarker(source);
        string hash = SourceHash(body);
        IEnumerable<string> actions = (previous?.Operations ?? []).Concat(operations.Length == 0 ? ["generated"] : operations);
        if (previous is not null && hash != previous.Sha256) actions = actions.Append("edited");
        return SourcePrefix + Encode(hash, actions) + "\n" + body;
    }

    internal static string WithoutMarker(string source)
    {
        while (source.StartsWith(SourcePrefix, StringComparison.Ordinal))
        {
            int end = source.IndexOf('\n');
            source = end < 0 ? "" : source[(end + 1)..];
        }
        return source;
    }

    internal static string ArchiveComment(string hash, IEnumerable<string> operations) => ArchivePrefix + Encode(hash, operations);

    private static string Encode(string hash, IEnumerable<string> operations) => JsonSerializer.Serialize(
        new ScratchProvenanceRecord(1, "ScratchASM", operations.Where(KnownOperations.Contains).Distinct().Order(StringComparer.Ordinal).ToArray(), hash), JsonOptions);

    private static ScratchProvenanceReport Report(bool present, ScratchProvenanceRecord? record, bool? matches) =>
        new(present, record is not null, matches, record, Notice);

    private static string FirstLine(string source)
    {
        int end = source.IndexOf('\n');
        return (end < 0 ? source : source[..end]).TrimEnd('\r');
    }

    private static ScratchProvenanceRecord? Parse(string marker, string prefix)
    {
        if (!marker.StartsWith(prefix, StringComparison.Ordinal) || marker.Length > 4096) return null;
        try
        {
            var record = JsonSerializer.Deserialize<ScratchProvenanceRecord>(marker[prefix.Length..], JsonOptions);
            return record is { Schema: 1, Tool: "ScratchASM", Sha256.Length: 64, Operations.Length: > 0 and <= 7 } &&
                record.Sha256.All(char.IsAsciiHexDigit) && record.Operations.All(KnownOperations.Contains) ? record : null;
        }
        catch (JsonException) { return null; }
    }

    private static string SourceHash(string source)
    {
        using SHA256 hash = SHA256.Create();
        using (CryptoStream sink = new(Stream.Null, hash, CryptoStreamMode.Write))
        using (StreamWriter writer = new(sink, new UTF8Encoding(false))) writer.Write(source);
        return Convert.ToHexStringLower(hash.Hash!);
    }

    // Hash entry bytes as they are written, without another decompression pass or asset buffer.
    internal sealed class ArchiveHash : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void WriteEntry(ZipArchive archive, string name, byte[] bytes) =>
            WriteEntry(archive, name, bytes.LongLength, stream => stream.Write(bytes));

        public void WriteEntry(ZipArchive archive, string name, long length, Action<Stream> write)
        {
            using Stream destination = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            Append(name, length, destination, write);
        }

        public void Append(string name, long length, Stream destination, Action<Stream> write)
        {
            _hash.AppendData(Encoding.UTF8.GetBytes(name + "\0" + length.ToString(CultureInfo.InvariantCulture) + "\0"));
            using HashingWriteStream stream = new(destination, _hash);
            write(stream);
            if (stream.Written != length) throw new InvalidDataException($"ZIP entry length changed while writing: {name}");
        }

        public string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());
        public void Dispose() => _hash.Dispose();
    }

    private sealed class HashingWriteStream(Stream inner, IncrementalHash hash) : Stream
    {
        public long Written { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            hash.AppendData(buffer);
            Written = checked(Written + buffer.Length);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
