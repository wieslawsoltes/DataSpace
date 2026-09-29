using System.Text.Json;
using DataSpace.DataSources;

/// <summary>Warm JSON page-read scenario; construction is intentionally outside the timed actions.</summary>
public sealed class SourcePagingScenario : IDisposable
{
    public const int RowCount = 5000, ColumnCount = 32, Offset = 4700, Limit = 200;
    private readonly JsonDocument _reference;
    private readonly JsonDataSource _source;
    private readonly SourceTable _table;
    private readonly SourceRequest _request = new("data", Offset, Limit);
    public object Evidence { get; }

    public SourcePagingScenario()
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(0, RowCount).Select(row =>
            Enumerable.Range(0, ColumnCount).ToDictionary(col => "Field" + col, col => (string?)(row + ":" + col))));
        _reference = JsonDocument.Parse(json);
        _source = new JsonDataSource(json);
        _table = _source.GetTablesAsync().GetAwaiter().GetResult()[0];
        var expected = ReferencePage(); var actual = IndexedPage();
        if (JsonSerializer.Serialize(expected, SourceLimits.Json) != JsonSerializer.Serialize(actual, SourceLimits.Json))
            throw new Exception("Source page outputs differ.");
        var pager = new SourcePager(_source);
        pager.ReadAsync(_request).GetAwaiter().GetResult(); var before = _source.MaterializedRows;
        for (var i = 0; i < 10; i++) pager.ReadAsync(_request).GetAwaiter().GetResult();
        var decoded = _source.MaterializedRows - before;
        if (decoded != 0 || pager.Reads != 1 || pager.CacheHits != 10) throw new Exception("Unexpected page-cache work.");
        Evidence = new { rows = RowCount, columns = ColumnCount, offset = Offset, limit = Limit,
            utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(json), cacheReads = pager.Reads,
            cacheHits = pager.CacheHits, additionalDecodedRows = decoded,
            scope = "Warm JSON page reads only. Parsing, schema scanning, row-handle index construction, file/network I/O and rendering excluded. The row-handle index adds O(row count) storage." };
    }

    public SourcePage ReferencePage()
    {
        var rows = new string?[Limit][];
        for (var i = 0; i < Limit; i++)
        {
            var row = _reference.RootElement[Offset + i];
            rows[i] = _table.Columns.Select(col => row.TryGetProperty(col.Name, out var value) ? value.GetString() : null).ToArray();
        }
        var page = new SourcePage(_table.Columns.ToArray(), rows, Offset + Limit < RowCount);
        SourceLimits.Validate(page, Limit); return page;
    }
    public SourcePage IndexedPage() => _source.ReadAsync(_request).GetAwaiter().GetResult();
    public void Dispose() { _reference.Dispose(); _source.DisposeAsync().GetAwaiter().GetResult(); }
}
