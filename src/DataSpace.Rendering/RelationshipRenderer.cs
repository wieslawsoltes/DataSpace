using DataSpace.Core;
using SkiaSharp;

namespace DataSpace.Rendering;

public sealed class RelationshipViewState
{
    public float Zoom { get; set; } = 1;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public string? SelectedTable { get; set; }
    public string? SelectedRelationship { get; set; }
}
public readonly record struct RelationshipHit(string? Table, string? Field, bool Header);

public sealed class RelationshipRenderer : IDisposable
{
    public const float TableWidth = 245;
    public const float HeaderHeight = 32;
    public const float FieldHeight = 25;
    public OfficeTheme Theme { get; }
    private readonly DrawingResources _drawing;
    public RelationshipRenderer(OfficeTheme? theme = null) { Theme = theme ?? OfficeTheme.Default; _drawing = new(Theme); }
    public static SKRect TableBounds(TableDefinition table) => new((float)table.DiagramX, (float)table.DiagramY, (float)table.DiagramX + TableWidth, (float)table.DiagramY + HeaderHeight + table.Fields.Count * FieldHeight + 8);
    public RelationshipHit HitTest(DatabaseDocument document, RelationshipViewState state, float x, float y)
    {
        x = x / state.Zoom + state.OffsetX; y = y / state.Zoom + state.OffsetY;
        foreach (var table in document.Tables.AsEnumerable().Reverse())
        {
            var bounds = TableBounds(table); if (!bounds.Contains(x, y)) continue;
            if (y < bounds.Top + HeaderHeight) return new(table.Name, null, true);
            var row = (int)((y - bounds.Top - HeaderHeight) / FieldHeight);
            return new(table.Name, row >= 0 && row < table.Fields.Count ? table.Fields[row].Name : null, false);
        }
        return new(null, null, false);
    }
    public void Draw(SKCanvas canvas, float width, float height, DatabaseDocument document, RelationshipViewState state)
    {
        canvas.Clear(SKColor.Parse("F8F8F8")); canvas.Save(); canvas.Scale(state.Zoom); canvas.Translate(-state.OffsetX, -state.OffsetY);
        for (var x = (int)(state.OffsetX / 20) * 20; x < state.OffsetX + width / state.Zoom; x += 20)
            for (var y = (int)(state.OffsetY / 20) * 20; y < state.OffsetY + height / state.Zoom; y += 20)
                _drawing.Circle(canvas, x, y, .7f, SKColor.Parse("DADADA"));
        foreach (var relation in document.Relationships)
        {
            var parent = document.Table(relation.ParentTable); var child = document.Table(relation.ChildTable);
            var from = TableBounds(parent); var to = TableBounds(child);
            var p = parent.Fields.FindIndex(f => Names.Equal(f.Name, relation.ParentField));
            var c = child.Fields.FindIndex(f => Names.Equal(f.Name, relation.ChildField));
            var leftToRight = from.MidX < to.MidX;
            var start = new SKPoint(leftToRight ? from.Right : from.Left, from.Top + HeaderHeight + (p + .5f) * FieldHeight);
            var end = new SKPoint(leftToRight ? to.Left : to.Right, to.Top + HeaderHeight + (c + .5f) * FieldHeight);
            var direction = leftToRight ? 1 : -1;
            var mid = (start.X + end.X) / 2;
            using var path = new SKPath(); path.MoveTo(start); path.LineTo(start.X + direction * 18, start.Y);
            path.LineTo(mid, start.Y); path.LineTo(mid, end.Y); path.LineTo(end.X - direction * 18, end.Y); path.LineTo(end);
            var color = relation.Name == state.SelectedRelationship ? Theme.Accent : SKColor.Parse("738895");
            _drawing.Path(canvas, path, color, relation.EnforceIntegrity ? 1.7f : 1);
            _drawing.Text(canvas, "1", start.X + direction * 8 - (direction < 0 ? 8 : 0), start.Y - 5, Theme.MutedText, 12, true);
            _drawing.Text(canvas, "∞", end.X - direction * 20, end.Y - 5, Theme.MutedText, 16);
        }
        foreach (var table in document.Tables)
        {
            var bounds = TableBounds(table);
            _drawing.Fill(canvas, new(bounds.Left + 3, bounds.Top + 3, bounds.Right + 3, bounds.Bottom + 3), SKColor.Parse("18000000"));
            _drawing.Fill(canvas, bounds, Theme.Surface);
            _drawing.Fill(canvas, new(bounds.Left, bounds.Top, bounds.Right, bounds.Top + HeaderHeight), Names.Equal(table.Name, state.SelectedTable) ? Theme.SelectionHeader : SKColor.Parse("E9EFF3"));
            IconRenderer.Draw(canvas, "table", new(bounds.Left + 10, bounds.Top + 8, bounds.Left + 26, bounds.Top + 24), Theme.Accent, _drawing);
            _drawing.CellText(canvas, table.Name, new(bounds.Left + 28, bounds.Top, bounds.Right - 6, bounds.Top + HeaderHeight), Theme.Text, 13, true);
            for (var i = 0; i < table.Fields.Count; i++)
            {
                var field = table.Fields[i]; var y = bounds.Top + HeaderHeight + i * FieldHeight;
                if (field.PrimaryKey) IconRenderer.Draw(canvas, "key", new(bounds.Left + 9, y + 5, bounds.Left + 23, y + 19), SKColor.Parse("B88E20"), _drawing);
                _drawing.CellText(canvas, field.Name, new(bounds.Left + 24, y, bounds.Right - 50, y + FieldHeight), Theme.Text, 12.5f, field.PrimaryKey);
                _drawing.CellText(canvas, ShortType(field.Type), new(bounds.Right - 52, y, bounds.Right - 2, y + FieldHeight), Theme.MutedText, 10, right: true);
            }
            _drawing.Stroke(canvas, bounds, Names.Equal(table.Name, state.SelectedTable) ? Theme.Accent : SKColor.Parse("A7B2BA"), Names.Equal(table.Name, state.SelectedTable) ? 1.5f : 1);
        }
        canvas.Restore();
    }
    private static string ShortType(FieldType type) => type switch { FieldType.AutoNumber => "Auto", FieldType.Integer => "Int", FieldType.YesNo => "Bool", FieldType.Currency => "Money", FieldType.DateTime => "Date", FieldType.Guid => "Guid", FieldType.Decimal => "Dec", _ => "Text" };
    public void Dispose() => _drawing.Dispose();
}
