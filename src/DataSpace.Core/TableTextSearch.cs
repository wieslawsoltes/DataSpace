namespace DataSpace.Core;

public enum TextMatchMode { AnyPart, WholeField, StartOfField }
public sealed record TableSearchOptions(string Text, TextMatchMode Match = TextMatchMode.AnyPart, bool MatchCase = false, bool Backwards = false, bool Formatted = false);
public sealed record TableSearchHit(int Row, int Column, string RecordId, string Field);

/// <summary>Literal, ordinal Find/Replace over a snapshot, without indexing or copying the complete record graph.</summary>
public static class TableTextSearch
{
    private static StringComparison Comparison(TableSearchOptions options) => options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private static void Validate(TableSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Text) || options.Text.Length > 4096 || !Enum.IsDefined(options.Match))
            throw new DataSpaceException("Enter 1–4,096 characters to find and a valid match mode.");
    }
    private static bool Matches(string? value, TableSearchOptions options) => value is not null && (options.Match switch
    {
        TextMatchMode.WholeField => value.Equals(options.Text, Comparison(options)),
        TextMatchMode.StartOfField => value.StartsWith(options.Text, Comparison(options)),
        _ => value.Contains(options.Text, Comparison(options))
    });
    private static int FindField(IReadOnlyList<FieldDefinition> fields, string name)
    {
        for (var index = 0; index < fields.Count; index++) if (Names.Equal(fields[index].Name, name)) return index;
        return -1;
    }
    public static TableSearchHit? FindNext(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> rows, TableSearchOptions options,
        int startRow = -1, int startColumn = -1, string? onlyField = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(rows);
        Validate(options); cancellationToken.ThrowIfCancellationRequested();
        if (fields.Count == 0 || rows.Count == 0) return null;
        var validStart = startRow >= 0 && startRow < rows.Count && startColumn >= 0 && startColumn < fields.Count;
        if (onlyField is not null)
        {
            // A field-scoped search need not visit every other column in a wide table.
            var column = FindField(fields, onlyField);
            if (column < 0) return null;
            var field = fields[column];
            long first = options.Backwards ? rows.Count - 1 : 0;
            if (validStart) first = startRow + (options.Backwards ? (column < startColumn ? 0 : -1) : (column > startColumn ? 0 : 1));
            for (var step = 0; step < rows.Count; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = (int)((first + (options.Backwards ? -(long)step : step) + rows.Count) % rows.Count);
                var record = rows[row]; var raw = record[field.Name];
                var value = raw is null ? null : options.Formatted ? FieldValues.Display(field, raw) : raw;
                cancellationToken.ThrowIfCancellationRequested();
                if (Matches(value, options)) return new(row, column, record.Id, field.Name);
            }
            return null;
        }
        var total = (long)fields.Count * rows.Count;
        var start = validStart ? (long)startRow * fields.Count + startColumn : options.Backwards ? 0 : -1;
        Record? cached = null; var cachedRow = -1;
        // Scan once and wrap once, including the starting cell only after all other cells.
        for (long step = 1; step <= total; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var at = (start + (options.Backwards ? -step : step) + total) % total;
            var row = (int)(at / fields.Count); var column = (int)(at % fields.Count); var field = fields[column];
            if (row != cachedRow) { cached = rows[row]; cachedRow = row; }
            var raw = cached![field.Name];
            var value = raw is null ? null : options.Formatted ? FieldValues.Display(field, raw) : raw;
            cancellationToken.ThrowIfCancellationRequested();
            if (Matches(value, options)) return new(row, column, cached.Id, field.Name);
        }
        return null;
    }
    public static RecordEdit[] Replacements(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> rows, TableSearchOptions options,
        string replacement, string? onlyField = null, int maximumEdits = 100000, long maximumCharacters = 16 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(rows);
        Validate(options); ArgumentNullException.ThrowIfNull(replacement); cancellationToken.ThrowIfCancellationRequested();
        if (options.Formatted) throw new DataSpaceException("Turn off Search as formatted before replacing values.");
        if (maximumEdits < 1 || maximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumEdits));
        if (replacement.Length > 1_000_000) throw new DataSpaceException("Replacement text is too large.");
        var selected = fields.Where(field => field.Type != FieldType.AutoNumber && (onlyField is null || Names.Equal(field.Name, onlyField))).ToArray();
        if (selected.Length == 0) return [];
        var edits = new List<RecordEdit>(); long characters = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var field in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = row[field.Name]; if (!Matches(value, options)) continue;
                var text = Replace(value!, options, replacement, maximumCharacters - characters, cancellationToken);
                // Identical matches are not edits and consume neither budget.
                if (text == value) continue;
                if (edits.Count >= maximumEdits) throw new DataSpaceException("Replace All exceeds its cell limit.");
                characters += text.Length; edits.Add(new(row.Id, field.Name, text));
            }
        }
        cancellationToken.ThrowIfCancellationRequested(); return edits.ToArray();
    }
    private static string Replace(string value, TableSearchOptions options, string replacement, long budget, CancellationToken cancellationToken)
    {
        long length;
        if (options.Match != TextMatchMode.AnyPart)
        {
            var replacedLength = options.Match == TextMatchMode.WholeField ? value.Length : options.Text.Length;
            if (value.AsSpan(0, replacedLength).SequenceEqual(replacement.AsSpan())) return value;
            length = (long)value.Length - replacedLength + replacement.Length;
        }
        else
        {
            long count = 0; var position = 0; var changed = false;
            while ((position = value.IndexOf(options.Text, position, Comparison(options))) >= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!value.AsSpan(position, options.Text.Length).SequenceEqual(replacement.AsSpan())) changed = true;
                count++; position += options.Text.Length;
            }
            if (!changed) return value;
            length = value.Length + count * (replacement.Length - options.Text.Length);
        }
        if (length > 1_000_000 || length > budget) throw new DataSpaceException("Replace All exceeds its retained-value limit.");
        cancellationToken.ThrowIfCancellationRequested();
        return options.Match switch
        {
            TextMatchMode.WholeField => replacement,
            TextMatchMode.StartOfField => replacement + value[options.Text.Length..],
            _ => value.Replace(options.Text, replacement, Comparison(options))
        };
    }
}
