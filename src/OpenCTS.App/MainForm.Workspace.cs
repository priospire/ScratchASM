using System.Text.Json.Nodes;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenCTS.Core;

namespace OpenCTS.App;

public sealed partial class MainForm
{
    private Panel? _inspector, _guidePanel;
    private SplitContainer? _editorPanels;
    private StagePreview? _preview;
    private ActivityLine? _progress;
    private readonly ListBox _sprites = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly ListBox _assets = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _projectSummary = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 4, 10, 0) };
    private readonly RichTextBox _guideText = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), DetectUrls = false };
    private ScratchProjectDocument? _previewDocument;
    private bool _inspectorWanted = true, _showSounds, _refreshingProject, _refreshProjectPending, _renderingPreview, _renderPreviewPending;
    private WaveOutEvent? _soundOutput;
    private WaveStream? _soundReader;
    private (ScratchProjectEditSession? Session, string Source, string SavedText, string AppliedText)? _assetUndo;

    private Control BuildInspector()
    {
        foreach (ListBox list in new[] { _sprites, _assets })
        {
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Font.Height + 8;
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using SolidBrush background = new(selected ? _theme.Accent : list.BackColor);
                e.Graphics.FillRectangle(background, e.Bounds);
                Color foreground = selected ? (_theme.Accent.GetBrightness() > 0.6 ? Color.Black : Color.White) : _theme.Text;
                TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), list.Font,
                    Rectangle.Inflate(e.Bounds, -6, 0), foreground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        _inspector = new Panel { Dock = DockStyle.Right, Width = 275, Padding = new Padding(8, 0, 0, 0) };
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        foreach (float height in new[] { 34f, 186, 44, 34, 118, 34, 90, 34 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles[4] = new RowStyle(SizeType.Percent, 55);
        layout.RowStyles[6] = new RowStyle(SizeType.Percent, 45);
        ToolStrip tools = NewTools();
        AddTool(tools, "Project", "\uE8B9", null);
        AddTool(tools, "", "\uE72C", async (_, _) => await RefreshProjectAsync(), "Refresh preview");
        AddTool(tools, "", "\uE711", (_, _) => { _inspectorWanted = false; _inspector.Visible = false; }, "Hide project");
        layout.Controls.Add(tools, 0, 0);
        _preview = new StagePreview { Dock = DockStyle.Fill };
        layout.Controls.Add(_preview, 0, 1);
        layout.Controls.Add(_projectSummary, 0, 2);
        ToolStrip spriteTools = NewTools();
        AddTool(spriteTools, "Sprite", "\uE710", async (_, _) => await ChangeProjectAsync(document => document.AddSprite("Sprite")), "Add sprite");
        AddTool(spriteTools, "Import", "\uE8B5", ImportSprite, "Import .sprite3");
        layout.Controls.Add(spriteTools, 0, 3);
        layout.Controls.Add(_sprites, 0, 4);
        ToolStrip assetTools = NewTools();
        AddTool(assetTools, "Costumes", "\uEB9F", (_, _) => { _showSounds = false; UpdateAssets(); });
        AddTool(assetTools, "Sounds", "\uE189", (_, _) => { _showSounds = true; UpdateAssets(); });
        layout.Controls.Add(assetTools, 0, 5);
        layout.Controls.Add(_assets, 0, 6);
        ToolStrip addTools = NewTools();
        AddTool(addTools, "Add", "\uE710", async (_, _) => await AddMediaAsync());
        AddTool(addTools, "", "\uE768", (_, _) => PreviewAsset(), "Preview selected asset");
        AddTool(addTools, "", "\uE71A", (_, _) => _soundOutput?.Stop(), "Stop sound");
        layout.Controls.Add(addTools, 0, 7);
        _sprites.SelectedIndexChanged += (_, _) => { UpdateAssets(); _ = RenderPreviewAsync(); };
        _assets.DoubleClick += (_, _) => PreviewAsset();
        _sprites.ContextMenuStrip = new ContextMenuStrip();
        _sprites.ContextMenuStrip.Items.Add("Go to source", null, (_, _) =>
        {
            TreeNode? target = _outline.Nodes.Cast<TreeNode>().FirstOrDefault(node => node.Text == _sprites.SelectedItem?.ToString());
            if (target?.Tag is SourceLocation location) { _sourceEditor.Select(CtsSourcePosition.GetOffset(_sourceEditor.Text, location), 0); _sourceEditor.ScrollToCaret(); _sourceEditor.Focus(); }
        });
        _sprites.ContextMenuStrip.Items.Add("Add sprite", null, async (_, _) => await ChangeProjectAsync(document => document.AddSprite("Sprite")));
        _inspector.Controls.Add(layout);
        return _inspector;
    }
    private void ToggleInspector()
    {
        if (_inspector is null) return;
        _inspectorWanted = !_inspector.Visible;
        _inspector.Visible = _inspectorWanted;
        if (Width < 1100 && _workspace is not null) _workspace.Panel1Collapsed = _inspectorWanted;
        if (_inspectorWanted) _ = RefreshProjectAsync();
    }
    private ScratchProjectDocument BuildDocument(string source) => _editSession?.Materialize(source) ?? ScratchProjectDocument.Compile(source);
    private async Task RefreshProjectAsync()
    {
        if (_packageOnly || IsDisposed) return;
        if (_refreshingProject) { _refreshProjectPending = true; return; }
        _refreshingProject = true;
        string source = _sourceEditor.Text;
        int version = _diagnosticsVersion;
        ScratchProjectEditSession? session = _editSession;
        try
        {
            var result = await Task.Run(() =>
            {
                var document = session?.Materialize(source) ?? ScratchProjectDocument.Compile(source);
                return (document, summary: SummarizeProject(document));
            });
            if (IsDisposed || version != _diagnosticsVersion) return;
            SetPreviewDocument(result.document, Math.Max(0, _sprites.SelectedIndex), result.summary);
        }
        catch (Exception ex) when (IsDocumentError(ex)) { if (!IsDisposed) _projectSummary.Text = "Preview unavailable. Fix source errors, then refresh."; }
        finally
        {
            _refreshingProject = false;
            if (!IsDisposed && (_refreshProjectPending || version != _diagnosticsVersion))
            { _refreshProjectPending = false; _ = RefreshProjectAsync(); }
        }
    }
    private static (int Bytes, bool Custom, bool Warning) SummarizeProject(ScratchProjectDocument document)
    {
        int bytes = document.JsonBytes.Length;
        var warnings = ScratchCompatibility.Inspect(document.Project, bytes);
        return (bytes, warnings.Any(issue => issue.Code == "SASM4001"), warnings.Count > 0);
    }
    private void SetPreviewDocument(ScratchProjectDocument document, int selected, (int Bytes, bool Custom, bool Warning) summary)
    {
        _previewDocument = document;
        _sprites.BeginUpdate();
        _sprites.Items.Clear();
        foreach (JsonNode? target in document.Project["targets"]!.AsArray()) _sprites.Items.Add(target?["name"]?.ToString() ?? "Target");
        _sprites.SelectedIndex = Math.Clamp(selected, 0, _sprites.Items.Count - 1);
        _sprites.EndUpdate();
        _projectSummary.Text = $"{Math.Max(0, _sprites.Items.Count - 1)} sprites  |  project.json {summary.Bytes / 1048576d:F2} / 5 MiB";
        if (summary.Warning) _projectSummary.Text += "\n" + (summary.Custom ? "TurboWarp / custom extensions" : "Scratch save budget warning");
        _projectSummary.ForeColor = summary.Warning ? _theme.Warning : _theme.Muted;
    }
    private async Task RenderPreviewAsync()
    {
        ScratchProjectDocument? document = _previewDocument;
        int selected = _sprites.SelectedIndex;
        if (document is null || selected < 0) return;
        if (_renderingPreview) { _renderPreviewPending = true; return; }
        _renderingPreview = true;
        try
        {
            Color accent = _theme.Accent;
            Bitmap bitmap = await Task.Run(() => StagePreview.Render(document, selected, accent));
            if (IsDisposed || document != _previewDocument || selected != _sprites.SelectedIndex) bitmap.Dispose();
            else _preview?.SetImage(bitmap);
        }
        catch (Exception ex) when (IsDocumentError(ex) || ex is System.Xml.XmlException or System.Runtime.InteropServices.ExternalException)
        { if (!IsDisposed) { _preview?.SetImage(null); _projectSummary.Text = "Image preview unavailable: " + ex.Message; } }
        finally
        {
            _renderingPreview = false;
            if (!IsDisposed && _renderPreviewPending) { _renderPreviewPending = false; _ = RenderPreviewAsync(); }
        }
    }
    private void UpdateAssets()
    {
        _soundOutput?.Stop();
        _assets.BeginUpdate();
        try
        {
            _assets.Items.Clear();
            if (_previewDocument is null || _sprites.SelectedIndex < 0) return;
            foreach (JsonNode? asset in _previewDocument.Project["targets"]![_sprites.SelectedIndex]![_showSounds ? "sounds" : "costumes"]!.AsArray())
                _assets.Items.Add(asset?["name"]?.ToString() ?? "Asset");
            if (_assets.Items.Count > 0) _assets.SelectedIndex = 0;
        }
        finally { _assets.EndUpdate(); }
    }
    private async Task ChangeProjectAsync(Func<ScratchProjectDocument, int> change)
    {
        if (_isBusy || _packageOnly) return;
        string source = _sourceEditor.Text;
        ScratchProjectEditSession? previousSession = _editSession;
        string previousSaved = _savedText;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => { var document = BuildDocument(source); int selected = change(document); return (document, selected, session: document.CreateSession(), summary: SummarizeProject(document)); });
            if (IsDisposed) return;
            _editSession = result.session;
            _editSessionPath = _loadedPath ?? Path.Combine(Directory.GetCurrentDirectory(), "untitled.sasm");
            ReplaceEditorText(_editSession.SourceText);
            _assetUndo = (previousSession, source, previousSaved, _sourceEditor.Text);
            _savedText = "\0";
            _sourceEditor.ResetHistory();
            UpdateDocumentTitle();
            SetPreviewDocument(result.document, result.selected, result.summary);
            SetStatus("Project updated. Save source to keep the asset companion.", _theme.Success);
        }
        catch (Exception ex) when (IsDocumentError(ex)) { SetStatus(ex.Message, _theme.Error); }
        finally { if (!IsDisposed) { SetBusy(false); ScheduleDiagnostics(); } }
    }
    private void UndoAssetChange()
    {
        if (_isBusy || _assetUndo is not { } previous) return;
        if (_sourceEditor.Text != previous.AppliedText)
        { _activity.Text = "Undo subsequent source edits before undoing the asset change."; return; }
        _editSession = previous.Session;
        _editSessionPath = _editSession is null ? null : _loadedPath ?? Path.Combine(Directory.GetCurrentDirectory(), "untitled.sasm");
        _savedText = previous.SavedText;
        _assetUndo = null;
        ReplaceEditorText(previous.Source);
        _sourceEditor.ResetHistory();
        _ = RefreshProjectAsync();
    }
    private async void ImportSprite(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new() { Filter = "Scratch sprite (*.sprite3)|*.sprite3" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await ChangeProjectAsync(document => document.ImportSprite(dialog.FileName));
    }
    private async Task AddMediaAsync()
    {
        int target = _sprites.SelectedIndex;
        if (target < 0) return;
        bool sound = _showSounds;
        using OpenFileDialog dialog = new() { Filter = sound ? "Sound (*.wav;*.mp3)|*.wav;*.mp3" : "Costume (*.svg;*.png;*.jpg;*.jpeg)|*.svg;*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ChangeProjectAsync(document =>
        {
            if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidDataException("Media import is limited to 16 MiB per file.");
            if (sound)
            {
                using AudioFileReader reader = new(dialog.FileName);
                if (reader.TotalTime > TimeSpan.FromMinutes(10)) throw new InvalidDataException("Sound import is limited to 10 minutes.");
                SampleToWaveProvider16 provider = new(reader);
                using MemoryStream buffer = new();
                long count = 0;
                using (WaveFileWriter writer = new(buffer, provider.WaveFormat))
                {
                    byte[] block = new byte[16384];
                    int read;
                    while ((read = provider.Read(block, 0, block.Length)) > 0)
                    {
                        count += read / provider.WaveFormat.BlockAlign;
                        if (buffer.Length + read > 128L * 1024 * 1024 || count > (long)provider.WaveFormat.SampleRate * 600)
                            throw new InvalidDataException("Decoded sound exceeds 128 MiB or 10 minutes.");
                        writer.Write(block, 0, read);
                    }
                }
                document.AddSound(target, Path.GetFileNameWithoutExtension(dialog.FileName), buffer.ToArray(), provider.WaveFormat.SampleRate, count);
            }
            else
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                string format = Path.GetExtension(dialog.FileName)[1..].ToLowerInvariant();
                using Bitmap bitmap = StagePreview.ReadCostume(bytes, format);
                document.AddCostume(target, Path.GetFileNameWithoutExtension(dialog.FileName), bytes, format, bitmap.Width / 2d, bitmap.Height / 2d);
            }
            return target;
        });
    }
    private void PreviewAsset()
    {
        if (_previewDocument is null || _sprites.SelectedIndex < 0 || _assets.SelectedIndex < 0) return;
        JsonNode asset = _previewDocument.Project["targets"]![_sprites.SelectedIndex]![_showSounds ? "sounds" : "costumes"]![_assets.SelectedIndex]!;
        if (!_previewDocument.Assets.TryGetValue(asset["md5ext"]!.ToString(), out byte[]? bytes)) return;
        try
        {
            if (_showSounds)
            {
                _soundOutput?.Stop(); _soundOutput?.Dispose(); _soundReader?.Dispose();
                MemoryStream stream = new(bytes, false);
                _soundReader = asset["dataFormat"]?.ToString() == "mp3" ? new Mp3FileReader(stream) : new WaveFileReader(stream);
                _soundOutput = new WaveOutEvent(); _soundOutput.Init(_soundReader); _soundOutput.Play();
            }
            else
            {
                using Bitmap bitmap = StagePreview.ReadCostume(bytes, asset["dataFormat"]!.ToString());
                using Form viewer = new() { Text = asset["name"]!.ToString(), Size = new Size(600, 500), StartPosition = FormStartPosition.CenterParent, BackColor = _theme.EditorBackground };
                viewer.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = bitmap, SizeMode = PictureBoxSizeMode.Zoom });
                viewer.Shown += (_, _) => NativeTheme.Title(viewer, _theme.IsDark, _theme.Surface, _theme.Text);
                viewer.ShowDialog(this);
            }
        }
        catch (Exception ex) when (IsDocumentError(ex) || ex is NAudio.MmException) { SetStatus(ex.Message, _theme.Error); }
    }
    private async Task RunProjectToolAsync(bool vanilla)
    {
        if (_isBusy) return;
        string source = _sourceEditor.Text;
        SetBusy(true);
        try
        {
            ProjectToolReport report = await Task.Run(() => ScratchCompatibility.Optimize(BuildDocument(source), vanilla));
            ShowIssues(report.Issues, string.Join(Environment.NewLine, report.Changes), _theme.Success);
            if (vanilla && !report.CanExportVanilla) { _activity.Text = "Vanilla export blocked: unresolved compatibility issues. Original project unchanged."; return; }
            using SaveFileDialog dialog = new() { Filter = "Scratch project (*.sb3)|*.sb3", FileName = Path.GetFileNameWithoutExtension(_loadedPath ?? "project") + (vanilla ? ".scratch.sb3" : ".optimized.sb3"), OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (SameDocument(dialog.FileName, _loadedPath) || SameDocument(dialog.FileName, _editSession?.InputPath)) throw new IOException("Choose a new filename; the original project is protected.");
            await Task.Run(() => report.Document.Write(dialog.FileName, true));
            ShowIssues(report.Issues, "Wrote " + dialog.FileName + Environment.NewLine + string.Join(Environment.NewLine, report.Changes), _theme.Success);
        }
        catch (Exception ex) when (IsDocumentError(ex)) { SetStatus(ex.Message, _theme.Error); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private Control BuildGuidePanel()
    {
        _guidePanel = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(14) };
        ToolStrip tools = NewTools(); tools.Dock = DockStyle.Top;
        ToolStripComboBox topics = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        string[] files = ["quick-start.md", "scratchasm.md", "round-trips.md", "project-tools.md"];
        topics.Items.AddRange(["Getting started", "Language reference", "Import and export", "Project tools"]);
        topics.SelectedIndexChanged += (_, _) => LoadGuide(files[topics.SelectedIndex]);
        tools.Items.Add(topics);
        ToolStripTextBox search = new() { ToolTipText = "Find in guide", Width = 140 };
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || search.Text.Length == 0) return;
            int start = _guideText.SelectionStart + _guideText.SelectionLength;
            int index = _guideText.Find(search.Text, Math.Min(start, _guideText.TextLength), RichTextBoxFinds.None);
            if (index < 0) _guideText.Find(search.Text, 0, RichTextBoxFinds.None);
            _guideText.ScrollToCaret(); e.SuppressKeyPress = true;
        };
        tools.Items.Add(search);
        AddTool(tools, "Source", "\uE70F", (_, _) => { _guidePanel.Visible = false; if (_editorPanels is not null) _editorPanels.Visible = true; });
        _guidePanel.Controls.Add(_guideText); _guidePanel.Controls.Add(tools);
        topics.SelectedIndex = 0;
        return _guidePanel;
    }
    private void ShowGuide()
    {
        if (_guidePanel is null) return;
        _guidePanel.Visible = true; _guidePanel.BringToFront();
        if (_editorPanels is not null) _editorPanels.Visible = false;
    }
    private void LoadGuide(string name)
    {
        _guideName = name;
        var assembly = typeof(MainForm).Assembly;
        string? resource = assembly.GetManifestResourceNames().FirstOrDefault(item => item.EndsWith("." + name, StringComparison.Ordinal));
        if (resource is null) return;
        using StreamReader reader = new(assembly.GetManifestResourceStream(resource)!);
        string markdown = reader.ReadToEnd();
        System.Text.StringBuilder text = new();
        List<(int Start, int Length)> headings = [];
        List<(int Start, string Source)> examples = [];
        int codeStart = -1;
        foreach (string line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("~~~", StringComparison.Ordinal) || line.StartsWith(new string((char)96, 3), StringComparison.Ordinal))
            {
                if (codeStart < 0) codeStart = text.Length;
                else { examples.Add((codeStart, text.ToString(codeStart, text.Length - codeStart))); codeStart = -1; }
                continue;
            }
            string content = codeStart < 0 ? line.Replace(((char)96).ToString(), "", StringComparison.Ordinal) : line;
            if (codeStart < 0 && line.StartsWith('#'))
            { content = line.TrimStart('#', ' '); headings.Add((text.Length, content.Length)); }
            text.Append(content).Append('\n');
        }
        _guideText.Text = text.ToString();
        _guideText.SelectAll();
        _guideText.SelectionColor = _theme.Text;
        using Font body = new("Segoe UI", 10);
        using Font heading = new("Segoe UI Semibold", 13, FontStyle.Bold);
        using Font code = CreateMonoFont(10);
        _guideText.SelectionFont = body;
        foreach (var item in headings)
        { _guideText.Select(item.Start, item.Length); _guideText.SelectionFont = heading; _guideText.SelectionColor = _theme.Text; }
        foreach (var item in examples)
        {
            _guideText.Select(item.Start, item.Source.Length); _guideText.SelectionFont = code;
            foreach (var span in CtsSyntaxClassifier.Classify(item.Source))
            {
                _guideText.Select(item.Start + span.Start, span.Length);
                Color color = ColorTranslator.FromHtml(span.Color);
                if (_theme.IsDark && span.Color == ScratchCategoryColors.StringLiteral) color = Color.FromArgb(157, 205, 173);
                else if (_theme.IsDark && span.Color == ScratchCategoryColors.NumberLiteral) color = Color.FromArgb(189, 148, 221);
                else if (_theme.IsDark && span.Color is ScratchCategoryColors.NeutralKeyword or ScratchCategoryColors.RawSyntax) color = _theme.Muted;
                _guideText.SelectionColor = color;
            }
        }
        _guideText.Select(0, 0);
    }
    private string? _guideName;
    private static ToolStrip NewTools() => new() { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(3), AutoSize = false, Height = 32 };
    private static void AddTool(ToolStrip strip, string label, string glyph, EventHandler? action, string? hint = null)
    {
        ToolStripButton button = new(label) { Image = MakeGlyph(glyph), ToolTipText = hint ?? label, Padding = new Padding(3) };
        if (action is not null) button.Click += action;
        strip.Items.Add(button);
    }
}
