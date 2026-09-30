namespace DataSpace.Controls;

/// <summary>Reusable Access-style local append mapping. Destination schema stays authoritative.</summary>
public sealed class AppendTableControl : UserControl, IDisposable
{
    private readonly ComboBox _table = OfficeVisuals.Combo([]);
    private readonly ComboBox _source = OfficeVisuals.Combo([]);
    private readonly ComboBox _destination = OfficeVisuals.Combo([]);
    private readonly TableDefinition[] _tables;
    private readonly string[] _sourceNames;
    private string?[] _mapping = [];
    private bool _loading;
    public string DestinationTable => _table.SelectedItem as string ?? throw new DataSpaceException("Select a destination table.");

    public AppendTableControl(IEnumerable<TableDefinition> tables, IReadOnlyList<FieldDefinition> sourceFields, string sourceName, int recordCount)
    {
        // Retain detached schema only; this control never keeps a second record graph.
        _tables = tables.Select(table => new TableDefinition { Name = table.Name, Fields = table.Fields.Select(TableSchemaDraft.Copy).ToList() }).ToArray();
        _sourceNames = sourceFields.Select(field => field.Name).ToArray();
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        var info = OfficeVisuals.Text($"Append {recordCount:N0} records from the current {sourceName} datasheet view. Existing records are not replaced. The complete append is undoable.", 13);
        info.TextWrapping = TextWrapping.Wrap; panel.Children.Add(info);
        EditorVisuals.Labeled(panel, "Destination table", _table);
        EditorVisuals.Labeled(panel, "Append source field", _source);
        EditorVisuals.Labeled(panel, "Append to field", _destination);
        var note = OfficeVisuals.Text("Matching names are selected automatically except AutoNumber. Skip an AutoNumber to generate new values; explicitly map it to keep source values. Unmapped fields use destination defaults or null. Duplicate keys and invalid values cancel the whole append.", 12, "666666");
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        Content = panel;
        _loading = true;
        foreach (var table in _tables) _table.Items.Add(table.Name);
        foreach (var name in _sourceNames) _source.Items.Add(name);
        _table.SelectedItem = _tables.FirstOrDefault(table => Names.Equal(table.Name, sourceName))?.Name ?? _tables.FirstOrDefault()?.Name;
        _source.SelectedIndex = _sourceNames.Length == 0 ? -1 : 0;
        _loading = false; ResetMappings();
        _table.SelectionChanged += (_, _) => { if (!_loading) ResetMappings(); };
        _source.SelectionChanged += (_, _) => { if (!_loading) ShowMapping(); };
        _destination.SelectionChanged += (_, _) =>
        {
            if (_loading || _source.SelectedIndex < 0) return;
            _mapping[_source.SelectedIndex] = _destination.SelectedIndex <= 0 ? null : (string)_destination.SelectedItem;
        };
    }
    private void ResetMappings()
    {
        var table = _tables.FirstOrDefault(table => table.Name == DestinationTable);
        if (table is null) return;
        _mapping = _sourceNames.Select(name => table.Fields.FirstOrDefault(field => Names.Equal(field.Name, name) && field.Type != FieldType.AutoNumber)?.Name).ToArray();
        _loading = true; _destination.Items.Clear(); _destination.Items.Add("(Do not append)");
        foreach (var field in table.Fields) _destination.Items.Add(field.Name);
        _loading = false; ShowMapping();
    }
    private void ShowMapping()
    {
        _loading = true;
        _destination.SelectedIndex = _source.SelectedIndex < 0 || _mapping[_source.SelectedIndex] is not { } name
            ? 0 : _destination.Items.IndexOf(name);
        _loading = false;
    }
    public (string[] Columns, int[] SourceOrdinals) CaptureMapping()
    {
        var selected = _mapping.Select((name, index) => (name, index)).Where(item => item.name is not null).ToArray();
        if (selected.Length == 0) throw new DataSpaceException("Map at least one source field.");
        var columns = selected.Select(item => item.name!).ToArray();
        if (columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Length) throw new DataSpaceException("A destination field can be mapped only once.");
        return (columns, selected.Select(item => item.index).ToArray());
    }
    public void Dispose() { _loading = true; _table.Items.Clear(); _source.Items.Clear(); _destination.Items.Clear(); Content = null; }
}
