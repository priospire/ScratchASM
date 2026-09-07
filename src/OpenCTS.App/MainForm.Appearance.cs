using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.App;

public sealed partial class MainForm
{
    private bool _titleThemeApplied;
    private UiTheme CreateCustomTheme()
    {
        UiTheme basis = _darkModeCheckBox.Checked ? UiTheme.Dark : UiTheme.Light;
        static Color ColorOr(string? color, Color fallback)
        {
            try { return color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? ColorTranslator.FromHtml(color) : fallback; }
            catch (ArgumentException) { return fallback; }
        }
        return basis with
        {
            Accent = ColorOr(_preferences.Accent, basis.Accent),
            Background = ColorOr(_preferences.Background, basis.Background),
            Surface = ColorOr(_preferences.Surface, basis.Surface),
            EditorBackground = ColorOr(_preferences.Editor, basis.EditorBackground),
            InputBackground = ColorOr(_preferences.Editor, basis.InputBackground),
            StatusBackground = ColorOr(_preferences.Surface, basis.StatusBackground),
            Text = ColorOr(_preferences.Foreground, basis.Text),
            EditorText = ColorOr(_preferences.Foreground, basis.EditorText)
        };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _titleThemeApplied = NativeTheme.Title(this, _theme.IsDark, _theme.Surface, _theme.Text);
    }
    protected override void OnShown(EventArgs e)
    {
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        base.OnShown(e);
    }
    private void ApplyWorkspaceTheme()
    {
        _titleThemeApplied = NativeTheme.Title(this, _theme.IsDark, _theme.Surface, _theme.Text);
        var renderer = new WorkspaceRenderer(_theme.Surface, _theme.Text, _theme.Accent, _theme.InputBackground);
        void Menus(ToolStrip strip)
        {
            strip.Renderer = renderer;
            strip.BackColor = _theme.Surface;
            strip.ForeColor = _theme.Text;
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = _theme.Text;
                item.BackColor = _theme.Surface;
                if (item is ToolStripDropDownItem dropdown) Menus(dropdown.DropDown);
                if (item is ToolStripControlHost host)
                {
                    host.Control.BackColor = _theme.InputBackground;
                    host.Control.ForeColor = _theme.Text;
                    NativeTheme.Apply(host.Control, _theme.IsDark);
                }
            }
        }
        void Walk(Control parent)
        {
            NativeTheme.Apply(parent, _theme.IsDark);
            if (parent.ContextMenuStrip is not null) Menus(parent.ContextMenuStrip);
            foreach (Control child in parent.Controls)
            {
                if (child is ToolStrip tools) Menus(tools);
                else if (child is ListBox or TreeView or ComboBox)
                { child.BackColor = _theme.Surface; child.ForeColor = _theme.Text; }
                else if (child is RichTextBox)
                { child.BackColor = _theme.EditorBackground; child.ForeColor = _theme.Text; }
                else if (child is SplitContainer) child.BackColor = _theme.Background;
                Walk(child);
            }
        }
        Walk(this);
        _guideText.BackColor = _theme.EditorBackground;
        _guideText.ForeColor = _theme.Text;
        if (_guideName is not null) LoadGuide(_guideName);
        if (_statusStrip is not null) { _statusStrip.Renderer = renderer; _statusStrip.BackColor = _theme.Surface; }
        if (_preview is not null) _preview.BackColor = _theme.Background;
        if (_progress is not null) { _progress.BackColor = _theme.Background; _progress.ForeColor = _theme.Accent; }
        float size = Math.Clamp(_preferences.FontSize, 9, 22);
        if (_sourceEditor.Font.Size != size) { Font old = _sourceEditor.Font; _sourceEditor.Font = CreateMonoFont(size); old.Dispose(); }
        _documentLabel.ForeColor = _theme.Accent;
    }
    private void ShowAppearance()
    {
        using Form dialog = new() { Text = "Appearance", Size = new Size(420, 456), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = _theme.Surface, ForeColor = _theme.Text, Padding = new Padding(18), Font = Font };
        TableLayoutPanel grid = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (int i = 0; i < 9; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        ThemePreference edited = _preferences;
        void Swatch(int row, string label, Color initial, Action<string> set)
        {
            grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            Button button = new() { BackColor = initial, Dock = DockStyle.Right, Width = 84, FlatStyle = FlatStyle.Flat, AccessibleName = label };
            button.Click += (_, _) =>
            {
                using ColorDialog picker = new() { Color = button.BackColor, FullOpen = true };
                if (picker.ShowDialog(dialog) == DialogResult.OK) { button.BackColor = picker.Color; set(ColorTranslator.ToHtml(picker.Color)); }
            };
            grid.Controls.Add(button, 1, row);
        }
        Swatch(0, "Accent", _theme.Accent, color => edited = edited with { Accent = color });
        Swatch(1, "Window background", _theme.Background, color => edited = edited with { Background = color });
        Swatch(2, "Panels and bars", _theme.Surface, color => edited = edited with { Surface = color });
        Swatch(3, "Editor background", _theme.EditorBackground, color => edited = edited with { Editor = color });
        Swatch(4, "UI text", _theme.Text, color => edited = edited with { Foreground = color });
        grid.Controls.Add(new Label { Text = "Editor font size", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        NumericUpDown font = new() { Minimum = 9, Maximum = 22, Value = (decimal)Math.Clamp(edited.FontSize, 9, 22), Dock = DockStyle.Fill, BackColor = _theme.InputBackground, ForeColor = _theme.Text };
        grid.Controls.Add(font, 1, 5);
        CheckBox animations = new() { Text = "Animations", Checked = edited.Animations, Dock = DockStyle.Fill };
        grid.Controls.Add(animations, 0, 6); grid.SetColumnSpan(animations, 2);
        Button reset = new() { Text = "Reset colors", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        reset.Click += (_, _) => { edited = new ThemePreference(_theme.IsDark); dialog.DialogResult = DialogResult.OK; };
        grid.Controls.Add(reset, 0, 7);
        Button apply = new() { Text = "Apply", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, BackColor = _theme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        apply.Click += (_, _) => edited = edited with { FontSize = (float)font.Value, Animations = animations.Checked };
        grid.Controls.Add(apply, 1, 7);
        dialog.Controls.Add(grid); dialog.AcceptButton = apply;
        dialog.Shown += (_, _) => NativeTheme.Title(dialog, _theme.IsDark, _theme.Surface, _theme.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        { _preferences = edited; ApplyTheme(); ApplyScratchAsmHighlighting(); SaveThemePreference(); }
    }
    private void ShowExtensions()
    {
        using Form dialog = new() { Text = "TurboWarp extensions", Size = new Size(640, 560), StartPosition = FormStartPosition.CenterParent,
            BackColor = _theme.Surface, ForeColor = _theme.Text, Padding = new Padding(14), Font = Font };
        ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, BackColor = _theme.EditorBackground, ForeColor = _theme.Text };
        TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search extensions", BackColor = _theme.InputBackground, ForeColor = _theme.Text };
        Label warning = new() { Text = "TurboWarp extensions do not run in vanilla Scratch.", ForeColor = _theme.Warning, Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
        Button add = new() { Text = "Add declaration", Dock = DockStyle.Bottom, Height = 34, BackColor = _theme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var entries = TurboWarpExtensionCatalog.Entries;
        void Filter()
        {
            list.Items.Clear();
            list.Items.AddRange(entries.Where(entry => (entry.Name + " " + entry.Id).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        add.Click += (_, _) =>
        {
            if (_isBusy || list.SelectedItem is not TurboWarpExtension entry) return;
            CtsParseResult parse = CtsParser.Parse(_sourceEditor.Text);
            CtsTargetDeclaration? stage = parse.CompilationUnit.Targets.FirstOrDefault(target => target.IsStage);
            if (stage is null) return;
            int start = CtsSourcePosition.GetOffset(_sourceEditor.Text, stage.Span.Start);
            int brace = _sourceEditor.Text.IndexOf('{', start);
            if (brace < 0) return;
            _sourceEditor.Select(brace + 1, 0);
            _sourceEditor.SelectedText = $"\n  extension {JsonSerializer.Serialize(entry.Id)} {JsonSerializer.Serialize(entry.Url)} {JsonSerializer.Serialize(entry.Color)}\n";
            dialog.Close();
        };
        dialog.Controls.Add(list); dialog.Controls.Add(search); dialog.Controls.Add(warning); dialog.Controls.Add(add);
        dialog.Shown += (_, _) => { NativeTheme.Title(dialog, _theme.IsDark, _theme.Surface, _theme.Text); NativeTheme.Apply(list, _theme.IsDark); };
        dialog.ShowDialog(this);
    }
}
