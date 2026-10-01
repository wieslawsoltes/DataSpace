using DataSpace.Core;
using DataSpace.Rendering;
using SkiaSharp;
using Xunit;

namespace DataSpace.Tests;

/// <summary>Real CPU Skia raster/geometry checks, not hardware-GPU or native UI qualification.</summary>
public sealed class DatasheetRenderingTests
{
    private static FieldDefinition[] Fields() =>
    [new() { Name = "Frozen", Width = 60 }, new() { Name = "Moving", Width = 100 }, new() { Name = "Last", Width = 90 }];
    private static Record[] Rows() => Enumerable.Range(0, 8).Select(index => new Record
        { Values = new() { ["Frozen"] = "Fixed " + index, ["Moving"] = "Middle " + index, ["Last"] = "End " + index } }).ToArray();

    [Theory]
    [InlineData(1f)] [InlineData(1.25f)] [InlineData(2f)]
    public void FrozenBoundsAndHitTestingAgreeAtDifferentZooms(float zoom)
    {
        using var renderer = new DatasheetRenderer(); var fields = Fields();
        var state = new DatasheetViewState { FrozenColumnCount = 1, OffsetX = 75, OffsetY = 18, RowHeight = 36, Zoom = zoom };
        Assert.Equal(89, renderer.FrozenEdge(fields, state));
        Assert.Equal(new SKRect(29, 84, 89, 120), renderer.CellBounds(fields, 2, 0, state));
        Assert.Equal(new SKRect(14, 84, 114, 120), renderer.CellBounds(fields, 2, 1, state));
        Assert.Equal(new GridHit(GridHitKind.Cell, 2, 0), renderer.HitTest(fields, 8, state, 35 * zoom, 90 * zoom));
        Assert.Equal(new GridHit(GridHitKind.Cell, 2, 1), renderer.HitTest(fields, 8, state, 94 * zoom, 90 * zoom));
        Assert.Equal(new GridHit(GridHitKind.Cell, 2, 2), renderer.HitTest(fields, 8, state, 125 * zoom, 90 * zoom));
        Assert.Equal(GridHitKind.ColumnResize, renderer.HitTest(fields, 8, state, 89 * zoom, 10 * zoom).Kind);
    }

    [Fact]
    public void ScrollingDoesNotChangeFrozenPixels()
    {
        using var renderer = new DatasheetRenderer(); var fields = Fields(); var rows = Rows();
        var state = new DatasheetViewState { FrozenColumnCount = 1, RowHeight = 36, FontSize = 15, ShowTotals = true };
        using var first = new SKBitmap(240, 180); using var second = new SKBitmap(240, 180);
        using (var canvas = new SKCanvas(first)) renderer.Draw(canvas, 240, 180, fields, rows, state);
        state.OffsetX = 75;
        using (var canvas = new SKCanvas(second)) renderer.Draw(canvas, 240, 180, fields, rows, state);
        for (var y = 0; y < 180; y++) for (var x = 31; x < 86; x++) Assert.Equal(first.GetPixel(x, y), second.GetPixel(x, y));
        var different = false;
        for (var y = 0; y < 180 && !different; y++) for (var x = 95; x < 235; x++) if (first.GetPixel(x, y) != second.GetPixel(x, y)) { different = true; break; }
        Assert.True(different, "The scrollable part must actually move.");
    }

    [Fact]
    public void DrawingPreservesParentClipTransformAndSaveDepth()
    {
        using var renderer = new DatasheetRenderer(); using var bitmap = new SKBitmap(300, 180); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Magenta); canvas.Translate(10, 10); canvas.ClipRect(new SKRect(0, 0, 240, 120));
        var depth = canvas.SaveCount; var matrix = canvas.TotalMatrix;
        renderer.Draw(canvas, 240, 120, Fields(), Rows(), new() { FrozenColumnCount = 1, OffsetX = 60 });
        Assert.Equal(depth, canvas.SaveCount); Assert.Equal(matrix, canvas.TotalMatrix);
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel(5, 5)); Assert.Equal(SKColors.Magenta, bitmap.GetPixel(255, 60));
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel(100, 135)); Assert.NotEqual(SKColors.Magenta, bitmap.GetPixel(15, 15));
    }

    [Fact]
    public void InvalidCellRestoresParentCanvasStateBeforePropagatingFailure()
    {
        using var renderer = new DatasheetRenderer(); using var bitmap = new SKBitmap(200, 120); using var canvas = new SKCanvas(bitmap);
        canvas.Translate(4, 5); var depth = canvas.SaveCount; var matrix = canvas.TotalMatrix;
        var fields = new[] { new FieldDefinition { Name = "Flag", Type = FieldType.YesNo } };
        var rows = new[] { new Record { Values = new() { ["Flag"] = "not a boolean" } } };
        Assert.Throws<DataSpaceException>(() => renderer.Draw(canvas, 180, 100, fields, rows, new()));
        Assert.Equal(depth, canvas.SaveCount); Assert.Equal(matrix, canvas.TotalMatrix);
    }

    [Fact]
    public void BestFitReadsOnlyItsBoundedSample()
    {
        using var renderer = new DatasheetRenderer(); var rows = new SampleOnlyRows();
        var width = renderer.MeasureColumn(new() { Name = "Value" }, rows, new(), maximumRows: 1);
        Assert.InRange(width, 40, 2000); Assert.Equal(1, rows.Reads);
    }
    private sealed class SampleOnlyRows : IReadOnlyList<Record>
    {
        public int Reads { get; private set; }
        public int Count => 1000000;
        public Record this[int index] { get { Assert.Equal(0, index); Reads++; return new() { Values = new() { ["Value"] = "Text" } }; } }
        public IEnumerator<Record> GetEnumerator() => throw new InvalidOperationException("Best Fit must not enumerate every record.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
