using DataSpace.Core;
using DataSpace.Rendering;
using SkiaSharp;
using Xunit;

namespace DataSpace.Tests;

public sealed class DatasheetColumnSelectionTests
{
    private static FieldDefinition[] Fields() => Enumerable.Range(0, 4)
        .Select(index => new FieldDefinition { Name = "F" + index, Width = 80 }).ToArray();

    [Fact]
    public void SelectionExtendsInEitherDirectionWithoutMovingTheCurrentRow()
    {
        var state = new DatasheetViewState { SelectedRow = 8, AnchorRow = 8 };
        state.SelectColumns(2, 4); state.SelectColumns(0, 4, true);
        Assert.Equal((0, 2), state.SelectedColumns(4)); Assert.Equal((0, 999999), state.SelectedRows(1000000));
        Assert.Equal(8, state.SelectedRow); Assert.Equal(8, state.AnchorRow);
        state.SelectColumns(3, 4, true); Assert.Equal((2, 3), state.SelectedColumns(4));
        state.SelectColumns(1, 4); Assert.Equal((1, 1), state.SelectedColumns(4));
    }

    [Fact]
    public void EmptyAndClampedRangesRemainWellDefined()
    {
        var state = new DatasheetViewState();
        state.SelectColumns(-99, 4); state.SelectColumns(99, 4, true);
        Assert.Equal((0, 3), state.SelectedColumns(4)); Assert.Equal((0, -1), state.SelectedRows(0));
        state.SelectColumns(0, 0); Assert.False(state.WholeColumnSelection); Assert.Equal((0, -1), state.SelectedColumns(0));
        state.SelectedRow = 3; state.AnchorRow = 1; Assert.Equal((1, 3), state.SelectedRows(4));
    }

    [Fact]
    public void GroupCommandsUsePresentationOrderAndLeaveTheSourceUntouched()
    {
        var fields = Fields(); var layout = new DatasheetLayout { ColumnOrder = ["F3", "F2", "F1", "F0"] };
        var frozen = DatasheetFieldCommands.Freeze(fields, layout, ["f1", "F2"]);
        Assert.Equal(new[] { "F2", "F1" }, frozen.FrozenFields);
        Assert.Equal(new[] { "F2", "F1", "F3", "F0" }, frozen.VisibleFields(fields).Select(field => field.Name));
        Assert.Empty(layout.FrozenFields); Assert.Equal("F0", fields[0].Name);
        var hidden = DatasheetFieldCommands.Hide(fields, frozen, ["F1", "F2"]);
        Assert.Equal(new[] { "F3", "F0" }, hidden.VisibleFields(fields).Select(field => field.Name));
        Assert.Empty(frozen.HiddenFields);
    }

    [Fact]
    public void HidingEveryRemainingColumnRejectsWithoutChangingTheLayout()
    {
        var layout = new DatasheetLayout { HiddenFields = ["F0", "F3"] }; var fields = Fields();
        Assert.Throws<DataSpaceException>(() => DatasheetFieldCommands.Hide(fields, layout, ["F1", "F2"]));
        Assert.Equal(new[] { "F0", "F3" }, layout.HiddenFields);
    }

    [Theory]
    [InlineData("missing", "F1")]
    [InlineData("F1", "f1")]
    [InlineData("F0", "F1")]
    public void InvalidOrHiddenNamesRejectTheEntireCommand(string first, string second)
    {
        var layout = new DatasheetLayout { HiddenFields = ["F0"] };
        Assert.Throws<DataSpaceException>(() => DatasheetFieldCommands.Freeze(Fields(), layout, [first, second]));
        Assert.Empty(layout.FrozenFields);
    }

    [Fact]
    public void MultiFieldLayoutIsOneUndoablePresentationOnlyTransaction()
    {
        var table = new TableDefinition { Name = "T", Fields = Fields().ToList() };
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["F1"] = "unchanged" });
        var workspace = new DatabaseWorkspace(new() { Tables = [table] }); var before = workspace.Document;
        var layout = DatasheetFieldCommands.Freeze(before.Tables[0].Fields, before.Tables[0].Datasheet, ["F1", "F2"]);
        workspace.ConfigureDatasheet("T", layout);
        Assert.Same(before.Tables[0].Records[0], workspace.Document.Tables[0].Records[0]);
        Assert.Equal(new[] { "F1", "F2" }, DocumentCodec.Clone(workspace.Document).Tables[0].Datasheet.FrozenFields);
        workspace.Undo(); Assert.Empty(workspace.Document.Tables[0].Datasheet.FrozenFields);
        workspace.Redo(); Assert.Equal(new[] { "F1", "F2" }, workspace.Document.Tables[0].Datasheet.FrozenFields);
    }

    [Theory]
    [InlineData(1f, 0)] [InlineData(1f, 2)] [InlineData(2f, 2)]
    public void ColumnSelectionPaintsHeadersAndExistingRowsButNotTheNewRecord(float zoom, int count)
    {
        using var renderer = new DatasheetRenderer(); var fields = Fields();
        var state = new DatasheetViewState { FrozenColumnCount = 1, AlternateRows = false, Zoom = zoom };
        state.SelectColumns(2, fields.Length); state.SelectColumns(1, fields.Length, true);
        var rows = Enumerable.Range(0, count).Select(_ => new Record()).ToArray();
        using var bitmap = new SKBitmap((int)(400 * zoom), (int)(160 * zoom)); using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, bitmap.Width, bitmap.Height, fields, rows, state);
        Assert.Equal(renderer.Theme.Header, bitmap.GetPixel((int)(34 * zoom), (int)(3 * zoom)));
        Assert.Equal(renderer.Theme.SelectionHeader, bitmap.GetPixel((int)(112 * zoom), (int)(3 * zoom)));
        Assert.Equal(renderer.Theme.SelectionHeader, bitmap.GetPixel((int)(192 * zoom), (int)(3 * zoom)));
        if (count > 0)
        {
            Assert.Equal(renderer.Theme.Selection, bitmap.GetPixel((int)(112 * zoom), (int)(40 * zoom)));
            Assert.Equal(renderer.Theme.Surface, bitmap.GetPixel((int)(34 * zoom), (int)(40 * zoom)));
        }
        Assert.Equal(renderer.Theme.Surface, bitmap.GetPixel((int)(112 * zoom), (int)((30 + count * 27 + 6) * zoom)));
    }
}
