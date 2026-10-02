using DataSpace.Core;
using Xunit;

namespace DataSpace.Tests;

public sealed class DatasheetUiTests
{
    private static DatabaseWorkspace Workspace()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        RecordOperations.Insert(table, new Dictionary<string,string?> { ["Title"] = "Alpha alpha" });
        RecordOperations.Insert(table, new Dictionary<string,string?> { ["Title"] = "Beta" });
        RecordOperations.Insert(table, new Dictionary<string,string?> { ["Title"] = null });
        return new(document);
    }
    [Fact]
    public void LayoutOrderVisibilityAndFreezeDoNotReorderSchema()
    {
        var fields = new[] { new FieldDefinition { Name = "A" }, new FieldDefinition { Name = "B" }, new FieldDefinition { Name = "C" } };
        var layout = new DatasheetLayout { ColumnOrder = ["C", "B", "A"], FrozenFields = ["B"], HiddenFields = ["C"] };
        var visible = layout.VisibleFields(fields);
        Assert.Equal(new[] { "B", "A" }, visible.Select(f => f.Name)); Assert.Equal(1, layout.FrozenCount(visible));
        Assert.Equal(new[] { "A", "B", "C" }, fields.Select(f => f.Name));
    }
    [Fact]
    public void NewAndRemovedFieldsCannotLeaveAnEmptyView()
    {
        var fields = new[] { new FieldDefinition { Name = "New" } };
        var layout = new DatasheetLayout { ColumnOrder = ["Removed"], HiddenFields = ["New"], FrozenFields = ["Removed"] };
        Assert.Equal("New", Assert.Single(layout.VisibleFields(fields)).Name); Assert.Equal(0, layout.FrozenCount(fields));
    }
    [Fact]
    public void MetadataTransactionSharesRecordsButDetachesSettingsAndWidth()
    {
        var workspace = Workspace(); var before = workspace.Document; var rows = before.Tables[0].Records.ToArray();
        var layout = new DatasheetLayout { ColumnOrder = ["Title", "ID"], HiddenFields = ["ID"], FontSize = 15, RowHeight = 35, Bold = true };
        workspace.ConfigureDatasheet("Table1", layout, "Title", 310);
        var table = workspace.Document.Tables[0];
        Assert.Equal(310, table.Field("Title").Width); Assert.Equal(220, before.Tables[0].Field("Title").Width);
        Assert.All(rows, row => Assert.Same(row, table.Records.First(r => r.Id == row.Id)));
        layout.HiddenFields.Clear(); Assert.Single(table.Datasheet.HiddenFields);
        var roundtrip = DocumentCodec.Clone(workspace.Document).Tables[0];
        Assert.Equal(35, roundtrip.Datasheet.RowHeight); Assert.True(roundtrip.Datasheet.Bold); Assert.Equal("Title", Assert.Single(roundtrip.Datasheet.VisibleFields(roundtrip.Fields)).Name);
        workspace.Undo(); Assert.Equal(13, workspace.Document.Tables[0].Datasheet.FontSize);
        workspace.Redo(); Assert.Equal(15, workspace.Document.Tables[0].Datasheet.FontSize);
    }
    [Theory]
    [InlineData(double.NaN, 13)] [InlineData(0, 13)] [InlineData(201, 13)] [InlineData(27, 100)]
    public void InvalidDimensionsPublishNothing(double height, double font)
    {
        var workspace = Workspace(); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.ConfigureDatasheet("Table1", new() { RowHeight = height, FontSize = font }));
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void InvalidColumnsAndStaleRevisionsFail()
    {
        var workspace = Workspace();
        Assert.Throws<DataSpaceException>(() => workspace.ConfigureDatasheet("Table1", new() { FrozenFields = ["ID", "id"] }));
        Assert.Throws<DataSpaceException>(() => workspace.ConfigureDatasheet("Table1", new(), expectedRevision: 999));
        Assert.Throws<DataSpaceException>(() => workspace.ConfigureDatasheet("Table1", new(), "Title", double.PositiveInfinity));
    }
    [Fact]
    public void LegacyDocumentsReceiveDefaultLayout()
    {
        var json = "{\"Name\":\"Legacy\",\"Tables\":[{\"Name\":\"T\",\"Fields\":[{\"Name\":\"Value\"}]}]}";
        var table = DocumentCodec.Deserialize(json).Tables[0]; Assert.Equal(27, table.Datasheet.RowHeight); Assert.Empty(table.Datasheet.HiddenFields);
    }
    [Fact]
    public void FindWrapsInBothDirectionsAndRespectsScope()
    {
        var table = Workspace().Document.Tables[0];
        var forward = new TableSearchOptions("a");
        var first = TableTextSearch.FindNext(table.Fields, table.Records, forward, onlyField: "Title")!;
        var second = TableTextSearch.FindNext(table.Fields, table.Records, forward, first.Row, first.Column, "Title")!;
        var wrap = TableTextSearch.FindNext(table.Fields, table.Records, forward, second.Row, second.Column, "Title")!;
        Assert.Equal(0, first.Row); Assert.Equal(1, second.Row); Assert.Equal(first, wrap);
        Assert.Equal(1, TableTextSearch.FindNext(table.Fields, table.Records, forward with { Backwards = true }, onlyField: "Title")!.Row);
        Assert.Null(TableTextSearch.FindNext(table.Fields, table.Records, forward, onlyField: "ID"));
    }
    [Theory]
    [InlineData(TextMatchMode.AnyPart, false, "Alpha", "X X")]
    [InlineData(TextMatchMode.AnyPart, true, "Alpha", "X alpha")]
    [InlineData(TextMatchMode.StartOfField, false, "alpha", "X alpha")]
    [InlineData(TextMatchMode.WholeField, false, "ALPHA ALPHA", "X")]
    public void ReplacementModesUseLiteralOrdinalText(TextMatchMode mode, bool matchCase, string text, string expected)
    {
        var table = Workspace().Document.Tables[0]; var options = new TableSearchOptions(text, mode, matchCase);
        var edit = Assert.Single(TableTextSearch.Replacements(table.Fields, table.Records, options, "X"));
        Assert.Equal(expected, edit.Value); Assert.Equal("Title", edit.Field);
    }
    [Fact]
    public void ReplaceAllIsAtomicAndUndoable()
    {
        var workspace = Workspace(); var table = workspace.Document.Tables[0];
        var edits = TableTextSearch.Replacements(table.Fields, table.Records, new("a"), "z", "Title");
        workspace.UpdateRecords("replace", table.Name, edits);
        Assert.Equal("zlphz zlphz", workspace.Document.Tables[0].Records[0]["Title"]?.ToLowerInvariant());
        Assert.Null(workspace.Document.Tables[0].Records[2]["Title"]);
        workspace.Undo(); Assert.Equal("Alpha alpha", workspace.Document.Tables[0].Records[0]["Title"]);
        workspace.Redo(); Assert.Equal("Betz", workspace.Document.Tables[0].Records[1]["Title"]);
    }
    [Fact]
    public void FormattedFindDoesNotPermitFormattedReplace()
    {
        var fields = new[] { new FieldDefinition { Name = "Value", Type = FieldType.Decimal, Format = "F2" } };
        var rows = new[] { new Record { Values = new() { ["Value"] = "1.5" } } };
        Assert.NotNull(TableTextSearch.FindNext(fields, rows, new("1.50", Formatted: true)));
        Assert.Null(TableTextSearch.FindNext(fields, rows, new("1.50")));
        Assert.Throws<DataSpaceException>(() => TableTextSearch.Replacements(fields, rows, new("1.50", Formatted: true), "2"));
    }
    [Fact]
    public void AutoNumbersAreNeverReplacedAndLiteralWildcardsDoNotExpand()
    {
        var table = Workspace().Document.Tables[0];
        Assert.Empty(TableTextSearch.Replacements(table.Fields, table.Records, new("1"), "9", "ID"));
        Assert.Null(TableTextSearch.FindNext(table.Fields, table.Records, new("*")));
    }
    [Fact]
    public void ExpansionAndCountBudgetsStopBeforeBuildingOversizedEdits()
    {
        var fields = new[] { new FieldDefinition { Name = "T" } };
        var rows = new[] { new Record { Values = new() { ["T"] = "aaaa" } }, new Record { Values = new() { ["T"] = "a" } } };
        Assert.Throws<DataSpaceException>(() => TableTextSearch.Replacements(fields, rows, new("a"), "long", maximumCharacters: 15));
        Assert.Throws<DataSpaceException>(() => TableTextSearch.Replacements(fields, rows, new("a"), "b", maximumEdits: 1));
        Assert.ThrowsAny<OperationCanceledException>(() => TableTextSearch.Replacements(fields, rows, new("a"), "b", cancellationToken: new(true)));
        Assert.ThrowsAny<OperationCanceledException>(() => TableTextSearch.FindNext(fields, rows, new("a"), cancellationToken: new(true)));
    }
    [Fact]
    public void FailedTypedReplacementDoesNotApplyOtherValidCells()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        table.Fields.Add(new() { Name = "Amount", Type = FieldType.Integer });
        RecordOperations.Insert(table, new Dictionary<string,string?> { ["Title"] = "12", ["Amount"] = "12" });
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        var edits = TableTextSearch.Replacements(before.Tables[0].Fields, before.Tables[0].Records, new("12"), "bad");
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("replace", "Table1", edits)); Assert.Same(before, workspace.Document);
    }
}
