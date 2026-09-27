using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class CrosstabAndMakeTableTests
{
    internal static DatabaseDocument Sales()
    {
        var table = new TableDefinition { Name = "Sales", Fields =
        [new() { Name = "Region", MaxLength = 600 }, new() { Name = "Quarter", Type = FieldType.Integer }, new() { Name = "Amount", Type = FieldType.Currency }] };
        foreach (var row in new (string Region, string? Quarter, string? Amount)[]
        { ("West", "1", "10"), ("West", "1", "20"), ("West", "2", "5"), ("East", "2", "7"), ("East", "2", null), ("East", null, "2") })
            RecordOperations.Insert(table, new Dictionary<string, string?> { ["Region"] = row.Region, ["Quarter"] = row.Quarter, ["Amount"] = row.Amount });
        var document = new DatabaseDocument { Tables = [table] }; SchemaValidator.Validate(document); return document;
    }
    [Fact]
    public void DynamicCrosstabHasSortedTypedColumnsAndSparseCells()
    {
        var result = new QueryEngine().Select(Sales(), "TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region ORDER BY Region PIVOT Quarter;");
        Assert.Equal(new[] { "Region", "1", "2" }, result.Fields.Select(f => f.Name));
        Assert.Equal("East", result.Records[0]["Region"]); Assert.Null(result.Records[0]["1"]); Assert.Equal("7", result.Records[0]["2"]);
        Assert.Equal("30", result.Records[1]["1"]); Assert.Equal("5", result.Records[1]["2"]);
        Assert.Equal(FieldType.Decimal, result.Fields[1].Type); Assert.Equal(3, result.Statistics.CrosstabCells);
        Assert.Equal(2, result.Statistics.PeakAggregateGroups); Assert.Equal(0, result.Statistics.BufferedAggregateRows);
    }
    [Fact]
    public void FixedHeadingsPreserveOrderMissingColumnsAndUnrestrictedRowTotals()
    {
        var result = new QueryEngine().Select(Sales(), "TRANSFORM Sum(Amount) SELECT Region, Sum(Amount) AS Total FROM Sales GROUP BY Region ORDER BY Region PIVOT Quarter IN (2,3);");
        Assert.Equal(new[] { "Region", "Total", "2", "3" }, result.Fields.Select(f => f.Name));
        Assert.Equal("9", result.Records[0]["Total"]); Assert.Equal("35", result.Records[1]["Total"]);
        Assert.Null(result.Records[0]["3"]); Assert.Null(result.Records[1]["3"]);
    }
    [Theory]
    [InlineData("COUNT(*)", "2")]
    [InlineData("COUNT(Amount)", "2")]
    [InlineData("SUM(Amount)", "30")]
    [InlineData("AVG(Amount)", "15")]
    [InlineData("MIN(Amount)", "10")]
    [InlineData("MAX(Amount)", "20")]
    [InlineData("FIRST(Amount)", "10")]
    [InlineData("LAST(Amount)", "20")]
    public void CrosstabAggregates(string aggregate, string expected)
    {
        var result = new QueryEngine().Select(Sales(), $"TRANSFORM {aggregate} SELECT Region FROM Sales GROUP BY Region PIVOT Quarter;");
        Assert.Equal(expected, result.Records.Single(r => r["Region"] == "West")["1"]);
    }
    [Fact]
    public void CrosstabCountDistinguishesMissingCellAndOnlyNullValues()
    {
        var document = Sales(); document.Table("Sales").Records[3]["Amount"] = null;
        var result = new QueryEngine().Select(document, "TRANSFORM Count(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN (1,2);");
        var east = result.Records.Single(r => r["Region"] == "East"); Assert.Null(east["1"]); Assert.Equal("0", east["2"]);
    }
    [Fact]
    public void CrosstabFiltersParametersHavingAndSavedSourcesCompose()
    {
        var document = Sales(); document.Queries.Add(new() { Name = "Source", Sql = "SELECT * FROM Sales WHERE Amount >= @minimum", Parameters = new() { ["minimum"] = "10" } });
        const string sql = "TRANSFORM Sum(Amount) SELECT Region, Sum(Amount) AS Total FROM [Source] GROUP BY Region HAVING Total > 20 ORDER BY Total DESC PIVOT Quarter IN (@q,2)";
        var result = new QueryEngine().Select(document, sql, new Dictionary<string, object?> { ["q"] = 1, ["minimum"] = 0 });
        Assert.Single(result.Records); Assert.Equal("35", result.Records[0]["Total"]);
        document.Queries.Add(new() { Name = "Pivoted", Sql = "TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN (1,2)" });
        Assert.Equal(2, new QueryEngine().Select(document, "SELECT * FROM Pivoted").Records.Count);
    }
    [Fact]
    public void CrosstabSupportsJoinsAndExpressionHeadings()
    {
        var document = Sales(); document.Tables.Add(new() { Name = "Regions", Fields = [new() { Name = "Code" }, new() { Name = "Label" }] });
        RecordOperations.Insert(document.Table("Regions"), new Dictionary<string, string?> { ["Code"] = "West", ["Label"] = "Western" });
        var result = new QueryEngine().Select(document, "TRANSFORM Sum(s.Amount) SELECT r.Label FROM Regions r INNER JOIN Sales s ON r.Code=s.Region GROUP BY r.Label PIVOT ('Q' & s.Quarter)");
        Assert.Single(result.Records); Assert.Equal("30", result.Records[0]["Q1"]); Assert.Equal("Western", result.Records[0]["Label"]);
    }
    [Fact]
    public void EmptyCrosstabRetainsDeclaredSchema()
    {
        var result = new QueryEngine().Select(Sales(), "TRANSFORM Count(*) SELECT Region FROM Sales WHERE 1=0 GROUP BY Region PIVOT Quarter IN (3,1)");
        Assert.Empty(result.Records); Assert.Equal(new[] { "Region", "3", "1" }, result.Fields.Select(f => f.Name));
        Assert.Equal(FieldType.Integer, result.Fields[1].Type);
    }
    [Theory]
    [InlineData("TRANSFORM Amount SELECT Region FROM Sales GROUP BY Region PIVOT Quarter")]
    [InlineData("TRANSFORM Sum(Sum(Amount)) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter")]
    [InlineData("TRANSFORM Sum(*) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales PIVOT Quarter")]
    [InlineData("TRANSFORM Sum(Amount) SELECT * FROM Sales GROUP BY Region PIVOT Quarter")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN (1,1.0)")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN (NULL)")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN ('Region')")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter IN (Missing)")]
    [InlineData("TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Sum(Quarter)")]
    [InlineData("TRANSFORM Sum(Amount) SELECT TOP 2 Region FROM Sales GROUP BY Region PIVOT Quarter")]
    public void InvalidCrosstabsFailWithoutChangingInput(string sql)
    {
        var document = Sales(); var before = DocumentCodec.Serialize(document);
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(document, sql));
        Assert.Equal(before, DocumentCodec.Serialize(document));
    }
    [Fact]
    public void CrosstabLimitsAndCancellationAreEnforced()
    {
        const string sql = "TRANSFORM Sum(Amount) SELECT Region FROM Sales GROUP BY Region PIVOT Quarter";
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumCrosstabColumns = 2 }).Select(Sales(), sql));
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumCrosstabCells = 2 }).Select(Sales(), sql));
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumResultRows = 1 }).Select(Sales(), sql));
        Assert.Throws<OperationCanceledException>(() => new QueryEngine().Select(Sales(), sql, cancellationToken: new(true)));
    }
    [Fact]
    public void MakeTableIsAtomicUndoableAndPreservesTypeSizeButNotConstraints()
    {
        var document = Sales(); document.Table("Sales").Fields[0].Required = true;
        var workspace = new DatabaseWorkspace(document); var engine = new QueryEngine();
        const string sql = "SELECT Region, Amount INTO [Archive] FROM Sales WHERE Quarter=1 ORDER BY Amount DESC";
        Assert.False(engine.IsReadOnly(sql)); Assert.Throws<DataSpaceException>(() => engine.Select(workspace.Document, sql));
        var result = engine.Execute(workspace, sql); Assert.True(result.IsAction); Assert.Equal(2, result.AffectedRecords);
        var archive = workspace.Document.Table("Archive"); Assert.Equal(600, archive.Fields[0].MaxLength); Assert.False(archive.Fields[0].Required);
        Assert.Equal(FieldType.Currency, archive.Fields[1].Type); Assert.Equal("20", archive.Records[0]["Amount"]);
        Assert.DoesNotContain(archive.Records[0].Id, document.Table("Sales").Records.Select(r => r.Id));
        workspace.Undo(); Assert.Single(workspace.Document.Tables); workspace.Redo(); Assert.Equal(2, workspace.Document.Table("Archive").Records.Count);
    }
    [Fact]
    public void MakeEmptyTablePreservesDirectSourceSchema()
    {
        var workspace = new DatabaseWorkspace(Sales());
        new QueryEngine().Execute(workspace, "SELECT * INTO Empty FROM Sales WHERE 1=0");
        Assert.Empty(workspace.Document.Table("Empty").Records); Assert.Equal(FieldType.Currency, workspace.Document.Table("Empty").Field("Amount").Type);
    }
    [Theory]
    [InlineData("SELECT * INTO Sales FROM Sales")]
    [InlineData("SELECT 1/0 AS Broken INTO NewTable FROM Sales")]
    [InlineData("SELECT * INTO [Bad.Name] FROM Sales")]
    [InlineData("INSERT INTO Sales SELECT * INTO Other FROM Sales")]
    [InlineData("SELECT Region FROM Sales UNION SELECT Region INTO Bad FROM Sales")]
    public void MakeTableFailureLeavesWorkspaceAndHistoryUntouched(string sql)
    {
        var workspace = new DatabaseWorkspace(Sales()); var original = workspace.Document;
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Execute(workspace, sql)); Assert.Same(original, workspace.Document); Assert.False(workspace.CanUndo);
    }
    [Fact]
    public void IndexDdlValidatesAndRollsBack()
    {
        var workspace = new DatabaseWorkspace(Sales()); var engine = new QueryEngine();
        engine.Execute(workspace, "CREATE INDEX RegionQuarter ON Sales (Region ASC, Quarter)");
        Assert.Equal(new[] { "Region", "Quarter" }, workspace.Document.Table("Sales").Indexes[0].Fields);
        var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "CREATE UNIQUE INDEX Duplicates ON Sales (Region, Quarter)")); Assert.Same(before, workspace.Document);
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "CREATE INDEX Bad ON Sales (Missing)"));
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "CREATE INDEX Bad ON Sales (Region DESC)"));
        engine.Execute(workspace, "DROP INDEX RegionQuarter ON Sales"); Assert.Empty(workspace.Document.Table("Sales").Indexes);
        workspace.Undo(); Assert.Single(workspace.Document.Table("Sales").Indexes);
    }
}
