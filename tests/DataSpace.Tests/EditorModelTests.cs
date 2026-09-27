using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class EditorModelTests
{
    private static DatabaseWorkspace Create()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Bravo" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Alpha" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Alpha" });
        return new(document);
    }
    [Fact]
    public void FilteredSortedRowsRetainDistinctStableIdentities()
    {
        var workspace = Create(); var source = workspace.Document.Tables[0];
        var view = TableView.Select(workspace.Document, source.Name, "[Title] = 'Alpha'", "ID", true);
        Assert.Equal(source.Records.Skip(1).Reverse().Select(row => row.Id), view.Records.Select(row => row.Id));
        Assert.Equal(2, view.Records.Select(row => row.Id).Distinct().Count());
        var id = view.Records[0].Id;
        workspace.Edit("edit", document => RecordOperations.Update(document, source.Name, id, new Dictionary<string, string?> { ["Title"] = "Edited" }));
        Assert.Equal("Edited", workspace.Document.Tables[0].Records.Single(row => row.Id == id)["Title"]);
        Assert.Equal(1, workspace.Document.Tables[0].Records.Count(row => row["Title"] == "Alpha"));
    }
    [Fact]
    public void TableViewDoesNotExposeMutableLiveRowsOrMetadata()
    {
        var workspace = Create(); var table = workspace.Document.Tables[0]; var view = TableView.Select(workspace.Document, table.Name);
        view.Records[0]["Title"] = "Preview"; view.Fields[0].Width = 999;
        Assert.Equal("Bravo", table.Records[0]["Title"]); Assert.Equal(70, table.Fields[0].Width);
    }
    [Fact]
    public void TableViewSearchAndTypedNumericSortWork()
    {
        var workspace = Create(); var table = workspace.Document.Tables[0];
        var view = TableView.Select(workspace.Document, table.Name, sortField: "ID", descending: true, search: "alPHa");
        Assert.Equal(new[] { "3", "2" }, view.Records.Select(row => row["ID"]));
    }
    [Theory]
    [InlineData("COUNT(*) > 1")]
    [InlineData("1 = 1); DELETE FROM [Table1]; --")]
    [InlineData("[Missing] = 1")]
    public void InvalidTableFiltersCannotMutateTheDocument(string filter)
    {
        var workspace = Create(); var original = DocumentCodec.Serialize(workspace.Document);
        Assert.ThrowsAny<Exception>(() => TableView.Select(workspace.Document, "Table1", filter));
        Assert.Equal(original, DocumentCodec.Serialize(workspace.Document));
    }
    [Fact]
    public void SchemaConversionFailureIsAtomicAndDraftSurvives()
    {
        var workspace = Create(); var before = workspace.Document;
        var draft = new TableSchemaDraft(workspace.Document, "Table1"); draft.Fields[1].Field.Type = FieldType.Integer;
        Assert.Throws<DataSpaceException>(() => draft.Apply(workspace));
        Assert.Same(before, workspace.Document); Assert.Equal(FieldType.Integer, draft.Fields[1].Field.Type); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void NewFieldDefaultIsAppliedToEveryExistingRecordAndUndoRestores()
    {
        var workspace = Create(); var ids = workspace.Document.Tables[0].Records.Select(row => row.Id).ToArray();
        var draft = new TableSchemaDraft(workspace.Document, "Table1");
        draft.Fields.Add(new(null, new() { Name = "Quantity", Type = FieldType.Integer, Required = true, DefaultValue = "7" })); draft.Apply(workspace);
        Assert.All(workspace.Document.Tables[0].Records, row => Assert.Equal("7", row["Quantity"]));
        Assert.Equal(ids, workspace.Document.Tables[0].Records.Select(row => row.Id)); workspace.Undo(); Assert.Equal(2, workspace.Document.Tables[0].Fields.Count);
    }
    [Fact]
    public void RenameCyclePreservesValuesAndUpdatesBindings()
    {
        var workspace = Create(); workspace.Edit("field", document => RecordOperations.AddField(document.Table("Table1"), new() { Name = "Other", DefaultValue = "Other value" }));
        var before = workspace.Document.Tables[0].Records[0]; var oldTitle = before["Title"];
        var draft = new TableSchemaDraft(workspace.Document, "Table1"); draft.Fields[1].Field.Name = "Other"; draft.Fields[2].Field.Name = "Title"; draft.Apply(workspace);
        var after = workspace.Document.Tables[0].Records[0]; Assert.Equal(oldTitle, after["Other"]); Assert.Equal("Other value", after["Title"]); Assert.Equal(before.Id, after.Id);
    }
    [Fact]
    public void StaleSchemaDraftDoesNotOverwriteConcurrentEdits()
    {
        var workspace = Create(); var draft = new TableSchemaDraft(workspace.Document, "Table1");
        workspace.Edit("name", document => document.Name = "Updated");
        Assert.Throws<DataSpaceException>(() => draft.Apply(workspace)); Assert.Equal("Updated", workspace.Document.Name);
    }
    [Fact]
    public void SavedQueryDependenciesBlockUnsafeFieldRenaming()
    {
        var workspace = Create(); workspace.Edit("query", document => ObjectFactory.CreateQuery(document, "Table1"));
        var draft = new TableSchemaDraft(workspace.Document, "Table1"); draft.Fields[1].Field.Name = "Renamed";
        Assert.Throws<DataSpaceException>(() => draft.Apply(workspace)); Assert.Equal("Title", workspace.Document.Tables[0].Fields[1].Name);
    }
    [Fact]
    public void FactoriesCreateValidatedIndependentObjectsWithUniqueNames()
    {
        var workspace = Create();
        workspace.Edit("objects", document =>
        {
            ObjectFactory.CreateTable(document); ObjectFactory.CreateQuery(document, "Table1"); ObjectFactory.CreateQuery(document, "Table1");
            ObjectFactory.CreateForm(document, "Table1"); ObjectFactory.CreateReport(document, "Table1"); ObjectFactory.CreateMacro(document, "Table1");
        });
        SchemaValidator.Validate(DocumentCodec.Clone(workspace.Document));
        Assert.Equal(2, workspace.Document.Tables.Count); Assert.Equal(2, workspace.Document.Queries.Select(q => q.Name).Distinct().Count());
        Assert.NotEmpty(workspace.Document.Forms[0].Controls);
    }
}
