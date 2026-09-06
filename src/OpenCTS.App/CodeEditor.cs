using System.Runtime.InteropServices;
using OpenCTS.Core;

namespace OpenCTS.App;

public sealed class CodeEditor : RichTextBox
{
    private readonly List<EditState> _undo = [];
    private readonly Stack<EditState> _redo = new();
    private string _previousText = "";
    private int _previousCaret;
    private bool _restoring;
    private bool _coloring;
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
    }
    protected override void OnTextChanged(EventArgs e)
    {
        if (_coloring || Text == _previousText) return;
        if (!_restoring)
        {
            _undo.Add(new EditState(_previousText, _previousCaret));
            _redo.Clear();
            while (_undo.Count > 100 || _undo.Count > 1 && _undo.Sum(item => (long)item.Text.Length) > 16 * 1024 * 1024) _undo.RemoveAt(0);
        }
        _previousText = Text;
        _previousCaret = SelectionStart;
        base.OnTextChanged(e);
    }
    protected override void OnSelectionChanged(EventArgs e)
    {
        if (_coloring) return;
        _previousCaret = SelectionStart;
        base.OnSelectionChanged(e);
    }
    public void ResetHistory() { _undo.Clear(); _redo.Clear(); ClearUndo(); _previousText = Text; _previousCaret = SelectionStart; }
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
        try { Text = state.Text; Select(Math.Min(state.Caret, TextLength), 0); ScrollToCaret(); }
        finally { _restoring = false; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
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
            SelectedText = "\n" + indent;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void ApplyColors(IReadOnlyList<CtsColorSpan> spans, Color textColor, Color commentColor, bool dark)
    {
        if (!IsHandleCreated || IsDisposed) return;
        int start = SelectionStart;
        int length = SelectionLength;
        NativePoint scroll = default;
        SendPoint(Handle, 0x04DD, IntPtr.Zero, ref scroll);
        _coloring = true;
        Send(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try
        {
            SelectAll();
            SelectionColor = textColor;
            foreach (CtsColorSpan span in spans)
            {
                if (span.Start < 0 || span.Start + span.Length > TextLength) continue;
                Select(span.Start, span.Length);
                Color color = span.Kind == "Comment" ? commentColor : ColorTranslator.FromHtml(span.Color);
                if (dark && span.Color == ScratchCategoryColors.StringLiteral) color = Color.FromArgb(157, 205, 173);
                else if (dark && span.Color == ScratchCategoryColors.NumberLiteral) color = Color.FromArgb(189, 148, 221);
                else if (dark && color.GetBrightness() < 0.3F) color = Color.FromArgb(178, 183, 192);
                if (!dark && span.Kind != "Comment") color = Color.FromArgb((int)(color.R * 0.65), (int)(color.G * 0.65), (int)(color.B * 0.65));
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
