using DataSpace.Core;
using DataSpace.Rendering;
using SkiaSharp;
using Xunit;

namespace DataSpace.Tests;

public sealed class DatasheetEditorGeometryTests
{
    [Theory]
    [InlineData(1f)] [InlineData(1.25f)] [InlineData(2f)]
    public void NativeEditorCannotCoverFrozenColumnsHeadersOrTotals(float zoom)
    {
        using var renderer = new DatasheetRenderer();
        var fields = new[] { new FieldDefinition { Name = "Frozen", Width = 60 }, new FieldDefinition { Name = "Moving", Width = 100 } };
        var state = new DatasheetViewState { FrozenColumnCount = 1, OffsetX = 75, OffsetY = 18, RowHeight = 36, Zoom = zoom };
        Assert.Equal(new SKRect(89, 84, 114, 120), renderer.VisibleCellBounds(fields, 2, 1, state, 240 * zoom, 180 * zoom));
        Assert.Equal(new SKRect(29, 30, 89, 48), renderer.VisibleCellBounds(fields, 0, 0, state, 240 * zoom, 180 * zoom));
        state.ShowTotals = true;
        Assert.Equal(new SKRect(89, 48, 114, 74), renderer.VisibleCellBounds(fields, 1, 1, state, 240 * zoom, 110 * zoom));
    }
    [Fact]
    public void FullyOccludedOrInvalidCellsHaveNoEditorBounds()
    {
        using var renderer = new DatasheetRenderer();
        var fields = new[] { new FieldDefinition { Name = "Frozen", Width = 1000 }, new FieldDefinition { Name = "Moving", Width = 100 } };
        var state = new DatasheetViewState { FrozenColumnCount = 1 };
        Assert.True(renderer.VisibleCellBounds(fields, 0, 1, state, 240, 150).IsEmpty);
        Assert.True(renderer.VisibleCellBounds(fields, 100, 0, state, 240, 150).IsEmpty);
        Assert.True(renderer.VisibleCellBounds(fields, -1, 0, state, 240, 150).IsEmpty);
        Assert.True(renderer.VisibleCellBounds(fields, 0, 2, state, 240, 150).IsEmpty);
        Assert.True(renderer.VisibleCellBounds(fields, 0, 0, state, 0, 150).IsEmpty);
    }
}
