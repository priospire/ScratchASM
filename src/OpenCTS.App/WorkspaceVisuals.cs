using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Svg;
using OpenCTS.Core;

namespace OpenCTS.App;

internal sealed class StagePreview : Control
{
    private Bitmap? _image;
    public StagePreview() { DoubleBuffered = true; Height = 190; AccessibleName = "Stage preview"; }
    public void SetImage(Bitmap? image) { _image?.Dispose(); _image = image; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (_image is null) return;
        float scale = Math.Min((Width - 16) / 480f, (Height - 16) / 360f);
        RectangleF rectangle = new((Width - 480 * scale) / 2, (Height - 360 * scale) / 2, 480 * scale, 360 * scale);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, rectangle);
    }
    protected override void Dispose(bool disposing) { if (disposing) _image?.Dispose(); base.Dispose(disposing); }
    public static Bitmap Render(ScratchProjectDocument document, int selected)
    {
        Bitmap canvas = new(480, 360);
        using Graphics graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        JsonObject[] targets = document.Project["targets"]!.AsArray().OfType<JsonObject>().ToArray();
        foreach (JsonObject target in targets.OrderBy(target => Number(target, "layerOrder", 0)))
        {
            bool stage = target["isStage"]?.ToString() == "true";
            if (!stage && target["visible"]?.ToString() == "false") continue;
            if (target["costumes"] is not JsonArray { Count: > 0 } costumes) continue;
            int costumeIndex = Math.Clamp((int)Number(target, "currentCostume", 0), 0, costumes.Count - 1);
            JsonObject costume = costumes[costumeIndex]!.AsObject();
            if (!document.Assets.TryGetValue(costume["md5ext"]?.ToString() ?? "", out byte[]? bytes)) continue;
            using Bitmap bitmap = ReadCostume(bytes, costume["dataFormat"]?.ToString() ?? "png");
            double resolution = Math.Max(1, Number(costume, "bitmapResolution", 1));
            var state = graphics.Save();
            if (stage) graphics.DrawImage(bitmap, new Rectangle(0, 0, 480, 360));
            else
            {
                float scale = (float)(Number(target, "size", 100) / (100 * resolution));
                graphics.TranslateTransform(240 + (float)Number(target, "x", 0), 180 - (float)Number(target, "y", 0));
                string rotation = target["rotationStyle"]?.ToString() ?? "all around";
                if (rotation == "all around") graphics.RotateTransform((float)Number(target, "direction", 90) - 90);
                if (rotation == "left-right" && Number(target, "direction", 90) < 0) graphics.ScaleTransform(-1, 1);
                graphics.ScaleTransform(scale, scale);
                RectangleF rect = new(-(float)Number(costume, "rotationCenterX", bitmap.Width / 2d),
                    -(float)Number(costume, "rotationCenterY", bitmap.Height / 2d), bitmap.Width, bitmap.Height);
                graphics.DrawImage(bitmap, rect);
                if (Array.IndexOf(targets, target) == selected)
                {
                    using Pen pen = new(Color.FromArgb(0, 156, 168), Math.Max(1, 1 / Math.Max(0.01f, scale)));
                    graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
            graphics.Restore(state);
        }
        return canvas;
    }
    public static Bitmap ReadCostume(byte[] bytes, string format)
    {
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Costume preview is limited to 16 MiB per image.");
        using MemoryStream stream = new(bytes, false);
        if (format == "svg")
        {
            SvgDocument.DisableDtdProcessing = true;
            SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
            SvgDocument.ResolveExternalImages = ExternalType.None;
            SvgDocument.ResolveExternalElements = ExternalType.None;
            SvgDocument svg = SvgDocument.Open<SvgDocument>(stream) ?? throw new InvalidDataException("The SVG could not be read.");
            SizeF dimensions = svg.GetDimensions();
            if (!float.IsFinite(dimensions.Width) || !float.IsFinite(dimensions.Height) ||
                dimensions.Width <= 0 || dimensions.Height <= 0 || dimensions.Width > 4096 || dimensions.Height > 4096)
                throw new InvalidDataException("Costume dimensions exceed the 4096 pixel preview limit.");
            return svg.Draw((int)Math.Ceiling(dimensions.Width), (int)Math.Ceiling(dimensions.Height))
                ?? throw new InvalidDataException("Costume could not be rendered.");
        }
        using Image image = Image.FromStream(stream);
        if (image.Width > 4096 || image.Height > 4096) throw new InvalidDataException("Costume dimensions exceed the 4096 pixel preview limit.");
        return new Bitmap(image);
    }
    private static double Number(JsonObject node, string name, double fallback) =>
        node[name] is JsonValue value && value.TryGetValue<double>(out double number) && double.IsFinite(number) ? number : fallback;
}

internal sealed class ActivityLine : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private int _position;
    private bool _active;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { _active = value; _timer.Enabled = value; Invalidate(); } }
    public ActivityLine() { Height = 2; DoubleBuffered = true; _timer.Tick += (_, _) => { _position = (_position + 12) % Math.Max(1, Width + 120); Invalidate(); }; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (Active) { using Brush brush = new SolidBrush(ForeColor); e.Graphics.FillRectangle(brush, _position - 120, 0, 120, Height); }
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal sealed class WorkspaceRenderer(Color surface, Color text, Color accent, Color border) : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(surface);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using Pen pen = new(border);
        if (e.Vertical) e.Graphics.DrawLine(pen, 3, 5, 3, e.Item.Height - 5);
        else e.Graphics.DrawLine(pen, 4, 2, e.Item.Width - 4, 2);
    }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => DrawSelection(e);
    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => DrawSelection(e);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => DrawSelection(e);
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? text : Color.Gray; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = text; base.OnRenderArrow(e); }
    private void DrawSelection(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && e.Item is not ToolStripButton { Checked: true }) return;
        using Brush brush = new SolidBrush(Color.FromArgb((surface.R * 3 + accent.R) / 4, (surface.G * 3 + accent.G) / 4, (surface.B * 3 + accent.B) / 4));
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        if (e.Item is ToolStripButton { Checked: true }) { using Pen pen = new(accent, 2); e.Graphics.DrawLine(pen, 2, e.Item.Height - 2, e.Item.Width - 2, e.Item.Height - 2); }
    }
}

internal static class NativeTheme
{
    public static void Apply(Control control, bool dark)
    {
        if (control.IsHandleCreated) SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
    }
    public static bool Title(Form form, bool dark, Color background, Color text)
    {
        if (!form.IsHandleCreated) return false;
        int value = dark ? 1 : 0;
        int status = DwmSetWindowAttribute(form.Handle, 20, ref value, 4);
        value = ColorTranslator.ToWin32(background);
        status |= DwmSetWindowAttribute(form.Handle, 35, ref value, 4);
        value = ColorTranslator.ToWin32(text);
        status |= DwmSetWindowAttribute(form.Handle, 36, ref value, 4);
        return status == 0;
    }
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string app, string? id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
