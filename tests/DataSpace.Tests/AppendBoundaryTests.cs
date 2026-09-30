using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class AppendBoundaryTests
{
    [Fact]
    public void FilteredOrderedViewMapsIntoAnotherTableWithoutTouchingSource()
    {
        var document = new DatabaseDocument();
        var source = ObjectFactory.CreateTable(document);
        foreach (var value in new[] { "Alpha", "Bravo", "Charlie" })
            RecordOperations.Insert(source, new Dictionary<string, string?> { ["Title"] = value });
        var destination = ObjectFactory.CreateTable(document);
        destination.Fields[1].Name = "Name";
        var workspace = new DatabaseWorkspace(document);
        var original = workspace.Document.Table(source.Name);
        var view = TableView.Open(workspace.Document, source.Name, "[ID] >= 2", "ID", true);
        workspace.AppendRecords("mapped append", destination.Name, ["Name"],
            view.Select(row => new string?[] { row["Title"] }));
        var target = workspace.Document.Table(destination.Name);
        Assert.Equal(new[] { "Charlie", "Bravo" }, target.Records.Select(row => row["Name"]));
        Assert.Equal(new[] { "1", "2" }, target.Records.Select(row => row["ID"]));
        Assert.Equal(3, workspace.Document.Table(source.Name).Records.Count);
        Assert.Same(original.Records[0], workspace.Document.Table(source.Name).Records[0]);
        Assert.All(target.Records, row => Assert.DoesNotContain(original.Records, old => old.Id == row.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too long")]
    public void RequiredAndLengthFailuresKeepRedoAvailable(string? value)
    {
        var table = new TableDefinition { Name = "Values", Fields = [new() { Name = "Text", Required = true, MaxLength = 3 }] };
        var workspace = new DatabaseWorkspace(new() { Tables = [table] });
        workspace.Edit("name", document => document.Name = "Changed"); workspace.Undo();
        var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("invalid", "Values", ["Text"], new[] { new string?[] { value } }));
        Assert.Same(before, workspace.Document); Assert.True(workspace.CanRedo);
        workspace.Redo(); Assert.Equal("Changed", workspace.Document.Name);
    }

    [Fact]
    public void GeneratedGuidAndTypedValuesUseDestinationSemantics()
    {
        var table = new TableDefinition { Name = "Values", Fields = [
            new() { Name = "Key", Type = FieldType.Guid, PrimaryKey = true },
            new() { Name = "Flag", Type = FieldType.YesNo },
            new() { Name = "Money", Type = FieldType.Currency },
            new() { Name = "When", Type = FieldType.DateTime } ] };
        var workspace = new DatabaseWorkspace(new() { Tables = [table] });
        workspace.AppendRecords("typed", "Values", ["Flag", "Money", "When"],
            new[] { new string?[] { "-1", "1.23456", "2000-02-03" } });
        var row = Assert.Single(workspace.Document.Table("Values").Records);
        Assert.True(Guid.TryParse(row["Key"], out _)); Assert.Equal("True", row["Flag"]);
        Assert.Equal("1.2346", row["Money"]); Assert.StartsWith("2000-02-03T", row["When"]!);
        SchemaValidator.Validate(DocumentSnapshot.Copy(workspace.Document));
    }

    [Fact]
    public void BudgetCountsGeneratedValuesAndNullRowsRollback()
    {
        var document = new DatabaseDocument(); ObjectFactory.CreateTable(document);
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("budget", "Table1", ["Title"],
            new[] { new string?[] { "abc" } }, maximumCharacters: 3));
        IReadOnlyList<string?>[] malformed = [new string?[] { "valid" }, null!];
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("null row", "Table1", ["Title"], malformed));
        Assert.Same(before, workspace.Document);
        Assert.Equal(1, workspace.AppendRecords("exact budget", "Table1", ["Title"],
            new[] { new string?[] { "abc" } }, maximumCharacters: 4));
        Assert.Equal("1", workspace.Document.Table("Table1").Records[0]["ID"]);
    }
    [Fact]
    public void MappingCallbacksCannotStartNestedTransactions()
    {
        var document = new DatabaseDocument(); ObjectFactory.CreateTable(document);
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        var columns = new ReentrantColumns(() => workspace.Edit("nested", candidate => candidate.Name = "Changed"));
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("append", "Table1", columns, new[] { new string?[] { "Value" } }));
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo);
    }

    private sealed class ReentrantColumns(Action callback) : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] { get { callback(); return "Title"; } }
        public IEnumerator<string> GetEnumerator() { yield return this[0]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
