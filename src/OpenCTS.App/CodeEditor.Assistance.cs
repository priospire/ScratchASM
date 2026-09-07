using OpenCTS.Core;
using OpenCTS.LanguageServices;

namespace OpenCTS.App;

public sealed partial class CodeEditor
{
    private readonly System.Windows.Forms.Timer _completionTimer = new() { Interval = 180 };
    private readonly ToolTip _diagnosticTip = new() { InitialDelay = 400, ReshowDelay = 200, AutoPopDelay = 10000 };
    private readonly ScratchAsmLanguageService _language = new();
    private IReadOnlyList<ScratchAsmSymbol> _completionSymbols = [];
    private readonly List<DiagnosticMark> _diagnosticMarks = [];
    private CompletionWindow? _completion;
    private int _completionStart;
    private int _completionEnd;
    private string? _hoverMessage;
    private static readonly Dictionary<string, string> LineTemplates = new(StringComparer.Ordinal)
    {
        ["stage"] = "stage {\n  |\n}",
        ["sprite"] = "sprite |Player {\n  \n}",
        ["var"] = "var |score = 0",
        ["list"] = "list |items = []",
        ["proc"] = "proc |calculate(value:num):\n  ",
        ["repeat"] = "repeat |10:\n  ",
        ["forever"] = "forever:\n  |",
        ["if"] = "if |true:\n  ",
        ["else"] = "else:\n  |",
        ["@greenflag"] = "@greenflag:\n  |",
        ["looks.say"] = "looks.say \"|Hello!\"",
        ["motion.move"] = "motion.move |10",
        ["control.wait"] = "control.wait |1"
    };

    private void InitializeAssistance()
    {
        _completionTimer.Tick += (_, _) => { _completionTimer.Stop(); ShowCompletion(false); };
        LostFocus += (_, _) => { _completionTimer.Stop(); HideCompletion(); _diagnosticTip.Hide(this); };
        VScroll += (_, _) => HideCompletion();
        HScroll += (_, _) => HideCompletion();
        Resize += (_, _) => HideCompletion();
    }

    public void SetCompletionSymbols(IReadOnlyList<ScratchAsmSymbol> symbols) => _completionSymbols = symbols;

    private void ScheduleCompletion()
    {
        _completionTimer.Stop();
        if (Focused && !ReadOnly && !_restoring) _completionTimer.Start();
    }

    private void HideCompletion() => _completion?.Hide();

