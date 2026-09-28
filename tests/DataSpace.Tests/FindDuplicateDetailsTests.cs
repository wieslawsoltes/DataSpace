using DataSpace.Core;
using DataSpace.Query;
using System.Text.Json;
using Xunit;

namespace DataSpace.Tests;

public sealed class FindDuplicateDetailsTests
{
    private static DatabaseDocument Fixture(FieldType type, params string?[] values)
    {
        var table = new TableDefinition { Name = "Source data", Fields = [
            new() { Name = "ID", Type = FieldType.Integer }, new() { Name = "Key", Type = type },
            new() { Name = "Other" } ] };
        for (var i = 0; i < values.Length; i++)
            RecordOperations.Insert(table, new Dictionary<string, string?>
            { ["ID"] = (i + 1).ToString(FieldValues.Culture), ["Key"] = values[i], ["Other"] = i % 2 == 0 ? "A" : "B" });
        var document = new DatabaseDocument { Tables = [table] };
        SchemaValidator.Validate(document);
        return document;
    }
    private static FindQueryDesign Design(bool includeNulls) => new()
    {
        Source = "Source data", MatchFields = ["Key"], OutputFields = ["ID"],
        SummaryOnly = false, IncludeNullKeys = includeNulls
    };
    private static string Reference(bool includeNulls) =>
        "SELECT [s].[ID] FROM [Source data] AS [s] WHERE " +
        (includeNulls ? "" : "[s].[Key] IS NOT NULL AND ") +
        "(SELECT COUNT(*) FROM [Source data] AS [d] WHERE " +
        (includeNulls ? "([d].[Key] = [s].[Key] OR ([d].[Key] IS NULL AND [s].[Key] IS NULL))" : "[d].[Key] = [s].[Key]") + ") > 1;";
    private static string?[] Ids(QueryResult result) => result.Records.Select(row => row["ID"]).ToArray();

    public static IEnumerable<object[]> KeyTypes()
    {
        yield return [FieldType.ShortText, new string?[] { "A", "a", "B", null, null, "", "" }];
        yield return [FieldType.LongText, new string?[] { "line\ntext", "line\ntext", "line\rtext", "1:2", null, null }];
        yield return [FieldType.Integer, new string?[] { "1", "+1", "-1", "0", "0", null, null }];
        yield return [FieldType.Decimal, new string?[] { "1.0", "1.00", "-2", "-2.000", null, null }];
        yield return [FieldType.Currency, new string?[] { "3.10001", "3.1", "3.2", null, null }];
        yield return [FieldType.DateTime, new string?[] { "2025-01-02T03:04:05", "2025-01-02T03:04:05", "2025-01-03T03:04:05", null, null }];
        yield return [FieldType.YesNo, new string?[] { "Yes", "-1", "False", "0", null, null }];
        yield return [FieldType.Guid, new string?[] { "AAAAAAAA-0000-0000-0000-000000000001", "aaaaaaaa-0000-0000-0000-000000000001", "bbbbbbbb-0000-0000-0000-000000000001", null, null }];
    }

