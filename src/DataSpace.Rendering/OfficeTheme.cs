using SkiaSharp;

namespace DataSpace.Rendering;

/// <summary>Original, configurable Office-style color and measurement tokens shared by all renderers.</summary>
public sealed class OfficeTheme
{
    public SKColor Accent { get; init; } = SKColor.Parse("A4373A");
    public SKColor AccentDark { get; init; } = SKColor.Parse("81292C");
    public SKColor Text { get; init; } = SKColor.Parse("252525");
    public SKColor MutedText { get; init; } = SKColor.Parse("656565");
    public SKColor Surface { get; init; } = SKColors.White;
    public SKColor Workspace { get; init; } = SKColor.Parse("E6E6E6");
    public SKColor Header { get; init; } = SKColor.Parse("F1F1F1");
    public SKColor AlternateRow { get; init; } = SKColor.Parse("F7FAFC");
    public SKColor GridLine { get; init; } = SKColor.Parse("D6DCE1");
    public SKColor Selection { get; init; } = SKColor.Parse("D8ECF9");
    public SKColor SelectionHeader { get; init; } = SKColor.Parse("B7D8EE");
    public SKColor ActiveCell { get; init; } = SKColor.Parse("E3A12C");
    public float RowHeight { get; init; } = 27;
    public float ColumnHeaderHeight { get; init; } = 30;
    public float RowHeaderWidth { get; init; } = 29;
    public float FontSize { get; init; } = 13;
    public string FontFamily { get; init; } = "Arial";
    public static OfficeTheme Default { get; } = new();
}

/// <summary>Reusable drawing resources. Own one instance per surface and dispose it with the host.</summary>
public sealed class DrawingResources : IDisposable
{
    private readonly Dictionary<(float Size, bool Bold), SKFont> _fonts = new();
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly bool _ownsRegular;
    private readonly bool _ownsBold;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    public DrawingResources(OfficeTheme? theme = null)
    {
        theme ??= OfficeTheme.Default;
        var regular = SKTypeface.FromFamilyName(theme.FontFamily);
        var bold = SKTypeface.FromFamilyName(theme.FontFamily, SKFontStyle.Bold);
        _regular = regular ?? SKTypeface.Default; _bold = bold ?? SKTypeface.Default;
        _ownsRegular = regular is not null; _ownsBold = bold is not null;
    }
    public SKFont Font(float size = 13, bool bold = false)
    {
        if (!_fonts.TryGetValue((size, bold), out var font))
        {
            font = new SKFont(bold ? _bold : _regular, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
            _fonts.Add((size, bold), font);
        }
        return font;
    }
    public void Fill(SKCanvas canvas, SKRect rect, SKColor color)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Fill; canvas.DrawRect(rect, _paint); }
    public void Stroke(SKCanvas canvas, SKRect rect, SKColor color, float width = 1)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = width; canvas.DrawRect(rect, _paint); }
    public void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width = 1)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = width; canvas.DrawLine(x1, y1, x2, y2, _paint); }
    public void Text(SKCanvas canvas, string text, float x, float baseline, SKColor color, float size = 13, bool bold = false)
    {
        _paint.Color = color; _paint.Style = SKPaintStyle.Fill;
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, Font(size, bold), _paint);
    }
    public void CellText(SKCanvas canvas, string text, SKRect rect, SKColor color, float size = 13, bool bold = false, bool right = false)
    {
        if (rect.Width <= 8 || rect.Height <= 0 || text.Length == 0) return;
        canvas.Save(); canvas.ClipRect(new(rect.Left + 5, rect.Top + 1, rect.Right - 5, rect.Bottom - 1));
        var font = Font(size, bold);
        var x = right ? Math.Max(rect.Left + 7, rect.Right - 8 - font.MeasureText(text)) : rect.Left + 8;
        var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
        Text(canvas, text.Replace('\r', ' ').Replace('\n', ' '), x, baseline, color, size, bold);
        canvas.Restore();
    }
    public void Path(SKCanvas canvas, SKPath path, SKColor color, float width = 1, bool fill = false)
    { _paint.Color = color; _paint.Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke; _paint.StrokeWidth = width; _paint.StrokeCap = SKStrokeCap.Round; _paint.StrokeJoin = SKStrokeJoin.Round; canvas.DrawPath(path, _paint); }
    public void Circle(SKCanvas canvas, float x, float y, float radius, SKColor color, bool fill = true)
    { _paint.Color = color; _paint.Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke; _paint.StrokeWidth = 1; canvas.DrawCircle(x, y, radius, _paint); }
    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear(); _paint.Dispose();
        if (_ownsRegular) _regular.Dispose(); if (_ownsBold) _bold.Dispose();
    }
}
