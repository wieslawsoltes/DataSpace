using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class SubqueryAdditionalTests
{
    private static string[] Rows(QueryResult result) => result.Records.Select(r => string.Join("|", result.Fields.Select(f => r[f.Name] ?? "<NULL>"))).ToArray();
    [Theory]
    [InlineData("IN (SELECT [Customer ID] FROM Orders)")]
    [InlineData("EXISTS (SELECT o.ID FROM Orders o WHERE o.[Customer ID]=Customers.ID)")]
    [InlineData("NOT EXISTS (SELECT o.ID FROM Orders o WHERE o.[Customer ID]=Customers.ID)")]
    public void QbeAcceptsReadOnlySubqueriesAndPreservesBooleanCriteria(string criterion)
    {
        var document = SampleDatabase.Create(); var engine = new QueryEngine();
        var actual = QueryCriteria.Compile("ID", criterion);
        var expected = criterion.StartsWith("IN") ? "ID " + criterion : criterion;
        Assert.Equal(Rows(engine.Select(document, "SELECT ID FROM Customers WHERE " + expected + " ORDER BY ID")),
            Rows(engine.Select(document, "SELECT ID FROM Customers WHERE " + actual + " ORDER BY ID")));
    }
    [Fact]
    public void UnionOrderBindingsAndVolatileSubqueriesAreNotIncorrectlyCached()
    {
        var document = SampleDatabase.Create(); var engine = new QueryEngine();
        Assert.ThrowsAny<Exception>(() => engine.Select(document,
            "SELECT t.ID FROM Customers t WHERE EXISTS (SELECT 1 AS N UNION SELECT 2 ORDER BY 1/(t.ID-2))"));
        document.Table("Customers").Records.Clear();
        Assert.Throws<DataSpaceException>(() => engine.Select(document,
            "SELECT ID FROM Customers WHERE EXISTS (SELECT 1 AS N UNION SELECT 2 ORDER BY Missing)"));
        var volatileResult = engine.Select(SampleDatabase.Create(), "SELECT (SELECT Now()) AS Stamp FROM Customers");
        Assert.Equal(18, volatileResult.Statistics.SubqueryExecutions); Assert.Equal(0, volatileResult.Statistics.SubqueryCacheHits);
    }
    [Fact]
    public void RandomizedMembershipMatchesTheUncachedReference()
    {
        var random = new Random(403);
        for (var trial = 0; trial < 30; trial++)
        {
            var document = new DatabaseDocument();
            foreach (var name in new[] { "T", "U" })
            {
                var table = new TableDefinition { Name = name, Fields = [new() { Name = "Id", Type = FieldType.Integer }, new() { Name = "K", Type = FieldType.Integer }] };
                for (var i = 0; i < 12; i++) RecordOperations.Insert(table, new Dictionary<string,string?> { ["Id"] = i.ToString(), ["K"] = random.Next(4)==0 ? null : random.Next(-2,5).ToString() });
                document.Tables.Add(table);
            }
            foreach (var predicate in new[] { "IN", "NOT IN", "= ANY", "<> ALL", "> ALL" })
            {
                var sql = "SELECT Id FROM T WHERE K " + predicate + " (SELECT K FROM U) ORDER BY Id";
                Assert.Equal(Rows(new QueryEngine(new() { EnableSubqueryCache = false, EnableMembershipIndexes = false }).Select(document, sql)),
                    Rows(new QueryEngine().Select(document, sql)));
            }
        }
    }
    [Fact]
    public void CachedQuantifiedWorkStillHasAComparisonBudget()
    {
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumIntermediateRows = 50, EnableMembershipIndexes = false }).Select(
            SampleDatabase.Create(), "SELECT ID FROM Customers WHERE 999 IN (SELECT ID FROM Orders)"));
    }
}
