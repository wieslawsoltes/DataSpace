using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class NumericAndCommentTests
{
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void LineCommentsNeverConsumeTheFollowingWhereClause(string newline)
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        for (var index = 0; index < 3; index++) RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Row" });
        var workspace = new DatabaseWorkspace(document); var engine = new QueryEngine();
        Assert.Single(engine.Select(workspace.Document, "SELECT ID FROM Table1 -- comment" + newline + "WHERE ID=2").Records);
        var action = engine.Execute(workspace, "DELETE FROM Table1 -- comment" + newline + "WHERE ID=2");
        Assert.Equal(1, action.AffectedRecords); Assert.Equal(new[] { "1", "3" }, workspace.Document.Table("Table1").Records.Select(r => r["ID"]));
    }
    [Theory]
    [InlineData(FieldType.Decimal)]
    [InlineData(FieldType.Currency)]
    public void EquivalentDecimalScalesCannotBypassUniqueConstraints(FieldType type)
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        RecordOperations.AddField(table, new() { Name = "Value", Type = type, Unique = true });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Value"] = "1.0" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Value"] = "2.00" });
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("duplicate", "Table1", [new(table.Records[1].Id, "Value", "1.000")]));
        Assert.Same(before, workspace.Document);
        workspace.UpdateRecords("same value", "Table1", [new(table.Records[0].Id, "Value", "1.000")]);
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo);
    }
    [Theory]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-0.0000000000000000000000000001")]
    public void CanonicalDecimalTextRoundTripsTheFullDecimalRange(string value)
    {
        var field = new FieldDefinition { Name = "Value", Type = FieldType.Decimal };
        Assert.Equal(value, FieldValues.Normalize(field, value));
        Assert.Equal(decimal.Parse(value, FieldValues.Culture), FieldValues.Parse(field, FieldValues.Normalize(field, value)));
    }
}
