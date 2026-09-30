using DataSpace.Core;
using System.Text.Json;

namespace DataSpace.DataSources;

public sealed record SourceColumn(string Name, string Kind = "text", string NativeType = "");
public sealed record SourceTable(string Id, string Name, SourceColumn[] Columns, string[] OrderColumns);
public sealed record SourceRequest(string Table, int Offset = 0, int Limit = 200)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Table) || Table.Length > 512) throw new DataSpaceException("Select a valid source table.");
        if (Offset < 0 || Offset > SourceLimits.MaxOffset || Limit is < 1 or > SourceLimits.MaxPageRows)
            throw new DataSpaceException("Invalid page range (1–1,000 rows; offset up to 1,000,000).");
    }
}
public sealed record SourcePage(SourceColumn[] Columns, string?[][] Rows, bool HasMore);

/// <summary>A read-only external connection. Imports are separate, atomic local operations, never write-through.</summary>
public interface IDataSource : IAsyncDisposable
{
    string DisplayName { get; }
    Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default);
}

public static class SourceLimits
{
    public const int MaxPageRows = 1000, MaxOffset = 1_000_000, MaxColumns = 128;
    public const int MaxFileBytes = 16 * 1024 * 1024, MaxPageBytes = 4 * 1024 * 1024;
    public const int MaxCellCharacters = 1_000_000, MaxImportRows = 100_000;
    public const int MaxImportCharacters = 16 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = SourceJsonContext.Default };
    public static void ValidateColumns(IReadOnlyList<SourceColumn> columns)
    {
        if (columns is null || columns.Count is < 1 or > MaxColumns)
            throw new DataSpaceException("Source requires 1–128 columns.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (column is null || string.IsNullOrEmpty(column.Name) || column.Name.Length > 512 || column.Kind is null || column.NativeType is null)
                throw new DataSpaceException("The data source returned invalid column metadata.");
            if (!names.Add(column.Name)) throw new DataSpaceException("The data source returned duplicate column names.");
        }
    }
    public static void Validate(SourcePage page, int requestedRows)
    {
        if (page is null || page.Rows is null || requestedRows is < 1 or > MaxPageRows || page.Rows.Length > requestedRows)
            throw new DataSpaceException("The data source returned an invalid page shape.");
        ValidateColumns(page.Columns);
        long characters = 0;
        foreach (var row in page.Rows)
        {
            if (row is null || row.Length != page.Columns.Length) throw new DataSpaceException("The data source returned an invalid row shape.");
            foreach (var cell in row)
            {
                if (cell?.Length > MaxCellCharacters) throw new DataSpaceException("A source cell exceeds the one-million-character limit.");
                characters += cell?.Length ?? 0;
                if (characters > MaxPageBytes / 2) throw new DataSpaceException("The source page is too large. Request fewer rows.");
            }
        }
    }
}
