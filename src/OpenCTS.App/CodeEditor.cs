using System.Runtime.InteropServices;
using OpenCTS.Core;

namespace OpenCTS.App;

public sealed partial class CodeEditor : RichTextBox
{
    private readonly List<EditState> _undo = [];
    private readonly Stack<EditState> _redo = new();
    private string _previousText = "";
    private int _previousCaret;
    private bool _restoring;
    private bool _coloring;
    private IReadOnlyList<CtsColorSpan> _spans = [];
    private Color _textColor, _commentColor;
    private bool _dark;
    private readonly System.Windows.Forms.Timer _paintTimer = new() { Interval = 45 };
    public event EventHandler? ViewportChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public new string Text
    {
        get => IsHandleCreated ? _previousText : base.Text;
        set { base.Text = value; _previousText = base.Text; }
    }
    [System.ComponentModel.Browsable(false)]
    public new int TextLength => _previousText.Length;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color GutterColor { get; set; } = Color.Gray;

    public CodeEditor()
    {
        BorderStyle = BorderStyle.None;
        Font = new Font("Cascadia Mono", 11F);
        WordWrap = false;
        DetectUrls = false;
        AcceptsTab = true;
        HideSelection = false;
        ContextMenuStrip menu = new();
        menu.Items.Add("Undo", null, (_, _) => Undo());
        menu.Items.Add("Redo", null, (_, _) => Redo());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Cut", null, (_, _) => { if (!ReadOnly) Cut(); });
        menu.Items.Add("Copy", null, (_, _) => Copy());
        menu.Items.Add("Paste", null, (_, _) => { if (!ReadOnly) Paste(DataFormats.GetFormat(DataFormats.UnicodeText)); });
        menu.Items.Add("Select all", null, (_, _) => SelectAll());
        ContextMenuStrip = menu;
        menu.Opening += (_, _) =>
        {
            menu.Items[0].Enabled = !ReadOnly && _undo.Count > 0;
            menu.Items[1].Enabled = !ReadOnly && _redo.Count > 0;
            menu.Items[3].Enabled = !ReadOnly && SelectionLength > 0;
            menu.Items[4].Enabled = SelectionLength > 0;
            menu.Items[5].Enabled = !ReadOnly;
        };
        VScroll += (_, _) => SchedulePaint();
        HScroll += (_, _) => SchedulePaint();
        Resize += (_, _) => SchedulePaint();
        _paintTimer.Tick += (_, _) => { _paintTimer.Stop(); PaintViewport(); ViewportChanged?.Invoke(this, EventArgs.Empty); };
        InitializeAssistance();
    }
    protected override void OnTextChanged(EventArgs e)
    {
        if (_coloring) return;
        string current = base.Text;
        if (current == _previousText) return;
        _spans = [];
        ClearDiagnosticMarks();
        if (!_restoring)
        {
            _undo.Add(new EditState(_previousText, _previousCaret));
            _redo.Clear();
            while (_undo.Count > 100 || _undo.Count > 1 && _undo.Sum(item => (long)item.Text.Length) > 16 * 1024 * 1024) _undo.RemoveAt(0);
        }
        _previousText = current;
        _previousCaret = SelectionStart;
        base.OnTextChanged(e);
        ScheduleCompletion();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        bool coloring = _coloring;
        _coloring = true;
        try { base.OnHandleCreated(e); _previousText = base.Text; }
        finally { _coloring = coloring; }
    }
    protected override void OnSelectionChanged(EventArgs e)
    {
        if (_coloring) return;
        _previousCaret = SelectionStart;
        HideCompletion();
        base.OnSelectionChanged(e);
    }
    public void ResetHistory() { _undo.Clear(); _redo.Clear(); ClearUndo(); _previousText = Text; _previousCaret = SelectionStart; }
    public void UpdateSavedSourceText(string source)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string previous = Text;
        if (source == previous) return;
        int previousEnd = previous.Length, sourceEnd = source.Length;
        while (previousEnd > 0 && char.IsWhiteSpace(previous[previousEnd - 1])) previousEnd--;
        while (sourceEnd > 0 && char.IsWhiteSpace(source[sourceEnd - 1])) sourceEnd--;
        int prefix = 0, suffix = 0;
        while (prefix < Math.Min(previousEnd, sourceEnd) && previous[prefix] == source[prefix]) prefix++;
        while (suffix < Math.Min(previousEnd, sourceEnd) - prefix && previous[previousEnd - suffix - 1] == source[sourceEnd - suffix - 1]) suffix++;
        int start = SelectionStart, end = start + SelectionLength;
        int Map(int offset) => offset >= previousEnd ? sourceEnd + Math.Min(offset - previousEnd, source.Length - sourceEnd)
            : offset < prefix ? offset : offset >= previousEnd - suffix ? offset + sourceEnd - previousEnd : sourceEnd - suffix;
        NativePoint scroll = default;
        if (IsHandleCreated) SendPoint(Handle, 0x04DD, IntPtr.Zero, ref scroll);
        _restoring = true;
        if (IsHandleCreated) Send(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try
        {
            // Save may change the header and normalize the final newline, while the code between stays intact.
            if (!previous.AsSpan(previousEnd).SequenceEqual(source.AsSpan(sourceEnd)))
            {
                Select(previousEnd, previous.Length - previousEnd);
                SelectedText = source[sourceEnd..];
            }
            if (prefix + suffix < previousEnd || prefix + suffix < sourceEnd)
            {
                Select(prefix, previousEnd - prefix - suffix);
                SelectedText = source.Substring(prefix, sourceEnd - prefix - suffix);
            }
            Select(Map(start), Math.Max(0, Map(end) - Map(start)));
            ClearUndo();
        }
        finally
        {
            _restoring = false;
            if (IsHandleCreated)
            {
                SendPoint(Handle, 0x04DE, IntPtr.Zero, ref scroll);
                Send(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                Invalidate();
            }
        }
    }
    public void LoadSourceText(string source)
    {
        _coloring = true;
        if (IsHandleCreated) Send(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try
        {
            Text = source;
            _previousText = base.Text;
            _previousCaret = 0;
            _spans = [];
            _completionSymbols = [];
            ClearDiagnosticMarks();
            HideCompletion();
            Select(0, 0);
            ClearUndo();
        }
        finally
        {
            _coloring = false;
            if (IsHandleCreated) Send(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            Invalidate();
        }
        base.OnTextChanged(EventArgs.Empty);
    }
    public new void Undo()
    {
        if (ReadOnly || _undo.Count == 0) return;
        _redo.Push(new EditState(Text, SelectionStart));
        EditState previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(previous);
    }
    public new void Redo()
    {
        if (ReadOnly || !_redo.TryPop(out EditState? next)) return;
        _undo.Add(new EditState(Text, SelectionStart));
        Restore(next);
    }
    private void Restore(EditState state)
    {
        _restoring = true;
        try { LoadSourceText(state.Text); Select(Math.Min(state.Caret, TextLength), 0); ScrollToCaret(); }
        finally { _restoring = false; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!ReadOnly && HandleCompletionKey(keyData)) return true;
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData is (Keys.Control | Keys.Y) or (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
        if (!ReadOnly && keyData == (Keys.Control | Keys.V)) { Paste(DataFormats.GetFormat(DataFormats.UnicodeText)); return true; }
        if (!ReadOnly && keyData == Keys.Tab) { SelectedText = "  "; return true; }
        if (!ReadOnly && keyData == Keys.Enter)
        {
            int first = GetFirstCharIndexOfCurrentLine();
            string before = Text[Math.Max(0, first)..SelectionStart];
            string indent = new(' ', before.TakeWhile(character => character == ' ').Count());
            if (before.TrimEnd().EndsWith(':') || before.TrimEnd().EndsWith('{')) indent += "  ";
            bool paired = before.TrimEnd().EndsWith('{') && SelectionLength == 0 && SelectionStart < TextLength && Text[SelectionStart] == '}';
            SelectedText = "\n" + indent + (paired ? "\n" + indent[..^2] : "");
            if (paired) SelectionStart -= indent.Length - 1;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void ApplyColors(IReadOnlyList<CtsColorSpan> spans, Color textColor, Color commentColor, bool dark)
    {
        _spans = spans;
        _textColor = textColor;
        _commentColor = commentColor;
        _dark = dark;
        PaintViewport();
    }
    private void SchedulePaint() { if (_coloring) return; _paintTimer.Stop(); _paintTimer.Start(); }
    public (int Start, int Length) VisibleRange()
    {
        int start = GetCharIndexFromPosition(Point.Empty);
        int end = GetCharIndexFromPosition(new Point(ClientSize.Width, ClientSize.Height));
        int first = GetFirstCharIndexFromLine(GetLineFromCharIndex(start));
        int from = Math.Max(Math.Max(0, first), start - 256);
        return (from, Math.Min(TextLength - from, Math.Min(20000, Math.Max(0, end - from) + 512)));
    }
    private void PaintViewport()
    {
        if (!IsHandleCreated || IsDisposed) return;
        (int from, int count) = VisibleRange();
        if (count == 0) return;
        int start = SelectionStart;
        int length = SelectionLength;
        NativePoint scroll = default;
        SendPoint(Handle, 0x04DD, IntPtr.Zero, ref scroll);
        _coloring = true;
        Send(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try
        {
            Select(from, count);
            SelectionColor = _textColor.IsEmpty ? ForeColor : _textColor;
            int low = 0, high = _spans.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_spans[middle].Start + _spans[middle].Length <= from) low = middle + 1;
                else high = middle;
            }
            for (int i = low; i < _spans.Count && _spans[i].Start < from + count; i++)
            {
                CtsColorSpan span = _spans[i];
                if (span.Start < 0 || span.Start + span.Length > TextLength) continue;
                int clipped = Math.Max(from, span.Start);
                Select(clipped, Math.Min(from + count, span.Start + span.Length) - clipped);
                Color color = span.Kind == "Comment" ? _commentColor : ColorTranslator.FromHtml(span.Color);
                if (_dark && span.Color == ScratchCategoryColors.StringLiteral) color = Color.FromArgb(157, 205, 173);
                else if (_dark && span.Color == ScratchCategoryColors.NumberLiteral) color = Color.FromArgb(189, 148, 221);
                else if (_dark && span.Color is ScratchCategoryColors.NeutralKeyword or ScratchCategoryColors.RawSyntax) color = Color.FromArgb(178, 183, 192);
                SelectionColor = color;
            }
            Select(start, length);
        }
        finally
        {
            SendPoint(Handle, 0x04DE, IntPtr.Zero, ref scroll);
            Send(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            _coloring = false;
            Invalidate();
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { DisposeAssistance(); _paintTimer.Dispose(); ContextMenuStrip?.Dispose(); }
        base.Dispose(disposing);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr Send(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendPoint(IntPtr window, int message, IntPtr wParam, ref NativePoint point);
    private sealed record EditState(string Text, int Caret);
}

internal sealed class LineNumberMargin : Control
{
    private readonly CodeEditor _editor;
    public LineNumberMargin(CodeEditor editor)
    {
        _editor = editor;
        DoubleBuffered = true;
        editor.VScroll += (_, _) => Invalidate();
        editor.TextChanged += (_, _) => Invalidate();
        editor.SelectionChanged += (_, _) => Invalidate();
        editor.Resize += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_editor.BackColor);
        int first = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(Point.Empty));
        int last = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(0, _editor.ClientSize.Height)));
        for (int line = first; line <= last; line++)
        {
            int index = _editor.GetFirstCharIndexFromLine(line);
            if (index < 0) break;
            Point position = _editor.GetPositionFromCharIndex(index);
            TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), _editor.Font,
                new Rectangle(0, position.Y, Width - 12, _editor.Font.Height + 2), _editor.GutterColor,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }
    }
}
