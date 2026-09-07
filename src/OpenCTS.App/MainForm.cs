using OpenCTS.Core;

namespace OpenCTS.App;

public sealed partial class MainForm : Form
{
    private readonly ScratchProjectConverter _converter = new();
    private readonly TextBox _inputPathTextBox = new();
    private readonly TextBox _outputPathTextBox = new();
    private readonly CodeEditor _sourceEditor = new();
    private readonly RichTextBox _statusTextBox = new();
    private readonly CheckBox _attemptRepairCheckBox = new();
    private readonly CheckBox _darkModeCheckBox = new();
    private readonly System.Windows.Forms.Timer _diagnosticsTimer = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _syntaxTimer = new() { Interval = 60 };
    private readonly List<DiagnosticDisplaySpan> _displayedDiagnostics = [];

    private UiTheme _theme = UiTheme.Dark;
    private ScratchProjectEditSession? _editSession;
    private string? _editSessionPath;
    private bool _isApplyingHighlight;
    private bool _isBusy;
    private int _diagnosticsVersion;
    private int _fullyColoredVersion = -1;

    public MainForm(string? initialPath = null, bool persistPreferences = true)
    {
        _persistPreferences = persistPreferences;
        Text = "ScratchASM IDE";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1240, 820);
        Font = new Font("Segoe UI", 9F);

        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;
        _syntaxTimer.Tick += (_, _) => { _syntaxTimer.Stop(); HighlightVisibleSource(); };
        _sourceEditor.ViewportChanged += (_, _) => { if (_fullyColoredVersion != _diagnosticsVersion) _syntaxTimer.Start(); };
        Controls.Add(BuildLayout());
        InitializeIde();
        ApplyTheme();
        SetStatus("Ready.", _theme.Muted);
        if (initialPath is null) Shown += async (_, _) => await RefreshProjectAsync();
        if (initialPath is not null) Shown += (_, _) =>
        {
            _inputPathTextBox.Text = initialPath;
            SetDefaultOutputPath(initialPath);
            LoadInputPreview(initialPath);
        };
    }

    private Control BuildLayout() => BuildIdeLayout();

    private Control CreatePathsPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(12));
        TableLayoutPanel paths = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        paths.Controls.Add(CreateInputRow(), 0, 0);
        paths.Controls.Add(CreateOutputRow(), 0, 1);
        shell.Controls.Add(paths);
        return shell;
    }

    private Control CreateInputRow()
    {
        TableLayoutPanel row = CreatePathRow("Input");
        _inputPathTextBox.PlaceholderText = ".sasm, .mono, .sb3, project.json, or project folder";
        _inputPathTextBox.Leave += InputPathTextBox_Leave;
        row.Controls.Add(_inputPathTextBox, 1, 0);
        row.Controls.Add(CreateButton("File", BrowseInputFile), 2, 0);
        row.Controls.Add(CreateButton("Folder", BrowseInputFolder), 3, 0);
        return row;
    }

    private Control CreateOutputRow()
    {
        TableLayoutPanel row = CreatePathRow("Output");
        _outputPathTextBox.PlaceholderText = "Output Scratch project path";
        row.Controls.Add(_outputPathTextBox, 1, 0);
        row.Controls.Add(CreateButton("Save As", BrowseOutputFile), 2, 0);
        _attemptRepairCheckBox.Text = "Repair";
        _attemptRepairCheckBox.AutoSize = true;
        _attemptRepairCheckBox.Margin = new Padding(10, 8, 0, 0);
        row.Controls.Add(_attemptRepairCheckBox, 3, 0);
        return row;
    }

    private Control CreateEditorPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(0));
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreatePanelHeader("ScratchASM Source"), 0, 0);

        _sourceEditor.Dock = DockStyle.Fill;
        _sourceEditor.BorderStyle = BorderStyle.None;
        _sourceEditor.AcceptsTab = true;
        _sourceEditor.WordWrap = false;
        _sourceEditor.DetectUrls = false;
        _sourceEditor.Font = CreateMonoFont(10F);
        _sourceEditor.TextChanged += SourceEditor_TextChanged;
        Panel body = new() { Dock = DockStyle.Fill };
        body.Controls.Add(_sourceEditor);
        body.Controls.Add(new LineNumberMargin(_sourceEditor) { Dock = DockStyle.Left, Width = 58 });
        panel.Controls.Add(body, 0, 1);
        shell.Controls.Add(panel);
        return shell;
    }

    private Control CreateStatusPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(0));
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreatePanelHeader("Diagnostics"), 0, 0);

        _statusTextBox.Dock = DockStyle.Fill;
        _statusTextBox.BorderStyle = BorderStyle.None;
        _statusTextBox.Multiline = true;
        _statusTextBox.ReadOnly = true;
        _statusTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _statusTextBox.Font = CreateMonoFont(9F);
        _statusTextBox.WordWrap = false;
        _statusTextBox.MouseDoubleClick += StatusTextBox_MouseDoubleClick;
        panel.Controls.Add(_statusTextBox, 0, 1);
        shell.Controls.Add(panel);
        return shell;
    }

    private static TableLayoutPanel CreatePathRow(string labelText)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label label = new()
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
        };
        row.Controls.Add(label, 0, 0);
        return row;
    }

    private Panel CreateSurfacePanel(Padding padding)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = padding,
            Margin = new Padding(0, 0, 0, 10),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private Label CreatePanelHeader(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
        };
    }

    private static Button CreateButton(string text, EventHandler clickHandler)
    {
        Button button = new()
        {
            Text = text,
            MinimumSize = new Size(84, 30),
            Margin = new Padding(8, 0, 0, 0)
        };
        button.Click += clickHandler;
        return button;
    }

    private void BrowseInputFile(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select ScratchASM or Scratch input",
            Filter = "ScratchASM and Scratch (*.sasm;*.mono;*.cts;*.sb3;*.json)|*.sasm;*.mono;*.cts;*.sb3;*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadInputPreview(dialog.FileName);
        }
    }

    private void BrowseInputFolder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select a folder containing project.json"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadInputPreview(dialog.SelectedPath);
        }
    }

    private void BrowseOutputFile(object? sender, EventArgs e)
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Save Scratch project",
            Filter = "Scratch 3 project (*.sb3)|*.sb3|All files (*.*)|*.*",
            DefaultExt = "sb3",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            dialog.FileName = TrimPath(_outputPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputPathTextBox.Text = dialog.FileName;
        }
    }

    private async void ConvertButton_Click(object? sender, EventArgs e)
    {
        await RunConversionAsync(forceRepair: false);
    }

    private async void RepairButton_Click(object? sender, EventArgs e)
    {
        await RunConversionAsync(forceRepair: true);
    }

    private void SaveSourceButton_Click(object? sender, EventArgs e) => SaveDocument(false);

    private bool SaveDocument(bool saveAs)
    {
        if (_isBusy || _sourceEditor.ReadOnly) return false;
        string? path = IsScratchAsmPath(_loadedPath ?? "") ? _loadedPath : null;
        if (saveAs || path is null)
        {
            using SaveFileDialog dialog = new()
            {
                Title = "Save ScratchASM source", Filter = "ScratchASM source (*.sasm)|*.sasm",
                DefaultExt = "sasm", AddExtension = true, OverwritePrompt = true,
                FileName = Path.ChangeExtension(_loadedPath ?? "project", ".sasm")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            if (_editSession is not null)
            {
                ConversionResult result = _editSession.SaveSource(_sourceEditor.Text, path, true);
                if (!result.Success) { ShowIssues(result.Issues, "Source save failed.", _theme.Error); return false; }
                ReplaceEditorText(File.ReadAllText(path));
            }
            else ScratchProjectEditSession.WriteSourceFile(path, _sourceEditor.Text, true);
            _loadedPath = Path.GetFullPath(path);
            _editSessionPath = _editSession is null ? null : _loadedPath;
            _inputPathTextBox.Text = _loadedPath;
            _savedText = _sourceEditor.Text;
            UpdateDocumentTitle();
            SetStatus($"Saved {path}", _theme.Success);
            return true;
        }
        catch (Exception ex) when (IsDocumentError(ex)) { SetStatus(ex.Message, _theme.Error); return false; }
    }

    private async Task RunConversionAsync(bool forceRepair)
    {
        if (_isBusy) return;
        string requested = TrimPath(_inputPathTextBox.Text);
        if (requested.Length > 0 && !SameDocument(requested, _loadedPath) && !await LoadDocumentAsync(requested)) return;
        string inputPath = _loadedPath ?? Path.Combine(Directory.GetCurrentDirectory(), "untitled.sasm");
        string outputPath = TrimPath(_outputPathTextBox.Text);
        if (File.Exists(outputPath) && MessageBox.Show(this, "Replace the existing output project?", "Export project",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        bool attemptRepair = forceRepair || _attemptRepairCheckBox.Checked;

        SetBusy(true);
        _diagnosticsTimer.Stop();
        _diagnosticsVersion++;
        SetStatus(forceRepair ? "Repairing..." : "Compiling...", _theme.Muted);

        try
        {
            List<ValidationIssue> prefixIssues = [];
            ConversionResult result;
            if (CanMergeEditedSb3(inputPath))
            {
                string source = _sourceEditor.Text;
                if (attemptRepair)
                {
                    ScratchAsmSourceRepairResult repair = ScratchAsmSourceRepairer.Repair(source);
                    source = repair.SourceText;
                    prefixIssues.AddRange(repair.Issues);
                    ReplaceEditorText(source);
                }

                ScratchProjectEditSession session = _editSession!;
                result = await Task.Run(() => session.WriteEdited(source, outputPath, overwrite: true));
            }
            else
            {
                string? sourceOverride = IsScratchAsmPath(inputPath) ? _sourceEditor.Text : null;
                if (attemptRepair && sourceOverride is not null)
                {
                    ScratchAsmSourceRepairResult repair = ScratchAsmSourceRepairer.Repair(sourceOverride);
                    sourceOverride = repair.SourceText;
                    prefixIssues.AddRange(repair.Issues);
                    ReplaceEditorText(sourceOverride);
                }

                result = await Task.Run(() => _converter.ConvertToSb3(inputPath, outputPath, new ConversionOptions
                {
                    AttemptSafeRepair = attemptRepair,
                    ScratchAsmSourceText = sourceOverride,
                    Overwrite = true
                }));
            }

            ShowConversionResult(result, prefixIssues);
        }
        catch (Exception ex) when (IsDocumentError(ex)) { SetStatus(ex.Message, _theme.Error); }
        finally
        {
            SetBusy(false);
        }
    }

    private bool CanMergeEditedSb3(string inputPath)
    {
        return _editSession is not null &&
            _editSessionPath is not null &&
            string.Equals(Path.GetFullPath(inputPath), _editSessionPath, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowConversionResult(ConversionResult result, IReadOnlyList<ValidationIssue> prefixIssues)
    {
        List<ValidationIssue> issues = [.. prefixIssues, .. result.Issues];
        if (result.Success)
        {
            string text = $"Wrote {result.OutputPath}";
            if (issues.Count == 0)
            {
                SetStatus(text, _theme.Success);
                return;
            }

            ShowIssues(issues, text, _theme.Success);
            return;
        }

        ShowIssues(issues, "Build failed.", _theme.Error);
    }

    private void SetDefaultOutputPath(string inputPath)
    {
        if (!string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            return;
        }

        try
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string outputDirectory;
            string outputName;

            if (Directory.Exists(fullInputPath))
            {
                outputDirectory = Directory.GetParent(fullInputPath)?.FullName ?? fullInputPath;
                outputName = Path.GetFileName(fullInputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            else
            {
                outputDirectory = Path.GetDirectoryName(fullInputPath) ?? Directory.GetCurrentDirectory();
                outputName = string.Equals(Path.GetFileName(fullInputPath), "project.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(outputDirectory)
                    : Path.GetFileNameWithoutExtension(fullInputPath);
            }

            if (string.IsNullOrWhiteSpace(outputName))
            {
                outputName = "project";
            }

            _outputPathTextBox.Text = Path.Combine(outputDirectory, outputName +
                (Path.GetExtension(fullInputPath).Equals(".sb3", StringComparison.OrdinalIgnoreCase) ? ".edited.sb3" : ".sb3"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus(ex.Message, _theme.Error);
        }
    }

    private void InputPathTextBox_Leave(object? sender, EventArgs e)
    {
        string path = TrimPath(_inputPathTextBox.Text);
        if (!_isBusy && path.Length > 0 && !SameDocument(path, _loadedPath)) LoadInputPreview(path);
    }

    private async void LoadInputPreview(string inputPath) => await LoadDocumentAsync(inputPath);

    private async Task<bool> LoadDocumentAsync(string inputPath)
    {
        if (_isBusy || SameDocument(inputPath, _loadedPath)) return !_isBusy;
        if (!ConfirmUnsaved()) { _inputPathTextBox.Text = _loadedPath ?? ""; return false; }
        SetBusy(true);
        _diagnosticsVersion++;
        _diagnosticsTimer.Stop();
        try
        {
            string fullPath = Path.GetFullPath(TrimPath(inputPath));
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) throw new FileNotFoundException($"Input was not found: {fullPath}");
            string text = "";
            ScratchProjectEditSession? session = null;
            List<ValidationIssue> issues = [];
            if (IsScratchAsmPath(fullPath))
            {
                text = await File.ReadAllTextAsync(fullPath);
                try { session = await Task.Run(() => ScratchProjectEditSession.OpenSourceCompanion(text, fullPath)); }
                catch (Exception ex) when (IsDocumentError(ex)) { issues.Add(new ValidationIssue(ex.Message, "$", null)); }
            }
            else if (Path.GetExtension(fullPath).Equals(".sb3", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    session = await Task.Run(() => ScratchProjectEditSession.Open(fullPath));
                    text = session.SourceText;
                    issues.AddRange(session.Issues);
                }
                catch (Exception ex) when (IsDocumentError(ex)) { issues.Add(new ValidationIssue(ex.Message, "$", null)); }
            }
            if (IsDisposed) return false;
            _loadedPath = fullPath;
            _assetUndo = null;
            _inputPathTextBox.Text = fullPath;
            _editSession = session;
            _editSessionPath = session is null ? null : fullPath;
            _packageOnly = !IsScratchAsmPath(fullPath) && session is not { CanEdit: true };
            ReplaceEditorText(text);
            _sourceEditor.ResetHistory();
            _savedText = text;
            _outputPathTextBox.Clear();
            SetDefaultOutputPath(fullPath);
            _outline.Nodes.Clear();
            UpdateDocumentTitle();
            if (issues.Count > 0) ShowIssues(issues, "Input diagnostics", _theme.Warning);
            else SetStatus($"Opened {Path.GetFileName(fullPath)}", _theme.Success);
            _ = RefreshProjectAsync();
            return true;
        }
        catch (Exception ex) when (IsDocumentError(ex))
        {
            _inputPathTextBox.Text = _loadedPath ?? "";
            SetStatus(ex.Message, _theme.Error);
            return false;
        }
        finally { if (!IsDisposed) { SetBusy(false); if (!_packageOnly) ScheduleDiagnostics(); } }
    }

    private void ReplaceEditorText(string text)
    {
        _isApplyingHighlight = true;
        try
        {
            _sourceEditor.LoadSourceText(text);
        }
        finally
        {
            _isApplyingHighlight = false;
        }

        UpdateDocumentTitle();
        ScheduleDiagnostics();
    }

    private void SourceEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isApplyingHighlight)
        {
            return;
        }

        UpdateDocumentTitle();
        ScheduleDiagnostics();
    }

    private void ScheduleDiagnostics()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsVersion++;
        _syntaxTimer.Stop();
        _syntaxTimer.Start();


        if (!_isBusy)
        {
            _diagnosticsTimer.Start();
        }
    }

    private async void DiagnosticsTimer_Tick(object? sender, EventArgs e)
    {
        _diagnosticsTimer.Stop();
        if (_isBusy || _packageOnly) return;
        if (_analysisRunning) { _diagnosticsTimer.Start(); return; }
        _analysisRunning = true;
        int version = _diagnosticsVersion;
        string source = _sourceEditor.Text;
        string name = IsScratchAsmPath(_loadedPath ?? "") ? _loadedPath! : "editor.sasm";
        try
        {
            var colors = await Task.Run(() => CtsSyntaxClassifier.Classify(source));
            if (IsDisposed || version != _diagnosticsVersion || _isBusy) return;
            _sourceEditor.ApplyColors(colors, _theme.EditorText, _theme.Muted, _theme.IsDark);
            _fullyColoredVersion = version;
            _activity.Text = "Checking source...";
            var analysis = await Task.Run(() => new OpenCTS.LanguageServices.DocumentAnalyzer().Analyze(source, name, includeColors: false));
            if (IsDisposed || version != _diagnosticsVersion || _isBusy) return;
            UpdateOutline(analysis.Symbols);
            _sourceEditor.SetCompletionSymbols(analysis.Symbols);
            ShowDiagnostics(analysis.Diagnostics.Select(item => new CtsDiagnostic(item.Code,
                item.Severity switch { "error" => DiagnosticSeverity.Error, "warning" => DiagnosticSeverity.Warning, _ => DiagnosticSeverity.Info }, item.Message,
                new SourceSpan(item.Range.Start, item.Range.End))).ToArray());
        }
        catch (Exception ex) when (IsDocumentError(ex)) { if (!IsDisposed) SetStatus(ex.Message, _theme.Error); }
        finally { _analysisRunning = false; if (!IsDisposed && version != _diagnosticsVersion && !_isBusy) ScheduleDiagnostics(); }
    }

    private void ShowDiagnostics(IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        _sourceEditor.ApplyDiagnostics(diagnostics);
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        if (diagnostics.Count == 0)
        {
            AppendStatusLine("No ScratchASM diagnostics.", _theme.Success, null);
            return;
        }

        foreach (CtsDiagnostic diagnostic in diagnostics.Take(200))
        {
            Color color = DiagnosticColor(diagnostic.Severity);
            AppendStatusLine(FormatDiagnostic(diagnostic), color, diagnostic.Span);
        }

        _statusTextBox.Select(0, 0);
        if (diagnostics.Count > 200) AppendStatusLine($"{diagnostics.Count - 200} more diagnostics. Fix the first errors and check again.", _theme.Muted, null);
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues, string header, Color headerColor)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        AppendStatusLine(header, headerColor, null);
        foreach (ValidationIssue issue in issues)
        {
            Color color = DiagnosticColor(issue.Severity);
            AppendStatusLine(Program.FormatIssue(issue), color, issue.Span);
        }

        _statusTextBox.Select(0, 0);
    }

    private void SetStatus(string text, Color color)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        AppendStatusLine(text, color, null);
    }

    private void AppendStatusLine(string text, Color color, SourceSpan? sourceSpan)
    {
        int start = _statusTextBox.TextLength;
        _statusTextBox.SelectionColor = color;
        _statusTextBox.AppendText(text);
        _activity.Text = text.Length > 160 ? text[..160] : text;
        if (sourceSpan is not null)
        {
            _displayedDiagnostics.Add(new DiagnosticDisplaySpan(start, text.Length, sourceSpan));
        }

        _statusTextBox.SelectionColor = _theme.Text;
        _statusTextBox.AppendText(Environment.NewLine);
    }

    private static string FormatDiagnostic(CtsDiagnostic diagnostic)
    {
        return $"{diagnostic.Severity} {diagnostic.Code} line {diagnostic.Span.Start.Line}, column {diagnostic.Span.Start.Column}: {diagnostic.Message}";
    }

    private Color DiagnosticColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => _theme.Error,
        DiagnosticSeverity.Warning => _theme.Warning,
        _ => Color.FromArgb(80, 180, 235)
    };

    private void StatusTextBox_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        int characterIndex = _statusTextBox.GetCharIndexFromPosition(e.Location);
        DiagnosticDisplaySpan? display = _displayedDiagnostics.FirstOrDefault(candidate =>
            characterIndex >= candidate.Start && characterIndex < candidate.Start + candidate.Length);
        if (display is null)
        {
            return;
        }

        int start = CtsSourcePosition.GetOffset(_sourceEditor.Text, display.Span.Start);
        int end = CtsSourcePosition.GetOffset(_sourceEditor.Text, display.Span.End);
        _sourceEditor.Select(start, Math.Max(0, end - start));
        _sourceEditor.ScrollToCaret();
        _sourceEditor.Focus();
    }

    private void HighlightVisibleSource()
    {
        if (_isBusy || _packageOnly || !_sourceEditor.IsHandleCreated || _fullyColoredVersion == _diagnosticsVersion) return;
        (int start, int length) = _sourceEditor.VisibleRange();
        string text = _sourceEditor.Text;
        length = Math.Min(length, 20000);
        if (start + length > text.Length) return;
        var colors = CtsSyntaxClassifier.Classify(text.Substring(start, length))
            .Select(span => span with { Start = span.Start + start }).ToArray();
        _sourceEditor.ApplyColors(colors, _theme.EditorText, _theme.Muted, _theme.IsDark);
    }
    private void ApplyScratchAsmHighlighting() => ScheduleDiagnostics();

    private void DarkModeCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _theme = _darkModeCheckBox.Checked ? UiTheme.Dark : UiTheme.Light;
        ApplyTheme();
        ApplyScratchAsmHighlighting();
        SaveThemePreference();
    }

    private void ApplyTheme()
    {
        _theme = CreateCustomTheme();
        BackColor = _theme.Background;
        ForeColor = _theme.Text;
        ApplyThemeToControl(this);
        _sourceEditor.BackColor = _theme.EditorBackground;
        _sourceEditor.ForeColor = _theme.EditorText;
        _statusTextBox.BackColor = _theme.StatusBackground;
        _statusTextBox.ForeColor = _theme.Text;
        _outline.BackColor = _theme.Surface;
        _outline.ForeColor = _theme.Text;
        _sourceEditor.GutterColor = _theme.Muted;
        _documentLabel.ForeColor = _theme.Success;
        if (_ideTools is not null) _ideTools.BackColor = _theme.Surface;
        ApplyWorkspaceTheme();
    }

    private void ApplyThemeToControl(Control control)
    {
        foreach (Control child in control.Controls)
        {
            child.ForeColor = _theme.Text;
            switch (child)
            {
                case TableLayoutPanel table:
                    table.BackColor = _theme.Background;
                    break;
                case Panel panel:
                    panel.BackColor = _theme.Surface;
                    break;
                case Label label:
                    label.BackColor = Color.Transparent;
                    break;
                case Button button:
                    button.BackColor = _theme.Accent;
                    button.ForeColor = Color.White;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = _theme.AccentDark;
                    button.FlatAppearance.BorderSize = 1;
                    button.Cursor = Cursors.Hand;
                    break;
                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = _theme.Text;
                    break;
                case TextBox textBox:
                    textBox.BackColor = _theme.InputBackground;
                    textBox.ForeColor = _theme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
            }

            ApplyThemeToControl(child);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _sourceEditor.ReadOnly = busy || _packageOnly;
        _inputPathTextBox.Enabled = !busy;
        _outputPathTextBox.Enabled = !busy;
        if (_ideTools is not null) _ideTools.Enabled = !busy;
        if (_inspector is not null) _inspector.Enabled = !busy;
        UseWaitCursor = busy;
        if (_progress is not null) _progress.Active = busy && _preferences.Animations;
        _attemptRepairCheckBox.Enabled = !busy;
    }

    private static string TrimPath(string path)
    {
        return path.Trim().Trim('"');
    }

    private static bool IsScratchAsmPath(string path)
    {
        return ScratchAsmLanguage.IsSupportedSourceName(path);
    }

    private static Font CreateMonoFont(float size)
    {
        try
        {
            return new Font("Cascadia Mono", size);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericMonospace, size);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diagnosticsTimer.Dispose();
            _syntaxTimer.Dispose();
            _soundOutput?.Dispose();
            _soundReader?.Dispose();
            if (_ideTools is not null)
                foreach (ToolStripItem item in _ideTools.Items) item.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record DiagnosticDisplaySpan(int Start, int Length, SourceSpan Span);

    private sealed record UiTheme(
        bool IsDark,
        Color Background,
        Color Surface,
        Color InputBackground,
        Color EditorBackground,
        Color StatusBackground,
        Color Text,
        Color EditorText,
        Color Muted,
        Color Accent,
        Color AccentDark,
        Color Success,
        Color Warning,
        Color Error)
    {
        public static UiTheme Dark { get; } = new(
            true,
            Color.FromArgb(12, 12, 14),
            Color.FromArgb(21, 21, 24),
            Color.FromArgb(17, 17, 20),
            Color.FromArgb(14, 14, 16),
            Color.FromArgb(16, 16, 19),
            Color.FromArgb(229, 234, 242),
            Color.FromArgb(229, 234, 242),
            Color.FromArgb(149, 160, 177),
            Color.FromArgb(0, 156, 168),
            Color.FromArgb(0, 116, 126),
            Color.FromArgb(34, 197, 94),
            Color.FromArgb(245, 158, 11),
            Color.FromArgb(239, 68, 68));

        public static UiTheme Light { get; } = new(
            false,
            Color.FromArgb(244, 247, 251),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(250, 252, 255),
            Color.FromArgb(24, 31, 42),
            Color.FromArgb(24, 31, 42),
            Color.FromArgb(96, 108, 124),
            Color.FromArgb(31, 117, 84),
            Color.FromArgb(26, 94, 69),
            Color.FromArgb(22, 163, 74),
            Color.FromArgb(202, 138, 4),
            Color.FromArgb(220, 38, 38));
    }
}
