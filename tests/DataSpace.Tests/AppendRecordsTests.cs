using DataSpace.Core;
using Xunit;

namespace DataSpace.Tests;

public sealed class AppendRecordsTests
{
    private static DatabaseWorkspace Workspace()
    {
        var document = new DatabaseDocument();
        var table = ObjectFactory.CreateTable(document);
        table.Fields.Add(new() { Name = "Amount", Type = FieldType.Decimal, DefaultValue = "1.50" });
        RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Original" });
        return new(document);
    }
    [Fact]
    public void UsesDefaultsAndKeepsOldRecordsSharedWithoutMutatingThem()
    {
        var workspace = Workspace(); var before = workspace.Document; var old = before.Table("Table1").Records[0];
        var oldValues = old.Values; var input = new string?[] { "New" }; var changes = 0;
        workspace.Changed += (_, _) => changes++;
        Assert.Equal(1, workspace.AppendRecords("append", "Table1", ["title"], new[] { input }));
        var table = workspace.Document.Table("Table1");
        Assert.Same(old, table.Records[0]); Assert.Same(oldValues, old.Values);
        Assert.Single(before.Table("Table1").Records); Assert.Equal(2, before.Table("Table1").NextAutoNumber);
        Assert.Equal("2", table.Records[1]["ID"]); Assert.Equal("1.5", table.Records[1]["Amount"]); Assert.Equal(3, table.NextAutoNumber);
        input[0] = "Mutated"; Assert.Equal("New", table.Records[1]["Title"]);
        Assert.Equal(1, changes); Assert.True(workspace.CanUndo);
        workspace.Undo(); Assert.Single(workspace.Document.Table("Table1").Records);
        workspace.Redo(); Assert.Equal(2, workspace.Document.Table("Table1").Records.Count);
    }
    [Theory]
    [InlineData("ID", "1")]
    [InlineData("ID", "9223372036854775807")]
    [InlineData("Amount", "invalid")]
    public void ConversionAndDuplicateErrorsPreserveDocumentHistoryAndGenerator(string column, string value)
    {
        var workspace = Workspace(); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("append", "Table1", [column], new[] { new string?[] { value } }));
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo); Assert.Equal(2, before.Table("Table1").NextAutoNumber);
    }
    [Fact]
    public void ExplicitAutoNumberAdvancesSubsequentGeneratedValues()
    {
        var workspace = Workspace();
        workspace.AppendRecords("append", "Table1", ["ID"], new[] { new string?[] { "40" }, new string?[] { null } });
        Assert.Equal(new[] { "1", "40", "41" }, workspace.Document.Table("Table1").Records.Select(row => row["ID"]));
        Assert.Equal(42, workspace.Document.Table("Table1").NextAutoNumber);
    }
    [Fact]
    public void SelfAppendEnumeratesPreStatementRowsOnce()
    {
        var workspace = Workspace(); var source = workspace.Document.Table("Table1");
        workspace.AppendRecords("self append", source.Name, ["Title"], source.Records.Select(row => new string?[] { row["Title"] }));
        Assert.Equal(2, workspace.Document.Table("Table1").Records.Count); Assert.Single(source.Records);
        Assert.NotEqual(source.Records[0].Id, workspace.Document.Table("Table1").Records[1].Id);
    }
    [Fact]
    public void CompositeUniqueAndForeignKeysAreCheckedWithoutMutatingOldRows()
    {
        var document = new DatabaseDocument();
        var parent = new TableDefinition { Name = "Parent", Fields = [new() { Name = "ID", Type = FieldType.Integer, PrimaryKey = true }] };
        var child = new TableDefinition { Name = "Child", Fields = [new() { Name = "PID", Type = FieldType.Integer }, new() { Name = "Value" }],
            Indexes = [new() { Name = "Pair", Unique = true, Fields = ["PID", "Value"] }] };
        RecordOperations.Insert(parent, new Dictionary<string, string?> { ["ID"] = "1" });
        RecordOperations.Insert(child, new Dictionary<string, string?> { ["PID"] = "1", ["Value"] = "One" });
        document.Tables = [parent, child]; document.Relationships = [new() { Name = "FK", ParentTable = "Parent", ParentField = "ID", ChildTable = "Child", ChildField = "PID", EnforceIntegrity = true }];
        var workspace = new DatabaseWorkspace(document); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("duplicate", "Child", ["PID", "Value"], new[] { new string?[] { "1", "one" } }));
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("orphan", "Child", ["PID", "Value"], new[] { new string?[] { "2", "Two" } }));
        Assert.Same(before, workspace.Document);
        workspace.AppendRecords("valid", "Child", ["PID", "Value"], new[] { new string?[] { "1", "Two" } });
        SchemaValidator.Validate(DocumentSnapshot.Copy(workspace.Document));
    }
    [Fact]
    public void SelfReferencingBatchCanResolveLaterParent()
    {
        var table = new TableDefinition { Name = "Nodes", Fields = [new() { Name = "ID", Type = FieldType.Integer, PrimaryKey = true }, new() { Name = "Parent", Type = FieldType.Integer }] };
        var workspace = new DatabaseWorkspace(new() { Tables = [table], Relationships = [new() { Name = "Tree", ParentTable = "Nodes", ParentField = "ID", ChildTable = "Nodes", ChildField = "Parent", EnforceIntegrity = true }] });
        workspace.AppendRecords("nodes", "Nodes", ["ID", "Parent"], new[] { new string?[] { "2", "1" }, new string?[] { "1", null } });
        Assert.Equal(2, workspace.Document.Table("Nodes").Records.Count);
    }
    [Fact]
    public void InvalidMappingFailsBeforeInputEnumeration()
    {
        var workspace = Workspace();
        IEnumerable<IReadOnlyList<string?>> Never() => throw new InvalidOperationException("Enumeration was attempted.");
        // Return a lazy iterator so validation, not argument evaluation, is tested.
        IEnumerable<IReadOnlyList<string?>> Rows() { foreach (var row in Never()) yield return row; }
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("append", "Table1", ["ID", "id"], Rows()));
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("append", "Table1", ["Unknown"], Rows()));
    }
    [Fact]
    public void WrongShapeLateExceptionAndCancellationRollbackWholeBatch()
    {
        var workspace = Workspace(); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("shape", "Table1", ["Title"], new[] { new string?[] { "valid" }, Array.Empty<string?>() }));
        IEnumerable<IReadOnlyList<string?>> Fails() { yield return new string?[] { "valid" }; throw new IOException("source failure"); }
        Assert.Throws<IOException>(() => workspace.AppendRecords("source", "Table1", ["Title"], Fails()));
        using var cancellation = new CancellationTokenSource();
        IEnumerable<IReadOnlyList<string?>> Cancels() { yield return new string?[] { "valid" }; cancellation.Cancel(); }
        Assert.ThrowsAny<OperationCanceledException>(() => workspace.AppendRecords("cancel", "Table1", ["Title"], Cancels(), cancellationToken: cancellation.Token));
        Assert.Same(before, workspace.Document); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void StaleNestedAndOversizedTransactionsAreRejected()
    {
        var workspace = Workspace(); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("stale", "Table1", ["Title"], [], 999));
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("budget", "Table1", ["Title"], new[] { new string?[] { "large" } }, maximumCharacters: 2));
        IEnumerable<IReadOnlyList<string?>> Nested() { workspace.Edit("bad", document => document.Name = "Changed"); yield break; }
        Assert.Throws<DataSpaceException>(() => workspace.AppendRecords("nested", "Table1", ["Title"], Nested()));
        Assert.Same(before, workspace.Document);
        Assert.Equal(0, workspace.AppendRecords("empty", "Table1", ["Title"], [])); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void MappingIsFrozenBeforeSourceEnumerationAndExplicitNullDoesNotUseDefault()
    {
        var workspace = Workspace(); var columns = new[] { "Amount" };
        IEnumerable<IReadOnlyList<string?>> Rows() { columns[0] = "Title"; yield return new string?[] { null }; }
        workspace.AppendRecords("append", "Table1", columns, Rows());
        Assert.Null(workspace.Document.Table("Table1").Records[1]["Amount"]);
    }
    [Fact]
    public void RandomizedBatchesMatchFullyDetachedReference()
    {
        var random = new Random(4119);
        for (var sample = 0; sample < 25; sample++)
        {
            var optimized = Workspace(); var reference = Workspace();
            var values = Enumerable.Range(0, random.Next(1, 20)).Select(index => new string?[] { "Title " + index, (random.Next(0, 1000) / 10m).ToString(FieldValues.Culture) }).ToArray();
            optimized.AppendRecords("append", "Table1", ["Title", "Amount"], values);
            reference.Edit("reference", document => { foreach (var row in values) RecordOperations.Insert(document.Table("Table1"), new Dictionary<string, string?> { ["Title"] = row[0], ["Amount"] = row[1] }); });
            var actual = optimized.Document.Table("Table1"); var expected = reference.Document.Table("Table1");
            Assert.Equal(expected.NextAutoNumber, actual.NextAutoNumber);
            Assert.Equal(expected.Records.Select(row => string.Join("|", expected.Fields.Select(field => row[field.Name]))), actual.Records.Select(row => string.Join("|", actual.Fields.Select(field => row[field.Name]))));
            SchemaValidator.Validate(DocumentSnapshot.Copy(optimized.Document));
        }
    }
}
