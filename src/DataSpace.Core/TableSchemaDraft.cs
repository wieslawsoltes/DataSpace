namespace DataSpace.Core;

public sealed record FieldDraft(string? OriginalName, FieldDefinition Field);

/// <summary>Detached table schema edits. Applying a draft preserves record identities and converts values atomically.</summary>
public sealed class TableSchemaDraft
{
    public string TableName { get; }
    public long Revision { get; }
    public string Description { get; set; }
    public List<FieldDraft> Fields { get; } = [];
    public TableSchemaDraft(DatabaseDocument document, string tableName)
    {
        var table = document.Table(tableName);
        TableName = table.Name; Revision = document.Revision; Description = table.Description;
        Fields.AddRange(table.Fields.Select(f => new FieldDraft(f.Name, Copy(f))));
    }
    public void Apply(DatabaseWorkspace workspace) => workspace.Edit("Edit table design", ApplyTo, Revision);
    public void ApplyTo(DatabaseDocument document)
    {
        var table = document.Table(TableName);
        if (Fields.Count is < 1 or > 256) throw new DataSpaceException("A table requires 1–256 fields.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Fields)
        {
            Names.Validate(entry.Field.Name);
            if (!names.Add(entry.Field.Name)) throw new DataSpaceException("Field names must be unique.");
            if (entry.OriginalName is { } original)
            {
                table.Field(original);
                if (!originals.Add(original)) throw new DataSpaceException("A source field cannot appear twice.");
            }
        }
        var removed = table.Fields.Where(f => !originals.Contains(f.Name)).Select(f => f.Name).ToArray();
        var renamed = Fields.Where(f => f.OriginalName is not null && !Names.Equal(f.OriginalName, f.Field.Name)).ToArray();
        // Do not guess at SQL dependencies by replacing substrings inside saved SQL.
        if ((removed.Length > 0 || renamed.Length > 0) && document.Queries.Count > 0)
            throw new DataSpaceException("Renaming or deleting fields requires reviewing saved SQL dependencies. Remove the dependent saved queries first; adding fields and editing properties are supported without removing them.");
        foreach (var field in removed)
        {
            if (table.Indexes.Any(i => i.Fields.Any(f => Names.Equal(f, field))) ||
                document.Relationships.Any(r => Names.Equal(r.ParentTable, TableName) && Names.Equal(r.ParentField, field) || Names.Equal(r.ChildTable, TableName) && Names.Equal(r.ChildField, field)) ||
                document.Forms.Any(f => Names.Equal(f.Source, TableName) && f.Controls.Any(c => Names.Equal(c.Field, field))) ||
                document.Reports.Any(r => Names.Equal(r.Source, TableName) && r.Fields.Any(f => Names.Equal(f, field))))
                throw new DataSpaceException($"Field '{field}' is used by an index, relationship, form or report. Remove that dependency first.");
            table.Fields.RemoveAll(f => Names.Equal(f.Name, field));
            foreach (var row in table.Records) row.Values.Remove(field);
        }
        // Temporary identifiers also make swaps and longer rename cycles deterministic.
        var temporary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in renamed)
        {
            var temp = Names.Available("DataSpaceRename", table.Fields.Select(f => f.Name).Concat(names));
            RecordOperations.RenameField(document, TableName, entry.OriginalName!, temp);
            temporary.Add(entry.OriginalName!, temp);
        }
        foreach (var entry in renamed) RecordOperations.RenameField(document, TableName, temporary[entry.OriginalName!], entry.Field.Name);
        foreach (var entry in Fields)
        {
            if (entry.OriginalName is null) RecordOperations.AddField(table, Copy(entry.Field));
            else
            {
                var current = table.Field(entry.Field.Name);
                table.Fields[table.Fields.IndexOf(current)] = Copy(entry.Field);
            }
        }
        table.Fields = Fields.Select(entry => table.Field(entry.Field.Name)).ToList();
        table.Description = Description;
    }
    public static FieldDefinition Copy(FieldDefinition field) => new()
    {
        Name = field.Name, Type = field.Type, Caption = field.Caption, Description = field.Description,
        Required = field.Required, PrimaryKey = field.PrimaryKey, Unique = field.Unique,
        AllowZeroLength = field.AllowZeroLength, MaxLength = field.MaxLength, DefaultValue = field.DefaultValue,
        Format = field.Format, Width = field.Width
    };
}
