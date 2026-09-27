using DataSpace.Core;
using SkiaSharp;

namespace DataSpace.Rendering;

public sealed class FormLayoutRenderer : IDisposable
{
    public OfficeTheme Theme { get; }
    private readonly DrawingResources _drawing;
    public FormLayoutRenderer(OfficeTheme? theme = null) { Theme = theme ?? OfficeTheme.Default; _drawing = new(Theme); }
    public static SKRect Bounds(LayoutControl control) => new((float)control.X, (float)control.Y, (float)(control.X + control.Width), (float)(control.Y + control.Height));
    public LayoutControl? HitTest(FormDefinition form, float x, float y) => form.Controls.LastOrDefault(c => Bounds(c).Contains(x, y));
    public void Draw(SKCanvas canvas, float width, float height, FormDefinition form, string? selectedId, float zoom = 1, bool grid = true)
    {
        canvas.Clear(Theme.Workspace); canvas.Save(); canvas.Scale(zoom);
        _drawing.Fill(canvas, new(0, 0, (float)form.Width, (float)form.Height), SKColor.Parse("FAFAFA"));
        _drawing.Fill(canvas, new(0, 0, (float)form.Width, 66), SKColor.Parse("EDF1F3"));
        _drawing.Text(canvas, string.IsNullOrEmpty(form.Title) ? form.Name : form.Title, 34, 43, Theme.Accent, 25);
        _drawing.Line(canvas, 0, 66, (float)form.Width, 66, Theme.GridLine);
        if (grid)
            for (var x = 10; x < Math.Min(form.Width, width / zoom); x += 10)
                for (var y = 76; y < Math.Min(form.Height, height / zoom); y += 10)
                    _drawing.Circle(canvas, x, y, .6f, SKColor.Parse("CBCBCB"));
        foreach (var control in form.Controls)
        {
            var bounds = Bounds(control);
            if (control.Kind is LayoutControlKind.TextBox or LayoutControlKind.CheckBox)
            {
                _drawing.Text(canvas, control.Caption, bounds.Left, bounds.Top - 8, Theme.Text, 12);
                if (control.Kind == LayoutControlKind.CheckBox)
                { _drawing.Fill(canvas, new(bounds.Left + 3, bounds.Top + 6, bounds.Left + 21, bounds.Top + 24), Theme.Surface); _drawing.Stroke(canvas, new(bounds.Left + 3, bounds.Top + 6, bounds.Left + 21, bounds.Top + 24), Theme.MutedText); }
                else { _drawing.Fill(canvas, bounds, Theme.Surface); _drawing.Stroke(canvas, bounds, SKColor.Parse("AAB5BB")); _drawing.CellText(canvas, "[" + control.Field + "]", bounds, Theme.MutedText, (float)control.FontSize); }
            }
            else _drawing.CellText(canvas, control.Caption, bounds, Theme.Text, (float)control.FontSize, control.Kind == LayoutControlKind.Heading);
            if (control.Id == selectedId)
            {
                _drawing.Stroke(canvas, bounds, Theme.Accent, 1.5f);
                foreach (var point in new[] { new SKPoint(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom) })
                { _drawing.Fill(canvas, new(point.X - 3, point.Y - 3, point.X + 3, point.Y + 3), Theme.Surface); _drawing.Stroke(canvas, new(point.X - 3, point.Y - 3, point.X + 3, point.Y + 3), Theme.Accent); }
            }
        }
        canvas.Restore();
    }
    public void Dispose() => _drawing.Dispose();
}
