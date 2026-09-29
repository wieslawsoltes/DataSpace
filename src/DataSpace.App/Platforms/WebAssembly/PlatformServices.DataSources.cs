using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DataSpace.Core;
using DataSpace.DataSources;

namespace DataSpace.App;

internal static partial class PlatformServices
{
    private static Task? _sqliteModule;
    private static Task LoadSqliteAsync() => _sqliteModule ??= LoadSqliteModuleAsync();
    private static async Task LoadSqliteModuleAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document");
        await JSHost.ImportAsync("DataSpaceSqlite", new Uri(new Uri(document.GetPropertyAsString("baseURI")!), "browser-sqlite.js").AbsoluteUri);
    }
    [JSImport("pickSqlite", "DataSpaceSqlite")] private static partial Task<string?> PickSqliteAsync();
    [JSImport("sqliteTables", "DataSpaceSqlite")] internal static partial Task<string> SqliteTablesAsync(string id);
    [JSImport("sqlitePage", "DataSpaceSqlite")] internal static partial Task<string> SqlitePageAsync(string id, string request);
    [JSImport("closeSqlite", "DataSpaceSqlite")] internal static partial void CloseSqlite(string id);
    [JSImport("exportSqlite", "DataSpaceSqlite")] private static partial Task<string> ExportSqliteJsonAsync(string json);
    public static async Task<IDataSource?> OpenSqliteAsync()
    {
        await LoadSqliteAsync(); var json = await PickSqliteAsync(); if (json is null) return null;
        using var response = JsonDocument.Parse(json);
        return new BrowserSqliteDataSource(response.RootElement.GetProperty("id").GetString()!, response.RootElement.GetProperty("name").GetString()!);
    }
    public static async Task<byte[]> ExportSqliteAsync(string name, FieldDefinition[] fields, Record[] rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); await LoadSqliteAsync();
        var json = JsonSerializer.Serialize(new { name, columns = fields.Select(f => f.Name), rows = rows.Select(r => fields.Select(f => r[f.Name]).ToArray()) });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > SourceLimits.MaxFileBytes) throw new DataSpaceException("SQLite export exceeds 16 MiB.");
        var output = await ExportSqliteJsonAsync(json); cancellationToken.ThrowIfCancellationRequested(); return Convert.FromBase64String(output);
    }
}
internal sealed class BrowserSqliteDataSource(string id, string name) : IDataSource
{
    public string DisplayName => name;
    private bool _closed;
    private async Task<T> ReadAsync<T>(Func<Task<string>> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_closed, this); token.ThrowIfCancellationRequested();
        try { var text = await action().WaitAsync(token); return JsonSerializer.Deserialize<T>(text, SourceLimits.Json) ?? throw new DataSpaceException("Invalid SQLite response."); }
        catch (OperationCanceledException) { await DisposeAsync(); throw; }
    }
    public Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default) => ReadAsync<SourceTable[]>(() => PlatformServices.SqliteTablesAsync(id), cancellationToken);
    public async Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate(); var page = await ReadAsync<SourcePage>(() => PlatformServices.SqlitePageAsync(id, JsonSerializer.Serialize(request, SourceLimits.Json)), cancellationToken);
        SourceLimits.Validate(page, request.Limit); return page;
    }
    public ValueTask DisposeAsync() { if (!_closed) { _closed = true; PlatformServices.CloseSqlite(id); } return ValueTask.CompletedTask; }
}
