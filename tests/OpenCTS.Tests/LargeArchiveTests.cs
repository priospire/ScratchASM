using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class LargeArchiveTests
{
    [TestMethod]
    public void IncorrectExpandedEntryLengthsStillFailExportWithoutLeavingOutput()
    {
        string directory = Directory.CreateTempSubdirectory("zip-length-").FullName;
        try
        {
            string input = Path.Combine(directory, "invalid.sb3");
            WriteArchive(input, 1, 100);
            byte[] zip = File.ReadAllBytes(input);
            int position = 0;
            bool changed = false;
            while (position < zip.Length)
            {
                int next = zip.AsSpan(position).IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
                if (next < 0) break;
                position += next;
                int nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 28));
                if (Encoding.UTF8.GetString(zip, position + 46, nameLength) == "asset0.bin")
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(position + 24), 1);
                    changed = true; break;
                }
                position += 46 + nameLength;
            }
            Assert.IsTrue(changed);
            File.WriteAllBytes(input, zip);
            var session = ScratchProjectEditSession.Open(input);
            string output = Path.Combine(directory, "output.sb3");
            var result = session.WriteEdited(session.SourceText, output);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(File.Exists(output));
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void TenThousandEntriesOpenLazilyAndRoundTripBeyondTheOldExpandedLimit()
    {
        string directory = Directory.CreateTempSubdirectory("large-scratch-").FullName;
        try
        {
            string input = Path.Combine(directory, "large.sb3");
            WriteArchive(input, 10000, 65536);
            var clock = Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var session = ScratchProjectEditSession.Open(input);
            var document = session.Materialize(session.SourceText);
            long allocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.IsTrue(session.CanEdit, string.Join("\n", session.Issues));
            Assert.IsTrue(session.Issues.Any(issue => issue.Code == "SASM4005"));
            Assert.AreEqual(10001, document.Assets.Count);
            Assert.IsLessThan(128L * 1024 * 1024, allocation, "Opening must not inflate 625 MiB of media.");
            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(10), $"Lazy open took {clock.Elapsed}.");
            File.Delete(input);
            string output = Path.Combine(directory, "roundtrip.sb3");
            Assert.IsTrue(session.WriteEdited(session.SourceText, output).Success);
            using ZipArchive archive = ZipFile.OpenRead(output);
            Assert.HasCount(10002, archive.Entries);
            using Stream payload = archive.GetEntry("asset9999.bin")!.Open();
            byte[] bytes = new byte[65536];
            payload.ReadExactly(bytes);
            Assert.IsTrue(bytes.All(value => value == 42));
            Console.WriteLine($"Large archive open/materialize: {allocation / 1048576d:F1} MiB allocated; 10,002 entries and 625 MiB expanded preserved.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void PackagingAndRepairAlsoAcceptMoreThan4096EntriesWithAWarning()
    {
        string directory = Directory.CreateTempSubdirectory("large-repair-").FullName;
        try
        {
            string input = Path.Combine(directory, "large.sb3");
            WriteArchive(input, 4500, 1);
            foreach (bool repair in new[] { false, true })
            {
                var result = new ScratchProjectConverter().ConvertToSb3(input, Path.Combine(directory, repair + ".sb3"),
                    new ConversionOptions { AttemptSafeRepair = repair });
                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(result.Issues.Any(issue => issue.Code == "SASM4005"));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void AStreamingAssetAbove128MiBCanBeOpenedAndExported()
    {
        string directory = Directory.CreateTempSubdirectory("large-entry-").FullName;
        try
        {
            string path = Path.Combine(directory, "large.sb3");
            WriteArchive(path, 0, 0);
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
            using (Stream stream = archive.CreateEntry("long.bin", CompressionLevel.Fastest).Open())
            {
                byte[] chunk = new byte[1024 * 1024];
                for (int i = 0; i < 129; i++) stream.Write(chunk);
            }
            var session = ScratchProjectEditSession.Open(path);
            string output = Path.Combine(directory, "streamed.sb3");
            Assert.IsTrue(session.WriteEdited(session.SourceText, output).Success);
            using ZipArchive result = ZipFile.OpenRead(output);
            Assert.AreEqual(129L * 1024 * 1024, result.GetEntry("long.bin")!.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void WriteArchive(string path, int assets, int bytesPerAsset)
    {
        CtsCompileResult source = CtsCompiler.Compile("stage {\n  var value = 0\n  @greenflag:\n    value = 1\n}\n");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (Stream json = archive.CreateEntry("project.json").Open()) json.Write(source.ProjectJsonBytes);
        foreach (var asset in source.Assets)
        {
            using Stream destination = archive.CreateEntry(asset.Key).Open();
            destination.Write(asset.Value);
        }
        byte[] bytes = Enumerable.Repeat((byte)42, bytesPerAsset).ToArray();
        for (int i = 0; i < assets; i++)
        {
            using Stream stream = archive.CreateEntry($"asset{i}.bin", CompressionLevel.Fastest).Open();
            stream.Write(bytes);
        }
    }
}
