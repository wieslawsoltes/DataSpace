using DataSpace.Core;
using DataSpace.Query;
using Xunit;

namespace DataSpace.Tests;

public sealed class UnionTypeTests
{
    [Theory]
    [InlineData("SELECT 1.0 AS Value UNION SELECT 1.00", "1")]
    [InlineData("SELECT TRUE AS Value UNION SELECT -1", "-1")]
    [InlineData("SELECT FALSE AS Value UNION SELECT 0", "0")]
    public void UnionDeduplicatesNumericallyEquivalentValues(string sql, string value)
    {
        var result = new QueryEngine().Select(new(), sql);
        Assert.Single(result.Records); Assert.Equal(value, result.Records[0]["Value"]);
    }
    [Fact]
    public void NullLiteralDoesNotForceLexicographicNumericOrdering()
    {
        var result = new QueryEngine().Select(new(), "SELECT NULL AS Value UNION ALL SELECT 10 UNION ALL SELECT 2 ORDER BY Value");
        Assert.Equal(FieldType.Decimal, result.Fields[0].Type);
        Assert.Equal(new string?[] { null, "2", "10" }, result.Records.Select(r => r["Value"]));
    }
    [Fact]
    public void UnionAllPreservesEquivalentValuesAfterNumericWidening()
    {
        var result = new QueryEngine().Select(new(), "SELECT TRUE AS Value UNION ALL SELECT -1.00 UNION ALL SELECT NULL");
        Assert.Equal(FieldType.Decimal, result.Fields[0].Type);
        Assert.Equal(new string?[] { "-1", "-1", null }, result.Records.Select(r => r["Value"]));
    }
    [Fact]
    public void AppendingNumericUnionKeepsCanonicalValuesAndValidatesUniqueness()
    {
        var workspace = new DatabaseWorkspace(new()); var engine = new QueryEngine();
        engine.Execute(workspace, "CREATE TABLE ValuesTable (Value DECIMAL UNIQUE)");
        Assert.Equal(1, engine.Execute(workspace, "INSERT INTO ValuesTable SELECT 1.0 UNION SELECT 1.00").AffectedRecords);
        var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => engine.Execute(workspace, "INSERT INTO ValuesTable SELECT 1.000"));
        Assert.Same(before, workspace.Document);
    }
}
