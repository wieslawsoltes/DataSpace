using System.Globalization;
using System.Text;
using System.Text.Json;
using DataSpace.Core;

namespace DataSpace.DataSources;

/// <summary>Immutable parsed JSON snapshot. Retains JSON elements, not a second fully materialized row/field dictionary graph.</summary>
public sealed class JsonDataSource : IDataSource
{
    private readonly JsonDocument _document;
    private readonly JsonElement _rows;
    private readonly SourceTable _table;
    private bool _disposed;
    public string DisplayName { get; }
    public int RowCount => _rows.GetArrayLength();
    public long MaterializedRows { get; private set; }

    public JsonDataSource(string json, string displayName = "JSON", string pointer = "")
    {
        if (Encoding.UTF8.GetByteCount(json) > SourceLimits.MaxFileBytes) throw new DataSpaceException("JSON files are limited to 16 MiB.");
        DisplayName = displayName;
        _document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        try
        {
            _rows = ResolvePointer(_document.RootElement, pointer);
            if (_rows.ValueKind != JsonValueKind.Array) throw new DataSpaceException("Select an array of JSON objects using a JSON Pointer, such as /data/items.");
            if (_rows.GetArrayLength() > SourceLimits.MaxImportRows) throw new DataSpaceException("JSON sources are limited to 100,000 rows.");
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) throw new DataSpaceException("Every JSON row must be an object.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in row.EnumerateObject())
                {
                    if (!seen.Add(property.Name)) throw new DataSpaceException("A JSON row contains duplicate property names.");
                    if (string.IsNullOrEmpty(property.Name) || property.Name.Length > 512) throw new DataSpaceException("JSON property names must contain 1–512 characters.");
                    var kind = Kind(property.Value);
                    columns[property.Name] = columns.TryGetValue(property.Name, out var old) ? MergeKind(old, kind) : kind;
                    if (columns.Count > SourceLimits.MaxColumns) throw new DataSpaceException("JSON sources are limited to 128 columns.");
                    if (Text(property.Value)?.Length > SourceLimits.MaxCellCharacters) throw new DataSpaceException("A JSON value is too large.");
                }
            }
            if (columns.Count == 0) throw new DataSpaceException("An empty JSON array has no discoverable columns. Supply at least one object with fields.");
            _table = new("data", "JSON data", columns.Select(c => new SourceColumn(c.Key, c.Value == "null" ? "text" : c.Value, "JSON")).ToArray(), []);
        }
        catch { _document.Dispose(); throw; }
    }
    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Number when value.TryGetInt64(out _) => "integer",
        // Preserve arbitrary-precision / exponent numbers as their original text, never round through double or decimal.
        _ => "text"
    };
    private static string MergeKind(string old, string next) => old == "null" ? next : next == "null" || old == next ? old : "text";
    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => value.GetRawText()
    };
    public static JsonElement ResolvePointer(JsonElement root, string pointer)
    {
        if (pointer.Length == 0) return root;
        if (!pointer.StartsWith('/')) throw new DataSpaceException("A JSON Pointer must start with '/'.");
        foreach (var part in pointer[1..].Split('/'))
        {
            for (var i = 0; i < part.Length; i++)
                if (part[i] == '~' && (++i == part.Length || part[i] is not ('0' or '1'))) throw new DataSpaceException("Invalid JSON Pointer escape.");
            var key = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var child)) root = child;
            else if (root.ValueKind == JsonValueKind.Array && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < root.GetArrayLength()) root = root[index];
            else throw new DataSpaceException("The JSON Pointer does not resolve to an existing value.");
        }
        return root;
    }
    public Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new[] { _table with { Columns = _table.Columns.ToArray(), OrderColumns = [] } });
    }
    public Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); request.Validate();
        if (request.Table != "data") throw new DataSpaceException("JSON table not found.");
        var result = new List<string?[]>();
        var end = Math.Min((long)request.Offset + request.Limit, RowCount);
        for (var index = request.Offset; index < end; index++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var row = _rows[index];
            result.Add(_table.Columns.Select(c => row.TryGetProperty(c.Name, out var value) ? Text(value) : null).ToArray());
        }
        var page = new SourcePage(_table.Columns.ToArray(), result.ToArray(), end < RowCount);
        SourceLimits.Validate(page, request.Limit); MaterializedRows += result.Count; return Task.FromResult(page);
    }
    public ValueTask DisposeAsync() { if (!_disposed) { _disposed = true; _document.Dispose(); } return ValueTask.CompletedTask; }
}
