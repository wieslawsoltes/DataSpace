using System.Net;
using System.Net.Http.Headers;
using DataSpace.DataSources;
using DataSpace.Gateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class GatewayTests
{
    [Fact]
    public async Task RealHttpGatewayRequiresTokenAndRestrictsTablesPagingAndMethods()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open(); using var create = db.CreateCommand(); create.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY, title TEXT); INSERT INTO items VALUES(1,'one'),(2,'two'); CREATE TABLE secrets(id INTEGER);"; create.ExecuteNonQuery();
            }
            var config = new Dictionary<string, string?>
            {
                ["GatewayToken"] = "dataspace-test-only-token-not-a-real-secret",
                ["AllowedOrigins:0"] = "https://example.test",
                ["SourceConnections:0:Id"] = "demo", ["SourceConnections:0:Name"] = "Demo", ["SourceConnections:0:Provider"] = "sqlite",
                ["SourceConnections:0:ConnectionString"] = "Data Source=" + path,
                ["SourceConnections:0:Tables:0:Id"] = "items", ["SourceConnections:0:Tables:0:Name"] = "items", ["SourceConnections:0:Tables:0:Schema"] = "",
                ["SourceConnections:0:Tables:0:OrderColumns:0"] = "id"
            };
            await using var app = GatewayHost.Create([], builder => { builder.Configuration.AddInMemoryCollection(config); builder.WebHost.UseUrls("http://127.0.0.1:0"); });
            await app.StartAsync();
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/sources")).StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/sources")).StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["GatewayToken"]);
                var list = await client.GetStringAsync("/v1/sources"); Assert.Contains("Demo", list); Assert.DoesNotContain(path, list);
                var tables = await client.GetStringAsync("/v1/sources/demo/tables"); Assert.Contains("items", tables); Assert.DoesNotContain("secrets", tables);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/sources/demo/rows?table=secrets")).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/sources/demo/rows?table=items&limit=1001")).StatusCode);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/v1/sources/demo/rows?table=items", null)).StatusCode);
                await using var source = new GatewayDataSource(address, config["GatewayToken"]!, new("demo", "Demo", "sqlite"));
                var page = await source.ReadAsync(new("items", 1, 1)); Assert.Equal("two", page.Rows[0][1]); Assert.False(page.HasMore);
                using var cross = new HttpRequestMessage(HttpMethod.Get, "/v1/sources"); cross.Headers.Add("Origin", "https://untrusted.test");
                using var response = await client.SendAsync(cross); Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
                Assert.True(response.Headers.CacheControl!.NoStore);
            }
            finally { await app.StopAsync(); }
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(301)][InlineData(401)][InlineData(500)]
    public async Task HttpFailuresNeverReflectServerDetails(int status)
    {
        using var client = new HttpClient(new Stub((HttpStatusCode)status, "Password=secret;host=private"));
        var error = await Assert.ThrowsAsync<DataSpace.Core.DataSpaceException>(() => SourceHttp.GetAsync(client, new Uri("https://example.test"), null, 5000, default));
        Assert.DoesNotContain("secret", error.Message);
    }
    [Fact]
    public async Task HttpBodySizeIsBoundedWithoutContentLength()
    {
        using var client = new HttpClient(new Stub(HttpStatusCode.OK, new string('x', 2000)));
        await Assert.ThrowsAsync<DataSpace.Core.DataSpaceException>(() => SourceHttp.GetAsync(client, new Uri("https://example.test"), null, 1000, default));
    }
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
