namespace DataSpace.Core;

// The input is a previously validated workspace snapshot with only canonical cell values changed.
// This deliberately does not normalize or replace dictionaries of shared, untouched records.
internal static class RecordEditValidation
{
    public static void Validate(DatabaseDocument document, TableDefinition table, IReadOnlySet<string> changedFields)
    {
        foreach (var field in table.Fields.Where(f => (f.PrimaryKey || f.Unique) && changedFields.Contains(f.Name))) Unique(table, [field.Name]);
        foreach (var index in table.Indexes.Where(i => i.Unique && i.Fields.Any(changedFields.Contains))) Unique(table, index.Fields);
        foreach (var relation in document.Relationships.Where(r => r.EnforceIntegrity &&
            (Names.Equal(r.ParentTable, table.Name) && changedFields.Contains(r.ParentField) || Names.Equal(r.ChildTable, table.Name) && changedFields.Contains(r.ChildField))))
        {
            var parent = document.Table(relation.ParentTable); var child = document.Table(relation.ChildTable);
            var keys = parent.Records.Select(r => r[relation.ParentField]).Where(v => v is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in child.Records)
                if (row[relation.ChildField] is { } value && !keys.Contains(value))
                    throw new DataSpaceException($"Relationship '{relation.Name}' has no parent for '{value}'.");
        }
    }
    private static void Unique(TableDefinition table, IReadOnlyList<string> fields)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Records)
        {
            string? key;
            if (fields.Count == 1) key = row[fields[0]];
            else
            {
                var values = fields.Select(f => row[f]).ToArray();
                key = values.Any(v => v is null) ? null : FieldValues.Key(values);
            }
            if (key is not null && !seen.Add(key)) throw new DataSpaceException($"Duplicate value in unique index '{table.Name}({string.Join(", ", fields)})'.");
        }
    }
}
