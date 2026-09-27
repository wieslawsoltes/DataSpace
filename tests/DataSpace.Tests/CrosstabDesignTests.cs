using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class CrosstabDesignTests
{
    private static CrosstabDesign Design() => new()
    { Source = "Sales", RowFields = ["Region"], ColumnExpression = "'Q' & Quarter", ValueExpression = "Amount", Aggregate = CrosstabAggregate.Sum, ShowRowTotals = true, FixedHeadings = "'Q2', 'Q1', 'Q3'" };
    [Fact]
    public void BuilderRoundTripsWithoutLosingFixedOrderTotalsOrFilters()
    {
        var document = CrosstabAndMakeTableTests.Sales(); var model = Design(); model.Where = "Amount > 0";
        var sql = model.ToSql(document); var restored = CrosstabDesign.FromSql(sql); var after = restored.ToSql(document);
        Assert.Equal(sql, after); Assert.True(restored.ShowRowTotals); Assert.Equal(model.RowFields, restored.RowFields);
        var result = new QueryEngine().Select(document, after);
        Assert.Equal(new[] { "Region", "Row Total", "Q2", "Q1", "Q3" }, result.Fields.Select(field => field.Name));
        Assert.Equal("35", result.Records[1]["Row Total"]);
    }
    [Fact]
    public void BuilderSupportsTypedParameterHeadings()
    {
        var document = CrosstabAndMakeTableTests.Sales(); var model = Design(); model.FixedHeadings = "@column";
        var result = new QueryEngine().Select(document, model.ToSql(document), new Dictionary<string, object?> { ["column"] = "Q1" });
        Assert.Equal("30", result.Records[1]["Q1"]);
    }
    [Theory]
    [InlineData("SELECT * FROM Sales")]
    [InlineData("TRANSFORM SUM(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter")]
    [InlineData("TRANSFORM SUM(Amount) SELECT Region FROM Sales GROUP BY Region ORDER BY Region DESC PIVOT Quarter")]
    [InlineData("TRANSFORM SUM(Amount) SELECT Region FROM Sales GROUP BY Region HAVING SUM(Amount)>0 ORDER BY Region PIVOT Quarter")]
    public void UnsupportedRestorationsNeverApproximateSql(string sql)
        => Assert.Throws<DataSpaceException>(() => CrosstabDesign.FromSql(sql));
    [Fact]
    public void InvalidBuilderValuesRejectWithoutModifyingDocument()
    {
        var document = CrosstabAndMakeTableTests.Sales(); var before = DocumentCodec.Serialize(document);
        foreach (var mutate in new Action<CrosstabDesign>[] {
            model => model.RowFields.Clear(), model => model.RowFields.Add("region"), model => model.RowFields[0] = "Missing",
            model => model.ColumnExpression = "Sum(Quarter)", model => model.ValueExpression = "*",
            model => model.FixedHeadings = "'Q1'); DELETE FROM Sales; --", model => model.FixedHeadings = "Region",
            model => model.Where = "1=1; DELETE FROM Sales", model => model.Aggregate = (CrosstabAggregate)999 })
        {
            var model = Design(); mutate(model); Assert.Throws<DataSpaceException>(() => model.ToSql(document));
        }
        Assert.Equal(before, DocumentCodec.Serialize(document));
    }
    [Fact]
    public void EmptyPivotStillChecksHeadingAndGroupStateBudgets()
    {
        var document = CrosstabAndMakeTableTests.Sales();
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumCrosstabColumns = 1 }).Select(document,
            "TRANSFORM Count(*) SELECT Region, Count(*) AS N FROM Sales WHERE 1=0 GROUP BY Region PIVOT Quarter"));
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumCrosstabCells = 1 }).Select(document,
            "TRANSFORM Count(*) SELECT Region FROM Sales GROUP BY Region PIVOT NULL"));
    }
    [Fact]
    public void ReusedSourceContextDoesNotAliasResultsOrSavedSources()
    {
        var document = CrosstabAndMakeTableTests.Sales(); var fast = new QueryEngine();
        var reference = new QueryEngine(new() { EnableReusableRowContexts = false });
        const string sql = "SELECT Region, Quarter, Amount FROM Sales ORDER BY Amount DESC";
        var a = fast.Select(document, sql); var b = reference.Select(document, sql);
        Assert.Equal(1, a.Statistics.SourceContextsCreated); Assert.Equal(6, b.Statistics.SourceContextsCreated);
        Assert.Equal(b.Records.Select(row => row["Amount"]), a.Records.Select(row => row["Amount"]));
        a.Records[0]["Amount"] = "999";
        Assert.DoesNotContain(document.Table("Sales").Records, row => row["Amount"] == "999");
        document.Queries.Add(new() { Name = "Saved", Sql = sql });
        Assert.Equal(6, fast.Select(document, "SELECT * FROM Saved").Records.Count);
    }
}