    private void ShowCompletion(bool explicitRequest)
    {
        if (ReadOnly || !Focused || SelectionLength != 0) return;
        int caret = SelectionStart;
        int line = Math.Max(0, GetFirstCharIndexOfCurrentLine());
        if (caret > TextLength || line > caret) return;
        if (caret - line > 2000) return;
        string before = Text[line..caret];
        if (!InCode(before)) return;
        int start = caret;
        while (start > line && (char.IsLetterOrDigit(Text[start - 1]) || Text[start - 1] is '_' or '.' or '@')) start--;
        string prefix = Text[start..caret];
        if (!explicitRequest && prefix.Length < 2) { HideCompletion(); return; }
        bool wholeLine = Text[line..start].All(char.IsWhiteSpace) &&
            (caret == TextLength || Text[caret] is '\n' or '\r');
        var candidates = _language.GetCompletions(prefix, prefix.Length, _completionSymbols)
            .Where(item => !prefix.StartsWith('@') || item.Kind == "hat")
            .Select(item => new CompletionChoice(
                item.Kind == "hat" ? "@" + item.Label : item.Label,
                item.Kind == "hat" ? "@" + item.InsertText : item.InsertText, item.Detail, null)).ToList();
        foreach (var template in LineTemplates)
        {
            if (!template.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.RemoveAll(item => item.Label == template.Key);
            candidates.Add(new CompletionChoice(template.Key, template.Key, "line template", wholeLine ? template.Value : null));
        }
        var matches = candidates.OrderBy(item => item.Template is null).ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(60).ToArray();
        if (matches.Length == 0) { HideCompletion(); return; }
        _completionStart = start;
        _completionEnd = caret;
        _completion ??= new CompletionWindow(AcceptCompletion);
        _completion.SetChoices(matches, BackColor, ForeColor, _dark, Font);
        Point anchor = PointToScreen(GetPositionFromCharIndex(caret));
        Rectangle screen = Screen.FromPoint(anchor).WorkingArea;
        _completion.Location = new Point(Math.Clamp(anchor.X, screen.Left, Math.Max(screen.Left, screen.Right - _completion.Width)),
            anchor.Y + Font.Height + _completion.Height < screen.Bottom ? anchor.Y + Font.Height : Math.Max(screen.Top, anchor.Y - _completion.Height));
        if (!_completion.Visible) _completion.Show(FindForm());
    }

    private bool HandleCompletionKey(Keys key)
    {
        if (key == (Keys.Control | Keys.Space)) { ShowCompletion(true); return true; }
        if (_completion?.Visible != true) return false;
        switch (key)
        {
            case Keys.Escape: _completionTimer.Stop(); HideCompletion(); return true;
            case Keys.Up: _completion.MoveSelection(-1); return true;
            case Keys.Down: _completion.MoveSelection(1); return true;
            case Keys.Tab:
            case Keys.Enter: AcceptCompletion(); return true;
            default: return false;
        }
    }

    private void AcceptCompletion()
    {
        if (ReadOnly || _completion?.Choice is not CompletionChoice choice || SelectionStart != _completionEnd) { HideCompletion(); return; }
        string insertion = choice.Template ?? choice.Insert;
        int line = Math.Max(0, GetFirstCharIndexFromLine(GetLineFromCharIndex(_completionStart)));
        string indent = new(' ', Text[line.._completionStart].TakeWhile(character => character == ' ').Count());
        insertion = insertion.Replace("\n", "\n" + indent, StringComparison.Ordinal);
        int marker = choice.Template is null ? -1 : insertion.IndexOf('|');
        if (marker >= 0) insertion = insertion.Remove(marker, 1);
        int start = _completionStart;
        HideCompletion();
        Select(start, _completionEnd - start);
        SelectedText = insertion;
        Select(start + (marker >= 0 ? marker : insertion.Length), 0);
        _completionTimer.Stop();
    }

    private static bool InCode(string text)
    {
        bool quoted = false, escaped = false;
        foreach (char character in text)
        {
            if (escaped) { escaped = false; continue; }
            if (quoted && character == '\\') { escaped = true; continue; }
            if (character == '"') quoted = !quoted;
            if (!quoted && character == '#') return false;
        }
        return !quoted;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!ReadOnly && e.KeyChar is '{' or '(' or '[' or '"' or '}' or ')' or ']')
        {
            int start = SelectionStart;
            if (SelectionLength == 0 && start < TextLength && Text[start] == e.KeyChar && e.KeyChar is '}' or ')' or ']' or '"')
            { Select(start + 1, 0); e.Handled = true; }
            else if (InCode(Text[Math.Max(0, GetFirstCharIndexOfCurrentLine())..start]) && e.KeyChar is '{' or '(' or '[' or '"')
            {
                char close = e.KeyChar switch { '{' => '}', '(' => ')', '[' => ']', _ => '"' };
                string selection = SelectedText;
                SelectedText = e.KeyChar + selection + close;
                Select(start + 1, selection.Length);
                e.Handled = true;
            }
        }
        base.OnKeyPress(e);
    }

    public void ApplyDiagnostics(IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        ClearDiagnosticMarks();
        foreach (CtsDiagnostic diagnostic in diagnostics.OrderBy(item => item.Severity).Take(200))
        {
            int lineStart = GetFirstCharIndexFromLine(Math.Max(0, diagnostic.Span.Start.Line - 1));
            if (lineStart < 0) continue;
            int start = Math.Clamp(lineStart + diagnostic.Span.Start.Column - 1, 0, TextLength);
            int lineEnd = Text.IndexOf('\n', start);
            if (lineEnd < 0) lineEnd = TextLength;
            // Project-wide messages mark their first token instead of covering the whole document.
            int end = diagnostic.Span.End.Line == diagnostic.Span.Start.Line
                ? Math.Clamp(lineStart + diagnostic.Span.End.Column - 1, start, lineEnd)
                : Math.Min(lineEnd, start + 16);
            _diagnosticMarks.Add(new DiagnosticMark(start, Math.Max(start + 1, end), diagnostic));
        }
        Invalidate();
    }

