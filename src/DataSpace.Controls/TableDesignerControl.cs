namespace DataSpace.Controls;

/// <summary>Detached field design grid, index editor and property sheet, reusable without the application shell.</summary>
public sealed class TableDesignerControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private TableSchemaDraft _draft;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly StackPanel _properties = new() { Spacing = 6, Margin = new(14) };
    private FieldDraft? _selected;
    public bool HasPendingChanges { get; private set; }
    public TableDesignerControl(DatabaseWorkspace workspace, string tableName)
    {
        _workspace = workspace; _draft = new(workspace.Document, tableName);
        var root = OfficeVisuals.Grid("Auto,*,Auto", "*,300");
        var toolbar = OfficeVisuals.Row(OfficeVisuals.Button("Add Field", AddField, "new"), OfficeVisuals.Button("Delete Field", DeleteField, "delete"), OfficeVisuals.Button("Move Up", () => Move(-1)), OfficeVisuals.Button("Move Down", () => Move(1)), OfficeVisuals.Button("Indexes", async () => await EditIndexesAsync(), "table", "TableIndexes"));
        toolbar.Margin = new(8); OfficeVisuals.Add(root, toolbar, columnSpan: 2);
        var grid = OfficeVisuals.Grid("30,*"); var header = OfficeVisuals.Grid("*", "36,220,180,*");
        var labels = new[] { "", "Field Name", "Data Type", "Description" };
        for (var i = 0; i < labels.Length; i++) { var text = OfficeVisuals.Text(labels[i], 12, bold: true); text.Margin = new(8, 0, 0, 0); OfficeVisuals.Add(header, text, column: i); }
        header.Background = OfficeVisuals.Brush("E9ECEF"); OfficeVisuals.Add(grid, header); OfficeVisuals.Add(grid, EditorVisuals.Scroll(_rows), 1);
        OfficeVisuals.Add(root, grid, 1); OfficeVisuals.Add(root, OfficeVisuals.Border(EditorVisuals.Scroll(_properties), "F7F7F7", thickness: new(1, 0, 0, 0)), 1, 1);
        var note = OfficeVisuals.Text("Field changes are staged. Save or change views to validate and apply them as one undoable transaction.", 11, "666666");
        note.Margin = new(10); note.TextWrapping = TextWrapping.Wrap; OfficeVisuals.Add(root, note, 2, columnSpan: 2); Content = root; BuildRows();
    }
    private async Task EditIndexesAsync()
    {
        try
        {
            Commit(); using var editor = new IndexDesignerControl(_workspace, _draft.TableName);
            var error = OfficeVisuals.Text("", 12, "9C252A"); error.TextWrapping = TextWrapping.Wrap;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Indexes — " + _draft.TableName, Content = OfficeVisuals.Stack(error, editor), PrimaryButtonText = "Save Indexes", CloseButtonText = "Cancel" };
            dialog.PrimaryButtonClick += (_, e) => { try { editor.Commit(); } catch (Exception exception) { error.Text = exception.Message; e.Cancel = true; } };
            await dialog.ShowAsync(); _draft = new(_workspace.Document, _draft.TableName); _selected = null; HasPendingChanges = false; BuildRows();
        }
        catch (Exception exception)
        {
            var text = OfficeVisuals.Text(exception.Message); text.TextWrapping = TextWrapping.Wrap;
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Table Design", Content = text, CloseButtonText = "Close" }.ShowAsync();
        }
    }
    private void AddField()
    {
        var field = new FieldDraft(null, new() { Name = Names.Available("Field", _draft.Fields.Select(f => f.Field.Name)) });
        _draft.Fields.Add(field); _selected = field; HasPendingChanges = true; BuildRows();
    }
    private void DeleteField() { if (_selected is null) return; _draft.Fields.Remove(_selected); _selected = _draft.Fields.FirstOrDefault(); HasPendingChanges = true; BuildRows(); }
    private void Move(int direction)
    {
        if (_selected is null) return; var index = _draft.Fields.IndexOf(_selected); var next = index + direction;
        if (next < 0 || next >= _draft.Fields.Count) return;
        _draft.Fields.RemoveAt(index); _draft.Fields.Insert(next, _selected); HasPendingChanges = true; BuildRows();
    }
    private void BuildRows()
    {
        _rows.Children.Clear(); _selected ??= _draft.Fields.FirstOrDefault();
        foreach (var entry in _draft.Fields)
        {
            var row = OfficeVisuals.Grid("34", "36,220,180,*"); var field = entry.Field; row.Background = OfficeVisuals.Brush(entry == _selected ? "FFF2D7" : "FFFFFF");
            OfficeVisuals.Add(row, OfficeVisuals.Button(field.PrimaryKey ? "◆" : "▸", () => { _selected = entry; BuildRows(); }));
            var name = OfficeVisuals.Input(field.Name); name.TextChanged += (_, _) => { if (field.Name != name.Text) { field.Name = name.Text; HasPendingChanges = true; } };
            var type = OfficeVisuals.Combo(Enum.GetNames<FieldType>(), field.Type.ToString());
            type.SelectionChanged += (_, _) => { if (type.SelectedItem is string value && value != field.Type.ToString()) { field.Type = Enum.Parse<FieldType>(value); HasPendingChanges = true; } };
            var description = OfficeVisuals.Input(field.Description); description.TextChanged += (_, _) => { if (field.Description != description.Text) { field.Description = description.Text; HasPendingChanges = true; } };
            foreach (var element in new Control[] { name, type, description }) element.GotFocus += (_, _) => { if (_selected != entry) { _selected = entry; BuildProperties(); } };
            AutomationProperties.SetName(name, "Field name " + field.Name); AutomationProperties.SetName(type, "Data type " + field.Name);
            OfficeVisuals.Add(row, name, column: 1); OfficeVisuals.Add(row, type, column: 2); OfficeVisuals.Add(row, description, column: 3); _rows.Children.Add(row);
        }
        BuildProperties();
    }
    private void BuildProperties()
    {
        _properties.Children.Clear(); _properties.Children.Add(OfficeVisuals.Text("Field Properties", 17, bold: true)); if (_selected is null) return;
        var field = _selected.Field;
        void Text(string label, string value, Action<string> assign)
        {
            var previous = value; var input = OfficeVisuals.Input(value);
            input.TextChanged += (_, _) => { if (previous == input.Text) return; previous = input.Text; assign(previous); HasPendingChanges = true; }; EditorVisuals.Labeled(_properties, label, input);
        }
        Text("Caption", field.Caption, value => field.Caption = value);
        Text("Default Value", field.DefaultValue ?? "", value => field.DefaultValue = value.Length == 0 ? null : value);
        Text("Format", field.Format, value => field.Format = value);
        var size = new NumberBox { Value = field.MaxLength, Minimum = 1, Maximum = 65535, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        size.ValueChanged += (_, _) => { if (double.IsFinite(size.Value) && field.MaxLength != (int)size.Value) { field.MaxLength = (int)size.Value; HasPendingChanges = true; } };
        EditorVisuals.Labeled(_properties, "Field Size", size);
        _properties.Children.Add(EditorVisuals.Check("Primary Key", field.PrimaryKey, value => { if (field.PrimaryKey != value) { field.PrimaryKey = value; HasPendingChanges = true; } }));
        _properties.Children.Add(EditorVisuals.Check("Required", field.Required, value => { if (field.Required != value) { field.Required = value; HasPendingChanges = true; } }));
        _properties.Children.Add(EditorVisuals.Check("Indexed (No Duplicates)", field.Unique, value => { if (field.Unique != value) { field.Unique = value; HasPendingChanges = true; } }));
        _properties.Children.Add(EditorVisuals.Check("Allow Zero Length", field.AllowZeroLength, value => { if (field.AllowZeroLength != value) { field.AllowZeroLength = value; HasPendingChanges = true; } }));
    }
    public void Commit()
    {
        if (!HasPendingChanges) return; _draft.Apply(_workspace);
        _draft = new(_workspace.Document, _draft.TableName); _selected = null; HasPendingChanges = false; BuildRows();
    }
    public void Dispose() { }
}
