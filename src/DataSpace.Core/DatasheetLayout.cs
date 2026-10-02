namespace DataSpace.Core;

/// <summary>Saved presentation only. Column order/visibility never changes the table schema or record values.</summary>
public sealed class DatasheetLayout
{
    public List<string> ColumnOrder { get; set; } = [];
    public List<string> HiddenFields { get; set; } = [];
    public List<string> FrozenFields { get; set; } = [];
    public double RowHeight { get; set; } = 27;
    public double FontSize { get; set; } = 13;
    public bool Bold { get; set; }
    public bool AlternateRows { get; set; } = true;
    public bool HorizontalGridLines { get; set; } = true;
    public bool VerticalGridLines { get; set; } = true;
    public DatasheetLayout Copy() => new() { ColumnOrder = new(ColumnOrder), HiddenFields = new(HiddenFields), FrozenFields = new(FrozenFields),
        RowHeight = RowHeight, FontSize = FontSize, Bold = Bold, AlternateRows = AlternateRows,
        HorizontalGridLines = HorizontalGridLines, VerticalGridLines = VerticalGridLines };

    public void Validate()
    {
        if (!double.IsFinite(RowHeight) || RowHeight is < 20 or > 200 || !double.IsFinite(FontSize) || FontSize is < 8 or > 36)
            throw new DataSpaceException("Row height must be 20–200 and font size 8–36.");
        foreach (var list in new[] { ColumnOrder, HiddenFields, FrozenFields })
        {
            if (list is null || list.Count > 256) throw new DataSpaceException("Invalid datasheet column layout.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in list) { Names.Validate(name); if (!seen.Add(name)) throw new DataSpaceException("A datasheet field occurs more than once."); }
        }
    }
    /// <summary>Unknown fields are ignored after schema edits; new fields appear at the end. Never hide every column.</summary>
    public FieldDefinition[] VisibleFields(IReadOnlyList<FieldDefinition> fields)
    {
        Validate();
        var byName = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var names = FrozenFields.Concat(ColumnOrder).Concat(fields.Select(f => f.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        var visible = names.Where(name => byName.ContainsKey(name) && !HiddenFields.Contains(name, StringComparer.OrdinalIgnoreCase)).Select(name => byName[name]).ToArray();
        return visible.Length == 0 && fields.Count > 0 ? [fields[0]] : visible;
    }
    public int FrozenCount(IReadOnlyList<FieldDefinition> visible) => visible.TakeWhile(f => FrozenFields.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).Count();
}

public sealed partial class DatabaseWorkspace
{
    /// <summary>Metadata-only transaction. Shares all existing records; performs no row normalization or constraint scans.</summary>
    public void ConfigureDatasheet(string tableName, DatasheetLayout layout, string? widthField = null, double? width = null, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(layout); CheckRevision(expectedRevision);
        layout.Validate();
        if (widthField is not null && (width is null || !double.IsFinite(width.Value) || width is < 40 or > 2000))
            throw new DataSpaceException("Column width must be 40–2,000.");
        _editing = true;
        try
        {
            var draft = DocumentSnapshot.Copy(Document, true); var table = draft.Table(tableName);
            table.Datasheet = layout.Copy();
            if (widthField is not null) table.Field(widthField).Width = width!.Value;
            Publish(draft, "Datasheet layout");
        }
        finally { _editing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
