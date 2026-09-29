using System.Text.Json;
using DataSpace.Core;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class JsonPagingTests
{
    [Fact]
    public async Task RandomWidePagesMatchIndependentPropertyLookups()
    {
        var rows = Enumerable.Range(0, 300).Select(row => Enumerable.Range(0, 32)
            .Where(col => (row + col) % 7 != 0).Reverse()
            .ToDictionary(col => "Field" + col, col => row % 5 == 0 ? null : (string?)(row + ":" + col))).ToArray();
        var json = JsonSerializer.Serialize(rows); using var reference = JsonDocument.Parse(json);
        await using var source = new JsonDataSource(json);
        var table = (await source.GetTablesAsync())[0];
        var random = new Random(9182);
        for (var sample = 0; sample < 30; sample++)
        {
            var offset = random.Next(0, 310); var count = random.Next(1, 24);
            var page = await source.ReadAsync(new("data", offset, count));
            var expected = Math.Min(count, Math.Max(0, 300 - offset)); Assert.Equal(expected, page.Rows.Length);
            Assert.Equal(offset + expected < 300, page.HasMore);
            for (var row = 0; row < expected; row++)
                for (var col = 0; col < table.Columns.Length; col++)
                {
                    var item = reference.RootElement[offset + row];
                    var value = item.TryGetProperty(table.Columns[col].Name, out var cell) && cell.ValueKind != JsonValueKind.Null ? cell.GetString() : null;
                    Assert.Equal(value, page.Rows[row][col]);
                }
        }
    }
    [Fact]
    public async Task OrderedFastPathFallsBackForMissingAndReorderedFields()
    {
        await using var source = new JsonDataSource("""
            [{"A":"a1","B":"b1","C":"c1"},
             {"C":"c2","A":"a2","B":"b2"},
             {"A":"a3","C":"c3"},
             {"B":null,"C":""}]
            """);
        var page = await source.ReadAsync(new("data", 0, 10));
        Assert.Equal(new[] { "A", "B", "C" }, page.Columns.Select(c => c.Name));
        Assert.Equal(new string?[] { "a1", "b1", "c1" }, page.Rows[0]);
        Assert.Equal(new string?[] { "a2", "b2", "c2" }, page.Rows[1]);
        Assert.Equal(new string?[] { "a3", null, "c3" }, page.Rows[2]);
        Assert.Equal(new string?[] { null, null, "" }, page.Rows[3]);
    }
    [Fact]
    public async Task CachedPageDoesNotDecodeAgainAndRefreshDoes()
    {
        await using var source = new JsonDataSource("[{\"x\":1},{\"x\":2}]"); var pager = new SourcePager(source);
        await pager.ReadAsync(new("data", 0, 1)); var decoded = source.MaterializedRows;
        for (var i = 0; i < 10; i++) Assert.Equal("1", (await pager.ReadAsync(new("data", 0, 1))).Rows[0][0]);
        Assert.Equal(decoded, source.MaterializedRows); Assert.Equal(10, pager.CacheHits);
        pager.Invalidate(); await pager.ReadAsync(new("data", 0, 1)); Assert.Equal(decoded + 1, source.MaterializedRows);
    }
    [Fact]
    public async Task CancelledEmptyPageHonorsCancellation()
    {
        await using var source = new JsonDataSource("[{\"x\":1}]");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(new("data", 100, 1), new(true)));
    }
    [Theory]
    [InlineData("/00")][InlineData("/01")][InlineData("/+")][InlineData("/-")]
    public void ArrayPointerIndicesMustBeCanonical(string pointer) =>
        Assert.Throws<DataSpaceException>(() => new JsonDataSource("[[{\"x\":1}],[{\"x\":2}]]", pointer: pointer));
}
