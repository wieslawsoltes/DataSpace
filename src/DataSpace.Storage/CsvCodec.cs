using System.Globalization;
using System.Text;
using DataSpace.Core;

namespace DataSpace.Storage;

public sealed class CsvOptions
{
    public char Delimiter { get; init; } = ',';
    public bool HasHeaders { get; init; } = true;
    public bool InferTypes { get; init; } = true;
    public bool EmptyValuesAreNull { get; init; } = true;
    public bool ProtectSpreadsheetFormulas { get; init; } = true;
    public int MaximumRecords { get; init; } = 100000;
}

public static class CsvCodec
{
    public static List<List<string>> Parse(string text, CsvOptions? options = null)
    {
        options ??= new();
        if (options.Delimiter is '"' or '\r' or '\n' || options.MaximumRecords < 1) throw new ArgumentException("Invalid CSV options.");
        if (text.Length > 32 * 1024 * 1024) throw new DataSpaceException("CSV import is limited to 32 MiB.");
        text = text.TrimStart('\uFEFF');
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var afterQuote = false; var started = false;
        void Cell() { row.Add(field.ToString()); field.Clear(); afterQuote = false; started = false; if (row.Count > 256) throw new DataSpaceException("CSV exceeds 256 fields."); }
        void Row() { Cell(); rows.Add(row); row = []; if (rows.Count > options.MaximumRecords + 1) throw new DataSpaceException("CSV record limit exceeded."); }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else { quoted = false; afterQuote = true; }
            }
            else if (c == options.Delimiter) Cell();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Row(); }
            else if (c == '"' && !started && field.Length == 0 && !afterQuote) { quoted = true; started = true; }
            else
            {
                if (afterQuote || c == '"') throw new DataSpaceException($"Invalid CSV quoting at character {i + 1}.");
                field.Append(c); started = true;
            }
        }
        if (quoted) throw new DataSpaceException("CSV contains an unterminated quoted field.");
        if (field.Length != 0 || row.Count != 0 || started || afterQuote) Row();
        return rows;
    }

    public static TableDefinition Import(string name, string text, CsvOptions? options = null)
    {
        options ??= new(); Names.Validate(name);
        var rows = Parse(text, options);
        if (rows.Count == 0) throw new DataSpaceException("The CSV file is empty.");
        var count = rows[0].Count;
        var headers = options.HasHeaders ? rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Field{i + 1}" : h.Trim()).ToArray()
            : Enumerable.Range(1, count).Select(i => $"Field{i}").ToArray();
        var records = rows.Skip(options.HasHeaders ? 1 : 0).ToList();
        if (records.Any(r => r.Count != count)) throw new DataSpaceException("CSV rows have different numbers of fields.");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length) throw new DataSpaceException("CSV headers contain duplicate field names.");
        var table = new TableDefinition { Name = name };
        for (var i = 0; i < count; i++)
        {
            Names.Validate(headers[i]); var values = records.Select(r => r[i]).Where(v => v.Length != 0).ToArray();
            var type = options.InferTypes ? Infer(values) : FieldType.LongText;
            table.Fields.Add(new() { Name = headers[i], Type = type, MaxLength = Math.Max(255, Math.Min(65535, values.Length == 0 ? 255 : values.Max(v => v.Length))), Width = i == 0 ? 160 : 180 });
        }
        foreach (var row in records) RecordOperations.Insert(table, headers.Select((h, i) => KeyValuePair.Create(h, options.EmptyValuesAreNull && row[i].Length == 0 ? null : row[i])).ToDictionary(p => p.Key, p => p.Value));
        SchemaValidator.ValidateTable(table); return table;
    }
    private static FieldType Infer(string[] values)
    {
        if (values.Length == 0) return FieldType.ShortText;
        if (values.All(v => new[] { "true", "false", "yes", "no" }.Contains(v, StringComparer.OrdinalIgnoreCase))) return FieldType.YesNo;
        // Leading zeroes often encode postal codes or product identifiers; preserve them as text.
        if (values.Any(v => v.Length > 1 && v[0] == '0' && char.IsDigit(v[1]))) return FieldType.ShortText;
        if (values.All(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return FieldType.Integer;
        if (values.All(v => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _))) return FieldType.Decimal;
        if (values.All(v => v.Length >= 10 && v[4] == '-' && v[7] == '-' && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))) return FieldType.DateTime;
        return values.Any(v => v.Length > 65535 || v.Contains('\n')) ? FieldType.LongText : FieldType.ShortText;
    }
    public static string Export(IReadOnlyList<FieldDefinition> fields, IEnumerable<Record> records, CsvOptions? options = null)
    {
        options ??= new(); var delimiter = options.Delimiter;
        static string Protect(string value) => value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' ? "'" + value : value;
        string Escape(string value) => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        var builder = new StringBuilder();
        if (options.HasHeaders) builder.AppendLine(string.Join(delimiter, fields.Select(f => Escape(options.ProtectSpreadsheetFormulas ? Protect(f.Name) : f.Name))));
        foreach (var record in records)
            builder.AppendLine(string.Join(delimiter, fields.Select(f =>
            {
                var value = record[f.Name] ?? "";
                if (options.ProtectSpreadsheetFormulas && f.Type is FieldType.ShortText or FieldType.LongText) value = Protect(value);
                return Escape(value);
            })));
        return builder.ToString();
    }
}
