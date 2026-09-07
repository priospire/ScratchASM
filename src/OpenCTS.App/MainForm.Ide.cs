using System.Text.Json;
using OpenCTS.Core;
using OpenCTS.LanguageServices;

namespace OpenCTS.App;

public sealed partial class MainForm
{
    private readonly TreeView _outline = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, ShowLines = false, ItemHeight = 25 };
    private readonly Label _documentLabel = new() { Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 6, 0, 0) };
    private readonly TextBox _search = new() { Width = 240, AccessibleName = "Find in source" };
    private readonly FlowLayoutPanel _searchPanel = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6), Visible = false, WrapContents = false };
    private readonly CheckBox _matchCase = new() { Text = "Match case", AutoSize = true };
    private readonly ToolStripStatusLabel _activity = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready" };
    private readonly ToolStripStatusLabel _caret = new() { Text = "Ln 1, Col 1" };
    private ToolStrip? _ideTools;
    private StatusStrip? _statusStrip;
    private Control? _pathsPanel;
    private SplitContainer? _workspace;
    private string? _loadedPath;
    private string _savedText = "";
    private bool _packageOnly;
    private bool _analysisRunning;
    private bool _closeApproved;
    private readonly bool _persistPreferences;
    private bool IsDirty => !_packageOnly && _sourceEditor.Text != _savedText;
    private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScratchASM", "preferences.json");

    private Control BuildIdeLayout()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        _ideTools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Height = 44, AutoSize = false, Padding = new Padding(8, 5, 8, 5), RenderMode = ToolStripRenderMode.System };
        void Tool(string label, string glyph, string hint, EventHandler action)
        {
            ToolStripButton button = new(label) { Image = MakeGlyph(glyph), ToolTipText = hint, Padding = new Padding(6, 0, 6, 0) };
            button.Click += action;
            _ideTools.Items.Add(button);
        }
        Tool("New", "\uE8A5", "New source (Ctrl+N)", async (_, _) => await NewDocumentAsync());
        Tool("Open", "\uE8E5", "Open source or Scratch project (Ctrl+O)", BrowseInputFile);
        Tool("Save", "\uE74E", "Save source (Ctrl+S)", SaveSourceButton_Click);
        Tool("Save as", "\uE792", "Save source as (Ctrl+Shift+S)", async (_, _) => await SaveDocumentAsync(true));
        _ideTools.Items.Add(new ToolStripSeparator());
        Tool("Export .sb3", "\uE768", "Export Scratch project (F5)", ConvertButton_Click);
        Tool("Repair", "\uE90F", "Attempt repair and export", RepairButton_Click);
        Tool("", "\uE73E", "Check source", (_, _) => CheckSource());
        _ideTools.Items.Add(new ToolStripSeparator());
        Tool("", "\uE7A7", "Undo (Ctrl+Z)", (_, _) => _sourceEditor.Undo());
        Tool("", "\uE7A6", "Redo (Ctrl+Y)", (_, _) => _sourceEditor.Redo());
        Tool("", "\uE721", "Find (Ctrl+F)", (_, _) => ShowFind());
        Tool("Project", "\uE8B9", "Sprites, costumes, and sounds", (_, _) => ToggleInspector());
        Tool("Guide", "\uE82D", "Language guide (F1)", (_, _) => ShowGuide());
        ToolStripDropDownButton utilities = new("Tools") { Image = MakeGlyph("\uE713") };
        utilities.DropDownItems.Add("Compact / Optimize...", null, async (_, _) => await RunProjectToolAsync(false));
        utilities.DropDownItems.Add("Check source", null, (_, _) => CheckSource());
        utilities.DropDownItems.Add("Export vanilla Scratch...", null, async (_, _) => await RunProjectToolAsync(true));
        utilities.DropDownItems.Add("TurboWarp extensions...", null, (_, _) => ShowExtensions());
        utilities.DropDownItems.Add("Undo last asset change", null, (_, _) => UndoAssetChange());
        utilities.DropDownItems.Add(new ToolStripSeparator());
        utilities.DropDownItems.Add("Appearance...", null, (_, _) => ShowAppearance());
        utilities.DropDownItems.Add("Input / output paths", null, (_, _) => { if (_pathsPanel is not null) _pathsPanel.Visible = !_pathsPanel.Visible; });
        _ideTools.Items.Add(utilities);
        ToolStripButton theme = new("Dark mode") { CheckOnClick = true, Checked = true, Alignment = ToolStripItemAlignment.Right };
        theme.CheckedChanged += (_, _) => { _darkModeCheckBox.Checked = theme.Checked; DarkModeCheckBox_CheckedChanged(null, EventArgs.Empty); };
        _darkModeCheckBox.Checked = true;
        _ideTools.Items.Add(theme);
        try
        {
            if (_persistPreferences && File.Exists(PreferencesPath))
            {
                _preferences = JsonSerializer.Deserialize<ThemePreference>(File.ReadAllText(PreferencesPath)) ?? new(true);
                theme.Checked = _preferences.Dark;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        Control paths = CreatePathsPanel();
        _pathsPanel = paths;
        paths.Visible = false;
        paths.Dock = DockStyle.Top;
        paths.Height = 98;
        paths.Margin = Padding.Empty;
        _inputPathTextBox.AccessibleName = "Input file";
        _outputPathTextBox.AccessibleName = "Output project";
        _inputPathTextBox.Dock = DockStyle.Fill;
        _outputPathTextBox.Dock = DockStyle.Fill;
        SplitContainer workspace = new() { Dock = DockStyle.Fill, Size = new Size(1100, 600), SplitterDistance = 205, Panel1MinSize = 140, SplitterWidth = 5 };
        _workspace = workspace;
        workspace.Panel1.Controls.Add(_outline);
        workspace.Panel1.Controls.Add(new Label { Text = "OUTLINE", Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 8, 0, 0) });
        SplitContainer panels = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(880, 600), SplitterDistance = 440, Panel1MinSize = 140, Panel2MinSize = 80, SplitterWidth = 5 };
        panels.Panel1.Controls.Add(CreateEditorPanel());
        panels.Panel1.Controls.Add(_searchPanel);
        panels.Panel1.Controls.Add(_documentLabel);
        panels.Panel2.Controls.Add(CreateStatusPanel());
        _sourceEditor.AccessibleName = "ScratchASM source";
        _searchPanel.Controls.Add(_search);
        _searchPanel.Controls.Add(CreateButton("Next", (_, _) => FindNext(false)));
        _searchPanel.Controls.Add(CreateButton("Previous", (_, _) => FindNext(true)));
        _searchPanel.Controls.Add(_matchCase);
        _searchPanel.Controls.Add(CreateButton("Close", (_, _) => { _searchPanel.Visible = false; _sourceEditor.Focus(); }));
        _editorPanels = panels;
        Panel center = new() { Dock = DockStyle.Fill };
        center.Controls.Add(panels);
        center.Controls.Add(BuildGuidePanel());
        workspace.Panel2.Controls.Add(center);
        workspace.Panel2.Controls.Add(BuildInspector());
        StatusStrip status = new() { SizingGrip = false };
        _statusStrip = status;
        status.Items.Add(_activity);
        status.Items.Add(_caret);
        status.Items.Add(new ToolStripStatusLabel("ScratchASM  |  UTF-8") { Margin = new Padding(16, 0, 10, 0) });
        root.Controls.Add(workspace);
        root.Controls.Add(paths);
        _progress = new ActivityLine { Dock = DockStyle.Top };
        root.Controls.Add(_progress);
        root.Controls.Add(_ideTools);
        root.Controls.Add(status);
        _activity.AutoToolTip = true;
        Resize += (_, _) => { if (_inspector is not null) _inspector.Visible = Width >= 1100 && _inspectorWanted; };
        return root;
    }

    private void InitializeIde()
    {
        KeyPreview = true;
        AllowDrop = true;
        _sourceEditor.SelectionChanged += (_, _) =>
        {
            int line = _sourceEditor.GetLineFromCharIndex(_sourceEditor.SelectionStart);
            _caret.Text = $"Ln {line + 1}, Col {_sourceEditor.SelectionStart - Math.Max(0, _sourceEditor.GetFirstCharIndexFromLine(line)) + 1}";
        };
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(e.Shift); } };
        _inputPathTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadInputPreview(TrimPath(_inputPathTextBox.Text)); } };
        _outline.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is SourceLocation location)
            {
                _sourceEditor.Select(CtsSourcePosition.GetOffset(_sourceEditor.Text, location), 0);
                _sourceEditor.ScrollToCaret();
                _sourceEditor.Focus();
            }
        };
        _outline.ContextMenuStrip = new ContextMenuStrip();
        _outline.ContextMenuStrip.Items.Add("Go to source", null, (_, _) =>
        {
            if (_outline.SelectedNode?.Tag is SourceLocation location)
            { _sourceEditor.Select(CtsSourcePosition.GetOffset(_sourceEditor.Text, location), 0); _sourceEditor.ScrollToCaret(); _sourceEditor.Focus(); }
        });
        _outline.ContextMenuStrip.Items.Add("Collapse all", null, (_, _) => _outline.CollapseAll());
        _outline.ContextMenuStrip.Items.Add("Expand all", null, (_, _) => _outline.ExpandAll());
        DragEnter += (_, e) => e.Effect = !_isBusy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) LoadInputPreview(files[0]); };
        FormClosing += async (_, e) =>
        {
            if (_closeApproved) return;
            if (_isBusy) { e.Cancel = true; return; }
            if (!IsDirty) return;
            e.Cancel = true;
            if (await ConfirmUnsavedAsync() && !IsDisposed) { _closeApproved = true; BeginInvoke(Close); }
        };
        NewDocument();
    }

    private void NewDocument()
    {
        _performanceWarning = null;
        _checkRequested = false;
        _assetUndo = null;
        _loadedPath = null;
        _editSession = null;
        _editSessionPath = null;
        _packageOnly = false;
        _sourceEditor.ReadOnly = false;
        _inputPathTextBox.Clear();
        _outputPathTextBox.Text = Path.Combine(Directory.GetCurrentDirectory(), "project.sb3");
        ScratchProjectDocument starter = ScratchProjectDocument.Compile("stage {\n  var my_variable = \"\"\n\n  @greenflag:\n    my_variable = \"Hello World!\"\n}\n");
        starter.Project["targets"]![0]!["variables"]!.AsObject().First().Value![0] = "my variable";
        _editSession = starter.CreateSession();
        _editSessionPath = Path.Combine(Directory.GetCurrentDirectory(), "untitled.sasm");
        ReplaceEditorText(_editSession.SourceText);
        _sourceEditor.ResetHistory();
        _savedText = _sourceEditor.Text;
        UpdateDocumentTitle();
        if (Visible) _ = RefreshProjectAsync();
    }
    private async Task NewDocumentAsync() { if (!_isBusy && await ConfirmUnsavedAsync()) NewDocument(); }

    private async Task<bool> ConfirmUnsavedAsync()
    {
        if (!IsDirty) return true;
        DialogResult choice = MessageBox.Show(this, "Save changes to the current source?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return choice == DialogResult.No || choice == DialogResult.Yes && await SaveDocumentAsync(false);
    }
    private void UpdateDocumentTitle()
    {
        string name = _loadedPath is null ? "Untitled.sasm" : Path.GetFileName(_loadedPath);
        string dirty = IsDirty ? " *" : "";
        _documentLabel.Text = name + dirty + (_editSession is null ? "" : "   |   Imported project");
        Text = $"{name}{dirty} - ScratchASM";
    }
    private void UpdateOutline(IReadOnlyList<ScratchAsmSymbol> symbols)
    {
        _outline.BeginUpdate();
        try
        {
            _outline.Nodes.Clear();
            Dictionary<string, TreeNode> targets = new(StringComparer.Ordinal);
            foreach (ScratchAsmSymbol symbol in symbols)
            {
                TreeNode node = new(symbol.Name) { Tag = symbol.Range.Start };
                if (symbol.Kind == ScratchAsmSymbolKind.Target) { targets[symbol.Name] = node; node.ForeColor = _theme.Success; _outline.Nodes.Add(node); }
                else if (symbol.Container is not null && targets.TryGetValue(symbol.Container, out TreeNode? parent)) parent.Nodes.Add(node);
                else _outline.Nodes.Add(node);
            }
            _outline.ExpandAll();
        }
        finally { _outline.EndUpdate(); }
    }
    private void ShowFind() { _searchPanel.Visible = true; _search.Focus(); _search.SelectAll(); }
    private void FindNext(bool previous)
    {
        if (_search.Text.Length == 0 || _sourceEditor.TextLength == 0) return;
        string text = _sourceEditor.Text;
        StringComparison comparison = _matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int position;
        if (previous)
        {
            int start = _sourceEditor.SelectionStart - 1;
            position = start >= 0 ? text.LastIndexOf(_search.Text, start, comparison) : -1;
            if (position < 0) position = text.LastIndexOf(_search.Text, comparison);
        }
        else
        {
            position = text.IndexOf(_search.Text, _sourceEditor.SelectionStart + _sourceEditor.SelectionLength, comparison);
            if (position < 0) position = text.IndexOf(_search.Text, comparison);
        }
        if (position < 0) { _activity.Text = "No matches."; return; }
        _sourceEditor.Select(position, _search.Text.Length);
        _sourceEditor.ScrollToCaret();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_isBusy)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: _ = NewDocumentAsync(); return true;
                case Keys.Control | Keys.O: BrowseInputFile(null, EventArgs.Empty); return true;
                case Keys.Control | Keys.S: _ = SaveDocumentAsync(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: _ = SaveDocumentAsync(true); return true;
                case Keys.Control | Keys.F: ShowFind(); return true;
                case Keys.F3: FindNext(false); return true;
                case Keys.Shift | Keys.F3: FindNext(true); return true;
                case Keys.F5: _ = RunConversionAsync(false); return true;
                case Keys.F1: ShowGuide(); return true;
                case Keys.Escape: _searchPanel.Visible = false; _sourceEditor.Focus(); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private static bool SameDocument(string path, string? other)
    {
        try { return other is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    private static bool IsDocumentError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException or FormatException or OverflowException or System.Xml.XmlException or System.Runtime.InteropServices.ExternalException or NAudio.MmException;
    private static Bitmap MakeGlyph(string glyph)
    {
        Bitmap image = new(20, 20);
        using Graphics graphics = Graphics.FromImage(image);
        using Font font = new("Segoe Fluent Icons", 11F);
        TextRenderer.DrawText(graphics, glyph, font, new Rectangle(0, 0, 20, 20), Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return image;
    }
    private void SaveThemePreference()
    {
        if (!_persistPreferences) return;
        try { ScratchProjectEditSession.WriteSourceFile(PreferencesPath, JsonSerializer.Serialize(_preferences with { Dark = _theme.IsDark }), true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _activity.Text = "Theme preference could not be saved."; }
    }
    private ThemePreference _preferences = new(true);
    private sealed record ThemePreference(bool Dark, string? Accent = null, string? Background = null,
        string? Surface = null, string? Editor = null, string? Foreground = null, float FontSize = 11, bool Animations = true);
}
