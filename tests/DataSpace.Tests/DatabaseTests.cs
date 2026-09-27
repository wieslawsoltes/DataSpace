using DataSpace.Core;
using DataSpace.Query;
using DataSpace.Storage;
using Xunit;

namespace DataSpace.Tests;

public sealed class DatabaseTests
{
    [Fact] public void SampleDatabaseHasValidRelationsAndObjects()
    {
        var document = SampleDatabase.Create(); SchemaValidator.Validate(document);
        Assert.Equal(18, document.Table("Customers").Records.Count);
        Assert.Equal(48, document.Table("Orders").Records.Count);
        Assert.Equal(96, document.Table("Order Details").Records.Count);
        Assert.Equal(3, document.Relationships.Count);
    }
    [Fact] public void JsonRoundTripPreservesEveryObjectAndRecord()
    {
        var document = SampleDatabase.Create(); var json = DocumentCodec.Serialize(document);
        Assert.Equal(json, DocumentCodec.Serialize(DocumentCodec.Deserialize(json)));
    }
    [Fact] public void FailedTransactionDoesNotChangeDocumentOrUndoHistory()
    {
        var workspace = new DatabaseWorkspace(SampleDatabase.Create()); var original = DocumentCodec.Serialize(workspace.Document);
        Assert.Throws<DataSpaceException>(() => workspace.Edit("invalid", d => d.Table("Customers").Records[0]["Company"] = null));
        Assert.Equal(original, DocumentCodec.Serialize(workspace.Document)); Assert.False(workspace.CanUndo);
    }
    [Fact] public void UndoRedoAreAtomicAndRevisionsAreMonotonic()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        w.Edit("rename", d => d.Table("Customers").Records[0]["Company"] = "Updated");
        Assert.Equal(1, w.Document.Revision); w.Undo(); Assert.Equal("Northwind Traders", w.Document.Table("Customers").Records[0]["Company"]);
        Assert.Equal(2, w.Document.Revision); w.Redo(); Assert.Equal("Updated", w.Document.Table("Customers").Records[0]["Company"]); Assert.Equal(3, w.Document.Revision);
    }
    [Fact] public void StaleRevisionIsRejected()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create()); w.Edit("edit", d => d.Name = "Changed");
        Assert.Throws<DataSpaceException>(() => w.Edit("stale", d => d.Name = "Lost", 0)); Assert.Equal("Changed", w.Document.Name);
    }
    [Fact] public void PrimaryKeyAndForeignKeyConstraintsAreEnforced()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        Assert.Throws<DataSpaceException>(() => w.Edit("duplicate", d => d.Table("Customers").Records[1]["ID"] = "1"));
        Assert.Throws<DataSpaceException>(() => w.Edit("orphan", d => d.Table("Orders").Records[0]["Customer ID"] = "999"));
        Assert.Throws<DataSpaceException>(() => w.Edit("delete parent", d => RecordOperations.Delete(d, "Customers", [d.Table("Customers").Records[0].Id])));
    }
    [Fact] public void CascadeDeleteRemovesOnlyRelatedChildren()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        w.Edit("delete order", d => RecordOperations.Delete(d, "Orders", [d.Table("Orders").Records[0].Id]));
        Assert.Equal(47, w.Document.Table("Orders").Records.Count); Assert.Equal(94, w.Document.Table("Order Details").Records.Count);
        w.Undo(); Assert.Equal(96, w.Document.Table("Order Details").Records.Count);
    }
    [Fact] public void CascadeUpdateChangesForeignKeys()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        w.Edit("cascade", d => { d.Relationships[0].CascadeUpdate = true; RecordOperations.Update(d, "Customers", d.Table("Customers").Records[0].Id, new Dictionary<string, string?> { ["ID"] = "101" }); });
        Assert.Equal(3, w.Document.Table("Orders").Records.Count(r => r["Customer ID"] == "101"));
    }
    [Fact] public void AutoNumbersNeverReuseDeletedValues()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        w.Edit("delete", d => RecordOperations.Delete(d, "Orders", [d.Table("Orders").Records[^1].Id]));
        w.Edit("add", d => RecordOperations.Insert(d.Table("Orders"), new Dictionary<string, string?> { ["Customer ID"] = "1" }));
        Assert.Equal("49", w.Document.Table("Orders").Records[^1]["ID"]);
    }
    [Fact] public void CompositeUniqueIndexRejectsDuplicates()
    {
        var d = SampleDatabase.Create(); var t = d.Table("Customers");
        t.Indexes.Add(new() { Name = "CompanyCity", Fields = ["Company", "City"], Unique = true }); SchemaValidator.Validate(d);
        t.Records[1]["Company"] = t.Records[0]["Company"]; t.Records[1]["City"] = t.Records[0]["City"];
        Assert.Throws<DataSpaceException>(() => SchemaValidator.Validate(d));
    }
    [Theory]
    [InlineData(FieldType.YesNo, "yes", "True")]
    [InlineData(FieldType.YesNo, "0", "False")]
    [InlineData(FieldType.Integer, "+0042", "42")]
    [InlineData(FieldType.Currency, "12.34567", "12.3457")]
    public void FieldNormalizationIsTyped(FieldType type, string input, string expected)
        => Assert.Equal(expected, FieldValues.Normalize(new() { Name = "Value", Type = type }, input));
    [Theory][InlineData("")][InlineData(" a")][InlineData("a.b")][InlineData("x[y]")]
    public void InvalidNamesAreRejected(string name) => Assert.Throws<DataSpaceException>(() => Names.Validate(name));
    [Fact] public void RenameFieldUpdatesRelationsFormsAndValues()
    {
        var w = new DatabaseWorkspace(SampleDatabase.Create());
        w.Edit("rename", d => RecordOperations.RenameField(d, "Customers", "ID", "CustomerKey"));
        Assert.Equal("1", w.Document.Table("Customers").Records[0]["CustomerKey"]);
        Assert.Equal("CustomerKey", w.Document.Relationships[0].ParentField);
        Assert.Equal("CustomerKey", w.Document.Forms[0].Controls[0].Field);
    }
}
