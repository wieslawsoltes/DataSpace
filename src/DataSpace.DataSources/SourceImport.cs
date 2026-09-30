using System.Text;
using System.Text.Json;
using DataSpace.Core;

namespace DataSpace.DataSources;

public static class SourceImport
{
    /// <summary>Import every field using conservative default types.</summary>
    public static Task<TableDefinition> ReadTableAsync(IDataSource source, SourceTable table, string name,
        int maximumRows = 10000, IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
        ReadTableAsync(source, table, name, SourceImportPlan.CreateDefault(table), maximumRows, progress, cancellationToken);

    /// <summary>Freeze and validate the field plan once, then build a detached table. No source writes occur.</summary>
    public static async Task<TableDefinition> ReadTableAsync(IDataSource source, SourceTable table, string name,
        SourceImportPlan plan, int maximumRows = 10000, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumRows is < 1 or > SourceLimits.MaxImportRows) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var prepared = plan.Prepare(table, name);
        var result = prepared.Table;
        result.Description = "Imported copy from " + source.DisplayName + "; not linked or write-through.";
        var offset = 0; long characters = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var limit = Math.Min(500, maximumRows - offset + 1);
            var page = await source.ReadAsync(new(prepared.SourceId, offset, limit), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); // Also guard adapters that ignore cancellation.
            SourceLimits.Validate(page, limit);
            if (!page.Columns.SequenceEqual(prepared.Schema)) throw new DataSpaceException("Source schema changed during import. No local records were changed.");
            if (offset + page.Rows.Length > maximumRows) throw new DataSpaceException($"Import exceeds {maximumRows:N0} rows. No partial table was imported.");
            foreach (var cells in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = new Record();
                for (var column = 0; column < prepared.Ordinals.Length; column++)
                {
                    var ordinal = prepared.Ordinals[column];
                    var field = result.Fields[column];
                    var value = ordinal < 0 ? (result.Records.Count + 1L).ToString(FieldValues.Culture) : cells[ordinal];
                    try { record[field.Name] = FieldValues.Normalize(field, value); }
                    catch (DataSpaceException error) { throw new DataSpaceException($"Import row {result.Records.Count + 1}, field '{field.Name}': {error.Message}"); }
                    characters += record[field.Name]?.Length ?? 0;
                    if (characters > SourceLimits.MaxImportCharacters) throw new DataSpaceException("Import exceeds the 16 Mi-character retained-value limit. Select fewer fields or a smaller source.");
                }
                result.Records.Add(record);
            }
            offset += page.Rows.Length; progress?.Report(offset);
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.HasMore) break;
            if (page.Rows.Length == 0) throw new DataSpaceException("Source returned an empty non-final page.");
        }
        SchemaValidator.Validate(new DatabaseDocument { Tables = [result] });
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    public static FieldDefinition[] Fields(IReadOnlyList<SourceColumn> columns)
    {
        SourceLimits.ValidateColumns(columns);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<FieldDefinition>();
        foreach (var column in columns)
        {
            var cleaned = new string(column.Name.Select(c => char.IsControl(c) || "[].!`".Contains(c) ? '_' : c).ToArray()).Trim();
            if (cleaned.Length == 0) cleaned = "Field"; cleaned = cleaned[..Math.Min(cleaned.Length, 56)];
            var name = cleaned; for (var index = 2; !names.Add(name); index++) name = cleaned + "_" + index;
            result.Add(new FieldDefinition { Name = name, Caption = column.Name, Description = "Source: " + column.Name + " (" + column.NativeType + ")", Width = 160,
                Type = column.Kind switch { "integer" => FieldType.Integer, "boolean" => FieldType.YesNo, _ => FieldType.LongText } });
        }
        return result.ToArray();
    }
    public static Record[] Records(SourcePage page)
    {
        var fields = Fields(page.Columns);
        return page.Rows.Select(cells => { var row = new Record(); for (var i = 0; i < cells.Length; i++) row[fields[i].Name] = cells[i]; return row; }).ToArray();
    }
    public static byte[] ExportJson(IReadOnlyList<FieldDefinition> fields, IEnumerable<Record> records) =>
        ExportJson(fields, records, CancellationToken.None);

    public static byte[] ExportJson(IReadOnlyList<FieldDefinition> fields, IEnumerable<Record> records,
        CancellationToken cancellationToken, int maximumBytes = SourceLimits.MaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(records);
        if (maximumBytes is < 1 or > SourceLimits.MaxFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var row in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                foreach (var field in fields)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WritePropertyName(field.Name); var value = row[field.Name];
                    if (value?.Length > SourceLimits.MaxCellCharacters) throw new DataSpaceException("A JSON export cell exceeds the size limit.");
                    if (value is null) writer.WriteNullValue();
                    else if (field.Type is FieldType.Integer or FieldType.AutoNumber) writer.WriteNumberValue(long.Parse(value, FieldValues.Culture));
                    else if (field.Type is FieldType.Decimal or FieldType.Currency) writer.WriteNumberValue(decimal.Parse(value, FieldValues.Culture));
                    else if (field.Type == FieldType.YesNo) writer.WriteBooleanValue(FieldValues.Parse(field, value) is true);
                    else writer.WriteStringValue(value);
                    if (writer.BytesCommitted + writer.BytesPending > maximumBytes) throw new DataSpaceException("JSON export exceeds the byte limit.");
                }
                writer.WriteEndObject();
                if (writer.BytesCommitted + writer.BytesPending > maximumBytes) throw new DataSpaceException("JSON export exceeds the byte limit.");
                // Keep writer buffering bounded and count pending UTF-8 bytes, not just flushed stream bytes.
                if (writer.BytesPending >= 16384) writer.Flush();
            }
            writer.WriteEndArray();
            if (writer.BytesCommitted + writer.BytesPending > maximumBytes) throw new DataSpaceException("JSON export exceeds the byte limit.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }
}
