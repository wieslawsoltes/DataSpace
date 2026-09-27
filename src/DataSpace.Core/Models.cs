namespace DataSpace.Core;

public enum FieldType { ShortText, LongText, Integer, Decimal, Currency, DateTime, YesNo, AutoNumber, Guid }
public enum LayoutControlKind { TextBox, Label, CheckBox, Heading }
public enum MacroActionKind { OpenObject, ApplyFilter, ClearFilter, GoToFirstRecord, GoToLastRecord, SaveDatabase }

public sealed class DatabaseDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Database1";
    public long Revision { get; set; }
    public List<TableDefinition> Tables { get; set; } = [];
    public List<RelationshipDefinition> Relationships { get; set; } = [];
    public List<QueryDefinition> Queries { get; set; } = [];
    public List<FormDefinition> Forms { get; set; } = [];
    public List<ReportDefinition> Reports { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];
    public TableDefinition Table(string name) => Tables.FirstOrDefault(t => Names.Equal(t.Name, name))
        ?? throw new DataSpaceException($"Table '{name}' does not exist.");
}

public sealed class TableDefinition
{
    public string Name { get; set; } = "Table1";
    public string Description { get; set; } = "";
    public List<FieldDefinition> Fields { get; set; } = [];
    public List<Record> Records { get; set; } = [];
    public List<IndexDefinition> Indexes { get; set; } = [];
    public long NextAutoNumber { get; set; } = 1;
    public double DiagramX { get; set; } = 40;
    public double DiagramY { get; set; } = 40;
    public FieldDefinition Field(string name) => Fields.FirstOrDefault(f => Names.Equal(f.Name, name))
        ?? throw new DataSpaceException($"Field '{name}' does not exist in '{Name}'.");
}

public sealed class FieldDefinition
{
    public string Name { get; set; } = "Field1";
    public FieldType Type { get; set; } = FieldType.ShortText;
    public string Caption { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Required { get; set; }
    public bool PrimaryKey { get; set; }
    public bool Unique { get; set; }
    public bool AllowZeroLength { get; set; } = true;
    public int MaxLength { get; set; } = 255;
    public string? DefaultValue { get; set; }
    public string Format { get; set; } = "";
    public double Width { get; set; } = 150;
    public string DisplayName => string.IsNullOrEmpty(Caption) ? Name : Caption;
}

public sealed class Record
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? this[string field]
    {
        get => Values.GetValueOrDefault(field);
        set => Values[field] = value;
    }
}

public sealed class IndexDefinition
{
    public string Name { get; set; } = "Index1";
    public List<string> Fields { get; set; } = [];
    public bool Unique { get; set; }
}

public sealed class RelationshipDefinition
{
    public string Name { get; set; } = "Relationship1";
    public string ParentTable { get; set; } = "";
    public string ParentField { get; set; } = "";
    public string ChildTable { get; set; } = "";
    public string ChildField { get; set; } = "";
    public bool EnforceIntegrity { get; set; } = true;
    public bool CascadeDelete { get; set; }
    public bool CascadeUpdate { get; set; }
}

public sealed class QueryDefinition
{
    public string Name { get; set; } = "Query1";
    public string Sql { get; set; } = "SELECT * FROM [Customers];";
    public Dictionary<string, string?> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LayoutControl
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public LayoutControlKind Kind { get; set; }
    public string Field { get; set; } = "";
    public string Caption { get; set; } = "Label";
    public double X { get; set; } = 30;
    public double Y { get; set; } = 80;
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 32;
    public double FontSize { get; set; } = 14;
}

public sealed class FormDefinition
{
    public string Name { get; set; } = "Form1";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public double Width { get; set; } = 860;
    public double Height { get; set; } = 600;
    public List<LayoutControl> Controls { get; set; } = [];
}

public sealed class ReportDefinition
{
    public string Name { get; set; } = "Report1";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "Report";
    public List<string> Fields { get; set; } = [];
    public bool Landscape { get; set; }
    public bool ShowTotals { get; set; } = true;
}

public sealed class MacroDefinition
{
    public string Name { get; set; } = "Macro1";
    public List<MacroStep> Steps { get; set; } = [];
}
public sealed class MacroStep
{
    public MacroActionKind Action { get; set; }
    public string Argument { get; set; } = "";
}

public sealed class DataSpaceException(string message) : Exception(message);

public static class Names
{
    public static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim() ||
            name.Any(c => char.IsControl(c) || "[].!`".Contains(c)))
            throw new DataSpaceException("Names must contain 1–64 characters, without leading/trailing spaces or [ ] . ! ` characters.");
    }
    public static string Available(string prefix, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++) if (!names.Contains(prefix + i)) return prefix + i;
    }
}
