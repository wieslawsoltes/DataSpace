using DataSpace.DataSources;

namespace DataSpace.Controls;

/// <summary>Reusable per-field import properties. One native property editor is reused for all source columns.</summary>
public sealed class ImportFieldOptionsControl : UserControl, IDisposable
{
    private static readonly (string Label, FieldType Type)[] Types =
    [ ("Short Text", FieldType.ShortText), ("Long Text", FieldType.LongText), ("Integer", FieldType.Integer),
      ("Decimal", FieldType.Decimal), ("Currency", FieldType.Currency), ("Date/Time", FieldType.DateTime),
      ("Yes/No", FieldType.YesNo), ("GUID", FieldType.Guid) ];
    private readonly ComboBox _field = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = OfficeVisuals.Input();
    private readonly ComboBox _type = OfficeVisuals.Combo(Types.Select(item => item.Label));
    private readonly TextBox _length = OfficeVisuals.Input("255");
    private readonly CheckBox _skip = new() { Content = "Do not import field (Skip)" };
    private readonly CheckBox _required = new() { Content = "Required value" };
    private readonly CheckBox _unique = new() { Content = "Indexed (No Duplicates)" };
    private readonly CheckBox _primary = new() { Content = "Use as primary key" };
    private readonly CheckBox _generate = new() { Content = "Add AutoNumber primary key" };
    private readonly TextBox _keyName = OfficeVisuals.Input("ID");
    private SourceImportPlan? _plan;
    private int _index = -1;
    private bool _loading;

    public ImportFieldOptionsControl()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "ImportFieldOptions");
        var panel = new StackPanel { Spacing = 7, Margin = new(12) };
        panel.Children.Add(OfficeVisuals.Text("Field Options", 18, "A4373A", true));
        var hint = OfficeVisuals.Text("Select a source field to set its destination name and data type. Conversions and keys are validated before the table is added.", 12, "666666");
        hint.TextWrapping = TextWrapping.Wrap; panel.Children.Add(hint);
        EditorVisuals.Labeled(panel, "Source field", _field);
        panel.Children.Add(_skip);
        var properties = OfficeVisuals.Grid("*", "*,20,*");
        var names = new StackPanel { Spacing = 6 }; var data = new StackPanel { Spacing = 6 };
        EditorVisuals.Labeled(names, "Destination field name", _name);
        EditorVisuals.Labeled(data, "Import data type", _type);
        EditorVisuals.Labeled(names, "Short Text maximum length", _length);
        data.Children.Add(_required); data.Children.Add(_unique); data.Children.Add(_primary);
        OfficeVisuals.Add(properties, names); OfficeVisuals.Add(properties, data, column: 2); panel.Children.Add(properties);
        panel.Children.Add(_generate); EditorVisuals.Labeled(panel, "Generated key field name", _keyName);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _field.SelectionChanged += (_, _) =>
        {
            if (_loading) return; Store(); _index = _field.SelectedIndex; Load();
        };
        _name.TextChanged += (_, _) => Store(); _length.TextChanged += (_, _) => Store(); _keyName.TextChanged += (_, _) => Store();
        _type.SelectionChanged += (_, _) => { Store(); EnableProperties(); };
        foreach (var check in new[] { _skip, _required, _unique })
        { check.Checked += (_, _) => { Store(); EnableProperties(); }; check.Unchecked += (_, _) => { Store(); EnableProperties(); }; }
        _primary.Checked += (_, _) =>
        {
            if (_loading || _plan is null) return;
            for (var i = 0; i < _plan.Fields.Count; i++) _plan.Fields[i] = _plan.Fields[i] with { PrimaryKey = false };
            _loading = true; _generate.IsChecked = false; _required.IsChecked = _unique.IsChecked = true; _loading = false;
            Store(); EnableProperties();
        };
        _primary.Unchecked += (_, _) => Store();
        _generate.Checked += (_, _) =>
        {
            if (_loading || _plan is null) return;
            for (var i = 0; i < _plan.Fields.Count; i++) _plan.Fields[i] = _plan.Fields[i] with { PrimaryKey = false };
            _loading = true; _primary.IsChecked = false; _loading = false; Store(); EnableProperties();
        };
        _generate.Unchecked += (_, _) => { Store(); EnableProperties(); };
    }

    public void SetPlan(SourceImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Store();
        _loading = true; _plan = plan; _field.Items.Clear();
        foreach (var field in plan.Fields) _field.Items.Add(field.SourceName);
        _index = plan.Fields.Count == 0 ? -1 : 0; _field.SelectedIndex = _index;
        _generate.IsChecked = plan.GeneratedKeyName is not null;
        _keyName.Text = plan.GeneratedKeyName ?? Names.Available("ID", plan.Fields.Select(field => field.Name));
        _loading = false; Load();
    }

    /// <summary>Read current native input values, then return a detached specification for an asynchronous import.</summary>
    public SourceImportPlan CapturePlan()
    {
        Store();
        if (_plan is null) throw new DataSpaceException("Choose a source table first.");
        return new() { Fields = _plan.Fields.ToList(), GeneratedKeyName = _plan.GeneratedKeyName };
    }

    private void Store()
    {
        if (_loading || _plan is null || _index < 0 || _index >= _plan.Fields.Count) return;
        var type = _type.SelectedIndex >= 0 ? Types[_type.SelectedIndex].Type : FieldType.LongText;
        _plan.Fields[_index] = _plan.Fields[_index] with
        {
            Name = _name.Text, Type = type, Include = _skip.IsChecked != true,
            Required = _required.IsChecked == true, Unique = _unique.IsChecked == true, PrimaryKey = _primary.IsChecked == true,
            MaxLength = int.TryParse(_length.Text, out var length) ? length : 0
        };
        _plan.GeneratedKeyName = _generate.IsChecked == true ? _keyName.Text : null;
    }

    private void Load()
    {
        if (_plan is null || _index < 0 || _index >= _plan.Fields.Count) return;
        _loading = true;
        var field = _plan.Fields[_index]; _name.Text = field.Name;
        _type.SelectedIndex = Array.FindIndex(Types, item => item.Type == field.Type);
        _length.Text = field.MaxLength.ToString(FieldValues.Culture); _skip.IsChecked = !field.Include;
        _required.IsChecked = field.Required; _unique.IsChecked = field.Unique; _primary.IsChecked = field.PrimaryKey;
        _loading = false; EnableProperties();
    }

    private void EnableProperties()
    {
        var enabled = _skip.IsChecked != true;
        _name.IsEnabled = _type.IsEnabled = _required.IsEnabled = _unique.IsEnabled = _primary.IsEnabled = enabled;
        _length.IsEnabled = enabled && _type.SelectedIndex == 0;
        _keyName.IsEnabled = _generate.IsChecked == true;
    }

    public void Dispose()
    {
        _loading = true; _plan = null; _index = -1; _field.Items.Clear();
        Content = null;
    }

}
