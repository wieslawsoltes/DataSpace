using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class AggregationAndTopKTests
{
    private static DatabaseDocument Fixture(int count = 1000)
    {
        var table = new TableDefinition { Name = "Items", Fields = [new() { Name = "ID", Type = FieldType.Integer }, new() { Name = "Category" }, new() { Name = "Value", Type = FieldType.Decimal }] };
        var random = new Random(2749);
        for (var i = 0; i < count; i++)
            RecordOperations.Insert(table, new Dictionary<string, string?> { ["ID"] = i.ToString(), ["Category"] = "Group " + (i % 7), ["Value"] = i % 11 == 0 ? null : random.Next(-20, 60).ToString() });
        return new() { Tables = [table] };
    }
    private static void Equal(QueryResult expected, QueryResult actual)
    {
        Assert.Equal(expected.Fields.Select(f => (f.Name, f.Type)), actual.Fields.Select(f => (f.Name, f.Type)));
        Assert.Equal(expected.Records.Count, actual.Records.Count);
        for (var i = 0; i < expected.Records.Count; i++)
            Assert.Equal(expected.Fields.Select(f => expected.Records[i][f.Name]), actual.Fields.Select(f => actual.Records[i][f.Name]));
    }
    [Theory]
    [InlineData("SELECT Category, Count(*) AS N, Sum(Value) AS S, Avg(Value) AS A, Min(Value) AS L, Max(Value) AS H FROM Items GROUP BY Category ORDER BY S DESC")]
    [InlineData("SELECT Count(Value) AS N, Sum(Value)+1 AS S FROM Items HAVING Count(*)>10")]
    [InlineData("SELECT Category, IIf(Count(Value)>0,Sum(Value)/Count(Value),0) AS M FROM Items GROUP BY Category HAVING M>0 ORDER BY Avg(Value)")]
    [InlineData("SELECT Category, First(Value) AS F, Last(Value) AS L FROM Items GROUP BY Category")]
    [InlineData("SELECT Category FROM Items GROUP BY Category")]
    [InlineData("SELECT Count(*) AS N, Min(Value) AS V FROM Items WHERE 1=0")]
    [InlineData("SELECT Category, Count(*) AS N FROM Items WHERE 1=0 GROUP BY Category")]
    public void StreamingAggregatesMatchBufferedReference(string sql)
    {
        var document = Fixture();
        var expected = new QueryEngine(new() { EnableStreamingAggregates = false }).Select(document, sql);
        var actual = new QueryEngine().Select(document, sql); Equal(expected, actual);
        Assert.Equal(0, actual.Statistics.BufferedAggregateRows); Assert.InRange(actual.Statistics.PeakAggregateGroups, 0, 7);
    }
    [Theory]
    [InlineData("SELECT TOP 7 ID,Value FROM Items ORDER BY Value DESC, ID")]
    [InlineData("SELECT ID, Value AS V FROM Items ORDER BY V, ID DESC LIMIT 13 OFFSET 9")]
    [InlineData("SELECT TOP 5 DISTINCT Value FROM Items ORDER BY Value")]
    [InlineData("SELECT TOP 3 Category, Sum(Value) AS S FROM Items GROUP BY Category ORDER BY S DESC")]
    [InlineData("SELECT TOP 0 ID FROM Items ORDER BY ID")]
    [InlineData("SELECT ID,Value FROM Items ORDER BY 2,1 DESC LIMIT 5 OFFSET 10000")]
    [InlineData("SELECT TOP 9 ID FROM Items ORDER BY Value")]
    public void TopKMatchesStableFullSort(string sql)
    {
        var document = Fixture();
        var expected = new QueryEngine(new() { EnableTopKSort = false }).Select(document, sql);
        var actual = new QueryEngine().Select(document, sql); Equal(expected, actual);
        Assert.InRange(actual.Statistics.PeakSortRows, 0, expected.Statistics.PeakSortRows);
    }
    [Fact]
    public void TopKBufferIsBoundedByLimitPlusOffset()
    {
        var result = new QueryEngine().Select(Fixture(10000), "SELECT ID, Value FROM Items ORDER BY Value DESC LIMIT 10 OFFSET 20");
        Assert.Equal(10000, result.Statistics.SortCandidateRows); Assert.Equal(30, result.Statistics.PeakSortRows); Assert.Equal(10, result.Records.Count);
    }
    [Fact]
    public void RandomizedPlansMatchReferencePaths()
    {
        var document = Fixture(321); var fast = new QueryEngine(); var reference = new QueryEngine(new() { EnableTopKSort = false, EnableStreamingAggregates = false });
        var random = new Random(6921);
        for (var i = 0; i < 60; i++)
        {
            var sql = i % 2 == 0 ? $"SELECT ID,Value FROM Items ORDER BY Value {(i % 3 == 0 ? "DESC" : "ASC")} LIMIT {random.Next(1, 40)} OFFSET {random.Next(0, 100)}"
                : $"SELECT Category, Sum(Value) AS S, Count(*) AS N FROM Items WHERE ID>{random.Next(0, 200)} GROUP BY Category ORDER BY S LIMIT {random.Next(1, 9)}";
            Equal(reference.Select(document, sql), fast.Select(document, sql));
        }
    }
    [Fact]
    public void OptimizationsDoNotBypassSafetyBoundsOrCancellation()
    {
        var document = Fixture(); const string sql = "SELECT TOP 1 ID FROM Items ORDER BY ID DESC";
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumIntermediateRows = 10 }).Select(document, sql));
        Assert.Throws<OperationCanceledException>(() => new QueryEngine().Select(document, sql, cancellationToken: new(true)));
        foreach (var options in new[] { new QueryOptions { MaximumCrosstabColumns = 0 }, new() { MaximumCrosstabCells = 0 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueryEngine(options));
    }
}
