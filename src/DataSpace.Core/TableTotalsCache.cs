namespace DataSpace.Core;

/// <summary>Single-pass numeric totals for a stable record snapshot. Call Invalidate after in-place external mutations.</summary>
public sealed class TableTotalsCache
{
    private IReadOnlyList<FieldDefinition>? _fields;
    private IReadOnlyList<Record>? _records;
    private string[] _values = [];
    public int ComputationCount { get; private set; }
    public void Invalidate() { _fields = null; _records = null; _values = []; }
    public IReadOnlyList<string> GetValues(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> records)
    {
        if (ReferenceEquals(_fields, fields) && ReferenceEquals(_records, records)) return _values;
        var values = new string[fields.Count]; Array.Fill(values, "");
        if (fields.Count != 0) values[0] = records.Count.ToString("N0", FieldValues.Culture);
        var columns = Enumerable.Range(0, fields.Count).Where(i => fields[i].Type is FieldType.Decimal or FieldType.Currency or FieldType.Integer).ToArray();
        var sums = new decimal[columns.Length]; var overflow = new bool[columns.Length];
        if (columns.Length != 0)
        foreach (var record in records)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (overflow[i]) continue;
                var field = fields[columns[i]];
                try { sums[i] += Convert.ToDecimal(FieldValues.Parse(field, record[field.Name]) ?? 0, FieldValues.Culture); }
                catch (OverflowException) { overflow[i] = true; }
            }
        }
        for (var i = 0; i < columns.Length; i++) values[columns[i]] = overflow[i] ? "Overflow" : sums[i].ToString(fields[columns[i]].Type == FieldType.Integer ? "N0" : "N2", FieldValues.Culture);
        _fields = fields; _records = records; _values = values; ComputationCount++; return values;
    }
}
