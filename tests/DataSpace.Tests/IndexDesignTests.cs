using DataSpace.Core;
using Xunit;
namespace DataSpace.Tests;
public sealed class IndexDesignTests
{
    private static DatabaseWorkspace Create()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Same" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Same" }); return new(document);
    }
    [Fact]
    public void CompositeIndexValidatesFinalTuplesAndIsUndoable()
    {
        var workspace = Create(); var design = new IndexDesign(workspace.Document, "Table1");
        design.Indexes.Add(new() { Name = "Composite", Unique = true, Fields = ["Title", "ID"] }); design.Apply(workspace);
        Assert.Equal(new[] { "Title", "ID" }, workspace.Document.Table("Table1").Indexes[0].Fields);
        design.Indexes[0].Fields.Remove("ID"); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => design.Apply(workspace)); Assert.Same(before, workspace.Document);
        workspace.Undo(); Assert.Empty(workspace.Document.Table("Table1").Indexes);
    }
    [Theory]
    [InlineData("Missing", "ID")]
    [InlineData("ID", "id")]
    public void UnknownAndDuplicateIndexFieldsAreRejected(string first, string second)
    {
        var workspace = Create(); var before = workspace.Document; var design = new IndexDesign(before, "Table1");
        design.Indexes.Add(new() { Name = "Invalid", Fields = [first, second] });
        Assert.Throws<DataSpaceException>(() => design.Apply(workspace)); Assert.Same(before, workspace.Document);
    }
    [Fact]
    public void StaleDraftAndDuplicateNamesCannotOverwriteIndexes()
    {
        var workspace = Create(); var design = new IndexDesign(workspace.Document, "Table1");
        workspace.Edit("name", d => d.Name = "Other"); Assert.Throws<DataSpaceException>(() => design.Apply(workspace));
        design = new(workspace.Document, "Table1");
        design.Indexes.Add(new() { Name = "Index", Fields = ["ID"] }); design.Indexes.Add(new() { Name = "index", Fields = ["Title"] });
        Assert.Throws<DataSpaceException>(() => design.Apply(workspace)); Assert.Empty(workspace.Document.Table("Table1").Indexes);
    }
    [Fact]
    public void FastRecordEditsRespectCompositeIndexes()
    {
        var workspace = Create(); var design = new IndexDesign(workspace.Document, "Table1");
        design.Indexes.Add(new() { Name = "Composite", Unique = true, Fields = ["ID", "Title"] }); design.Apply(workspace);
        var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("duplicate", "Table1", [new(before.Tables[0].Records[1].Id, "ID", "1")]));
        Assert.Same(before, workspace.Document);
    }
}
