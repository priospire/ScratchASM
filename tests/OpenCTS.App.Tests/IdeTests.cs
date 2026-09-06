using System.Drawing.Imaging;
using System.Reflection;
using OpenCTS.App;
using OpenCTS.Core;

[assembly: DoNotParallelize]

namespace OpenCTS.App.Tests;

[TestClass]
public sealed class IdeTests
{
    [TestMethod]
    public void HighlightingKeepsUndoRedoAndCaretIntact() => OnUiThread(() =>
    {
        using Form form = new();
        using CodeEditor editor = new() { Dock = DockStyle.Fill, Text = "stage {\n}\n" };
        form.Controls.Add(editor);
        form.Show();
        editor.ResetHistory();
        string original = editor.Text;
        editor.Select(0, 0);
        editor.SelectedText = "# edit\n";
        string changed = editor.Text;
        int caret = editor.SelectionStart;
        editor.ApplyColors(CtsSyntaxClassifier.Classify(editor.Text), Color.White, Color.Gray, true);
        Assert.AreEqual(caret, editor.SelectionStart);
        editor.Undo();
        Assert.AreEqual(original, editor.Text);
        editor.Redo();
        Assert.AreEqual(changed, editor.Text);
    });

    [TestMethod]
    public void SourceAndImportedProjectsRenderAtDesktopAndCompactSizes() => OnUiThread(() =>
    {
        string root = FindRoot();
        string directory = Path.Combine(root, "artifacts", "ui-qa");
        Directory.CreateDirectory(directory);
        string input = Path.Combine(root, "samples", "roundtrip.sasm");
        string archive = Path.Combine(directory, "import.sb3");
        ConversionResult conversion = new ScratchProjectConverter().ConvertToSb3(input, archive, new ConversionOptions { Overwrite = true });
        Assert.IsTrue(conversion.Success, string.Join(";", conversion.Issues));
        using MainForm form = new(input, persistPreferences: false);
        form.Show();
        CodeEditor editor = Descendants(form).OfType<CodeEditor>().Single();
        PumpUntil(() => editor.Text.Contains("Calculator", StringComparison.Ordinal) && !editor.ReadOnly);
        PumpUntil(() => Descendants(form).OfType<TreeView>().Single().Nodes.Count > 0);
        ToolStrip tools = Descendants(form).OfType<ToolStrip>().First(strip => strip.Items.OfType<ToolStripButton>().Any());
        ToolStripButton theme = tools.Items.OfType<ToolStripButton>().Single(button => button.Text == "Dark mode");
        foreach ((bool dark, Size size, string name) in new[]
        {
            (true, new Size(1240, 820), "dark-desktop"),
            (false, new Size(1240, 820), "light-desktop"),
            (true, new Size(900, 600), "dark-compact")
        })
        {
            theme.Checked = dark;
            form.Size = size;
            Application.DoEvents();
            Assert.IsGreaterThan(300, editor.Width);
            Assert.IsGreaterThan(100, editor.Height);
            foreach (Control control in Descendants(form).Where(control => control.Visible && control is TextBox))
                Assert.IsGreaterThan(16, control.Height, control.AccessibleName);
            using Bitmap bitmap = new(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
        }
        typeof(MainForm).GetMethod("LoadInputPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [archive]);
        PumpUntil(() => form.Text.Contains("import.sb3", StringComparison.Ordinal) && !editor.ReadOnly);
        StringAssert.Contains(editor.Text, "rawblocks {");
        Assert.DoesNotContain('*', form.Text);
        string exported = Path.Combine(directory, "exported.sasm");
        ScratchProjectEditSession session = ScratchProjectEditSession.Open(archive);
        Assert.IsTrue(session.SaveSource(session.SourceText, exported, true).Success);
        typeof(MainForm).GetMethod("LoadInputPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [exported]);
        PumpUntil(() => form.Text.Contains("exported.sasm", StringComparison.Ordinal) && !editor.ReadOnly);
        StringAssert.Contains(editor.Text, "project \"");
        theme.Checked = true;
    });

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    private static void PumpUntil(Func<bool> done)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.IsTrue(done(), "UI operation did not complete.");
    }
    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpenCTS.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Solution directory was not found.");
    }
}
