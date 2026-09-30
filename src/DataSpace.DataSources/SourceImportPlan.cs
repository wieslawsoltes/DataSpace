using DataSpace.Core;

namespace DataSpace.DataSources;

/// <summary>One source-to-destination field mapping. Source names are case-sensitive; local names are not.</summary>
public sealed record SourceImportField(string SourceName, string Name, FieldType Type,
    bool Include = true, bool Required = false, bool Unique = false, bool PrimaryKey = false, int MaxLength = 255);

/// <summary>Session-local import specification. No endpoint, token, or connection string is stored here.</summary>
public sealed class SourceImportPlan
{
    public List<SourceImportField> Fields { get; set; } = [];
    public string? GeneratedKeyName { get; set; }

    public static SourceImportPlan CreateDefault(SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var fields = SourceImport.Fields(table.Columns);
        return new() { Fields = fields.Select((field, i) => new SourceImportField(table.Columns[i].Name, field.Name, field.Type)).ToList() };
    }

    /// <summary>Validate names, field selection and key policy before any records are read.</summary>
    public void Validate(SourceTable table, string tableName) => Prepare(table, tableName);

    internal PreparedImport Prepare(SourceTable table, string tableName)
    {
        ArgumentNullException.ThrowIfNull(table);
        Names.Validate(tableName);
        new SourceRequest(table.Id).Validate();
        SourceLimits.ValidateColumns(table.Columns);
        var schema = table.Columns.ToArray();
        if (Fields is null || Fields.Count is < 1 or > SourceLimits.MaxColumns)
            throw new DataSpaceException("Choose 1–128 source field mappings.");
        var mappings = Fields.ToArray(); // Freeze the UI draft before asynchronous source reads.
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new TableDefinition { Name = tableName };
        var ordinals = new List<int>();
        var keyName = GeneratedKeyName;
        if (keyName is not null)
        {
            Names.Validate(keyName);
            result.Fields.Add(new() { Name = keyName, Type = FieldType.AutoNumber, PrimaryKey = true, Required = true, Unique = true, Width = 80 });
            ordinals.Add(-1);
        }
        foreach (var mapping in mappings)
        {
            if (mapping is null || !sourceNames.Add(mapping.SourceName))
                throw new DataSpaceException("Each source field can be mapped only once.");
            var ordinal = Array.FindIndex(schema, column => column.Name == mapping.SourceName);
            if (ordinal < 0) throw new DataSpaceException("An import field no longer exists in the source.");
            if (!mapping.Include) continue;
            if (!Enum.IsDefined(mapping.Type) || mapping.Type == FieldType.AutoNumber)
                throw new DataSpaceException("Choose a stored data type for source fields; generate an AutoNumber key separately.");
            Names.Validate(mapping.Name);
            result.Fields.Add(new()
            {
                Name = mapping.Name, Caption = schema[ordinal].Name,
                Description = "Source: " + schema[ordinal].Name + " (" + schema[ordinal].NativeType + ")",
                Type = mapping.Type, Required = mapping.Required || mapping.PrimaryKey,
                Unique = mapping.Unique || mapping.PrimaryKey, PrimaryKey = mapping.PrimaryKey,
                AllowZeroLength = !mapping.PrimaryKey, MaxLength = mapping.MaxLength, Width = 160
            });
            ordinals.Add(ordinal);
        }
        if (ordinals.All(ordinal => ordinal < 0)) throw new DataSpaceException("Include at least one source field.");
        if (result.Fields.Count > SourceLimits.MaxColumns) throw new DataSpaceException("An import supports at most 128 destination fields, including its generated key.");
        SchemaValidator.ValidateTable(result);
        return new(table.Id, schema, result, ordinals.ToArray());
    }
}

internal sealed record PreparedImport(string SourceId, SourceColumn[] Schema, TableDefinition Table, int[] Ordinals);
