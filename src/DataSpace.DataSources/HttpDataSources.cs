using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DataSpace.Core;

namespace DataSpace.DataSources;

public sealed record GatewaySource(string Id, string Name, string Provider);

public static class SourceHttp
{
    public static Uri ValidateEndpoint(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new DataSpaceException("Use HTTPS, or HTTP on loopback for local development. Credentials must not be embedded in URLs.");
        return uri;
    }
    public static HttpClient CreateClient() => OperatingSystem.IsBrowser() ? new HttpClient() : new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    public static async Task<string> GetAsync(HttpClient client, Uri uri, string? token, int maximumBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // .NET's browser handler passes these Fetch options to the browser. Never follow a credential-bearing redirect.
        request.Options.Set(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), new Dictionary<string, object> { ["redirect"] = "error", ["credentials"] = "omit" });
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new DataSpaceException($"Data source request failed (HTTP {(int)response.StatusCode}). Check the endpoint, permission and server configuration.");
        if (response.Content.Headers.ContentLength > maximumBytes) throw new DataSpaceException("Source response exceeds the size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(); var buffer = new byte[16384];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new DataSpaceException("Source response exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
    public static async Task<JsonDataSource> OpenJsonAsync(HttpClient client, string address, string pointer = "", CancellationToken cancellationToken = default)
    {
        var uri = ValidateEndpoint(address);
        var text = await GetAsync(client, uri, null, SourceLimits.MaxFileBytes, cancellationToken);
        // Do not retain signed URL query parameters in document metadata.
        return new JsonDataSource(text, "JSON endpoint " + uri.Host, pointer);
    }
}

/// <summary>Read-only HTTPS gateway client. Owns a session-only access token, never a database connection string.</summary>
public sealed class GatewayDataSource : IDataSource
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _base;
    private string? _token;
    private readonly string _id;
    public string DisplayName { get; }
    public GatewayDataSource(string endpoint, string token, GatewaySource source, HttpClient? client = null)
    {
        _base = Base(endpoint); _token = token; _id = source.Id; DisplayName = source.Name + " (" + source.Provider + ")";
        _client = client ?? SourceHttp.CreateClient(); _ownsClient = client is null;
    }
    private static Uri Base(string endpoint)
    {
        var uri = SourceHttp.ValidateEndpoint(endpoint);
        if (uri.Query.Length != 0) throw new DataSpaceException("Gateway base URLs must not contain query parameters.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    public static async Task<GatewaySource[]> DiscoverAsync(string endpoint, string token, CancellationToken cancellationToken = default)
    {
        using var client = SourceHttp.CreateClient();
        var text = await SourceHttp.GetAsync(client, new Uri(Base(endpoint), "v1/sources"), token, SourceLimits.MaxPageBytes, cancellationToken);
        return JsonSerializer.Deserialize<GatewaySource[]>(text, SourceLimits.Json) ?? throw new DataSpaceException("Invalid source catalog.");
    }
    public async Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var text = await SourceHttp.GetAsync(_client, new Uri(_base, "v1/sources/" + Uri.EscapeDataString(_id) + "/tables"), _token, SourceLimits.MaxPageBytes, cancellationToken);
        return JsonSerializer.Deserialize<SourceTable[]>(text, SourceLimits.Json) ?? throw new DataSpaceException("Invalid table catalog.");
    }
    public async Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        var path = "v1/sources/" + Uri.EscapeDataString(_id) + "/rows?table=" + Uri.EscapeDataString(request.Table) + "&offset=" + request.Offset + "&limit=" + request.Limit;
        var text = await SourceHttp.GetAsync(_client, new Uri(_base, path), _token, SourceLimits.MaxPageBytes, cancellationToken);
        var page = JsonSerializer.Deserialize<SourcePage>(text, SourceLimits.Json) ?? throw new DataSpaceException("Invalid data page.");
        SourceLimits.Validate(page, request.Limit); return page;
    }
    public ValueTask DisposeAsync() { _token = null; if (_ownsClient) _client.Dispose(); return ValueTask.CompletedTask; }
}
