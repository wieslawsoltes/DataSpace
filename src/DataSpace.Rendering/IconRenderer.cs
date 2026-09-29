using SkiaSharp;

namespace DataSpace.Rendering;

/// <summary>Original vector icons, authored as reusable Skia geometry rather than copied Office assets.</summary>
public static class IconRenderer
{
    public static void Draw(SKCanvas canvas, string icon, SKRect bounds, SKColor color, DrawingResources drawing)
    {
        canvas.Save(); canvas.Translate(bounds.Left, bounds.Top); canvas.Scale(bounds.Width / 24, bounds.Height / 24);
        void Line(float x1, float y1, float x2, float y2, SKColor? tint = null, float width = 1.5f) => drawing.Line(canvas, x1, y1, x2, y2, tint ?? color, width);
        void Rect(float x, float y, float w, float h, bool fill = false, SKColor? tint = null)
        { if (fill) drawing.Fill(canvas, new(x, y, x + w, y + h), tint ?? color); else drawing.Stroke(canvas, new(x, y, x + w, y + h), tint ?? color, 1.4f); }
        void Path(float[] points, bool close = false, bool fill = false)
        {
            using var path = new SKPath(); path.MoveTo(points[0], points[1]);
            for (var i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]);
            if (close) path.Close(); drawing.Path(canvas, path, color, 1.5f, fill);
        }
        switch (icon)
        {
            case "database":
                using (var cylinder = new SKPath())
                {
                    cylinder.AddOval(new SKRect(3, 2, 21, 8));
                    cylinder.MoveTo(3, 5); cylinder.LineTo(3, 19); cylinder.CubicTo(3, 23, 21, 23, 21, 19); cylinder.LineTo(21, 5);
                    cylinder.MoveTo(3, 12); cylinder.CubicTo(3, 16, 21, 16, 21, 12);
                    drawing.Path(canvas, cylinder, color, 1.5f);
                }
                break;
            case "server":
                Rect(3, 2, 18, 7); Rect(3, 12, 18, 7); Line(7, 5.5f, 14, 5.5f); Line(7, 15.5f, 14, 15.5f);
                drawing.Circle(canvas, 18, 5.5f, 1, color); drawing.Circle(canvas, 18, 15.5f, 1, color); Line(12, 19, 12, 23); Line(5, 23, 19, 23); break;
            case "json":
                Path([8, 2, 5, 2, 5, 9, 2, 12, 5, 15, 5, 22, 8, 22]);
                Path([16, 2, 19, 2, 19, 9, 22, 12, 19, 15, 19, 22, 16, 22]);
                drawing.Circle(canvas, 12, 9, 1, color); drawing.Circle(canvas, 12, 15, 1, color); break;
            case "check": Path([3, 12, 9, 18, 21, 5]); break;
            case "table": case "datasheet":
                Rect(2, 3, 20, 18); Rect(2, 3, 20, 5, true, color.WithAlpha(65));
                Line(2, 8, 22, 8); Line(2, 14, 22, 14); Line(8, 8, 8, 21); Line(15, 8, 15, 21); break;
            case "query":
                Rect(2, 3, 14, 15); Line(2, 8, 16, 8); Line(7, 8, 7, 18); Line(2, 13, 11, 13);
                drawing.Circle(canvas, 16, 15, 5, color.WithAlpha(40)); drawing.Circle(canvas, 16, 15, 5, color, false); Line(19.5f, 19, 23, 23, width: 2); break;
            case "form":
                Rect(2, 2, 20, 20); Rect(2, 2, 20, 5, true, color.WithAlpha(65));
                Line(5, 11, 9, 11); Rect(12, 9, 7, 4); Line(5, 18, 9, 18); Rect(12, 16, 7, 4); break;
            case "report":
                Path([5, 2, 16, 2, 21, 7, 21, 22, 5, 22], true); Path([16, 2, 16, 7, 21, 7]);
                Line(8, 11, 17, 11); Line(8, 14, 17, 14); Line(8, 18, 11, 18, width: 3); Line(14, 17, 17, 17, width: 5); break;
            case "macro": case "run":
                Path([7, 3, 21, 12, 7, 21], true, true); break;
            case "save":
                Path([3, 2, 18, 2, 22, 6, 22, 22, 3, 22], true); Rect(7, 2, 10, 7); Rect(7, 14, 11, 8); Line(15, 3, 15, 7, width: 2); break;
            case "undo": case "redo":
                if (icon == "redo") { canvas.Translate(24, 0); canvas.Scale(-1, 1); }
                Path([8, 4, 2, 10, 8, 16]);
                using (var curve = new SKPath()) { curve.MoveTo(3, 10); curve.CubicTo(18, 5, 25, 13, 19, 21); drawing.Path(canvas, curve, color, 1.8f); } break;
            case "copy": Rect(3, 2, 14, 17); Rect(8, 7, 13, 16, true, SKColors.White); Rect(8, 7, 13, 16); break;
            case "paste": Rect(5, 4, 16, 18); Rect(9, 2, 8, 4, true, SKColor.Parse("E9CC8C")); Rect(9, 2, 8, 4); Line(9, 10, 17, 10); Line(9, 14, 17, 14); Line(9, 18, 15, 18); break;
            case "cut": drawing.Circle(canvas, 6, 18, 3, color, false); drawing.Circle(canvas, 17, 18, 3, color, false); Line(8, 16, 19, 2); Line(15, 16, 4, 2); break;
            case "delete": Line(4, 6, 20, 6); Line(9, 3, 15, 3); Path([6, 6, 7, 22, 17, 22, 18, 6]); Line(10, 10, 10, 18); Line(14, 10, 14, 18); break;
            case "new": Rect(3, 2, 14, 19); Line(17, 12, 17, 22, SKColor.Parse("3B8245"), 2); Line(12, 17, 22, 17, SKColor.Parse("3B8245"), 2); break;
            case "filter": Path([2, 4, 22, 4, 15, 12, 15, 21, 9, 18, 9, 12], true); break;
            case "clear": Path([4, 4, 20, 20]); Path([20, 4, 4, 20]); break;
            case "search": case "find": drawing.Circle(canvas, 10, 10, 7, color, false); Line(15, 15, 23, 23, width: 2.5f); break;
            case "replace": drawing.Text(canvas, "a", 1, 10, color, 12, true); drawing.Text(canvas, "b", 14, 23, color, 12, true); Path([9, 7, 20, 7, 17, 4]); Path([15, 17, 4, 17, 7, 20]); break;
            case "sort-asc": case "sort-desc":
                drawing.Text(canvas, icon == "sort-asc" ? "A" : "Z", 1, 10, color, 10, true); drawing.Text(canvas, icon == "sort-asc" ? "Z" : "A", 1, 23, color, 10, true);
                Line(17, 3, 17, 20); Path([13, 16, 17, 21, 21, 16]); break;
            case "relationships": Rect(1, 3, 8, 8); Rect(15, 14, 8, 8); Path([9, 7, 13, 7, 13, 18, 15, 18]); break;
            case "key": drawing.Circle(canvas, 7, 7, 5, color, false); Line(10, 10, 21, 21, width: 2); Line(15, 15, 18, 12, width: 2); Line(18, 18, 21, 15, width: 2); break;
            case "design": Path([3, 17, 17, 3, 22, 8, 8, 22, 2, 23], true); Line(14, 6, 19, 11); Line(4, 17, 8, 21); break;
            case "sql": drawing.Text(canvas, "SQL", 0, 16, color, 10, true); Line(2, 21, 22, 21); break;
            case "refresh":
                using (var path = new SKPath()) { path.MoveTo(19, 7); path.CubicTo(8, -3, -1, 9, 6, 18); path.CubicTo(12, 26, 23, 19, 20, 13); drawing.Path(canvas, path, color, 1.8f); }
                Path([19, 2, 19, 8, 13, 8]); break;
            case "import": case "export":
                Rect(2, 3, 12, 18); var right = icon == "export";
                Line(10, 12, 23, 12); if (right) Path([18, 7, 23, 12, 18, 17]); else Path([15, 7, 10, 12, 15, 17]); break;
            case "totals": Path([20, 3, 5, 3, 12, 12, 5, 21, 20, 21]); break;
            case "open": case "folder": Path([2, 5, 9, 5, 12, 8, 22, 8, 22, 21, 2, 21], true); Line(2, 10, 22, 10); break;
            case "print": Rect(6, 2, 12, 7); Rect(2, 9, 20, 10); Rect(6, 15, 12, 7, true, SKColors.White); Rect(6, 15, 12, 7); drawing.Circle(canvas, 18, 12, 1, color); break;
            case "info": case "help": drawing.Circle(canvas, 12, 12, 10, color, false); drawing.Text(canvas, "?", 8, 18, color, 17, true); break;
            default: drawing.Circle(canvas, 12, 12, 8, color, false); Line(8, 12, 16, 12); Line(12, 8, 12, 16); break;
        }
        canvas.Restore();
    }
}