    [Theory]
    [MemberData(nameof(KeyTypes))]
    public void SingleKeyDetailsMatchCorrelatedReferenceForEverySupportedKeyFamily(FieldType type, string?[] values)
    {
        var document = Fixture(type, values);
        var before = DocumentCodec.Serialize(document);
        foreach (var nulls in new[] { false, true })
        {
            var engine = new QueryEngine();
            var sql = Design(nulls).ToSql(document);
            var expected = engine.Select(document, Reference(nulls));
            var actual = engine.Select(document, sql);
            Assert.Equal(Ids(expected), Ids(actual));
            Assert.InRange(actual.Statistics.SubqueryExecutions, 0L, 2L);
            var noCache = new QueryEngine(new() { EnableSubqueryCache = false, EnableMembershipIndexes = false });
            Assert.Equal(Ids(expected), Ids(noCache.Select(document, sql)));
        }
        Assert.Equal(before, DocumentCodec.Serialize(document));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void EmptyAndAllNullInputsPreserveExplicitNullInclusion(int count)
    {
        var document = Fixture(FieldType.ShortText, new string?[count]);
        foreach (var nulls in new[] { false, true })
        {
            var result = new QueryEngine().Select(document, Design(nulls).ToSql(document));
            Assert.Equal(nulls && count > 1 ? count : 0, result.Records.Count);
        }
    }

    [Fact]
    public void ScanWorkIsLinearWithAdmittedIndependentSetsAndNoCorrelatedExecutions()
    {
        const int count = 1000;
        var document = Fixture(FieldType.ShortText, Enumerable.Range(0, count)
            .Select(i => i % 19 == 0 ? null : "Group " + i % 31).ToArray());
        var result = new QueryEngine().Select(document, Design(true).ToSql(document));
        Assert.Equal(count, result.Records.Count);
        Assert.Equal(2L, result.Statistics.SubqueryExecutions);
        Assert.Equal(3L * count, result.Statistics.SourceRowsRead);
        Assert.True(result.Statistics.MembershipIndexProbes > 0);
        Assert.Equal(0L, result.Statistics.SubqueryComparisons);
        Assert.True(result.Statistics.SubqueryCacheHits > 0);
    }

    [Fact]
    public void ZeroCacheBudgetChangesOnlyWorkNotResults()
    {
        var document = Fixture(FieldType.ShortText, "A", "a", "B", null, null);
        var sql = Design(true).ToSql(document);
        var expected = new QueryEngine().Select(document, sql);
        var actual = new QueryEngine(new() { MaximumSubqueryCacheBytes = 0 }).Select(document, sql);
        Assert.Equal(Ids(expected), Ids(actual));
        Assert.Equal(0L, actual.Statistics.SubqueryCacheBytes);
    }

    [Fact]
    public void ReusingEngineAfterAnEditDoesNotReusePreviousDuplicateKeys()
    {
        var workspace = new DatabaseWorkspace(Fixture(FieldType.ShortText, "A", "a", "B"));
        var engine = new QueryEngine(); var sql = Design(false).ToSql(workspace.Document);
        Assert.Equal(new[] { "1", "2" }, Ids(engine.Select(workspace.Document, sql)));
        workspace.UpdateRecords("Change duplicate key", "Source data",
            [new(workspace.Document.Tables[0].Records[1].Id, "Key", "B")]);
        Assert.Equal(new[] { "2", "3" }, Ids(engine.Select(workspace.Document, sql)));
        workspace.Undo();
        Assert.Equal(new[] { "1", "2" }, Ids(engine.Select(workspace.Document, sql)));
    }

    [Fact]
    public void CompositeKeysKeepNullSafeReferencePathAndSelectedOutputs()
    {
        var document = Fixture(FieldType.ShortText, "A", "A", "A", null, null);
        var design = Design(true); design.MatchFields.Add("Other");
        var sql = design.ToSql(document);
        Assert.DoesNotContain(" IN (SELECT ", sql);
        Assert.Equal(new[] { "1", "3" }, Ids(new QueryEngine().Select(document, sql)));
    }

    [Fact]
    public void CancellationRemainsObservableBeforeExecution()
    {
        var document = Fixture(FieldType.ShortText, "A", "a");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new QueryEngine().Select(document,
            Design(true).ToSql(document), cancellationToken: cts.Token));
    }

    [Fact]
    public void EmittedSqlMatchesThePortableReferenceFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "duplicate-detail-sql.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var document = Fixture(FieldType.ShortText);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var includeNulls = item.GetProperty("includeNulls").GetBoolean();
            Assert.Equal(item.GetProperty("optimizedSql").GetString(), Design(includeNulls).ToSql(document));
            Assert.Equal(item.GetProperty("referenceSql").GetString(), Reference(includeNulls));
        }
    }
}
