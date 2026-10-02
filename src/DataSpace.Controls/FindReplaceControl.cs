namespace DataSpace.Controls;

/// <summary>Literal Find/Replace options with explicit confirmation before replacing an entire view.</summary>
public sealed class FindReplaceControl : UserControl, IDisposable
{
    private readonly TextBox _find = OfficeVisuals.Input();
    private readonly TextBox _replace = OfficeVisuals.Input();
    private readonly ComboBox _scope, _match = OfficeVisuals.Combo(["Any Part of Field", "Whole Field", "Start of Field"]);
    private readonly ComboBox _direction = OfficeVisuals.Combo(["Down", "Up"]);
    private readonly CheckBox _case = new() { Content = "Match case" }, _formatted = new() { Content = "Search as formatted" };
    private readonly Button _all;
    private bool _armed;
    public event Action? FindRequested;
    public event Action<bool>? ReplaceRequested;
    public StatusMessageControl Status { get; } = new();
    public ValidationMessageControl Error { get; } = new();
    public string? OnlyField => _scope.SelectedIndex <= 0 ? null : (string)_scope.SelectedItem;
    public string Replacement => _replace.Text;
    public TableSearchOptions Options => new(_find.Text, (TextMatchMode)_match.SelectedIndex, _case.IsChecked == true, _direction.SelectedIndex == 1, _formatted.IsChecked == true);
    public FindReplaceControl(IEnumerable<string> fields, string? selectedField, bool replace)
    {
        _scope = OfficeVisuals.Combo(new[] { "All displayed fields" }.Concat(fields), selectedField);
        var panel = new StackPanel { Spacing = 8, MinWidth = 410 };
        EditorVisuals.Labeled(panel, "Find what", _find); EditorVisuals.Labeled(panel, "Replace with", _replace);
        EditorVisuals.Labeled(panel, "Look in", _scope); EditorVisuals.Labeled(panel, "Match", _match); EditorVisuals.Labeled(panel, "Search direction", _direction);
        AutomationProperties.SetName(_case, "Match case"); AutomationProperties.SetName(_formatted, "Search as formatted");
        panel.Children.Add(OfficeVisuals.Row(_case, _formatted));
        _all = OfficeVisuals.Button("Replace All", () =>
        {
            if (!_armed) { _armed = true; _all.Content = "Confirm Replace All"; AutomationProperties.SetName(_all, "Confirm Replace All"); Status.Text = "Replace every matching value in this view? Press Confirm Replace All to apply one undoable change."; return; }
            Disarm(); ReplaceRequested?.Invoke(true);
        });
        panel.Children.Add(OfficeVisuals.Row(OfficeVisuals.Button("Find Next", () => { Disarm(); FindRequested?.Invoke(); }),
            OfficeVisuals.Button("Replace", () => { Disarm(); ReplaceRequested?.Invoke(false); }), _all));
        panel.Children.Add(Error); panel.Children.Add(Status);
        var note = OfficeVisuals.Text("Search uses literal text, not wildcards. Replace uses stored values; AutoNumber fields are never replaced. The current filtered view defines the scope.", 11, "666666"); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        Content = panel;
        _find.TextChanged += (_, _) => Disarm(); _replace.TextChanged += (_, _) => Disarm();
        foreach (var combo in new[] { _scope, _match, _direction }) combo.SelectionChanged += (_, _) => Disarm();
        foreach (var check in new[] { _case, _formatted }) { check.Checked += (_, _) => Disarm(); check.Unchecked += (_, _) => Disarm(); }
        Loaded += (_, _) => (replace ? _replace : _find).Focus(FocusState.Programmatic);
    }
    private void Disarm() { _armed = false; _all.Content = "Replace All"; AutomationProperties.SetName(_all, "Replace All"); }
    public void Dispose() { Content = null; _scope.Items.Clear(); }
}
