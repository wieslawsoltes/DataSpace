using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class QueryTests
{
    private readonly DatabaseDocument _document = SampleDatabase.Create();
    private readonly QueryEngine _engine = new();
    [Fact] public void AllSampleQueriesExecute()
    {
        foreach (var query in _document.Queries) Assert.NotEmpty(_engine.Select(_document, query.Sql).Records);
        Assert.Equal(4, _engine.Select(_document, _document.Queries[0].Sql).Records.Count);
        Assert.Equal(16, _engine.Select(_document, _document.Queries[1].Sql).Records.Count);
        Assert.Equal(18, _engine.Select(_document, _document.Queries[2].Sql).Records.Count);
    }
    [Fact] public void SelectWildcardReturnsTypedFields()
    { var result = _engine.Select(_document, "select * from Customers;"); Assert.Equal(9, result.Fields.Count); Assert.Equal(FieldType.YesNo, result.Fields[^1].Type); }
    [Fact] public void JoinPreservesForeignKeyMatches()
    {
        var result = _engine.Select(_document, "SELECT o.ID, c.Company FROM Orders o INNER JOIN Customers c ON o.[Customer ID]=c.ID ORDER BY o.ID");
        Assert.Equal(48, result.Records.Count); Assert.Equal("Northwind Traders", result.Records[0]["Company"]);
    }
    [Fact] public void LeftJoinPreservesUnmatchedRows()
    {
        var result = _engine.Select(_document, "SELECT c.ID, o.ID AS OrderID FROM Customers c LEFT JOIN Orders o ON c.ID = o.[Customer ID] AND o.ID < 2");
        Assert.Equal(18, result.Records.Count); Assert.Equal(17, result.Records.Count(r => r["OrderID"] is null));
    }
    [Theory]
    [InlineData("SELECT Count(*) AS N FROM Orders", "N", "48")]
    [InlineData("SELECT TOP 1 ID FROM Customers ORDER BY ID DESC", "ID", "18")]
    [InlineData("SELECT ID FROM Customers ORDER BY ID LIMIT 1 OFFSET 3", "ID", "4")]
    [InlineData("SELECT Count(*) AS N FROM Orders WHERE ID < 0", "N", "0")]
    [InlineData("SELECT Sum(Amount) AS N FROM Orders WHERE ID < 0", "N", null)]
    [InlineData("SELECT IIf(1=1,'yes','no') AS Answer", "Answer", "yes")]
    [InlineData("SELECT 'O''Brien' AS Name", "Name", "O'Brien")]
    public void ProjectionAndAggregation(string sql, string field, string? expected)
        => Assert.Equal(expected, _engine.Select(_document, sql).Records[0][field]);
    [Fact] public void ParameterizedQueriesDoNotInterpretDataAsSql()
    {
        var args = new Dictionary<string, object?> { ["country"] = "UK' OR 1=1 --" };
        Assert.Empty(_engine.Select(_document, "SELECT * FROM Customers WHERE Country=@country", args).Records);
        args["COUNTRY"] = "UK"; args.Remove("country");
        Assert.Equal(4, _engine.Select(_document, "SELECT * FROM Customers WHERE Country=@country", args).Records.Count);
    }
    [Fact] public void MissingParameterAndAmbiguousFieldsAreErrors()
    {
        Assert.Throws<DataSpaceException>(() => _engine.Select(_document, "SELECT * FROM Customers WHERE Country=@missing"));
        Assert.Throws<DataSpaceException>(() => _engine.Select(_document, "SELECT ID FROM Customers c INNER JOIN Orders o ON c.ID=o.[Customer ID]"));
    }
    [Fact] public void GroupingHavingAndAliasesWork()
    {
        var result = _engine.Select(_document, "SELECT Country, Count(*) AS N FROM Customers GROUP BY Country HAVING Count(*) > 1 ORDER BY N DESC, Country");
        Assert.Equal("UK", result.Records[0]["Country"]); Assert.Equal("4", result.Records[0]["N"]);
    }
    [Fact] public void DistinctAndDateLiteralsWork()
    {
        Assert.Equal(11, _engine.Select(_document, "SELECT DISTINCT Country FROM Customers").Records.Count);
        Assert.NotEmpty(_engine.Select(_document, "SELECT * FROM Orders WHERE [Order Date] BETWEEN #2026-09-01# AND #2026-09-05#").Records);
    }
    [Theory]
    [InlineData("SELECT * FROM Customers WHERE ID IN (1,3,5)", 3)]
    [InlineData("SELECT * FROM Customers WHERE ID NOT IN (1,3,5)", 15)]
    [InlineData("SELECT * FROM Customers WHERE Country LIKE 'U*'", 7)]
    [InlineData("SELECT * FROM Customers WHERE ID BETWEEN 2 AND 5", 4)]
    [InlineData("SELECT * FROM Customers WHERE ID NOT BETWEEN 2 AND 5", 14)]
    [InlineData("SELECT * FROM Customers WHERE ID = NULL", 0)]
    [InlineData("SELECT * FROM Customers WHERE NULL OR ID = 1", 1)]
    [InlineData("SELECT * FROM Customers WHERE NULL AND ID = 1", 0)]
    public void PredicatesImplementNullAndBooleanSemantics(string sql, int expected) => Assert.Equal(expected, _engine.Select(_document, sql).Records.Count);
    [Fact] public void DdlAndDmlCommitAtomically()
    {
        var w = new DatabaseWorkspace(new() { Name = "Test" });
        _engine.Execute(w, "CREATE TABLE People (ID COUNTER PRIMARY KEY, Name TEXT(50) NOT NULL, Age INTEGER)");
        Assert.Equal(2, _engine.Execute(w, "INSERT INTO People (Name,Age) VALUES ('Alice',30),('Bob',40)").AffectedRecords);
        Assert.Equal(1, _engine.Execute(w, "UPDATE People SET Age=Age+1 WHERE Name='Alice'").AffectedRecords);
        Assert.Equal("31", w.Document.Table("People").Records[0]["Age"]);
        var before = DocumentCodec.Serialize(w.Document);
        Assert.Throws<DataSpaceException>(() => _engine.Execute(w, "UPDATE People SET ID=1"));
        Assert.Equal(before, DocumentCodec.Serialize(w.Document));
        Assert.Equal(1, _engine.Execute(w, "DELETE FROM People WHERE Age>35").AffectedRecords);
        _engine.Execute(w, "ALTER TABLE People ADD Note MEMO"); Assert.Equal(4, w.Document.Table("People").Fields.Count);
        _engine.Execute(w, "ALTER TABLE People DROP Note"); Assert.Equal(3, w.Document.Table("People").Fields.Count);
        w.Undo(); Assert.Equal(4, w.Document.Table("People").Fields.Count);
    }
    [Fact] public void BatchSqlAndUnsupportedSyntaxNeverPartiallyExecute()
    {
        var w = new DatabaseWorkspace(_document);
        Assert.Throws<DataSpaceException>(() => _engine.Execute(w, "DELETE FROM Orders; DROP TABLE Customers;"));
        Assert.Equal(48, w.Document.Table("Orders").Records.Count);
    }
    [Theory][InlineData("SELECT 1/0")][InlineData("SELECT UnknownFunction(1)")][InlineData("SELECT Company, Count(*) FROM Customers")][InlineData("SELECT 'unterminated")]
    public void InvalidQueriesProduceDiagnostics(string sql) => Assert.Throws<DataSpaceException>(() => _engine.Select(_document, sql));
    [Fact] public void QueryBudgetAndCancellationAreEnforced()
    {
        var engine = new QueryEngine(new() { MaximumIntermediateRows = 2 });
        Assert.Throws<DataSpaceException>(() => engine.Select(_document, "SELECT * FROM Customers"));
        Assert.Throws<OperationCanceledException>(() => _engine.Select(_document, "SELECT * FROM Customers", cancellationToken: new(true)));
    }
    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("Nz(NULL,'fallback')", "fallback")]
    [InlineData("UCase('access')", "ACCESS")]
    [InlineData("LCase('SQL')", "sql")]
    [InlineData("Left('Northwind',5)", "North")]
    [InlineData("Right('Northwind',4)", "wind")]
    [InlineData("Mid('Northwind',6,4)", "wind")]
    [InlineData("Len('Skia')", "4")]
    [InlineData("Round(12.345,2)", "12.34")]
    [InlineData("Year(#2026-09-27#)", "2026")]
    [InlineData("'Data' & NULL & 'Space'", "DataSpace")]
    [InlineData("IIf(False, 1/0, 'safe')", "safe")]
    public void ScalarExpressions(string expression, string expected)
        => Assert.Equal(expected, FieldValues.FromObject(ExpressionEvaluator.Evaluate(expression, new Dictionary<string, object?>())));
}
