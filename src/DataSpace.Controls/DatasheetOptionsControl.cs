namespace DataSpace.Controls;

/// <summary>Reusable detached column visibility/order/freeze and datasheet formatting editor.</summary>
public sealed class DatasheetOptionsControl : UserControl, IDisposable
{
    private readonly DatasheetLayout _layout;
    private readonly List<string> _order;
    private readonly ComboBox _field;
    private readonly CheckBox _visible = new() { Content = "Show field" };
    private readonly CheckBox _frozen = new() { Content = "Keep field visible while scrolling" };
    private readonly TextBox _height, _font;
    private bool _loading;
    public DatasheetOptionsControl(IReadOnlyList<FieldDefinition> fields, DatasheetLayout layout, string? selectedField = null)
    {
        _layout = layout.Copy();
        _order = layout.VisibleFields(fields).Select(f => f.Name).Concat(layout.ColumnOrder).Concat(fields.Select(f => f.Name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => fields.Any(f => Names.Equal(name, f.Name))).ToList();
        _field = OfficeVisuals.Combo(_order, selectedField);
        _height = OfficeVisuals.Input(layout.RowHeight.ToString(FieldValues.Culture));
        _font = OfficeVisuals.Input(layout.FontSize.ToString(FieldValues.Culture));
        var panel = new StackPanel { Spacing = 8, MinWidth = 390 };
        EditorVisuals.Labeled(panel, "Datasheet field", _field);
        AutomationProperties.SetName(_visible, "Show field"); AutomationProperties.SetName(_frozen, "Keep field visible while scrolling");
        panel.Children.Add(_visible); panel.Children.Add(_frozen);
        panel.Children.Add(OfficeVisuals.Row(OfficeVisuals.Button("Move Left", () => Move(-1)), OfficeVisuals.Button("Move Right", () => Move(1))));
        EditorVisuals.Labeled(panel, "Row height", _height); EditorVisuals.Labeled(panel, "Datasheet font size", _font);
        panel.Children.Add(EditorVisuals.Check("Bold text", layout.Bold, value => _layout.Bold = value));
        panel.Children.Add(EditorVisuals.Check("Alternate row shading", layout.AlternateRows, value => _layout.AlternateRows = value));
        panel.Children.Add(EditorVisuals.Check("Horizontal gridlines", layout.HorizontalGridLines, value => _layout.HorizontalGridLines = value));
        panel.Children.Add(EditorVisuals.Check("Vertical gridlines", layout.VerticalGridLines, value => _layout.VerticalGridLines = value));
        var note = OfficeVisuals.Text("Frozen fields move to the left. Hidden fields retain their data. Layout is saved with the table and can be undone.", 12, "666666"); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        Content = panel; _field.SelectionChanged += (_, _) => ShowField();
        _visible.Checked += (_, _) => Set(_layout.HiddenFields, false); _visible.Unchecked += (_, _) => Set(_layout.HiddenFields, true);
        _frozen.Checked += (_, _) => Set(_layout.FrozenFields, true); _frozen.Unchecked += (_, _) => Set(_layout.FrozenFields, false);
        ShowField();
    }
    private void Set(List<string> names, bool add)
    {
        if (_loading || _field.SelectedItem is not string field) return;
        names.RemoveAll(name => Names.Equal(name, field)); if (add) names.Add(field);
    }
    private void ShowField()
    {
        _loading = true; var field = _field.SelectedItem as string;
        _visible.IsChecked = !_layout.HiddenFields.Any(name => Names.Equal(name, field));
        _frozen.IsChecked = _layout.FrozenFields.Any(name => Names.Equal(name, field)); _loading = false;
    }
    private void Move(int direction)
    {
        var index = _field.SelectedIndex; var next = index + direction;
        if (index < 0 || next < 0 || next >= _order.Count) return;
        (_order[index], _order[next]) = (_order[next], _order[index]);
        _loading = true; _field.Items.Clear(); foreach (var name in _order) _field.Items.Add(name); _field.SelectedIndex = next; _loading = false; ShowField();
    }
    public DatasheetLayout Capture()
    {
        if (!double.TryParse(_height.Text, System.Globalization.NumberStyles.Float, FieldValues.Culture, out var height) ||
            !double.TryParse(_font.Text, System.Globalization.NumberStyles.Float, FieldValues.Culture, out var font)) throw new DataSpaceException("Enter numeric row height and font size.");
        var copy = _layout.Copy(); copy.ColumnOrder = new(_order); copy.RowHeight = height; copy.FontSize = font;
        copy.FrozenFields = _order.Where(name => copy.FrozenFields.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (_order.All(name => copy.HiddenFields.Contains(name, StringComparer.OrdinalIgnoreCase))) throw new DataSpaceException("Keep at least one field visible.");
        copy.Validate(); return copy;
    }
    public void Dispose() { _loading = true; _field.Items.Clear(); Content = null; }
}
