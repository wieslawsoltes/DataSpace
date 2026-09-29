using System.Data;
using System.Data.Common;
using System.Globalization;
using DataSpace.Core;

namespace DataSpace.DataSources.Relational;

public enum SourceDialect { Sqlite, PostgreSql, MySql, SqlServer }
public sealed record TableBinding(string Id, string Name, string Schema, string[] OrderColumns);

/// <summary>Only configured tables may be read. Identifiers come from server configuration/schema; row limits are parameters.</summary>
public sealed class RelationalDataSource : IDataSource
{
    private readonly Func<DbConnection> _connection;
    private readonly TableBinding[] _bindings;
    private readonly SourceDialect _dialect;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private SourceTable[]? _catalog;
    private DateTime _catalogAt;
    private readonly Action? _close;
    private bool _disposed;
    public string DisplayName { get; }
    public RelationalDataSource(string name, SourceDialect dialect, Func<DbConnection> connection, IEnumerable<TableBinding> tables, Action? close = null)
    {
        DisplayName = name; _dialect = dialect; _connection = connection; _bindings = tables.ToArray(); _close = close;
        if (_bindings.Length is < 1 or > 128 || _bindings.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != _bindings.Length)
            throw new DataSpaceException("Configure 1–128 uniquely identified source tables.");
        foreach (var table in _bindings)
        {
            Quote(table.Name, dialect); if (table.Schema.Length != 0) Quote(table.Schema, dialect);
            foreach (var column in table.OrderColumns) Quote(column, dialect);
            if (table.OrderColumns.Length == 0) throw new DataSpaceException("Configure a stable, unique ordering key for every external table.");
        }
    }
    public static string Quote(string value, SourceDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl)) throw new DataSpaceException("Invalid source identifier.");
        return dialect switch
        {
            SourceDialect.MySql => "`" + value.Replace("`", "``", StringComparison.Ordinal) + "`",
            SourceDialect.SqlServer => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]",
            _ => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
        };
    }
    private string TableSql(TableBinding table) => (table.Schema.Length == 0 ? "" : Quote(table.Schema, _dialect) + ".") + Quote(table.Name, _dialect);
    private async Task<DbConnection> OpenAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); var connection = _connection();
        try
        {
            await connection.OpenAsync(token);
            if (_dialect == SourceDialect.Sqlite)
            {
                await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF;";
                await command.ExecuteNonQueryAsync(token);
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default) =>
        _dialect == SourceDialect.Sqlite ? Task.Run(() => CatalogAsync(cancellationToken), cancellationToken) : CatalogAsync(cancellationToken);
    private async Task<SourceTable[]> CatalogAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); await _schemaGate.WaitAsync(token);
        try
        {
            if (_catalog is not null && DateTime.UtcNow - _catalogAt < TimeSpan.FromSeconds(30)) return CopyCatalog(_catalog);
            await using var connection = await OpenAsync(token); var result = new List<SourceTable>();
            foreach (var binding in _bindings)
            {
                await using var command = connection.CreateCommand(); command.CommandTimeout = 20;
                command.CommandText = "SELECT * FROM " + TableSql(binding) + " WHERE 1=0";
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, token);
                if (reader.FieldCount is < 1 or > SourceLimits.MaxColumns) throw new DataSpaceException("External tables require 1–128 columns.");
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new SourceColumn(reader.GetName(i), "text", reader.GetDataTypeName(i))).ToArray();
                if (columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != columns.Length) throw new DataSpaceException("Duplicate external columns.");
                foreach (var key in binding.OrderColumns)
                    if (!columns.Any(c => string.Equals(c.Name, key, StringComparison.Ordinal)) && !(_dialect == SourceDialect.Sqlite && key == "_rowid_"))
                        throw new DataSpaceException("Configured ordering column does not exist: " + key);
                result.Add(new(binding.Id, binding.Schema.Length == 0 ? binding.Name : binding.Schema + "." + binding.Name, columns, binding.OrderColumns.ToArray()));
            }
            _catalog = result.ToArray(); _catalogAt = DateTime.UtcNow; return CopyCatalog(_catalog);
        }
        finally { _schemaGate.Release(); }
    }
    private static SourceTable[] CopyCatalog(SourceTable[] tables) => tables.Select(t => t with { Columns = t.Columns.ToArray(), OrderColumns = t.OrderColumns.ToArray() }).ToArray();
    public Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        return _dialect == SourceDialect.Sqlite ? Task.Run(() => ReadCoreAsync(request, cancellationToken), cancellationToken) : ReadCoreAsync(request, cancellationToken);
    }
    private async Task<SourcePage> ReadCoreAsync(SourceRequest request, CancellationToken token)
    {
        var binding = _bindings.SingleOrDefault(t => t.Id == request.Table) ?? throw new DataSpaceException("Source table is not allowed.");
        var catalog = await CatalogAsync(token); var table = catalog.Single(t => t.Id == request.Table);
        await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandTimeout = 20;
        var projection = string.Join(",", table.Columns.Select(c => Quote(c.Name, _dialect)));
        command.CommandText = "SELECT " + projection + " FROM " + TableSql(binding) + " ORDER BY " + string.Join(",", binding.OrderColumns.Select(c => Quote(c, _dialect))) +
            (_dialect == SourceDialect.SqlServer ? " OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY" : " LIMIT @limit OFFSET @offset");
        Add(command, "@offset", request.Offset); Add(command, "@limit", request.Limit + 1);
        using var registration = token.Register(() => { try { command.Cancel(); } catch { /* Best effort; disposal and caller cancellation still apply. */ } });
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token);
        var rows = new List<string?[]>(); long characters = 0; var more = false;
        while (await reader.ReadAsync(token))
        {
            token.ThrowIfCancellationRequested();
            if (rows.Count == request.Limit) { more = true; break; }
            var values = new string?[reader.FieldCount];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = await reader.IsDBNullAsync(i, token) ? null : ToText(reader.GetValue(i));
                if (values[i]?.Length > SourceLimits.MaxCellCharacters) throw new DataSpaceException("External cell exceeds the size limit.");
                characters += values[i]?.Length ?? 0;
                if (characters > SourceLimits.MaxPageBytes / 2) throw new DataSpaceException("External page exceeds the size limit. Request fewer rows.");
            }
            rows.Add(values);
        }
        var page = new SourcePage(table.Columns, rows.ToArray(), more); SourceLimits.Validate(page, request.Limit); return page;
    }
    private static void Add(DbCommand command, string name, int value) { var p = command.CreateParameter(); p.ParameterName = name; p.DbType = DbType.Int32; p.Value = value; command.Parameters.Add(p); }
    public static string ToText(object value) => value switch
    {
        byte[] data => "hex:" + Convert.ToHexString(data),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };
    public ValueTask DisposeAsync() { if (!_disposed) { _disposed = true; _close?.Invoke(); } return ValueTask.CompletedTask; }
}
