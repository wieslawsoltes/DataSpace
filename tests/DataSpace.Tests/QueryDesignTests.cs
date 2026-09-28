using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class QueryDesignTests
{
    private readonly DatabaseDocument _document = SampleDatabase.Create();
    private readonly QueryEngine _engine = new();
    private static string[] Values(QueryResult result) => result.Records.Select(r => string.Join("|", result.Fields.Select(f => r[f.Name] ?? "<NULL>"))).ToArray();

    [Theory]
    [InlineData("SELECT * FROM Customers ORDER BY ID")]
    [InlineData("SELECT c.* FROM Customers c WHERE c.ID < 4 ORDER BY c.ID DESC")]
    [InlineData("SELECT Company AS Business, ID FROM Customers WHERE Country='UK' OR Country='Poland' ORDER BY 2 DESC")]
    [InlineData("SELECT TOP 5 Company FROM Customers ORDER BY ID OFFSET 2")]
    [InlineData("SELECT DISTINCT Country FROM Customers ORDER BY Country")]
    [InlineData("SELECT c.Company,o.ID FROM Customers c LEFT JOIN Orders o ON c.ID=o.[Customer ID] WHERE c.ID < 3 ORDER BY c.ID,o.ID")]
    [InlineData("SELECT Country,Count(*) AS N FROM Customers GROUP BY Country HAVING Count(*)>1 ORDER BY N DESC,Country")]
    [InlineData("SELECT 1 AS K FROM Customers GROUP BY Country ORDER BY Country")]
    [InlineData("SELECT ID FROM Customers WHERE ID BETWEEN 2 AND 4 AND Company NOT LIKE 'X*' ORDER BY ID")]
    [InlineData("SELECT Company FROM Customers WHERE Country IN ('UK','Poland',NULL) ORDER BY Company")]
    [InlineData("SELECT IIf(TRUE,'A''B','other') AS [Quoted value], #2026-01-02# AS Day")]
    public void SelectRoundTripPreservesResultsAndOutputNames(string sql)
    {
        var design = QueryDesign.FromSql(sql); var rebuilt = design.ToSql();
        var expected = _engine.Select(_document, sql); var actual = _engine.Select(_document, rebuilt);
        Assert.Equal(expected.Fields.Select(f => f.Name), actual.Fields.Select(f => f.Name));
        Assert.Equal(Values(expected), Values(actual));
        Assert.Equal(Values(actual), Values(_engine.Select(_document, QueryDesign.FromSql(rebuilt).ToSql())));
    }
    [Fact]
    public void ParametersSurviveRoundTrip()
    {
        const string sql = "SELECT Company FROM Customers WHERE ID>=@minimum ORDER BY ID";
        var parameters = new Dictionary<string, object?> { ["minimum"] = 15 };
        Assert.Equal(Values(_engine.Select(_document, sql, parameters)), Values(_engine.Select(_document, QueryDesign.FromSql(sql).ToSql(), parameters)));
    }
    [Fact]
    public void GroupedFunctionExpressionsUseStructuralEquality()
    {
        const string sql = "SELECT LCase(Country) AS C, Count(*) AS N FROM Customers GROUP BY LCase(Country) ORDER BY C";
        var result = _engine.Select(_document, sql);
        Assert.NotEmpty(result.Records);
        Assert.Equal(Values(result), Values(_engine.Select(_document, QueryDesign.FromSql(sql).ToSql())));
    }
    [Theory]
    [InlineData(">= 2 AND <= 4", 3)]
    [InlineData("Between 2 And 4 OR 18", 4)]
    [InlineData("(1 OR 2) AND < 2", 1)]
    [InlineData("NOT (>= 2 AND <= 17)", 2)]
    [InlineData("In (1,3,18)", 3)]
    [InlineData("Is Null", 0)]
    [InlineData("Is Not Null", 18)]
    [InlineData("(1)+(2)", 1)]
    public void NumericCriteriaSupportAccessShorthand(string criterion, int count)
    {
        var predicate = QueryCriteria.Compile("ID", criterion);
        Assert.Equal(count, _engine.Select(_document, "SELECT * FROM Customers WHERE " + predicate).Records.Count);
    }
    [Theory]
    [InlineData("UK", 4)]
    [InlineData("UK OR Poland", 5)]
    [InlineData("Like 'U*'", 7)]
    [InlineData("= 'UK' Or = 'Poland'", 5)]
    [InlineData("In ('UK','Poland')", 5)]
    public void TextCriteriaAreLiteralsRatherThanUnboundNames(string criterion, int count)
        => Assert.Equal(count, _engine.Select(_document, "SELECT * FROM Customers WHERE " + QueryCriteria.Compile("Country", criterion)).Records.Count);

    [Fact]
    public void CriteriaRowsAndHiddenFieldsDoNotChangeProjection()
    {
        var design = new QueryDesign(); design.AddTable(_document, "Customers");
        design.AddField("Customers", "Company");
        var country = design.AddField("Customers", "Country"); country.Show = false; country.Criteria = ["UK", "Poland"];
        var id = design.AddField("Customers", "ID"); id.Show = false; id.Criteria = ["< 10", "> 0"]; id.Sort = QuerySort.Ascending;
        var result = _engine.Select(_document, design.ToSql());
        Assert.Single(result.Fields); Assert.Equal(3, result.Records.Count);
    }
    [Fact]
    public void TotalsHaveDistinctPreAndPostGroupCriteria()
    {
        var design = new QueryDesign(); design.AddTable(_document, "Customers");
        var country = design.AddField("Customers", "Country"); country.Total = QueryTotal.GroupBy; country.Sort = QuerySort.Ascending;
        design.Columns.Add(new() { Expression = "*", Total = QueryTotal.Count, Alias = "N", Criteria = [">=2"] });
        design.Columns.Add(new() { Expression = "ID", Total = QueryTotal.Where, Show = false, Criteria = ["<18"] });
        var sql = design.ToSql(); Assert.Contains("WHERE", sql); Assert.Contains("HAVING", sql);
        Assert.Equal(Values(_engine.Select(_document, "SELECT Customers.Country,Count(*) AS N FROM Customers WHERE ID<18 GROUP BY Customers.Country HAVING Count(*)>=2 ORDER BY Customers.Country")), Values(_engine.Select(_document, sql)));
    }
    [Fact]
    public void MixedPhaseOrRowsAreRejectedRatherThanChangingMeaning()
    {
        var design = new QueryDesign(); design.AddTable(_document, "Customers");
        design.Columns.Add(new() { Expression = "*", Total = QueryTotal.Count, Criteria = [">2", "<10"] });
        design.Columns.Add(new() { Expression = "ID", Show = false, Total = QueryTotal.Where, Criteria = ["<5", ">15"] });
        Assert.Throws<DataSpaceException>(() => design.ToSql());
    }
    [Fact]
    public void AddTableSuggestsExistingRelationshipsAndSupportsAliases()
    {
        var design = new QueryDesign(); design.AddTable(_document, "Customers");
        var orders = design.AddTable(_document, "Orders");
        Assert.Equal(QueryJoinKind.Inner, orders.Join); Assert.Contains("[Orders].[Customer ID]", orders.Condition);
        var self = design.AddTable(_document, "Customers"); Assert.NotEqual("Customers", self.Alias);
    }
    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("UPDATE Customers SET Company='Oops'")]
    [InlineData("SELECT Company FROM Customers UNION SELECT Name FROM Products")]
    [InlineData("SELECT * FROM Customers; DELETE FROM Orders")]
    public void UnsupportedDesignImportIsExplicitAndNonMutating(string sql)
    {
        var before = DocumentCodec.Serialize(_document);
        Assert.Throws<DataSpaceException>(() => QueryDesign.FromSql(sql)); Assert.Equal(before, DocumentCodec.Serialize(_document));
    }
    [Fact]
    public void DesignerStatePreservesCriteriaHiddenColumnsAndPositionsOnlyForMatchingSql()
    {
        var design = new QueryDesign(); design.AddTable(_document, "Customers").X = 380;
        var company = design.AddField("Customers", "Company"); company.Criteria = ["Like 'N*'", "Like 'A*'"];
        var state = design.Serialize(); var restored = QueryDesign.Restore(design.ToSql(), state);
        Assert.Equal(380, restored.Sources[0].X); Assert.Equal(company.Criteria, restored.Columns[0].Criteria);
        var changed = QueryDesign.Restore("SELECT ID FROM Products", state);
        Assert.Equal("Products", changed.Sources[0].Table);
        Assert.Equal("Customers", QueryDesign.Restore("SELECT ID FROM Customers", "not json").Sources[0].Table);
    }
    [Fact]
    public void DesignerStateRoundTripsWithDatabaseFiles()
    {
        var query = _document.Queries[0]; var design = QueryDesign.FromSql(query.Sql); query.Sql = design.ToSql(); query.DesignerState = design.Serialize();
        Assert.Equal(query.DesignerState, DocumentCodec.Deserialize(DocumentCodec.Serialize(_document)).Queries[0].DesignerState);
    }
    [Theory]
    [InlineData(">1; DELETE FROM Customers")]
    [InlineData("IN (SELECT ID INTO Stolen FROM Customers)")]
    [InlineData("IN (SELECT ID FROM Customers; DELETE FROM Customers)")]
    [InlineData("IN (DELETE FROM Customers)")]
    public void StatementInjectionIsRejected(string criterion) => Assert.Throws<DataSpaceException>(() => QueryCriteria.Compile("ID", criterion));
    [Fact]
    public void InvalidColumnConfigurationsFailBeforeExecution()
    {
        var design = QueryDesign.FromSql("SELECT * FROM Customers");
        design.Columns[0].Sort = QuerySort.Ascending; Assert.Throws<DataSpaceException>(() => design.ToSql());
        design.Columns[0].Sort = QuerySort.None; design.Columns[0].Show = false; Assert.Throws<DataSpaceException>(() => design.ToSql());
        design.Columns[0].Show = true; design.Sources.Add(new() { Table = "Orders", Alias = "Customers" }); Assert.Throws<DataSpaceException>(() => design.ToSql());
    }
}
