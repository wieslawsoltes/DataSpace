using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class PerformanceModelTests
{
    private static DatabaseWorkspace Workspace(int count = 20)
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        for (var index = 0; index < count; index++) RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Row " + index });
        return new(document);
    }
    [Fact]
    public void DirectSnapshotPreservesEverySerializedPropertyAndDetachesCollections()
    {
        var document = SampleDatabase.Create(); document.Queries[0].DesignerState = "state";
        var snapshot = DocumentSnapshot.Copy(document);
        Assert.Equal(DocumentCodec.Serialize(document), DocumentCodec.Serialize(snapshot));
        snapshot.Tables[0].Records[0]["Company"] = "Changed"; snapshot.Forms[0].Controls[0].Caption = "Changed";
        Assert.NotEqual(snapshot.Tables[0].Records[0]["Company"], document.Tables[0].Records[0]["Company"]);
        Assert.NotEqual(snapshot.Forms[0].Controls[0].Caption, document.Forms[0].Controls[0].Caption);
    }
    [Fact]
    public void LazyViewDoesNotMaterializeUnseenRowsAndHasBoundedCache()
    {
        var workspace = Workspace(10000); var view = TableView.Open(workspace.Document, "Table1");
        Assert.Equal(10000, view.Count); Assert.Equal(0, view.MaterializedRecordCount);
        var page = view.ReadPage(20, 30); Assert.Equal(30, view.MaterializedRecordCount);
        Assert.Equal("Row 20", page[0]["Title"]);
        Assert.Equal(20, view.IndexOfRecord(page[0].Id)); Assert.Equal(30, view.MaterializedRecordCount);
        _ = view.ReadPage(100, 600); Assert.Equal(VirtualTableView.CacheCapacity, view.CachedRecordCount);
        page[0]["Title"] = "Detached"; Assert.Equal("Row 20", workspace.Document.Table("Table1").Records[20]["Title"]);
    }
    [Fact]
    public void FastCellEditCopiesTouchedRowsAndProtectsHistory()
    {
        var workspace = Workspace(); var before = workspace.Document; var id = before.Tables[0].Records[0].Id;
        workspace.UpdateRecords("edit", "Table1", [new(id, "Title", "Updated")]);
        Assert.Equal("Row 0", before.Tables[0].Records[0]["Title"]);
        Assert.Same(before.Tables[0].Records[1], workspace.Document.Tables[0].Records[1]);
        Assert.NotSame(before.Tables[0].Records[0], workspace.Document.Tables[0].Records[0]);
        workspace.Undo(); Assert.Equal("Row 0", workspace.Document.Tables[0].Records[0]["Title"]);
        workspace.Redo(); Assert.Equal("Updated", workspace.Document.Tables[0].Records[0]["Title"]);
        workspace.Edit("generic", d => d.Tables[0].Records[1]["Title"] = "Generic");
        Assert.Equal("Row 1", before.Tables[0].Records[1]["Title"]);
    }
    [Fact]
    public void FastEditRetainsUniqueAndRequiredConstraintsAtomically()
    {
        var workspace = Workspace(); workspace.Edit("constraints", d => { d.Table("Table1").Field("Title").Unique = true; d.Table("Table1").Field("Title").Required = true; });
        var before = workspace.Document; var rows = before.Tables[0].Records;
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("duplicate", "Table1", [new(rows[0].Id, "Title", rows[1]["Title"])]));
        Assert.Same(before, workspace.Document);
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("null", "Table1", [new(rows[0].Id, "Title", null)]));
        Assert.Same(before, workspace.Document);
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("stale", "Table1", [new(rows[0].Id, "Title", "new")], -1));
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("missing", "Table1", [new("missing", "Title", "new")]));
        Assert.Same(before, workspace.Document);
    }
    [Fact]
    public void FastRangeEditsValidateFinalUniqueStateNotIntermediateState()
    {
        var workspace = Workspace(); workspace.Edit("unique", d => d.Table("Table1").Field("Title").Unique = true);
        var rows = workspace.Document.Tables[0].Records;
        workspace.UpdateRecords("swap", "Table1", [new(rows[0].Id, "Title", rows[1]["Title"]), new(rows[1].Id, "Title", rows[0]["Title"])]);
        Assert.Equal("Row 1", workspace.Document.Tables[0].Records[0]["Title"]);
        Assert.Equal("Row 0", workspace.Document.Tables[0].Records[1]["Title"]);
    }
    [Fact]
    public void FastEditsRetainReferentialChecksAndCascadeFallback()
    {
        var workspace = new DatabaseWorkspace(SampleDatabase.Create()); var before = workspace.Document;
        var order = before.Table("Orders").Records[0]; var parent = before.Table("Customers").Records[0];
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("orphan", "Orders", [new(order.Id, "Customer ID", "9999")]));
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("restricted", "Customers", [new(parent.Id, "ID", "200")]));
        Assert.Same(before, workspace.Document);
        workspace.Edit("enable cascade", document => document.Relationships.First(r => r.Name == "Customers_Orders").CascadeUpdate = true);
        workspace.UpdateRecords("cascade", "Customers", [new(parent.Id, "ID", "200")]);
        Assert.Equal("1", parent["ID"]); Assert.Contains(workspace.Document.Table("Orders").Records, r => r["Customer ID"] == "200");
        SchemaValidator.Validate(DocumentSnapshot.Copy(workspace.Document));
    }
    [Fact]
    public void NoOpCellEditDoesNotDirtyDocumentOrAddHistory()
    {
        var workspace = Workspace(); var before = workspace.Document; var row = before.Tables[0].Records[0];
        workspace.UpdateRecords("noop", "Table1", [new(row.Id, "Title", row["Title"])]);
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void FilterSortLazyAndEagerPathsAgreeAndOldViewsStayStable()
    {
        var workspace = Workspace(100); var original = workspace.Document;
        var view = TableView.Open(original, "Table1", "ID > 30", "ID", true, "Row");
        Assert.Equal(0, view.MaterializedRecordCount); Assert.Equal("100", view[0]["ID"]);
        var eager = TableView.Select(original, "Table1", "ID > 30", "ID", true, "Row");
        Assert.Equal(eager.Records.Select(r => r.Id), view.Select(r => r.Id));
        workspace.UpdateRecords("edit", "Table1", [new(view[0].Id, "Title", "Changed")]);
        Assert.Equal("Row 99", view[0]["Title"]);
    }
    [Fact]
    public void EmptyViewsValidateNamesAndCancellation()
    {
        var workspace = Workspace(0);
        Assert.Throws<DataSpaceException>(() => TableView.Open(workspace.Document, "Table1", "Missing=1"));
        Assert.Throws<DataSpaceException>(() => TableView.Open(workspace.Document, "Table1", sortField: "Missing"));
        Assert.Throws<OperationCanceledException>(() => TableView.Open(workspace.Document, "Table1", cancellationToken: new(true)));
    }
}
