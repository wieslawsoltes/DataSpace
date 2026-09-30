namespace DataSpace.Core;

public sealed partial class DatabaseWorkspace
{
    /// <summary>
    /// Append positional rows in one undoable transaction. Omitted fields use defaults,
    /// AutoNumber/GUID generation, or null. Existing record dictionaries are not copied.
    /// Input enumeration, conversion, cancellation and constraint failures publish nothing.
    /// </summary>
    public int AppendRecords(string label, string tableName, IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows, long? expectedRevision = null,
        CancellationToken cancellationToken = default, long maximumCharacters = 16 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(columns); ArgumentNullException.ThrowIfNull(rows);
        CheckRevision(expectedRevision); cancellationToken.ThrowIfCancellationRequested();
        if (maximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var added = 0; long characters = 0; _editing = true;
        try
        {
            // Protect mapping callbacks as well as row callbacks from nested edits.
            // Read at most the validated number of columns, without an unbounded enumerator.
            var count = columns.Count;
            if (count is < 1 or > 256) throw new DataSpaceException("Map 1–256 destination fields.");
            var source = Document.Table(tableName);
            var names = new string[count];
            for (var index = 0; index < count; index++)
            { cancellationToken.ThrowIfCancellationRequested(); names[index] = columns[index]; }
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
                if (!mapped.Add(source.Field(name).Name)) throw new DataSpaceException("A destination field can be mapped only once.");
            var ordinals = source.Fields.Select(field => Array.FindIndex(names, name => Names.Equal(name, field.Name))).ToArray();
            var draft = DocumentSnapshot.Copy(Document, true); var table = draft.Table(tableName);
            // Existing snapshots are already validated. Only new rows are normalized;
            // read-only constraint checks below never mutate the shared old records.
            foreach (var cells in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cells is null || cells.Count != names.Length) throw new DataSpaceException($"Append row {added + 1} has the wrong number of values.");
                if (table.Records.Count >= 1_000_000) throw new DataSpaceException("A table supports at most 1,000,000 records.");
                var record = new Record();
                for (var index = 0; index < table.Fields.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var field = table.Fields[index];
                    var value = ordinals[index] < 0 ? field.DefaultValue : cells[ordinals[index]];
                    if (field.Type == FieldType.AutoNumber && value is null)
                    {
                        if (table.NextAutoNumber == long.MaxValue) throw new DataSpaceException("AutoNumber capacity exceeded.");
                        value = (table.NextAutoNumber++).ToString(FieldValues.Culture);
                    }
                    else if (field.Type == FieldType.Guid && value is null) value = Guid.NewGuid().ToString();
                    if (value?.Length > 1_000_000) throw new DataSpaceException("An append cell exceeds one million characters.");
                    try { value = FieldValues.Normalize(field, value); }
                    catch (DataSpaceException error) { throw new DataSpaceException($"Append row {added + 1}, field '{field.Name}': {error.Message}"); }
                    if (field.Type == FieldType.AutoNumber && value is not null)
                    {
                        var number = long.Parse(value, FieldValues.Culture);
                        if (number == long.MaxValue) throw new DataSpaceException("AutoNumber capacity exceeded.");
                        table.NextAutoNumber = Math.Max(table.NextAutoNumber, number + 1);
                    }
                    if (characters > maximumCharacters - (value?.Length ?? 0)) throw new DataSpaceException("Append exceeds its retained-value character limit.");
                    characters += value?.Length ?? 0; record[field.Name] = value;
                }
                table.Records.Add(record); added++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (added > 0)
            {
                RecordEditValidation.Validate(draft, table, table.Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
                cancellationToken.ThrowIfCancellationRequested(); Publish(draft, label);
            }
        }
        finally { _editing = false; }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }
}
