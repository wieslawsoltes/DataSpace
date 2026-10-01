namespace DataSpace.Core;

public static class SchemaValidator
{
    public static void Validate(DatabaseDocument document)
    {
        if (document.FormatVersion != 1) throw new DataSpaceException($"Unsupported database format {document.FormatVersion}.");
        Names.Validate(document.Name);
        if (document.Tables is null || document.Relationships is null || document.Queries is null || document.Forms is null || document.Reports is null || document.Macros is null)
            throw new DataSpaceException("Database collections must not be null.");
        if (document.Tables.Count > 256) throw new DataSpaceException("A database can contain at most 256 tables.");
        UniqueNames(document.Tables.Select(t => t.Name), "table");
        UniqueNames(document.Queries.Select(q => q.Name), "query");
        UniqueNames(document.Forms.Select(f => f.Name), "form");
        UniqueNames(document.Reports.Select(r => r.Name), "report");
        UniqueNames(document.Macros.Select(m => m.Name), "macro");
        UniqueNames(document.Relationships.Select(r => r.Name), "relationship");
        foreach (var table in document.Tables) ValidateTable(table);
        foreach (var relation in document.Relationships)
        {
            var parent = document.Table(relation.ParentTable);
            var child = document.Table(relation.ChildTable);
            var parentField = parent.Field(relation.ParentField);
            var childField = child.Field(relation.ChildField);
            if (!relation.EnforceIntegrity) continue;
            if (!parentField.PrimaryKey && !parentField.Unique && !parent.Indexes.Any(i => i.Unique && i.Fields.Count == 1 && Names.Equal(i.Fields[0], parentField.Name)))
                throw new DataSpaceException($"The parent field '{parent.Name}.{parentField.Name}' must have a unique index.");
            static FieldType BaseType(FieldType type) => type == FieldType.AutoNumber ? FieldType.Integer : type;
            if (BaseType(parentField.Type) != BaseType(childField.Type)) throw new DataSpaceException("Related fields must have compatible types.");
            var keys = parent.Records.Select(r => r[parentField.Name]).Where(v => v is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in child.Records)
                if (row[childField.Name] is { } key && !keys.Contains(key))
                    throw new DataSpaceException($"Relationship '{relation.Name}': '{child.Name}.{childField.Name}' value '{key}' has no parent record.");
        }
        foreach (var query in document.Queries)
        {
            if (query.Sql is null || query.Sql.Length > 65536) throw new DataSpaceException("Queries are limited to 65,536 characters.");
            if (query.DesignerState?.Length > 524288) throw new DataSpaceException("Query designer state is too large.");
            query.Parameters = new(query.Parameters ?? [], StringComparer.OrdinalIgnoreCase);
        }
        foreach (var form in document.Forms)
        {
            var source = document.Table(form.Source);
            if (form.Controls is null || form.Controls.Count > 512 || !Finite(form.Width, 100, 10000) || !Finite(form.Height, 100, 10000))
                throw new DataSpaceException("Invalid form dimensions or controls.");
            foreach (var control in form.Controls)
            {
                if (!Enum.IsDefined(control.Kind) || !Finite(control.X, 0, 10000) || !Finite(control.Y, 0, 10000) ||
                    !Finite(control.Width, 16, 10000) || !Finite(control.Height, 16, 10000) || !Finite(control.FontSize, 6, 120))
                    throw new DataSpaceException("Invalid form control geometry.");
                if (control.Kind is LayoutControlKind.TextBox or LayoutControlKind.CheckBox) source.Field(control.Field);
            }
        }
        foreach (var report in document.Reports)
        {
            if (!document.Tables.Any(t => Names.Equal(t.Name, report.Source)) && !document.Queries.Any(q => Names.Equal(q.Name, report.Source)))
                throw new DataSpaceException($"Report '{report.Name}' has an unknown record source.");
            if (report.Fields is null) throw new DataSpaceException("Report fields must not be null.");
        }
        foreach (var macro in document.Macros)
            if (macro.Steps is null || macro.Steps.Count > 100 || macro.Steps.Any(s => !Enum.IsDefined(s.Action)))
                throw new DataSpaceException("Invalid macro actions.");
    }

