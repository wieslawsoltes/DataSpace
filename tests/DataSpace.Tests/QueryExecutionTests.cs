using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class QueryExecutionTests
{
    private static string[] Values(QueryResult result) => result.Records.Select(row => FieldValues.Key(result.Fields.Select(f => row[f.Name]))).ToArray();
    [Theory]
    [InlineData("SELECT c.Company,o.ID FROM Customers c INNER JOIN Orders o ON c.ID=o.[Customer ID] ORDER BY o.ID")]
    [InlineData("SELECT c.ID,o.ID AS O FROM Customers c LEFT JOIN Orders o ON c.ID=o.[Customer ID] AND o.ID<2 ORDER BY c.ID")]
    [InlineData("SELECT a.ID,b.ID AS B FROM Customers a INNER JOIN Customers b ON a.Country=b.Country AND a.City=b.City ORDER BY a.ID,b.ID")]
    [InlineData("SELECT a.ID,b.ID AS B FROM Customers a INNER JOIN Customers b ON a.ID<b.ID WHERE b.ID<6 ORDER BY a.ID,b.ID")]
    [InlineData("SELECT c.Company,Sum(o.Amount) AS Total FROM Customers c INNER JOIN Orders o ON c.ID=o.[Customer ID] GROUP BY c.Company ORDER BY c.Company")]
    public void HashAndReferenceJoinsProduceIdenticalResults(string sql)
    {
        var document = SampleDatabase.Create();
        var optimized = new QueryEngine().Select(document, sql);
        var reference = new QueryEngine(new() { EnableHashJoins = false }).Select(document, sql);
        Assert.Equal(Values(reference), Values(optimized)); Assert.Equal(reference.Fields.Select(f => f.Type), optimized.Fields.Select(f => f.Type));
    }
    [Fact]
    public void IndexedJoinEliminatesUnrelatedCandidatePairs()
    {
        var document = new DatabaseDocument(); var a = ObjectFactory.CreateTable(document); var b = ObjectFactory.CreateTable(document);
        for (var i = 0; i < 500; i++) { RecordOperations.Insert(a, new Dictionary<string, string?> { ["Title"] = "A" }); RecordOperations.Insert(b, new Dictionary<string, string?> { ["Title"] = "B" }); }
        var sql = "SELECT a.ID FROM Table1 a INNER JOIN Table2 b ON a.ID=b.ID";
        var optimized = new QueryEngine().Select(document, sql);
        var reference = new QueryEngine(new() { EnableHashJoins = false }).Select(document, sql);
        Assert.Equal(500, optimized.Records.Count); Assert.Equal(1, optimized.Statistics.HashJoins);
        Assert.Equal(500, optimized.Statistics.JoinComparisons); Assert.Equal(250000, reference.Statistics.JoinComparisons);
        Assert.Equal(Values(reference), Values(optimized));
    }
    [Fact]
    public void NullJoinKeysDoNotMatchAndCaseInsensitiveKeysDo()
    {
        var document = new DatabaseDocument(); var a = ObjectFactory.CreateTable(document); var b = ObjectFactory.CreateTable(document);
        foreach (var text in new string?[] { null, "Case", "other" }) RecordOperations.Insert(a, new Dictionary<string, string?> { ["Title"] = text });
        foreach (var text in new string?[] { null, "case", "case" }) RecordOperations.Insert(b, new Dictionary<string, string?> { ["Title"] = text });
        const string sql = "SELECT a.ID,b.ID AS B FROM Table1 a LEFT JOIN Table2 b ON a.Title=b.Title ORDER BY a.ID,b.ID";
        var optimized = new QueryEngine().Select(document, sql); Assert.Equal(4, optimized.Records.Count);
        Assert.Equal(Values(new QueryEngine(new() { EnableHashJoins = false }).Select(document, sql)), Values(optimized));
    }
    [Fact]
    public void StreamingTopStopsReadingAtRequiredRow()
    {
        var result = new QueryEngine().Select(SampleDatabase.Create(), "SELECT TOP 2 ID FROM Customers WHERE ID>4");
        Assert.Equal(new[] { "5", "6" }, result.Records.Select(r => r["ID"])); Assert.Equal(6, result.Statistics.SourceRowsRead);
        Assert.Equal(0, new QueryEngine().Select(SampleDatabase.Create(), "SELECT TOP 0 ID FROM Customers").Statistics.SourceRowsRead);
    }
    [Theory]
    [InlineData("SELECT NULL AS Value UNION SELECT NULL", 1)]
    [InlineData("SELECT 1 AS Value UNION ALL SELECT 1", 2)]
    [InlineData("SELECT 'Alpha' AS Value UNION SELECT 'alpha'", 1)]
    [InlineData("SELECT 1 AS Value UNION ALL SELECT 1 UNION SELECT 2", 2)]
    [InlineData("SELECT 1 AS Value UNION SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 2", 3)]
    public void UnionImplementsDistinctAllNullAndLeftAssociativeSemantics(string sql, int count)
    { var engine = new QueryEngine(); Assert.True(engine.IsReadOnly(sql)); Assert.Equal(count, engine.Select(new(), sql).Records.Count); }
    [Fact]
    public void UnionUsesFirstColumnNamesAndGlobalOrder()
    {
        var result = new QueryEngine().Select(new(), "SELECT 10 AS Number UNION ALL SELECT 2 AS Different ORDER BY Number");
        Assert.Equal("Number", result.Fields[0].Name); Assert.Equal(new[] { "2", "10" }, result.Records.Select(r => r["Number"]));
    }
    [Fact]
    public void TableUnionSupportsSavedQuerySourcesAndRejectsMismatchedColumns()
    {
        var document = SampleDatabase.Create(); var engine = new QueryEngine();
        Assert.Equal(18, engine.Select(document, "TABLE Customers UNION TABLE Customers").Records.Count);
        Assert.Throws<DataSpaceException>(() => engine.Select(document, "SELECT ID FROM Customers UNION SELECT ID,Company FROM Customers"));
        Assert.Throws<DataSpaceException>(() => engine.Select(document, "SELECT ID FROM Customers ORDER BY ID UNION SELECT ID FROM Orders"));
    }
    [Fact]
    public void SavedQueriesCanBeUsedAsSourcesWithDefaultAndOverriddenParameters()
    {
        var document = SampleDatabase.Create(); document.Queries.Add(new() { Name = "Recent", Sql = "SELECT ID,Company FROM Customers WHERE ID>=@min", Parameters = new() { ["min"] = "16" } });
        var engine = new QueryEngine(); Assert.Equal(3, engine.Select(document, "SELECT * FROM Recent").Records.Count);
        Assert.Single(engine.Select(document, "SELECT * FROM Recent", new Dictionary<string, object?> { ["min"] = 18 }).Records);
        document.Queries.Add(new() { Name = "Nested", Sql = "SELECT Company FROM Recent" });
        Assert.Equal(3, engine.Select(document, "TABLE Nested").Records.Count);
    }
    [Fact]
    public void CircularAndActionSavedSourcesFailWithoutMutation()
    {
        var document = SampleDatabase.Create(); document.Queries.Add(new() { Name = "CycleA", Sql = "SELECT * FROM CycleB" }); document.Queries.Add(new() { Name = "CycleB", Sql = "SELECT * FROM CycleA" });
        var before = DocumentCodec.Serialize(document); var engine = new QueryEngine();
        Assert.Throws<DataSpaceException>(() => engine.Select(document, "SELECT * FROM CycleA"));
        document.Queries.Add(new() { Name = "Action", Sql = "DELETE FROM Customers" });
        Assert.Throws<DataSpaceException>(() => engine.Select(document, "SELECT * FROM Action")); Assert.Equal(18, document.Table("Customers").Records.Count);
        document.Queries.RemoveAt(document.Queries.Count - 1); Assert.Equal(before, DocumentCodec.Serialize(document));
    }
    [Fact]
    public void AppendSelectAndSelfAppendAreAtomicAndUndoable()
    {
        var workspace = new DatabaseWorkspace(new()); var engine = new QueryEngine();
        engine.Execute(workspace, "CREATE TABLE People (ID COUNTER PRIMARY KEY, Name TEXT(30))");
        engine.Execute(workspace, "INSERT INTO People (Name) SELECT 'A' UNION ALL SELECT 'B'");
        Assert.Equal(2, engine.Execute(workspace, "INSERT INTO People (Name) SELECT Name FROM People").AffectedRecords);
        Assert.Equal(4, workspace.Document.Table("People").Records.Count); workspace.Undo(); Assert.Equal(2, workspace.Document.Table("People").Records.Count);
        var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "INSERT INTO People (ID,Name) SELECT 1,'duplicate' UNION ALL SELECT 50,'valid'"));
        Assert.Same(before, workspace.Document);
    }
    [Fact]
    public void EmptyQueriesStillBindUnknownAndAmbiguousFields()
    {
        var document = new DatabaseDocument(); ObjectFactory.CreateTable(document);
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(document, "SELECT Missing FROM Table1"));
        Assert.Throws<OperationCanceledException>(() => new QueryEngine().Select(document, "SELECT 1 UNION SELECT 2", cancellationToken: new(true)));
    }
}
