using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class ColumnPruningTests
{
    private static DatabaseDocument Fixture(int rows = 100, int columns = 32)
    {
        var table = new TableDefinition { Name = "Wide" };
        for (var c = 0; c < columns; c++) table.Fields.Add(new() { Name = "F" + c, Type = FieldType.Decimal });
        for (var r = 0; r < rows; r++) RecordOperations.Insert(table,
            Enumerable.Range(0, columns).ToDictionary(c => "F" + c, c => (string?)((r * (c + 1)) % 17).ToString()));
        var document = new DatabaseDocument { Tables = [table] }; SchemaValidator.Validate(document); return document;
    }
    private static void Same(QueryResult left, QueryResult right)
    {
        Assert.Equal(left.Fields.Select(f => (f.Name, f.Type)), right.Fields.Select(f => (f.Name, f.Type)));
        Assert.Equal(left.Records.Count, right.Records.Count);
        for (var i = 0; i < left.Records.Count; i++)
            Assert.Equal(left.Fields.Select(f => left.Records[i][f.Name]), right.Fields.Select(f => right.Records[i][f.Name]));
    }
    [Theory]
    [InlineData("SELECT F0 FROM Wide ORDER BY F0")]
    [InlineData("SELECT TOP 10 F0 AS Output FROM Wide WHERE F2 >= 3 ORDER BY F3, Output")]
    [InlineData("SELECT w.F0 + w.F2 AS Output FROM Wide w WHERE w.F3 IN (1, 2, 3) ORDER BY Output DESC")]
    [InlineData("SELECT F0, Sum(F2) AS Total FROM Wide GROUP BY F0 HAVING Sum(F3) > 1 ORDER BY Total")]
    [InlineData("SELECT Count(*) AS N FROM Wide")]
    [InlineData("SELECT TOP 3 42 AS Constant FROM Wide")]
    [InlineData("SELECT * FROM Wide ORDER BY F3")]
    [InlineData("TRANSFORM Sum(F2) SELECT F0, Count(*) AS N FROM Wide WHERE F3 > 0 GROUP BY F0 ORDER BY F0 PIVOT F1 IN (1, 2, 3)")]
    public void ProjectionFilterGroupingOrderingAndPivotMatchUnprunedExecution(string sql)
    {
        var document = Fixture();
        var reference = new QueryEngine(new() { EnableColumnPruning = false }).Select(document, sql);
        foreach (var reuse in new[] { false, true }) foreach (var streaming in new[] { false, true })
        {
            var actual = new QueryEngine(new() { EnableReusableRowContexts = reuse, EnableStreamingAggregates = streaming }).Select(document, sql);
            Same(reference, actual); Assert.True(actual.Statistics.SourceValuesRead <= reference.Statistics.SourceValuesRead);
        }
    }
    [Fact]
    public void WideProjectionDecodesOnlyReferencedValues()
    {
        var document = Fixture();
        const string sql = "SELECT TOP 5 F0 FROM Wide WHERE F1 >= 0 ORDER BY F0";
        var optimized = new QueryEngine().Select(document, sql);
        var reference = new QueryEngine(new() { EnableColumnPruning = false }).Select(document, sql);
        Same(reference, optimized);
        Assert.Equal(100, optimized.Statistics.SourceRowsRead);
        Assert.Equal(200, optimized.Statistics.SourceValuesRead);
        Assert.Equal(3200, reference.Statistics.SourceValuesRead);
        Assert.Equal(5, optimized.Statistics.PeakSortRows);
    }
    [Fact]
    public void CountStarReadsNoTypedFieldValuesButStillHonorsRowsAndLimits()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT Count(*) AS N FROM Wide");
        Assert.Equal("100", result.Records[0]["N"]); Assert.Equal(0, result.Statistics.SourceValuesRead);
        Assert.Equal(100, result.Statistics.SourceRowsRead);
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumIntermediateRows = 10 }).Select(Fixture(), "SELECT Count(*) FROM Wide"));
    }
    [Fact]
    public void ExplicitParametersAndResultAliasesDoNotHideInputFields()
    {
        var document = Fixture(); var parameters = new Dictionary<string, object?> { ["F0"] = 23m, ["cutoff"] = 2m };
        const string sql = "SELECT F2 AS F0, @F0 AS Supplied FROM Wide WHERE F1 > @cutoff ORDER BY F0";
        var result = new QueryEngine().Select(document, sql, parameters);
        Same(new QueryEngine(new() { EnableColumnPruning = false }).Select(document, sql, parameters), result);
        Assert.All(result.Records, row => Assert.Equal("23", row["Supplied"]));
    }
    [Theory]
    [InlineData("SELECT Missing FROM Wide")]
    [InlineData("SELECT Count(*) FROM Wide WHERE Missing = 1")]
    [InlineData("SELECT TOP 0 F0 FROM Wide ORDER BY Missing")]
    [InlineData("TRANSFORM Sum(F0) SELECT F1 FROM Wide GROUP BY F1 PIVOT Missing")]
    public void BindingStillRejectsUnknownFieldsInEmptyTables(string sql)
    { Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(Fixture(0), sql)); }
    [Fact]
    public void SavedQueriesJoinsAndMakeTableRetainSemantics()
    {
        var document = Fixture();
        document.Queries.Add(new() { Name = "Filtered", Sql = "SELECT F0, F1 FROM Wide WHERE F2 > 0" });
        const string saved = "SELECT TOP 5 F0 FROM Filtered ORDER BY F1";
        Same(new QueryEngine(new() { EnableColumnPruning = false }).Select(document, saved), new QueryEngine().Select(document, saved));
        const string join = "SELECT a.F0, b.F1 FROM Wide a INNER JOIN Wide b ON a.F2=b.F2 WHERE a.F3=1 ORDER BY a.F0";
        Same(new QueryEngine(new() { EnableColumnPruning = false }).Select(document, join), new QueryEngine().Select(document, join));
        var workspace = new DatabaseWorkspace(document);
        new QueryEngine().Execute(workspace, "SELECT F0, F1 INTO Narrow FROM Wide WHERE F2 > 0");
        var expected = new QueryEngine(new() { EnableColumnPruning = false }).Select(document, "SELECT F0, F1 FROM Wide WHERE F2 > 0");
        Same(expected, QueryResult.FromTable(workspace.Document.Table("Narrow")));
    }
}