    public static void ValidateTable(TableDefinition table)
    {
        if (table.Datasheet is null) throw new DataSpaceException("Datasheet layout must not be null.");
        table.Datasheet.Validate();
        if (table.Fields is null || table.Records is null || table.Indexes is null || table.Fields.Count is < 1 or > 256 || table.Records.Count > 1000000)
            throw new DataSpaceException("Tables require 1–256 fields and at most 1,000,000 records.");
        UniqueNames(table.Fields.Select(f => f.Name), "field");
        UniqueNames(table.Indexes.Select(i => i.Name), "index");
        if (table.Fields.Count(f => f.PrimaryKey) > 1 || table.Fields.Count(f => f.Type == FieldType.AutoNumber) > 1)
            throw new DataSpaceException("Only one primary-key field and one AutoNumber field are supported per table; use unique indexes for composite keys.");
        if (!Finite(table.DiagramX, 0, 10000) || !Finite(table.DiagramY, 0, 10000)) throw new DataSpaceException("Invalid relationship diagram position.");
        foreach (var field in table.Fields)
        {
            if (!Enum.IsDefined(field.Type) || field.MaxLength is < 1 or > 65535 || !Finite(field.Width, 40, 2000))
                throw new DataSpaceException($"Invalid properties for field '{field.Name}'.");
            if (field.DefaultValue is not null) FieldValues.Normalize(field, field.DefaultValue);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var fields = table.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long largestAutoNumber = 0;
        foreach (var row in table.Records)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || !ids.Add(row.Id)) throw new DataSpaceException("Duplicate or missing record identity.");
            if (row.Values is null) throw new DataSpaceException("Record values must not be null.");
            try { row.Values = new(row.Values, StringComparer.OrdinalIgnoreCase); }
            catch (ArgumentException) { throw new DataSpaceException("Duplicate field names in a record."); }
            if (row.Values.Keys.Any(k => !fields.Contains(k))) throw new DataSpaceException("A record contains an unknown field.");
            foreach (var field in table.Fields)
            {
                row[field.Name] = FieldValues.Normalize(field, row[field.Name]);
                if (field.Type == FieldType.AutoNumber && row[field.Name] is { } value)
                    largestAutoNumber = Math.Max(largestAutoNumber, long.Parse(value, FieldValues.Culture));
            }
        }
        if (largestAutoNumber == long.MaxValue) throw new DataSpaceException("AutoNumber capacity exceeded.");
        table.NextAutoNumber = Math.Max(Math.Max(1, table.NextAutoNumber), largestAutoNumber + 1);
        foreach (var field in table.Fields.Where(f => f.PrimaryKey || f.Unique)) CheckUnique(table, [field.Name]);
        foreach (var index in table.Indexes)
        {
            if (index.Fields is null || index.Fields.Count == 0) throw new DataSpaceException("An index must contain fields.");
            foreach (var field in index.Fields) table.Field(field);
            if (index.Unique) CheckUnique(table, index.Fields);
        }
    }

    private static void CheckUnique(TableDefinition table, IReadOnlyList<string> fields)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Records)
        {
            var values = fields.Select(f => row[f]).ToArray();
            if (values.Any(v => v is null)) continue;
            if (!seen.Add(FieldValues.Key(values))) throw new DataSpaceException($"Duplicate value in unique index '{table.Name}({string.Join(", ", fields)})'.");
        }
    }
    private static void UniqueNames(IEnumerable<string> names, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) { Names.Validate(name); if (!seen.Add(name)) throw new DataSpaceException($"Duplicate {kind} name '{name}'."); }
    }
    private static bool Finite(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
}
