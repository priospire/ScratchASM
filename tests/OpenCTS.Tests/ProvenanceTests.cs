using System.IO.Compression;
using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ProvenanceTests
{
    [TestMethod]
    public void SourceMarkersAreDeterministicAndDetectEditsWithoutChangingCode()
    {
        const string body = "stage {\r\n  var message = \"Hello World!\"\r\n}\r\n";
        string marked = ScratchProvenance.StampSource(body, "generated");
        var report = ScratchProvenance.InspectSource(marked);
        Assert.IsTrue(report.Recognized);
        Assert.IsTrue(report.ContentHashMatches);
        CollectionAssert.Contains(report.Record!.Operations, "generated");
        Assert.AreEqual(marked, ScratchProvenance.StampSource(marked, "generated"));
        Assert.IsFalse(ScratchProvenance.InspectSource(marked + "# edit\n").ContentHashMatches);
        string saved = ScratchProvenance.StampSource(marked + "# edit\n", "source-saved");
        Assert.IsTrue(ScratchProvenance.InspectSource(saved).ContentHashMatches);
        CollectionAssert.Contains(ScratchProvenance.InspectSource(saved).Record!.Operations, "edited");
        var compiled = CtsCompiler.Compile(marked);
        Assert.IsFalse(compiled.Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void MissingMalformedAndUnknownMarkersDoNotClaimProvenance()
    {
        var missing = ScratchProvenance.InspectSource("stage {}\n");
        Assert.IsFalse(missing.MarkerPresent);
        Assert.IsNull(missing.ContentHashMatches);
        foreach (string content in new[] { "{}", "{", "null", "{\"schema\":2}", "{\"schema\":1,\"tool\":\"ScratchASM\",\"operations\":null,\"sha256\":null}" })
        {
            var malformed = ScratchProvenance.InspectSource(ScratchProvenance.SourcePrefix + content + "\nstage {}\n");
            Assert.IsTrue(malformed.MarkerPresent);
            Assert.IsFalse(malformed.Recognized);
            Assert.IsNull(malformed.ContentHashMatches);
        }
    }

    [TestMethod]
    public void ArchiveAndPortableSourceAreMarkedWithoutModifyingOriginalEntries()
    {
        string directory = Directory.CreateTempSubdirectory("provenance-").FullName;
        try
        {
            var document = ScratchProjectDocument.Compile("stage {\n  var score = 0\n}\n");
            string original = Path.Combine(directory, "original.sb3");
            document.Write(original);
            using (ZipArchive zip = ZipFile.Open(original, ZipArchiveMode.Update)) zip.Comment = "Unmarked input";
            byte[] originalBytes = File.ReadAllBytes(original);
            Assert.IsFalse(ScratchProvenance.Inspect(original).MarkerPresent);
            var session = ScratchProjectEditSession.Open(original);
            CollectionAssert.Contains(ScratchProvenance.InspectSource(session.SourceText).Record!.Operations, "imported");
            string sourcePath = Path.Combine(directory, "portable.sasm");
            Assert.IsTrue(session.SaveSource(session.SourceText, sourcePath).Success);
            Assert.IsTrue(ScratchProvenance.Inspect(sourcePath).ContentHashMatches);
            string roundtrip = Path.Combine(directory, "roundtrip.sb3");
            Assert.IsTrue(new ScratchProjectConverter().ConvertToSb3(sourcePath, roundtrip).Success);
            Assert.IsTrue(ScratchProvenance.Inspect(roundtrip).ContentHashMatches);
            using (ZipArchive before = ZipFile.OpenRead(original))
            using (ZipArchive after = ZipFile.OpenRead(roundtrip))
            {
                Assert.HasCount(before.Entries.Count, after.Entries);
                foreach (ZipArchiveEntry entry in before.Entries)
                {
                    using MemoryStream a = new(), b = new();
                    using Stream first = entry.Open(), second = after.GetEntry(entry.FullName)!.Open();
                    first.CopyTo(a); second.CopyTo(b);
                    CollectionAssert.AreEqual(a.ToArray(), b.ToArray(), entry.FullName);
                }
            }
            string edited = Path.Combine(directory, "edited.sb3");
            Assert.IsTrue(session.WriteEdited(session.SourceText + "\n# edited\n", edited).Success);
            CollectionAssert.Contains(ScratchProvenance.Inspect(edited).Record!.Operations, "edited");
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(original));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ArchiveHashCoversJsonAssetsAndExtraEntries()
    {
        string directory = Directory.CreateTempSubdirectory("provenance-hash-").FullName;
        try
        {
            var document = ScratchProjectDocument.Compile("stage {\n}\n");
            document.Assets["extra.bin"] = [1, 2, 3];
            foreach (string changed in new[] { "project.json", "extra.bin", document.Assets.Keys.First(key => key != "extra.bin") })
            {
                string path = Path.Combine(directory, Guid.NewGuid() + ".sb3");
                document.Write(path);
                Assert.IsTrue(ScratchProvenance.Inspect(path).ContentHashMatches);
                using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    zip.GetEntry(changed)!.Delete();
                    using Stream stream = zip.CreateEntry(changed).Open();
                    stream.Write(changed == "project.json" ? "{}"u8 : "modified"u8);
                }
                Assert.IsFalse(ScratchProvenance.Inspect(path).ContentHashMatches, changed);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void CompileRepairAndSourceSaveStampOutputsButPreferencesStayJson()
    {
        string directory = Directory.CreateTempSubdirectory("provenance-output-").FullName;
        try
        {
            string source = Path.Combine(directory, "main.sasm"), archive = Path.Combine(directory, "main.sb3");
            ScratchProjectEditSession.WriteSourceFile(source, "stage {\n}\n");
            Assert.IsTrue(ScratchProvenance.Inspect(source).ContentHashMatches);
            Assert.IsTrue(new ScratchProjectConverter().ConvertToSb3(source, archive).Success);
            CollectionAssert.Contains(ScratchProvenance.Inspect(archive).Record!.Operations, "compiled");
            string repair = Path.Combine(directory, "repair.sb3");
            Assert.IsTrue(new ScratchProjectConverter().ConvertToSb3(archive, repair, new ConversionOptions { AttemptSafeRepair = true }).Success);
            Assert.IsTrue(ScratchProvenance.Inspect(repair).ContentHashMatches);
            CollectionAssert.Contains(ScratchProvenance.Inspect(repair).Record!.Operations, "repaired");
            string preferences = Path.Combine(directory, "preferences.json");
            ScratchProjectEditSession.WriteSourceFile(preferences, "{\"dark\":true}");
            using var parsed = JsonDocument.Parse(File.ReadAllText(preferences));
            Assert.IsTrue(parsed.RootElement.GetProperty("dark").GetBoolean());
        }
        finally { Directory.Delete(directory, true); }
    }
}
