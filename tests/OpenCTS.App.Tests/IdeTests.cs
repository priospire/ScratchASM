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
    public void SvgPreviewUsesPixelDimensionsAndRejectsOversizedPhysicalUnits()
    {
        MethodInfo read = typeof(MainForm).Assembly.GetType("OpenCTS.App.StagePreview")!.GetMethod("ReadCostume")!;
        byte[] oversized = System.Text.Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="100in" height="100in"/>""");
        var error = Assert.Throws<TargetInvocationException>(() => read.Invoke(null, [oversized, "svg"]));
        Assert.IsInstanceOfType<InvalidDataException>(error.InnerException);
        byte[] valid = System.Text.Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40"><rect width="80" height="40" fill="#4C97FF"/></svg>""");
        using Bitmap bitmap = (Bitmap)read.Invoke(null, [valid, "svg"])!;
        Assert.AreEqual(new Size(80, 40), bitmap.Size);
        Assert.AreEqual(ColorTranslator.FromHtml("#4C97FF").ToArgb(), bitmap.GetPixel(40, 20).ToArgb());
    }
    [TestMethod]
    public void DiagnosticMarksPreserveSyntaxAndClearWhenSourceChanges() => OnUiThread(() =>
    {
        using Form form = new();
        using CodeEditor editor = new() { Dock = DockStyle.Fill, Text = "motion.move 10\nwarning\ninfo\n" };
        form.Controls.Add(editor); form.Show();
        Assert.AreEqual("motion.move 10\nwarning\ninfo\n", editor.Text, "Initial editor buffer must survive handle creation.");
        var diagnostics = new[]
        {
            new CtsDiagnostic("E1", DiagnosticSeverity.Error, "Error example", new SourceSpan(new SourceLocation(1, 1), new SourceLocation(1, 12))),
            new CtsDiagnostic("W1", DiagnosticSeverity.Warning, "Warning example", new SourceSpan(new SourceLocation(2, 1), new SourceLocation(2, 8))),
            new CtsDiagnostic("I1", DiagnosticSeverity.Info, "Info example", new SourceSpan(new SourceLocation(3, 1), new SourceLocation(3, 5)))
        };
        editor.ApplyColors(CtsSyntaxClassifier.Classify(editor.Text), Color.White, Color.Gray, true);
        editor.Select(0, 11);
        int color = editor.SelectionColor.ToArgb();
        editor.ApplyDiagnostics(diagnostics);
        editor.Refresh();
        Assert.AreEqual(color, editor.SelectionColor.ToArgb());
        Assert.AreEqual(DiagnosticSeverity.Error, editor.DiagnosticAt(1)!.Severity);
        Assert.AreEqual(DiagnosticSeverity.Warning, editor.DiagnosticAt(15)!.Severity);
        Assert.AreEqual(DiagnosticSeverity.Info, editor.DiagnosticAt(23)!.Severity);
        editor.Select(editor.TextLength, 0); editor.SelectedText = "# change";
        Assert.IsNull(editor.DiagnosticAt(1));
    });

    [TestMethod]
    public void CompletionInsertsLinesAndPairsBracesWithoutStealingFocus() => OnUiThread(() =>
    {
        using Form form = new();
        using CodeEditor editor = new() { Dock = DockStyle.Fill, Text = "stage {\n  @gre\n}\n" };
        form.Controls.Add(editor); form.Show(); editor.Focus();
        Assert.AreEqual("stage {\n  @gre\n}\n", editor.Text, "Initial completion buffer must survive handle creation.");
        editor.Select(editor.Text.IndexOf("@gre", StringComparison.Ordinal) + 4, 0);
        MethodInfo show = typeof(CodeEditor).GetMethod("ShowCompletion", BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodInfo accept = typeof(CodeEditor).GetMethod("AcceptCompletion", BindingFlags.NonPublic | BindingFlags.Instance)!;
        show.Invoke(editor, [true]);
        Application.DoEvents();
        Assert.IsTrue(editor.Focused);
        accept.Invoke(editor, null);
        StringAssert.Contains(editor.Text, "@greenflag:\n    ");
        Assert.IsFalse(CtsCompiler.Compile(editor.Text).Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error));
        editor.Undo();
        StringAssert.Contains(editor.Text, "@gre\n");
        editor.Text = ""; editor.Select(0, 0);
        var key = new KeyPressEventArgs('{');
        typeof(CodeEditor).GetMethod("OnKeyPress", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [key]);
        Assert.AreEqual("{}", editor.Text); Assert.AreEqual(1, editor.SelectionStart);
        object[] enter = [Message.Create(editor.Handle, 0x100, new IntPtr(13), IntPtr.Zero), Keys.Enter];
        typeof(CodeEditor).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, enter);
        Assert.AreEqual("{\n  \n}", editor.Text); Assert.AreEqual(4, editor.SelectionStart);
        editor.Text = "# mot"; editor.Select(editor.TextLength, 0);
        show.Invoke(editor, [true]); accept.Invoke(editor, null);
        Assert.AreEqual("# mot", editor.Text);
    });
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
        PumpUntil(() => Descendants(form).OfType<ListBox>().Any(list => list.Items.Contains("Calculator")));
        ToolStrip tools = Descendants(form).OfType<ToolStrip>().First(strip => strip.Items.OfType<ToolStripButton>().Any(button => button.Text == "Dark mode"));
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
    [TestMethod]
    public void LargeEditorColorsVisibleTextAndScrolledTextWithoutBlocking() => OnUiThread(() =>
    {
        using Form form = new() { Size = new Size(950, 700) };
        using CodeEditor editor = new() { Dock = DockStyle.Fill };
        form.Controls.Add(editor); form.Show();
        string source = "stage {\n  var counter = 0\n  @greenflag:\n" + string.Concat(Enumerable.Repeat("    counter += 1\n", 22000)) + "}\n";
        editor.Text = source;
        var spans = CtsSyntaxClassifier.Classify(source);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        editor.ApplyColors(spans, Color.White, Color.Gray, true);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(2), "Viewport painting stalled the UI.");
        editor.Select(source.IndexOf("counter", StringComparison.Ordinal), 7);
        Assert.AreEqual(ColorTranslator.FromHtml(ScratchCategoryColors.Variables).ToArgb(), editor.SelectionColor.ToArgb());
        int last = source.LastIndexOf("counter", StringComparison.Ordinal);
        editor.Select(last, 0); editor.ScrollToCaret();
        editor.ApplyColors(spans, Color.White, Color.Gray, false);
        editor.Select(last, 7);
        Assert.AreEqual(ColorTranslator.FromHtml(ScratchCategoryColors.Variables).ToArgb(), editor.SelectionColor.ToArgb());
    });

    [TestMethod]
    public void DefaultProgramAndIntegratedGuideAreUsable() => OnUiThread(() =>
    {
        using MainForm form = new(persistPreferences: false); form.Show();
        CodeEditor editor = Descendants(form).OfType<CodeEditor>().Single();
        StringAssert.Contains(editor.Text, "Hello World!");
        StringAssert.Contains(editor.Text, "my_variable");
        Assert.IsFalse(CtsCompiler.Compile(editor.Text).Diagnostics.Any(issue => issue.Severity == DiagnosticSeverity.Error));
        typeof(MainForm).GetMethod("ShowGuide", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        RichTextBox guide = Descendants(form).OfType<RichTextBox>().Single(control => control.Text.StartsWith("Start here", StringComparison.Ordinal));
        Assert.IsTrue(guide.Visible);
        StatusStrip status = Descendants(form).OfType<StatusStrip>().Single();
        Assert.IsLessThan(0.12f, status.BackColor.GetBrightness());
        int result = DwmGetWindowAttribute(form.Handle, 20, out int dark, 4);
        Assert.AreEqual(0, result, "Windows must accept dark title-bar mode.");
        Assert.AreEqual(1, dark);
        Assert.IsTrue((bool)typeof(MainForm).GetField("_titleThemeApplied", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!,
            "Windows must accept the custom caption and text colors.");
    });
    [TestMethod]
    public void MultiMegabyteArchiveLoadsAndHighlightsWithAnActiveMessageLoop() => OnUiThread(() =>
    {
        string directory = Path.Combine(FindRoot(), "artifacts", "ui-qa");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "heavy.sb3");
        var document = ScratchProjectDocument.Compile("stage {\n}\n");
        var blocks = document.Project["targets"]![0]!["blocks"]!.AsObject();
        for (int i = 0; i < 18000; i++)
            blocks["b" + i] = new System.Text.Json.Nodes.JsonObject
            {
                ["opcode"] = i == 0 ? "event_whenflagclicked" : "looks_say",
                ["parent"] = i == 0 ? null : "b" + (i - 1),
                ["next"] = i == 17999 ? null : "b" + (i + 1),
                ["topLevel"] = i == 0, ["shadow"] = false,
                ["fields"] = new System.Text.Json.Nodes.JsonObject(),
                ["inputs"] = i == 0 ? new System.Text.Json.Nodes.JsonObject() : new System.Text.Json.Nodes.JsonObject
                { ["MESSAGE"] = new System.Text.Json.Nodes.JsonArray(1, new System.Text.Json.Nodes.JsonArray(10, new string('x', 60))) }
            };
        document.Write(path, true);
        Assert.IsGreaterThan(5 * 1024 * 1024, document.JsonBytes.Length);
        using MainForm form = new(path, persistPreferences: false);
        int ticks = 0;
        using System.Windows.Forms.Timer pulse = new() { Interval = 25 };
        pulse.Tick += (_, _) => ticks++;
        pulse.Start();
        form.Show();
        CodeEditor editor = Descendants(form).OfType<CodeEditor>().Single();
        PumpUntil(() => editor.TextLength > 5 * 1024 * 1024 && !editor.ReadOnly);
        int opcode = editor.Text.IndexOf("event_whenflagclicked", StringComparison.Ordinal);
        editor.Select(opcode, 0); editor.ScrollToCaret();
        PumpUntil(() =>
        {
            editor.Select(opcode, "event_whenflagclicked".Length);
            return editor.SelectionColor.ToArgb() == ColorTranslator.FromHtml(ScratchCategoryColors.Events).ToArgb();
        });
        Assert.IsGreaterThan(0, ticks);
    });
    [TestMethod]
    public void AddSpriteButtonAndAssetUndoKeepTheSourceAndProjectTogether() => OnUiThread(() =>
    {
        using MainForm form = new(persistPreferences: false); form.Show();
        CodeEditor editor = Descendants(form).OfType<CodeEditor>().Single();
        string before = editor.Text;
        var spriteButton = Descendants(form).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripButton>()).Single(button => button.Text == "Sprite");
        spriteButton.PerformClick();
        PumpUntil(() => !editor.ReadOnly && Descendants(form).OfType<ListBox>().Any(list => list.Items.Contains("Sprite")));
        StringAssert.Contains(editor.Text, "sprite");
        typeof(MainForm).GetMethod("UndoAssetChange", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        Assert.AreEqual(before, editor.Text);
        PumpUntil(() => Descendants(form).OfType<ListBox>().All(list => !list.Items.Contains("Sprite")));
    });
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
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
            try
            {
                Application.EnableVisualStyles();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Control.CheckForIllegalCrossThreadCalls = true;
                EventHandler? run = null;
                run = (_, _) =>
                {
                    Application.Idle -= run;
                    try { action(); }
                    catch (Exception ex) { failure = ex; }
                    finally { Application.ExitThread(); }
                };
                Application.Idle += run;
                Application.Run();
            }
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
