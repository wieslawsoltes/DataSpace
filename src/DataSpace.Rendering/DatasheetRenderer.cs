using DataSpace.Core;
using SkiaSharp;

namespace DataSpace.Rendering;

public enum GridHitKind { None, Cell, ColumnHeader, ColumnResize, RowHeader, Corner, NewRecord }
public readonly record struct GridHit(GridHitKind Kind, int Row = -1, int Column = -1);
public sealed class DatasheetViewState
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public int SelectedRow { get; set; }
    public int SelectedColumn { get; set; }
    public int AnchorRow { get; set; }
    public int AnchorColumn { get; set; }
    public string? SortField { get; set; }
    public bool SortDescending { get; set; }
    public bool ReadOnly { get; set; }
    public bool ShowTotals { get; set; }
    public bool ShowNewRecord { get; set; } = true;
    public float Zoom { get; set; } = 1;
}

/// <summary>Viewport-only rendering, snapshot-cached totals and deterministic hit testing.</summary>
public sealed class DatasheetRenderer : IDisposable
{
    public OfficeTheme Theme { get; }
    private readonly DrawingResources _drawing;
    private readonly TableTotalsCache _totals = new();
    public DatasheetRenderer(OfficeTheme? theme = null) { Theme = theme ?? OfficeTheme.Default; _drawing = new(Theme); }
    public void InvalidateTotals() => _totals.Invalidate();
    public float ContentWidth(IReadOnlyList<FieldDefinition> fields) => Theme.RowHeaderWidth + fields.Sum(f => (float)f.Width);
    public float ContentHeight(int records, DatasheetViewState state) => Theme.ColumnHeaderHeight + (records + (state.ShowNewRecord && !state.ReadOnly ? 1 : 0)) * Theme.RowHeight;
    public SKRect CellBounds(IReadOnlyList<FieldDefinition> fields, int row, int column, DatasheetViewState state)
    {
        var left = Theme.RowHeaderWidth - state.OffsetX;
        for (var i = 0; i < column && i < fields.Count; i++) left += (float)fields[i].Width;
        var top = Theme.ColumnHeaderHeight + row * Theme.RowHeight - state.OffsetY;
        return new(left, top, left + (column >= 0 && column < fields.Count ? (float)fields[column].Width : 0), top + Theme.RowHeight);
    }
    public GridHit HitTest(IReadOnlyList<FieldDefinition> fields, int records, DatasheetViewState state, float x, float y)
    {
        x /= state.Zoom; y /= state.Zoom;
        if (x < 0 || y < 0) return new(GridHitKind.None);
        if (x < Theme.RowHeaderWidth && y < Theme.ColumnHeaderHeight) return new(GridHitKind.Corner);
        var column = -1; var left = Theme.RowHeaderWidth - state.OffsetX;
        for (var i = 0; i < fields.Count; i++)
        {
            var right = left + (float)fields[i].Width;
            if (y < Theme.ColumnHeaderHeight && Math.Abs(x - right) < 4 && x >= Theme.RowHeaderWidth) return new(GridHitKind.ColumnResize, Column: i);
            if (x >= left && x < right) { column = i; break; } left = right;
        }
        if (y < Theme.ColumnHeaderHeight) return column >= 0 ? new(GridHitKind.ColumnHeader, Column: column) : new(GridHitKind.None);
        var row = (int)Math.Floor((y - Theme.ColumnHeaderHeight + state.OffsetY) / Theme.RowHeight);
        if (row < 0 || row > records) return new(GridHitKind.None);
        if (row == records) return state.ShowNewRecord && !state.ReadOnly ? new(GridHitKind.NewRecord, row, Math.Max(0, column)) : new(GridHitKind.None);
        if (x < Theme.RowHeaderWidth) return new(GridHitKind.RowHeader, row);
        return column >= 0 ? new(GridHitKind.Cell, row, column) : new(GridHitKind.None);
    }
    public void Draw(SKCanvas canvas, float width, float height, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> records, DatasheetViewState state)
    {
        canvas.Clear(Theme.Surface); canvas.Save(); canvas.Scale(state.Zoom);
        width /= state.Zoom; height /= state.Zoom;
        var bodyBottom = height - (state.ShowTotals ? Theme.RowHeight : 0);
        var first = Math.Max(0, (int)(state.OffsetY / Theme.RowHeight));
        var end = Math.Min(records.Count + (state.ShowNewRecord && !state.ReadOnly ? 1 : 0), first + (int)Math.Ceiling(bodyBottom / Theme.RowHeight) + 2);
        var minRow = Math.Min(state.AnchorRow, state.SelectedRow); var maxRow = Math.Max(state.AnchorRow, state.SelectedRow);
        var minColumn = Math.Min(state.AnchorColumn, state.SelectedColumn); var maxColumn = Math.Max(state.AnchorColumn, state.SelectedColumn);
        canvas.Save(); canvas.ClipRect(new(Theme.RowHeaderWidth, Theme.ColumnHeaderHeight, width, bodyBottom));
        for (var row = first; row < end; row++)
        {
            var y = Theme.ColumnHeaderHeight + row * Theme.RowHeight - state.OffsetY;
            if (row % 2 != 0) _drawing.Fill(canvas, new(Theme.RowHeaderWidth, y, width, y + Theme.RowHeight), Theme.AlternateRow);
            var x = Theme.RowHeaderWidth - state.OffsetX;
            var record = row < records.Count ? records[row] : null;
            for (var column = 0; column < fields.Count; column++)
            {
                var field = fields[column]; var rect = new SKRect(x, y, x + (float)field.Width, y + Theme.RowHeight); x = rect.Right;
                if (rect.Right < Theme.RowHeaderWidth || rect.Left > width) continue;
                if (row >= minRow && row <= maxRow && column >= minColumn && column <= maxColumn) _drawing.Fill(canvas, rect, Theme.Selection);
                _drawing.Line(canvas, rect.Right - .5f, y, rect.Right - .5f, rect.Bottom, Theme.GridLine);
                if (record is not null)
                {
                    if (field.Type == FieldType.YesNo)
                    {
                        var check = new SKRect(rect.MidX - 5, rect.MidY - 5, rect.MidX + 5, rect.MidY + 5);
                        _drawing.Fill(canvas, check, Theme.Surface); _drawing.Stroke(canvas, check, Theme.MutedText);
                        if (FieldValues.Parse(field, record[field.Name]) is true)
                        {
                            using var path = new SKPath(); path.MoveTo(check.Left + 2, check.MidY); path.LineTo(check.MidX - 1, check.Bottom - 2); path.LineTo(check.Right - 1, check.Top + 2); _drawing.Path(canvas, path, Theme.Text, 1.6f);
                        }
                    }
                    else _drawing.CellText(canvas, FieldValues.Display(field, record[field.Name]), rect, Theme.Text, Theme.FontSize,
                        right: field.Type is FieldType.Integer or FieldType.AutoNumber or FieldType.Decimal or FieldType.Currency);
                }
                else if (field.Type == FieldType.AutoNumber) _drawing.CellText(canvas, "(New)", rect, Theme.MutedText, Theme.FontSize);
            }
            _drawing.Line(canvas, Theme.RowHeaderWidth, y + Theme.RowHeight - .5f, Math.Min(width, x), y + Theme.RowHeight - .5f, Theme.GridLine);
        }
        if (state.SelectedRow >= 0 && state.SelectedRow < records.Count && state.SelectedColumn >= 0 && state.SelectedColumn < fields.Count)
        {
            var active = CellBounds(fields, state.SelectedRow, state.SelectedColumn, state); active.Inflate(-1, -1); _drawing.Stroke(canvas, active, Theme.ActiveCell, 2);
        }
        canvas.Restore(); canvas.Save(); canvas.ClipRect(new(Theme.RowHeaderWidth, 0, width, Theme.ColumnHeaderHeight));
        _drawing.Fill(canvas, new(0, 0, width, Theme.ColumnHeaderHeight), Theme.Header);
        var headerX = Theme.RowHeaderWidth - state.OffsetX;
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i]; var rect = new SKRect(headerX, 0, headerX + (float)field.Width, Theme.ColumnHeaderHeight); headerX = rect.Right;
            if (rect.Right < Theme.RowHeaderWidth || rect.Left > width) continue;
            if (i == state.SelectedColumn) _drawing.Fill(canvas, rect, Theme.SelectionHeader);
            _drawing.Stroke(canvas, new(rect.Left - .5f, -.5f, rect.Right - .5f, rect.Bottom - .5f), Theme.GridLine);
            _drawing.CellText(canvas, field.DisplayName, new(rect.Left, rect.Top, rect.Right - 16, rect.Bottom), Theme.Text, Theme.FontSize);
            var cx = rect.Right - 10; var cy = rect.MidY + 1; using var arrow = new SKPath();
            if (Names.Equal(state.SortField, field.Name) && !state.SortDescending) { arrow.MoveTo(cx - 3, cy + 2); arrow.LineTo(cx + 3, cy + 2); arrow.LineTo(cx, cy - 2); }
            else { arrow.MoveTo(cx - 3, cy - 2); arrow.LineTo(cx + 3, cy - 2); arrow.LineTo(cx, cy + 2); }
            arrow.Close(); _drawing.Path(canvas, arrow, Theme.MutedText, fill: true);
        }
        canvas.Restore(); _drawing.Fill(canvas, new(0, 0, Theme.RowHeaderWidth, bodyBottom), Theme.Header);
        _drawing.Stroke(canvas, new(-.5f, -.5f, Theme.RowHeaderWidth - .5f, Theme.ColumnHeaderHeight - .5f), Theme.GridLine);
        for (var row = first; row < end; row++)
        {
            var y = Theme.ColumnHeaderHeight + row * Theme.RowHeight - state.OffsetY;
            if (y < Theme.ColumnHeaderHeight - Theme.RowHeight || y > bodyBottom) continue;
            canvas.Save(); canvas.ClipRect(new(0, Theme.ColumnHeaderHeight, Theme.RowHeaderWidth, bodyBottom));
            if (row >= minRow && row <= maxRow) _drawing.Fill(canvas, new(0, y, Theme.RowHeaderWidth, y + Theme.RowHeight), Theme.SelectionHeader);
            if (row == records.Count) _drawing.CellText(canvas, "*", new(2, y, Theme.RowHeaderWidth, y + Theme.RowHeight), Theme.Text, 17, true);
            else if (row == state.SelectedRow)
            {
                using var arrow = new SKPath(); arrow.MoveTo(10, y + 8); arrow.LineTo(16, y + Theme.RowHeight / 2); arrow.LineTo(10, y + Theme.RowHeight - 8); arrow.Close(); _drawing.Path(canvas, arrow, Theme.Text, fill: true);
            }
            _drawing.Line(canvas, 0, y + Theme.RowHeight - .5f, Theme.RowHeaderWidth, y + Theme.RowHeight - .5f, Theme.GridLine); canvas.Restore();
        }
        _drawing.Line(canvas, Theme.RowHeaderWidth - .5f, 0, Theme.RowHeaderWidth - .5f, bodyBottom, Theme.GridLine);
        if (state.ShowTotals) DrawTotals(canvas, width, height, fields, records, state);
        canvas.Restore();
    }
    private void DrawTotals(SKCanvas canvas, float width, float height, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> records, DatasheetViewState state)
    {
        var totals = _totals.GetValues(fields, records); var y = height - Theme.RowHeight;
        _drawing.Fill(canvas, new(0, y, width, height), Theme.Header); _drawing.Line(canvas, 0, y, width, y, Theme.GridLine);
        _drawing.CellText(canvas, "Σ", new(0, y, Theme.RowHeaderWidth, height), Theme.Text, 15, true);
        var x = Theme.RowHeaderWidth - state.OffsetX;
        canvas.Save(); canvas.ClipRect(new(Theme.RowHeaderWidth, y, width, height));
        for (var i = 0; i < fields.Count; i++)
        {
            var rect = new SKRect(x, y, x + (float)fields[i].Width, height); x = rect.Right;
            if (rect.Right < Theme.RowHeaderWidth || rect.Left > width) continue;
            _drawing.CellText(canvas, totals[i], rect, Theme.Text, Theme.FontSize, true, true);
            _drawing.Line(canvas, rect.Right - .5f, y, rect.Right - .5f, height, Theme.GridLine);
        }
        canvas.Restore();
    }
    public void Dispose() { _totals.Invalidate(); _drawing.Dispose(); }
}
