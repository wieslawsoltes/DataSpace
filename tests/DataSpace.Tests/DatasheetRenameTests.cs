using DataSpace.Core;
using Xunit;

namespace DataSpace.Tests;

public sealed class DatasheetRenameTests
{
    [Theory]
    [InlineData("Name")]
    [InlineData("TITLE")]
    public void RenameKeepsColumnPresentationValuesAndUndo(string name)
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        table.Datasheet = new() { ColumnOrder = ["Title", "ID"], FrozenFields = ["title"], HiddenFields = ["ID"] };
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Value" });
        var workspace = new DatabaseWorkspace(document);
        workspace.Edit("rename", copy => RecordOperations.RenameField(copy, table.Name, "Title", name));
        var renamed = DocumentCodec.Clone(workspace.Document).Tables[0];
        Assert.Equal(new[] { name, "ID" }, renamed.Datasheet.ColumnOrder);
        Assert.Equal(name, Assert.Single(renamed.Datasheet.FrozenFields));
        Assert.Equal(name, Assert.Single(renamed.Datasheet.VisibleFields(renamed.Fields)).Name);
        Assert.Equal("Value", renamed.Records[0][name]);
        workspace.Undo(); Assert.Equal("Title", workspace.Document.Tables[0].Datasheet.ColumnOrder[0]);
        workspace.Redo(); Assert.Equal(name, workspace.Document.Tables[0].Datasheet.ColumnOrder[0]);
    }
    [Fact]
    public void RenameCyclesFollowFieldIdentity()
    {
        var table = new TableDefinition { Name = "T", Fields = [new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" }],
            Datasheet = new() { ColumnOrder = ["C", "A", "B"], FrozenFields = ["A"], HiddenFields = ["B"] } };
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["A"] = "First", ["B"] = "Second" });
        var workspace = new DatabaseWorkspace(new() { Tables = [table] });
        var draft = new TableSchemaDraft(workspace.Document, "T");
        draft.Fields[0].Field.Name = "B"; draft.Fields[1].Field.Name = "A"; draft.Apply(workspace);
        var result = workspace.Document.Tables[0];
        Assert.Equal(new[] { "C", "B", "A" }, result.Datasheet.ColumnOrder);
        Assert.Equal("B", Assert.Single(result.Datasheet.FrozenFields)); Assert.Equal("A", Assert.Single(result.Datasheet.HiddenFields));
        Assert.Equal("First", result.Records[0]["B"]); Assert.Equal("Second", result.Records[0]["A"]);
    }
    [Fact]
    public void StaleTargetSpellingCannotOverrideRenamedFieldPresentation()
    {
        var table = new TableDefinition { Name = "T", Fields = [new() { Name = "A" }, new() { Name = "C" }],
            Datasheet = new() { ColumnOrder = ["B", "C", "A"], HiddenFields = ["B"], FrozenFields = ["B"] } };
        var workspace = new DatabaseWorkspace(new() { Tables = [table] });
        workspace.Edit("rename", copy => RecordOperations.RenameField(copy, "T", "A", "B"));
        var result = workspace.Document.Tables[0];
        Assert.Equal(new[] { "C", "B" }, result.Datasheet.ColumnOrder);
        Assert.Empty(result.Datasheet.HiddenFields); Assert.Empty(result.Datasheet.FrozenFields);
        SchemaValidator.Validate(DocumentSnapshot.Copy(workspace.Document));
    }
}
