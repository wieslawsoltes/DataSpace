namespace DataSpace.Controls;

public sealed class RecordNavigator : UserControl
{
    private readonly TextBox _position = OfficeVisuals.Input("1", width: 54);
    private readonly TextBlock _count = OfficeVisuals.Text("of 0", 11);
    private readonly TextBlock _filter = OfficeVisuals.Text("No Filter", 11, "666666");
    private readonly TextBox _find = OfficeVisuals.Input(placeholder: "Search", width: 145);
    private bool _updating;
    private int _total;
    public event Action<int>? Navigate;
    public event Action? NewRecord;
    public event Action<string>? SearchChanged;
    public RecordNavigator()
    {
        Height = 30;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(6, 1, 4, 1) };
        panel.Children.Add(OfficeVisuals.Text("Record:", 11));
        panel.Children.Add(OfficeVisuals.Button("|◂", () => Navigate?.Invoke(0)));
        panel.Children.Add(OfficeVisuals.Button("◂", () => Navigate?.Invoke(Math.Max(0, Value() - 2))));
        _position.MinHeight = 25; _position.Height = 25; _position.Padding = new(4, 2, 4, 2); _position.FontSize = 11;
        _position.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && !_updating) { Navigate?.Invoke(Math.Clamp(Value() - 1, 0, Math.Max(0, _total - 1))); e.Handled = true; } };
        AutomationProperties.SetName(_position, "Current record"); panel.Children.Add(_position); panel.Children.Add(_count);
        panel.Children.Add(OfficeVisuals.Button("▸", () => Navigate?.Invoke(Math.Min(_total - 1, Value()))));
        panel.Children.Add(OfficeVisuals.Button("▸|", () => Navigate?.Invoke(Math.Max(0, _total - 1))));
        panel.Children.Add(OfficeVisuals.Button("▸*", () => NewRecord?.Invoke(), automationId: "NewRecordNavigation"));
        _filter.Margin = new(10, 0, 15, 0); panel.Children.Add(_filter);
        _find.Height = 25; _find.MinHeight = 25; _find.FontSize = 11; _find.Padding = new(5, 2, 5, 2);
        _find.TextChanged += (_, _) => SearchChanged?.Invoke(_find.Text); AutomationProperties.SetAutomationId(_find, "RecordSearch"); panel.Children.Add(_find);
        Content = OfficeVisuals.Border(panel, "F3F3F3", "C8CDD1", new(0, 1, 0, 0));
    }
    private int Value() => int.TryParse(_position.Text, out var value) ? value : 1;
    public void Update(int index, int count, bool filtered)
    {
        _updating = true; _total = count; _position.Text = count == 0 ? "0" : (index + 1).ToString(); _count.Text = "of " + count.ToString("N0");
        _filter.Text = filtered ? "Filtered" : "No Filter"; _updating = false;
    }
    public void FocusSearch() => _find.Focus(FocusState.Programmatic);
}
