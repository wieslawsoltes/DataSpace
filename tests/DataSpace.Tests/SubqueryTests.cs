using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class SubqueryTests
{
    private static DatabaseDocument Fixture()
    {
        var t = new TableDefinition { Name = "T", Fields = [
            new() { Name = "Id", Type = FieldType.Integer, PrimaryKey = true }, new() { Name = "K", Type = FieldType.Integer },
            new() { Name = "Amount", Type = FieldType.Decimal }, new() { Name = "Caption" } ] };
        var u = new TableDefinition { Name = "U", Fields = [
            new() { Name = "Id", Type = FieldType.Integer, PrimaryKey = true }, new() { Name = "K", Type = FieldType.Integer },
            new() { Name = "Val", Type = FieldType.Decimal } ] };
        for (var i = 1; i <= 4; i++) RecordOperations.Insert(t, new Dictionary<string, string?>
        { ["Id"] = i.ToString(), ["K"] = i == 4 ? null : (i <= 2 ? "1" : "2"), ["Amount"] = (i * 10).ToString(), ["Caption"] = "Row " + i });
        var keys = new string?[] { "1", "1", "3", null }; var values = new string?[] { "5", "15", null, "25" };
        for (var i = 0; i < 4; i++) RecordOperations.Insert(u, new Dictionary<string, string?> { ["Id"] = (11 + i).ToString(), ["K"] = keys[i], ["Val"] = values[i] });
        var document = new DatabaseDocument { Tables = [t, u] }; SchemaValidator.Validate(document); return document;
    }
    private static string?[] Column(QueryResult result, string name = "Id") => result.Records.Select(r => r[name]).ToArray();
    private static void Same(QueryResult expected, QueryResult actual)
    {
        Assert.Equal(expected.Fields.Select(f => (f.Name, f.Type)), actual.Fields.Select(f => (f.Name, f.Type)));
        Assert.Equal(expected.Records.Count, actual.Records.Count);
        for (var i = 0; i < expected.Records.Count; i++) Assert.Equal(expected.Fields.Select(f => expected.Records[i][f.Name]), actual.Fields.Select(f => actual.Records[i][f.Name]));
    }
    [Theory]
    [InlineData("SELECT t.Id FROM T t WHERE EXISTS (SELECT u.Id FROM U u WHERE u.K=t.K) ORDER BY t.Id", "1,2")]
    [InlineData("SELECT t.Id FROM T t WHERE NOT EXISTS (SELECT u.Id FROM U u WHERE u.K=t.K) ORDER BY t.Id", "3,4")]
    [InlineData("SELECT Id FROM T WHERE K IN (SELECT K FROM U) ORDER BY Id", "1,2")]
    [InlineData("SELECT Id FROM T WHERE K NOT IN (SELECT K FROM U) ORDER BY Id", "")]
    [InlineData("SELECT Id FROM T WHERE K NOT IN (SELECT K FROM U WHERE K IS NOT NULL) ORDER BY Id", "3")]
    [InlineData("SELECT Id FROM T WHERE Amount > ALL (SELECT Val FROM U WHERE Val IS NOT NULL) ORDER BY Id", "3,4")]
    [InlineData("SELECT Id FROM T WHERE Amount < ANY (SELECT Val FROM U) ORDER BY Id", "1,2")]
    [InlineData("SELECT Id FROM T WHERE Amount < SOME (SELECT Val FROM U) ORDER BY Id", "1,2")]
    [InlineData("SELECT Id FROM T WHERE Amount > (SELECT AVG(Amount) FROM T) ORDER BY Id", "3,4")]
    public void MembershipExistenceScalarAndQuantifiedPredicates(string sql, string expected)
    {
        var document = Fixture(); var actual = new QueryEngine().Select(document, sql);
        Assert.Equal(expected.Length == 0 ? Array.Empty<string>() : expected.Split(','), Column(actual));
        Same(new QueryEngine(new() { EnableSubqueryCache = false, EnableMembershipIndexes = false, EnableColumnPruning = false, EnableReusableRowContexts = false }).Select(document, sql), actual);
    }
    [Fact]
    public void CorrelatedAggregateDoesNotAggregateTheOuterQuery()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT t.Id, (SELECT COUNT(*) FROM U u WHERE u.K=t.K) AS N FROM T t ORDER BY t.Id");
        Assert.Equal(new[] { "1", "2", "3", "4" }, Column(result)); Assert.Equal(new[] { "2", "2", "0", "0" }, Column(result, "N"));
    }
    [Fact]
    public void CorrelationAcrossTwoLevelsRetainsTheGrandparentInput()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT t.Id FROM T t WHERE EXISTS (SELECT u.Id FROM U u WHERE EXISTS (SELECT x.Id FROM T x WHERE x.Id=t.Id AND x.K=u.K)) ORDER BY t.Id");
        Assert.Equal(new[] { "1", "2" }, Column(result)); Assert.Equal(2, result.Statistics.PeakSubqueryDepth);
    }
    [Theory]
    [InlineData("NULL IN (SELECT Id FROM U WHERE 1=0)", "False")]
    [InlineData("NULL NOT IN (SELECT Id FROM U WHERE 1=0)", "True")]
    [InlineData("NULL = ANY (SELECT Id FROM U WHERE 1=0)", "False")]
    [InlineData("NULL = ALL (SELECT Id FROM U WHERE 1=0)", "True")]
    [InlineData("2 IN (SELECT K FROM U)", null)]
    [InlineData("2 NOT IN (SELECT K FROM U)", null)]
    [InlineData("1 IN (SELECT K FROM U)", "True")]
    [InlineData("NULL IN (SELECT K FROM U)", null)]
    [InlineData("20 > ALL (SELECT Val FROM U)", "False")]
    [InlineData("30 > ALL (SELECT Val FROM U)", null)]
    [InlineData("10 < ANY (SELECT Val FROM U)", "True")]
    [InlineData("30 < ANY (SELECT Val FROM U)", null)]
    [InlineData("EXISTS (SELECT NULL FROM U)", "True")]
    [InlineData("EXISTS (SELECT Count(*) FROM U WHERE 1=0)", "True")]
    [InlineData("EXISTS (SELECT Count(*) FROM U HAVING Count(*)=0)", "False")]
    [InlineData("EXISTS (SELECT TOP 0 Id FROM U)", "False")]
    [InlineData("EXISTS (SELECT DISTINCT K FROM U OFFSET 3)", "False")]
    [InlineData("(SELECT Id FROM U WHERE 1=0)", null)]
    public void NullEmptySetAndAggregateCardinality(string expression, string? expected)
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT " + expression + " AS V"); Assert.Equal(expected, result.Records.Single()["V"]);
    }
    [Fact]
    public void SimpleExistsDoesNotEvaluateItsProjection()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT EXISTS (SELECT 1/0 FROM U) AS V");
        Assert.Equal("True", result.Records.Single()["V"]); Assert.Equal(1, result.Statistics.SourceRowsRead);
    }
    [Fact]
    public void CorrelatedUnionBranchesUseTheSameOuterRow()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT t.Id FROM T t WHERE t.K IN (SELECT u.K FROM U u WHERE u.K=t.K UNION SELECT 99) ORDER BY t.Id");
        Assert.Equal(new[] { "1", "2" }, Column(result));
    }
    [Theory]
    [InlineData("SELECT (SELECT Id FROM U) FROM T")]
    [InlineData("SELECT Id FROM T WHERE Id IN (SELECT Id, K FROM U)")]
    [InlineData("SELECT (SELECT Missing FROM U) FROM T")]
    [InlineData("SELECT Id FROM T WHERE 1=0 AND EXISTS (SELECT Missing FROM U)")]
    [InlineData("SELECT t.Id FROM T t WHERE EXISTS (SELECT t.Caption FROM U t)")]
    [InlineData("SELECT (SELECT Id FROM U a CROSS JOIN U b) FROM T")]
    [InlineData("SELECT Id FROM T WHERE EXISTS (DELETE FROM U)")]
    [InlineData("SELECT Id FROM T WHERE EXISTS (SELECT Id INTO X FROM U)")]
    [InlineData("SELECT Id FROM T WHERE EXISTS (SELECT Id FROM U; DELETE FROM U)")]
    public void InvalidSubqueriesFailWithoutChangingTheDocument(string sql)
    {
        var document = Fixture(); var before = DocumentCodec.Serialize(document);
        Assert.ThrowsAny<Exception>(() => new QueryEngine().Select(document, sql)); Assert.Equal(before, DocumentCodec.Serialize(document));
    }
    [Fact]
    public void EmptyOuterSourceStillBindsSubqueryNamesAndArity()
    {
        var document = Fixture(); document.Table("T").Records.Clear();
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(document, "SELECT Id FROM T WHERE Id IN (SELECT Missing FROM U)"));
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(document, "SELECT Id FROM T WHERE Id IN (SELECT Id,K FROM U)"));
    }
    [Fact]
    public void AliasShadowingAndOuterUnqualifiedFallbackAreLexical()
    {
        var document = Fixture();
        Assert.Equal(4, new QueryEngine().Select(document, "SELECT t.Id FROM T t WHERE EXISTS (SELECT t.Id FROM U t WHERE t.K=3)").Records.Count);
        Assert.Equal(4, new QueryEngine().Select(document, "SELECT t.Id FROM T t WHERE EXISTS (SELECT Id FROM U WHERE Caption=t.Caption)").Records.Count);
    }
    [Fact]
    public void GroupedCorrelationOnlyUsesGroupedOuterFields()
    {
        var document = Fixture(); const string sql = "SELECT t.K,(SELECT COUNT(*) FROM U u WHERE u.K=t.K) AS N FROM T t GROUP BY t.K ORDER BY t.K";
        var result = new QueryEngine().Select(document, sql);
        Assert.Equal(new[] { "0", "2", "0" }, Column(result, "N"));
        Same(new QueryEngine(new() { EnableStreamingAggregates = false }).Select(document, sql), result);
        Assert.Throws<DataSpaceException>(() => new QueryEngine().Select(document, "SELECT t.K,(SELECT COUNT(*) FROM U u WHERE u.Id=t.Id) AS N FROM T t GROUP BY t.K"));
    }
    [Fact]
    public void OuterValuesAreConstantsWithinAnInnerAggregate()
    {
        var result = new QueryEngine().Select(Fixture(), "SELECT t.Id,(SELECT Count(*) + t.Id FROM U) AS N FROM T t ORDER BY t.Id");
        Assert.Equal(new[] { "5", "6", "7", "8" }, Column(result, "N"));
    }
    [Fact]
    public void UncorrelatedMembershipRunsOnceAndUsesAHashIndex()
    {
        const string sql = "SELECT Id FROM T WHERE K IN (SELECT K FROM U) ORDER BY Id";
        var document = Fixture(); var fast = new QueryEngine().Select(document, sql);
        var reference = new QueryEngine(new() { EnableSubqueryCache = false, EnableMembershipIndexes = false }).Select(document, sql);
        Same(reference, fast); Assert.Equal(1, fast.Statistics.SubqueryExecutions); Assert.Equal(3, fast.Statistics.SubqueryCacheHits);
        Assert.Equal(8, fast.Statistics.SourceRowsRead); Assert.Equal(20, reference.Statistics.SourceRowsRead); Assert.Equal(3, fast.Statistics.MembershipIndexProbes);
    }
    [Fact]
    public void CoercingMembershipFallsBackWithoutChangingResults()
    {
        const string sql = "SELECT Id FROM T WHERE K IN (SELECT CStr(K) FROM U WHERE K IS NOT NULL) ORDER BY Id";
        var document = Fixture(); var fast = new QueryEngine().Select(document, sql);
        Same(new QueryEngine(new() { EnableMembershipIndexes = false }).Select(document, sql), fast);
        Assert.Equal(new[] { "1", "2" }, Column(fast)); Assert.Equal(0, fast.Statistics.MembershipIndexProbes);
    }
    [Fact]
    public void CacheIsPerExecutionDocumentAndParameters()
    {
        var engine = new QueryEngine(); var document = Fixture();
        const string sql = "SELECT Id FROM T WHERE K IN (SELECT K FROM U WHERE Id=@id) ORDER BY Id";
        Assert.Equal(new[] { "1", "2" }, Column(engine.Select(document, sql, new Dictionary<string, object?> { ["id"] = 11 })));
        Assert.Empty(engine.Select(document, sql, new Dictionary<string, object?> { ["id"] = 13 }).Records);
        document.Table("U").Records.Clear(); Assert.Empty(engine.Select(document, sql, new Dictionary<string, object?> { ["id"] = 11 }).Records);
    }
    [Fact]
    public async Task SharedEngineDoesNotShareSubqueryValuesBetweenThreads()
    {
        var engine = new QueryEngine(); var document = Fixture();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => engine.Select(document,
            "SELECT Id FROM T WHERE K IN (SELECT @key) ORDER BY Id", new Dictionary<string, object?> { ["key"] = i % 2 + 1 }))));
        for (var i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0 ? new[] { "1", "2" } : new[] { "3" }, Column(results[i]));
    }
    [Fact]
    public void BudgetsAndCancellationCoverNestedWork()
    {
        var document = Fixture();
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumSubqueryExecutions = 2 }).Select(document, "SELECT t.Id,(SELECT COUNT(*) FROM U WHERE U.K=t.K) FROM T t"));
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumTotalSourceRows = 5 }).Select(document, "SELECT Id FROM T WHERE K IN (SELECT K FROM U)"));
        Assert.Throws<DataSpaceException>(() => new QueryEngine(new() { MaximumSubqueryDepth = 1 }).Select(document, "SELECT (SELECT (SELECT 1))"));
        Assert.Throws<OperationCanceledException>(() => new QueryEngine().Select(document, "SELECT (SELECT Id FROM U)", cancellationToken: new CancellationToken(true)));
        var uncached = new QueryEngine(new() { MaximumSubqueryCacheBytes = 0 }).Select(document, "SELECT (SELECT MAX(Val) FROM U) FROM T");
        Assert.Equal(4, uncached.Statistics.SubqueryExecutions); Assert.Equal(0, uncached.Statistics.SubqueryCacheBytes);
    }
    [Fact]
    public void UpdateInsertAndDeleteReadPreStatementDataAndRemainUndoable()
    {
        var workspace = new DatabaseWorkspace(Fixture()); var engine = new QueryEngine();
        var update = engine.Execute(workspace, "UPDATE T SET Amount=(SELECT MAX(Amount) FROM T)+1");
        Assert.Equal(4, update.AffectedRecords); Assert.All(workspace.Document.Table("T").Records, r => Assert.Equal("41", r["Amount"]));
        Assert.Equal(1, update.Statistics.SubqueryExecutions); workspace.Undo();
        engine.Execute(workspace, "INSERT INTO T (Id,Amount) VALUES (5,(SELECT MAX(Amount) FROM T)+1),(6,(SELECT MAX(Amount) FROM T)+1)");
        Assert.All(workspace.Document.Table("T").Records.Skip(4), r => Assert.Equal("41", r["Amount"])); workspace.Undo();
        Assert.Equal(2, engine.Execute(workspace, "DELETE * FROM T WHERE NOT EXISTS (SELECT U.Id FROM U WHERE U.K=T.K)").AffectedRecords);
        Assert.Equal(new[] { "1", "2" }, workspace.Document.Table("T").Records.Select(r => r["Id"])); workspace.Undo(); Assert.Equal(4, workspace.Document.Table("T").Records.Count);
    }
    [Fact]
    public void CorrelatedAssignmentsAndFailedScalarUpdatesAreAtomic()
    {
        var workspace = new DatabaseWorkspace(Fixture()); var engine = new QueryEngine();
        engine.Execute(workspace, "UPDATE T SET Amount=(SELECT MAX(Val) FROM U WHERE U.K=T.K) WHERE K IN (SELECT K FROM U)");
        Assert.Equal(new[] { "15", "15", "30", "40" }, workspace.Document.Table("T").Records.Select(r => r["Amount"]));
        var before = DocumentCodec.Serialize(workspace.Document);
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "UPDATE T SET Amount=(SELECT Val FROM U WHERE U.K=T.K)"));
        Assert.Equal(before, DocumentCodec.Serialize(workspace.Document));
    }
    [Fact]
    public void MakeTableSavedSourcesAndSqlDesignRoundTripsRetainSubqueries()
    {
        var document = Fixture(); const string sql = "SELECT t.Id FROM T t WHERE NOT EXISTS (SELECT u.Id FROM U u WHERE u.K=t.K -- inner comment\n) ORDER BY t.Id";
        var engine = new QueryEngine(); Same(engine.Select(document, sql), engine.Select(document, QueryDesign.FromSql(sql).ToSql()));
        document.Queries.Add(new() { Name = "Unmatched", Sql = sql });
        Assert.Equal(new[] { "3", "4" }, Column(engine.Select(document, "SELECT Id FROM Unmatched")));
        var workspace = new DatabaseWorkspace(document); engine.Execute(workspace, "SELECT t.Id INTO Archive FROM T t WHERE EXISTS (SELECT u.Id FROM U u WHERE u.K=t.K)");
        Assert.Equal(new[] { "1", "2" }, workspace.Document.Table("Archive").Records.Select(r => r["Id"]));
    }
}
