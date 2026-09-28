using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class FindQueryDesignTests
{
    private static DatabaseDocument Fixture()
    {
        var source = new TableDefinition { Name = "Source data", Fields = [new() { Name = "ID", Type = FieldType.Integer }, new() { Name = "Group" }, new() { Name = "Kind" }] };
        var related = new TableDefinition { Name = "Related data", Fields = [new() { Name = "Key" }, new() { Name = "Kind" }] };
        var groups = new string?[] { "A", "a", "A", "B", null, null };
        for (var i = 0; i < groups.Length; i++) RecordOperations.Insert(source, new Dictionary<string, string?> { ["ID"] = (i+1).ToString(), ["Group"] = groups[i], ["Kind"] = i == 2 ? "Y" : "X" });
        foreach (var key in new string?[] { "A", "A", null }) RecordOperations.Insert(related, new Dictionary<string, string?> { ["Key"] = key, ["Kind"] = "X" });
        return new() { Tables = [source, related] };
    }
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void DuplicateSummaryAndDetailsHaveExplicitNullSemantics(bool nulls, int groups)
    {
        var document = Fixture(); var engine = new QueryEngine();
        var design = new FindQueryDesign { Source = "Source data", MatchFields = ["Group", "Kind"], IncludeNullKeys = nulls };
        var result = engine.Select(document, design.ToSql(document));
        Assert.Equal(groups, result.Records.Count); Assert.All(result.Records, row => Assert.Equal("2", row[result.Fields[^1].Name]));
        design.SummaryOnly = false; design.OutputFields = ["ID"];
        result = engine.Select(document, design.ToSql(document));
        Assert.Single(result.Fields); Assert.Equal(nulls ? new[] { "1", "2", "5", "6" } : new[] { "1", "2" }, result.Records.Select(r => r["ID"]));
    }
    [Fact]
    public void UnmatchedSingleAndCompositeKeysRetainNullsWithoutDuplicateRows()
    {
        var document = Fixture(); var engine = new QueryEngine();
        var design = new FindQueryDesign { Kind = FindQueryKind.Unmatched, Source = "Source data", RelatedSource = "Related data", MatchFields = ["Group"], RelatedFields = ["Key"], OutputFields = ["ID"] };
        var result = engine.Select(document, design.ToSql(document));
        Assert.Equal(new[] { "4", "5", "6" }, result.Records.Select(r => r["ID"])); Assert.Equal(1, result.Statistics.SubqueryExecutions);
        design.MatchFields.Add("Kind"); design.RelatedFields.Add("Kind");
        result = engine.Select(document, design.ToSql(document));
        Assert.Equal(new[] { "3", "4", "5", "6" }, result.Records.Select(r => r["ID"])); Assert.Equal(1, result.Statistics.HashJoins);
    }
    [Fact]
    public void DefinitionsAreIndependentAndPersistAsOrdinarySavedSql()
    {
        var document = Fixture(); var design = new FindQueryDesign { Source = "Source data", MatchFields = ["Group"] };
        var before = DocumentCodec.Serialize(document); var sql = design.ToSql(document);
        Assert.Equal(before, DocumentCodec.Serialize(document));
        document.Queries.Add(new() { Name = "Duplicates", Sql = sql });
        Assert.Equal(2, new QueryEngine().Select(DocumentCodec.Deserialize(DocumentCodec.Serialize(document)), "SELECT * FROM Duplicates").Records.Count);
    }
    [Fact]
    public void InvalidFieldsPairsAndTypesFailBeforeExecuting()
    {
        var document = Fixture(); var design = new FindQueryDesign { Source = "Source data" };
        Assert.Throws<DataSpaceException>(() => design.ToSql(document));
        design.MatchFields = ["Group", "group"]; Assert.Throws<DataSpaceException>(() => design.ToSql(document));
        design.MatchFields = ["Missing"]; Assert.Throws<DataSpaceException>(() => design.ToSql(document));
        design.MatchFields = ["ID"]; design.Kind = FindQueryKind.Unmatched; design.RelatedSource = "Related data"; design.RelatedFields = ["Key"];
        Assert.Throws<DataSpaceException>(() => design.ToSql(document));
        design.MatchFields = ["Group", "Kind"]; Assert.Throws<DataSpaceException>(() => design.ToSql(document));
    }
}
