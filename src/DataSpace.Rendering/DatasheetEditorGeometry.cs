using DataSpace.Core;
using SkiaSharp;

namespace DataSpace.Rendering;

/// <summary>Shared logical bounds for native editors over a zoomed/frozen Skia datasheet.</summary>
public static class DatasheetEditorGeometry
{
    public static SKRect VisibleCellBounds(this DatasheetRenderer renderer, IReadOnlyList<FieldDefinition> fields,
        int row, int column, DatasheetViewState state, float viewportWidth, float viewportHeight)
    {
        if (row < 0 || column < 0 || column >= fields.Count || !float.IsFinite(state.Zoom) || state.Zoom <= 0 ||
            !float.IsFinite(viewportWidth) || !float.IsFinite(viewportHeight)) return SKRect.Empty;
        var bounds = renderer.CellBounds(fields, row, column, state);
        var left = column < state.FrozenColumnCount ? renderer.Theme.RowHeaderWidth : renderer.FrozenEdge(fields, state);
        var right = viewportWidth / state.Zoom;
        var bottom = viewportHeight / state.Zoom - (state.ShowTotals ? state.RowHeight : 0);
        var clipped = new SKRect(Math.Max(left, bounds.Left), Math.Max(renderer.Theme.ColumnHeaderHeight, bounds.Top),
            Math.Min(right, bounds.Right), Math.Min(bottom, bounds.Bottom));
        return clipped.Width > 0 && clipped.Height > 0 ? clipped : SKRect.Empty;
    }
}
