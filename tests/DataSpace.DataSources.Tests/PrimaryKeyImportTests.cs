using DataSpace.Core;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class PrimaryKeyImportTests
{
    [Theory]
    [InlineData("[{\"Code\":\"\"}]")]
    [InlineData("[{\"Code\":null}]")]
    public async Task TextPrimaryKeysRequireNonEmptyValues(string json)
    {
        await using var source = new JsonDataSource(json); var table = (await source.GetTablesAsync())[0];
        var plan = SourceImportPlan.CreateDefault(table); plan.Fields[0] = plan.Fields[0] with { Type = FieldType.ShortText, PrimaryKey = true };
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, table, "Copy", plan));
    }
    [Fact]
    public async Task RequiredNonKeyTextRetainsExplicitZeroLengthCompatibility()
    {
        await using var source = new JsonDataSource("[{\"Code\":\"\"}]"); var table = (await source.GetTablesAsync())[0];
        var plan = SourceImportPlan.CreateDefault(table); plan.Fields[0] = plan.Fields[0] with { Type = FieldType.ShortText, Required = true };
        Assert.Equal("", (await SourceImport.ReadTableAsync(source, table, "Copy", plan)).Records[0]["Code"]);
    }
}
