using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class CorrelatedCacheTests
{
    private static DatabaseDocument Fixture(int count = 40, int groups = 4)
    {
        var document = new DatabaseDocument();
        foreach (var (name, size) in new[] { ("T", count), ("U", 20) })
        {
            var table = new TableDefinition { Name = name, Fields = [
                new() { Name = "Id", Type = FieldType.Integer }, new() { Name = "K", Type = FieldType.Integer }] };
            for (var i = 0; i < size; i++) RecordOperations.Insert(table,
                new Dictionary<string, string?> { ["Id"] = i.ToString(), ["K"] = (i % groups).ToString() });
            document.Tables.Add(table);
        }
        SchemaValidator.Validate(document); return document;
    }
    private static QueryResult Compare(DatabaseDocument document, string sql, QueryOptions? options = null)
    {
        var slow = new QueryEngine(new() { EnableSubqueryCache = false }).Select(document, sql);
        var fast = new QueryEngine(options).Select(document, sql);
        Assert.Equal(slow.Fields.Select(f => (f.Name, f.Type)), fast.Fields.Select(f => (f.Name, f.Type)));
        Assert.Equal(slow.Records.Count, fast.Records.Count);
        for (var i = 0; i < fast.Records.Count; i++)
            Assert.Equal(slow.Fields.Select(f => slow.Records[i][f.Name]), fast.Fields.Select(f => fast.Records[i][f.Name]));
        return fast;
    }
    [Theory]
    [InlineData("(SELECT COUNT(*) FROM U u WHERE u.K=t.K)")]
    [InlineData("EXISTS (SELECT Id FROM U u WHERE u.K=t.K)")]
    [InlineData("t.Id IN (SELECT Id FROM U u WHERE u.K=t.K)")]
    [InlineData("t.Id < ANY (SELECT Id FROM U u WHERE u.K=t.K)")]
    [InlineData("t.Id >= ALL (SELECT Id FROM U u WHERE u.K=t.K)")]
    public void RepeatedCorrelationsReuseInnerResultsButNotOuterOperands(string expression)
    {
        var result = Compare(Fixture(), "SELECT t.Id," + expression + " AS V FROM T t ORDER BY t.Id");
        Assert.Equal(4, result.Statistics.SubqueryExecutions);
        Assert.Equal(36, result.Statistics.SubqueryCacheHits);
    }
    [Fact]
    public void MultilevelReferencesAndShadowedAliasesUseBoundLexicalCoordinates()
    {
        var result = Compare(Fixture(), "SELECT t.Id,(SELECT (SELECT t.K + u.K) FROM U u WHERE u.Id=0) AS V FROM T t ORDER BY t.Id");
        Assert.True(result.Statistics.SubqueryCacheHits >= 36);
        Compare(Fixture(), "SELECT t.Id,(SELECT (SELECT t.K + u.K FROM U t WHERE t.Id=0) FROM U u WHERE u.K=t.K AND u.Id<4) AS V FROM T t ORDER BY t.Id");
    }
    [Fact]
    public void CacheKeysRetainCaseNullsEmptyStringsAndTupleBoundaries()
    {
        var table = new TableDefinition { Name = "T", Fields = [new() { Name = "A" }, new() { Name = "B" }] };
        foreach (var (a, b) in new (string?, string?)[] { ("Ab", "c"), ("ab", "c"), (null, ""), ("", null), ("1:x", "y"), ("1", "x:y"), ("Ab", "c") })
            RecordOperations.Insert(table, new Dictionary<string, string?> { ["A"] = a, ["B"] = b });
        var result = Compare(new() { Tables = [table] }, "SELECT (SELECT Nz(t.A,'NULL') & ':' & Nz(t.B,'NULL')) AS V FROM T t");
        Assert.Equal(1, result.Statistics.SubqueryCacheHits);
        Assert.Equal("Ab:c", result.Records[0]["V"]); Assert.Equal("ab:c", result.Records[1]["V"]);
    }
    [Fact]
    public void CacheKeysRetainDecimalScaleObservableByStringConversion()
    {
        var table = new TableDefinition { Name = "T", Fields = [new() { Name = "V", Type = FieldType.Decimal }] };
        // The reader accepts canonical numeric values; direct construction also
        // permits distinct decimal scales, which CStr can observe.
        foreach (var value in new[] { "1.0", "1.00", "1.0" }) table.Records.Add(new() { Values = new() { ["V"] = value } });
        var result = Compare(new() { Tables = [table] }, "SELECT (SELECT CStr(t.V)) AS V FROM T t");
        Assert.Equal(1, result.Statistics.SubqueryCacheHits);
        Assert.Equal(new[] { "1.0", "1.00", "1.0" }, result.Records.Select(r => r["V"]));
    }
    [Fact]
    public void NullAndEmptyResultsAreCachedWithoutInventingRows()
    {
        var document = Fixture(); document.Table("T").Records[0]["K"] = null; document.Table("T").Records[4]["K"] = null;
        var result = Compare(document, "SELECT t.Id,(SELECT Id FROM U u WHERE u.K=t.K AND u.Id>100) AS V FROM T t ORDER BY t.Id");
        Assert.All(result.Records, row => Assert.Null(row["V"]));
        Assert.Equal(5, result.Statistics.SubqueryExecutions); Assert.Equal(35, result.Statistics.SubqueryCacheHits);
    }
    [Fact]
    public void AdmissionCountAndByteBudgetsFallBackToEquivalentEvaluation()
    {
        var result = Compare(Fixture(258, 129), "SELECT (SELECT t.K) AS V FROM T t");
        Assert.Equal(130, result.Statistics.SubqueryExecutions); Assert.Equal(128, result.Statistics.SubqueryCacheHits);
        foreach (var bytes in new long[] { 0, 128, 1024 })
        {
            var limited = Compare(Fixture(), "SELECT (SELECT t.K) AS V FROM T t", new() { MaximumSubqueryCacheBytes = bytes });
            Assert.InRange(limited.Statistics.SubqueryCacheBytes, 0, bytes);
            if (bytes < 512) Assert.Equal(0, limited.Statistics.SubqueryCacheHits);
        }
    }
    [Fact]
    public void OversizedKeysAreNotAdmitted()
    {
        var document = Fixture(2); var table = document.Table("T"); table.Fields.Add(new() { Name = "Text", Type = FieldType.LongText });
        foreach (var row in table.Records) row["Text"] = new string('x', 33000);
        var result = Compare(document, "SELECT (SELECT Len(t.Text)) AS V FROM T t");
        Assert.Equal(2, result.Statistics.SubqueryExecutions); Assert.Equal(0, result.Statistics.SubqueryCacheBytes);
    }
    [Fact]
    public void VolatileAndSavedSourcesRemainConservativelyUncached()
    {
        var document = Fixture();
        var volatileResult = new QueryEngine().Select(document, "SELECT (SELECT CStr(Now()) & CStr(t.K)) AS V FROM T t");
        Assert.Equal(40, volatileResult.Statistics.SubqueryExecutions); Assert.Equal(0, volatileResult.Statistics.SubqueryCacheHits);
        document.Queries.Add(new() { Name = "Saved", Sql = "SELECT K FROM U" });
        var saved = Compare(document, "SELECT (SELECT Count(*) FROM Saved s WHERE s.K=t.K) AS V FROM T t");
        Assert.Equal(0, saved.Statistics.SubqueryCacheHits);
    }
    [Fact]
    public void CachedReadsDoNotCrossParametersDocumentsOrConcurrentExecutions()
    {
        var engine = new QueryEngine(); var document = Fixture(); const string sql = "SELECT (SELECT t.K + @offset) AS V FROM T t";
        var first = engine.Select(document, sql, new Dictionary<string,object?> { ["offset"] = 1 });
        var next = engine.Select(document, sql, new Dictionary<string,object?> { ["offset"] = 2 });
        Assert.Equal("1", first.Records[0]["V"]); Assert.Equal("2", next.Records[0]["V"]);
        document.Table("T").Records[0]["K"] = "10";
        Assert.Equal("12", engine.Select(document, sql, new Dictionary<string,object?> { ["offset"] = 2 }).Records[0]["V"]);
        Parallel.For(0, 8, offset => Assert.Equal((10 + offset).ToString(), engine.Select(document, sql,
            new Dictionary<string,object?> { ["offset"] = offset }).Records[0]["V"]));
    }
    [Fact]
    public void CachedActionReadsKeepPreStatementSnapshotAndAtomicFailures()
    {
        var workspace = new DatabaseWorkspace(Fixture());
        var result = new QueryEngine().Execute(workspace, "UPDATE T SET K=(SELECT Max(innerRow.K) FROM T innerRow WHERE innerRow.K=T.K)+10");
        Assert.Equal(4, result.Statistics.SubqueryExecutions); Assert.Equal(36, result.Statistics.SubqueryCacheHits);
        Assert.Equal(new[] { "10", "11", "12", "13" }, workspace.Document.Table("T").Records.Take(4).Select(r => r["K"]));
        workspace.Undo(); var before = DocumentCodec.Serialize(workspace.Document);
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Execute(workspace, "UPDATE T SET K=(SELECT u.Id FROM U u WHERE u.K=T.K)"));
        Assert.Equal(before, DocumentCodec.Serialize(workspace.Document));
    }
    [Fact]
    public void RepeatedKeyRandomizedQueriesMatchTheReference()
    {
        var random = new Random(417);
        for (var trial = 0; trial < 24; trial++)
        {
            var document = Fixture(30, 5);
            foreach (var row in document.Table("T").Records) row["K"] = random.Next(5) == 0 ? null : random.Next(5).ToString();
            Compare(document, "SELECT t.Id,(SELECT Sum(u.Id) FROM U u WHERE u.K=t.K) AS V FROM T t ORDER BY t.Id");
            Compare(document, "SELECT t.Id FROM T t WHERE t.Id NOT IN (SELECT u.Id FROM U u WHERE u.K=t.K) ORDER BY t.Id");
            Compare(document, "SELECT t.K,(SELECT Count(*) FROM U u WHERE u.K=t.K) AS N FROM T t GROUP BY t.K ORDER BY t.K");
        }
    }
}
