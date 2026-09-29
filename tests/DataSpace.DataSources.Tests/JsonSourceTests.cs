using System.Text.Json;
using DataSpace.Core;
using DataSpace.DataSources;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class JsonSourceTests
{
    [Fact]
    public async Task TypesNullsMissingNestedAndPrecisionArePreserved()
    {
        await using var source = new JsonDataSource("""[{"ID":9223372036854775807,"Name":"Żółć 日本語 😀","Enabled":true,"Huge":123456789012345678901234567890.123,"Object":{"x":[1,2]},"Empty":"","Null":null},{"ID":2,"Name":null,"Enabled":false}]""");
        var table = (await source.GetTablesAsync())[0]; var page = await source.ReadAsync(new("data", 0, 1));
        Assert.True(page.HasMore); Assert.Equal("integer", table.Columns[0].Kind); Assert.Equal("boolean", table.Columns[2].Kind);
        Assert.Equal("9223372036854775807", page.Rows[0][0]); Assert.Equal("123456789012345678901234567890.123", page.Rows[0][3]);
        Assert.Equal("{\"x\":[1,2]}", page.Rows[0][4]); Assert.Equal("", page.Rows[0][5]); Assert.Null(page.Rows[0][6]);
        var imported = await SourceImport.ReadTableAsync(source, table, "Imported");
        Assert.Equal(2, imported.Records.Count); Assert.Equal(FieldType.Integer, imported.Fields[0].Type); Assert.Equal(FieldType.YesNo, imported.Fields[2].Type);
        Assert.Null(imported.Records[1]["Huge"]);
    }
    [Theory]
    [InlineData("[1]")]
    [InlineData("[]")]
    [InlineData("[{\"x\":1,\"x\":2}]")]
    [InlineData("{}")]
    public void InvalidJsonShapesFailExplicitly(string json) => Assert.ThrowsAny<Exception>(() => new JsonDataSource(json));
    [Theory]
    [InlineData("/data/items")]
    [InlineData("/a~1b/~0value")]
    public async Task JsonPointersResolveEscapedNames(string pointer)
    {
        var json = pointer == "/data/items" ? "{\"data\":{\"items\":[{\"x\":1}]}}" : "{\"a/b\":{\"~value\":[{\"x\":1}]}}";
        await using var source = new JsonDataSource(json, pointer: pointer); Assert.Single((await source.ReadAsync(new("data"))).Rows);
    }
    [Theory]
    [InlineData("data")][InlineData("/~2")][InlineData("/missing")]
    public void InvalidPointerFails(string pointer) => Assert.Throws<DataSpaceException>(() => new JsonDataSource("[{\"x\":1}]", pointer: pointer));
    [Fact]
    public async Task ImportNamesAreSafeDistinctAndCaptionsRetained()
    {
        await using var source = new JsonDataSource("""[{"a.b":1,"a_b":2,"A_B":3}]""");
        var table = await SourceImport.ReadTableAsync(source, (await source.GetTablesAsync())[0], "Imported");
        Assert.Equal(3, table.Fields.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("a.b", table.Fields[0].Caption); SchemaValidator.Validate(new() { Tables = [table] });
    }
    [Fact]
    public async Task PreviewMaterializesOnlyRequestedRowsAndDoesNotLeakMutableSchema()
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 10000).Select(i => new { id = i, title = "item " + i }));
        await using var source = new JsonDataSource(json); var catalog = await source.GetTablesAsync(); catalog[0].Columns[0] = new("changed");
        var page = await source.ReadAsync(new("data", 300, 25));
        Assert.Equal(25, source.MaterializedRows); Assert.Equal("300", page.Rows[0][0]); Assert.Equal("id", page.Columns[0].Name);
    }
    [Fact]
    public async Task FailedImportLimitDoesNotChangeWorkspace()
    {
        await using var source = new JsonDataSource("[{\"id\":1},{\"id\":2}]"); var workspace = new DatabaseWorkspace(new()); var snapshot = workspace.Document;
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, new("data", "data", [new("id", "integer", "JSON")], []), "Copy", 1));
        Assert.Same(snapshot, workspace.Document);
    }
    [Fact]
    public async Task CancellationDoesNotMaterializeMoreRows()
    {
        await using var source = new JsonDataSource("[{\"id\":1}]");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(new("data"), new CancellationToken(true))); Assert.Equal(0, source.MaterializedRows);
    }
    [Fact]
    public async Task JsonExportRoundTripsTypedRows()
    {
        await using var source = new JsonDataSource("[{\"id\":1,\"yes\":true,\"name\":\"\"}]");
        var table = await SourceImport.ReadTableAsync(source, (await source.GetTablesAsync())[0], "Copy");
        using var output = JsonDocument.Parse(SourceImport.ExportJson(table.Fields, table.Records));
        Assert.Equal(1, output.RootElement[0].GetProperty("id").GetInt32()); Assert.True(output.RootElement[0].GetProperty("yes").GetBoolean());
    }
    [Theory]
    [InlineData(-1, 200)][InlineData(0, 0)][InlineData(0, 1001)][InlineData(1000001, 1)]
    public void BoundsAreValidated(int offset, int limit) => Assert.Throws<DataSpaceException>(() => new SourceRequest("data", offset, limit).Validate());
    [Fact]
    public async Task PageCacheIsBoundedDetachedAndRefreshable()
    {
        await using var source = new JsonDataSource("[{\"id\":1},{\"id\":2},{\"id\":3}]"); var pager = new SourcePager(source, 2);
        var first = await pager.ReadAsync(new("data", 0, 1)); first.Rows[0][0] = "damaged";
        Assert.Equal("1", (await pager.ReadAsync(new("data", 0, 1))).Rows[0][0]); Assert.Equal(1, pager.Reads); Assert.Equal(1, pager.CacheHits);
        await pager.ReadAsync(new("data", 1, 1)); await pager.ReadAsync(new("data", 2, 1)); Assert.Equal(2, pager.CachedPages);
        pager.Invalidate(); await pager.ReadAsync(new("data", 2, 1)); Assert.Equal(4, pager.Reads);
    }
    [Theory]
    [InlineData("http://example.com")][InlineData("https://user:secret@example.com")][InlineData("file:///tmp/test.json")][InlineData("https://example.com/#data")]
    public void UnsafeEndpointSchemesAndCredentialsAreRejected(string address) => Assert.Throws<DataSpaceException>(() => SourceHttp.ValidateEndpoint(address));
}
