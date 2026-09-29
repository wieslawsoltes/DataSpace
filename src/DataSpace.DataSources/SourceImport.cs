using System.Text;
using System.Text.Json;
using DataSpace.Core;

namespace DataSpace.DataSources;

public static class SourceImport
{
    /// <summary>Build a detached table; caller commits once, after all pages and validation succeed.</summary>
    public static async Task<TableDefinition> ReadTableAsync(IDataSource source, SourceTable table, string name,
        int maximumRows = 10000, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        Names.Validate(name);
        if (maximumRows is < 1 or > SourceLimits.MaxImportRows) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var result = new TableDefinition { Name = name, Description = "Imported copy from " + source.DisplayName + "; not linked or write-through." };
        result.Fields = Fields(table.Columns).ToList();
        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var limit = Math.Min(500, maximumRows - offset + 1);
            var page = await source.ReadAsync(new(table.Id, offset, limit), cancellationToken);
            SourceLimits.Validate(page, limit);
            if (!page.Columns.SequenceEqual(table.Columns)) throw new DataSpaceException("Source schema changed during import. No local records were changed.");
            if (offset + page.Rows.Length > maximumRows) throw new DataSpaceException($"Import exceeds {maximumRows:N0} rows. No partial table was imported.");
            foreach (var cells in page.Rows)
            {
                var record = new Record();
                for (var column = 0; column < cells.Length; column++) record[result.Fields[column].Name] = FieldValues.Normalize(result.Fields[column], cells[column]);
                result.Records.Add(record);
            }
            offset += page.Rows.Length; progress?.Report(offset);
            if (!page.HasMore) break;
            if (page.Rows.Length == 0) throw new DataSpaceException("Source returned an empty non-final page.");
        }
        SchemaValidator.Validate(new DatabaseDocument { Tables = [result] }); return result;
    }
    public static FieldDefinition[] Fields(IReadOnlyList<SourceColumn> columns)
    {
        if (columns.Count is < 1 or > SourceLimits.MaxColumns) throw new DataSpaceException("Source requires 1–128 columns.");
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
    public static byte[] ExportJson(IReadOnlyList<FieldDefinition> fields, IEnumerable<Record> records)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var row in records)
            {
                writer.WriteStartObject();
                foreach (var field in fields)
                {
                    writer.WritePropertyName(field.Name); var value = row[field.Name];
                    if (value is null) writer.WriteNullValue();
                    else if (field.Type is FieldType.Integer or FieldType.AutoNumber) writer.WriteNumberValue(long.Parse(value, FieldValues.Culture));
                    else if (field.Type is FieldType.Decimal or FieldType.Currency) writer.WriteNumberValue(decimal.Parse(value, FieldValues.Culture));
                    else if (field.Type == FieldType.YesNo) writer.WriteBooleanValue(FieldValues.Parse(field, value) is true);
                    else writer.WriteStringValue(value);
                }
                writer.WriteEndObject();
                if (stream.Length > SourceLimits.MaxFileBytes) throw new DataSpaceException("JSON export exceeds 16 MiB.");
            }
            writer.WriteEndArray();
        }
        if (stream.Length > SourceLimits.MaxFileBytes) throw new DataSpaceException("JSON export exceeds 16 MiB.");
        return stream.ToArray();
    }
}
