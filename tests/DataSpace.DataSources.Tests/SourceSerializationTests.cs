using System.Text.Json;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class SourceSerializationTests
{
    [Fact]
    public void GeneratedContractsRoundTripWithWebPropertyNames()
    {
        var column = new SourceColumn("値", "text", "TEXT");
        var tables = new[] { new SourceTable("items", "Items", [column], ["値"]) };
        var tableJson = JsonSerializer.Serialize(tables, SourceLimits.Json);
        Assert.Contains("\"columns\"", tableJson);
        Assert.Equal(tables[0].Name, JsonSerializer.Deserialize<SourceTable[]>(tableJson, SourceLimits.Json)![0].Name);
        var page = new SourcePage([column], [["9223372036854775807"], [null], [""]], true);
        var pageJson = JsonSerializer.Serialize(page, SourceLimits.Json);
        var restored = JsonSerializer.Deserialize<SourcePage>(pageJson, SourceLimits.Json)!;
        Assert.True(restored.HasMore); Assert.Equal(page.Rows, restored.Rows);
        var request = new SourceRequest("items", 200, 200);
        Assert.Equal(request, JsonSerializer.Deserialize<SourceRequest>(JsonSerializer.Serialize(request, SourceLimits.Json), SourceLimits.Json));
        var sources = new[] { new GatewaySource("pg", "Reporting", "postgresql") };
        Assert.Equal(sources, JsonSerializer.Deserialize<GatewaySource[]>(JsonSerializer.Serialize(sources, SourceLimits.Json), SourceLimits.Json));
        var export = new SqliteExport("Copy", ["値"], page.Rows);
        var exportJson = JsonSerializer.Serialize(export, SourceLimits.Json);
        Assert.Contains("\"name\":\"Copy\"", exportJson);
        Assert.Equal(export.Rows, JsonSerializer.Deserialize<SqliteExport>(exportJson, SourceLimits.Json)!.Rows);
        Assert.Contains("SourceJsonContext", SourceLimits.Json.TypeInfoResolver!.GetType().Name);
    }
}