    public CtsDiagnostic? DiagnosticAt(int offset) =>
        _diagnosticMarks.FirstOrDefault(mark => offset >= mark.Start && offset < mark.End)?.Diagnostic;

    private void ClearDiagnosticMarks()
    {
        _diagnosticMarks.Clear();
        _hoverMessage = null;
        _diagnosticTip.Hide(this);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        CtsDiagnostic? diagnostic = DiagnosticAt(GetCharIndexFromPosition(e.Location));
        string? message = diagnostic is null ? null : $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}";
        if (message == _hoverMessage) return;
        _hoverMessage = message;
        _diagnosticTip.Hide(this);
        if (message is not null) _diagnosticTip.Show(message, this, e.X + 12, e.Y + Font.Height + 6, 10000);
    }

    protected override void OnMouseLeave(EventArgs e)
    { _hoverMessage = null; _diagnosticTip.Hide(this); base.OnMouseLeave(e); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || _coloring || _diagnosticMarks.Count == 0) return;
        using Graphics graphics = Graphics.FromHwnd(Handle);
        (int from, int length) = VisibleRange();
        foreach (DiagnosticMark mark in _diagnosticMarks)
        {
            if (mark.End < from || mark.Start > from + length) continue;
            Point start = GetPositionFromCharIndex(Math.Min(mark.Start, TextLength));
            Point end = GetPositionFromCharIndex(Math.Min(mark.End, TextLength));
            int right = end.Y == start.Y ? end.X : start.X + 16;
            int y = start.Y + Font.Height - 2;
            Color color = mark.Diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => Color.FromArgb(244, 92, 104),
                DiagnosticSeverity.Warning => Color.FromArgb(230, 172, 45),
                _ => Color.FromArgb(80, 180, 235)
            };
            using Pen pen = new(color);
            for (int x = Math.Max(0, start.X); x < Math.Min(ClientSize.Width, Math.Max(start.X + 8, right)); x += 4)
            {
                graphics.DrawLine(pen, x, y, x + 2, y - 2);
                graphics.DrawLine(pen, x + 2, y - 2, x + 4, y);
            }
        }
    }

    private void DisposeAssistance()
    { _completionTimer.Dispose(); _diagnosticTip.Dispose(); _completion?.Dispose(); }

    private sealed record DiagnosticMark(int Start, int End, CtsDiagnostic Diagnostic);
    private sealed record CompletionChoice(string Label, string Insert, string? Detail, string? Template);

    private sealed class CompletionWindow : Form
    {
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
        private bool _dark;
        public CompletionChoice? Choice => _list.SelectedItem as CompletionChoice;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000080; return parameters; }
        }
        public CompletionWindow(Action accept)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Padding = new Padding(1); Controls.Add(_list);
            _list.MouseDoubleClick += (_, _) => accept();
            _list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var choice = (CompletionChoice)_list.Items[e.Index];
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using SolidBrush background = new(selected ? (_dark ? Color.FromArgb(48, 53, 58) : Color.FromArgb(218, 229, 237)) : _list.BackColor);
                e.Graphics.FillRectangle(background, e.Bounds);
                string? color = CtsBlockRegistry.Definitions.FirstOrDefault(alias => alias.Name == choice.Label)?.CategoryColor;
                if (color is not null)
                {
                    using SolidBrush swatch = new(ColorTranslator.FromHtml(color));
                    e.Graphics.FillRectangle(swatch, e.Bounds.X + 6, e.Bounds.Y + 7, 6, 6);
                }
                Rectangle label = new(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 26, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, choice.Label, _list.Font, label, _list.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        public void SetChoices(CompletionChoice[] choices, Color background, Color foreground, bool dark, Font font)
        {
            _dark = dark; BackColor = dark ? Color.FromArgb(65, 65, 72) : Color.Silver;
            _list.BackColor = background; _list.ForeColor = foreground; _list.Font = font;
            _list.ItemHeight = font.Height + 8;
            _list.BeginUpdate(); _list.Items.Clear(); _list.Items.AddRange(choices); _list.SelectedIndex = 0; _list.EndUpdate();
            Size = new Size(320, Math.Min(8, choices.Length) * _list.ItemHeight + 2);
            NativeTheme.Apply(_list, dark);
        }
        public void MoveSelection(int delta) => _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
    }
}
