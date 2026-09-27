using DataSpace.Core;
using Xunit;
namespace DataSpace.Tests;
public sealed class TransactionDifferentialTests
{
    [Fact]
    public void RandomizedTargetedEditsAgreeWithFullValidation()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        table.Field("Title").Unique = true; table.Field("Title").Required = true; table.Field("Title").MaxLength = 12;
        RecordOperations.AddField(table, new() { Name = "Amount", Type = FieldType.Currency });
        RecordOperations.AddField(table, new() { Name = "Flag", Type = FieldType.YesNo });
        for (var i = 0; i < 30; i++) RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Row " + i });
        var fast = new DatabaseWorkspace(document); var reference = new DatabaseWorkspace(document); var random = new Random(4815);
        string?[] text = [null, "", "Same", "Valid", "too-long-for-the-field"];
        string?[] amount = [null, "12.25", "-7", "oops", "14.33333"];
        string?[] flag = [null, "true", "no", "-1", "not-bool"];
        for (var step = 0; step < 120; step++)
        {
            var row = table.Records[random.Next(table.Records.Count)].Id; var kind = random.Next(3);
            var field = kind == 0 ? "Title" : kind == 1 ? "Amount" : "Flag"; var values = kind == 0 ? text : kind == 1 ? amount : flag; var value = values[random.Next(values.Length)];
            var fastError = Xunit.Record.Exception(() => fast.UpdateRecords("edit", "Table1", [new(row, field, value)]));
            var referenceError = Xunit.Record.Exception(() => reference.Edit("edit", d => RecordOperations.Update(d, "Table1", row, new Dictionary<string, string?> { [field] = value })));
            Assert.Equal(referenceError is not null, fastError is not null);
            var a = DocumentSnapshot.Copy(fast.Document); var b = DocumentSnapshot.Copy(reference.Document); a.Revision = b.Revision = 0;
            Assert.Equal(DocumentCodec.Serialize(b), DocumentCodec.Serialize(a));
        }
    }
    [Fact]
    public void CompositeValidationIsNotMaskedByThePrimaryKey()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        RecordOperations.AddField(table, new() { Name = "Category" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Same", ["Category"] = "A" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Same", ["Category"] = "B" });
        table.Indexes.Add(new() { Name = "Composite", Unique = true, Fields = ["Title", "Category"] });
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.UpdateRecords("duplicate", "Table1", [new(table.Records[1].Id, "Category", "a")]));
        Assert.Same(before, workspace.Document);
    }
}
