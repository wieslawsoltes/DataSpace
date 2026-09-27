namespace DataSpace.Controls;

public enum DatabaseObjectKind { Table, Query, Form, Report, Macro, Relationships }
public sealed record DatabaseObjectItem(DatabaseObjectKind Kind, string Name)
{
    public string Key => Kind + ":" + Name;
    public string Icon => Kind switch { DatabaseObjectKind.Table => "table", DatabaseObjectKind.Query => "query", DatabaseObjectKind.Form => "form", DatabaseObjectKind.Report => "report", DatabaseObjectKind.Macro => "macro", _ => "relationships" };
}

public sealed class NavigationPane : UserControl
{
    private readonly StackPanel _items = new() { Spacing = 0 };
    private readonly TextBox _search = OfficeVisuals.Input(placeholder: "Search...");
    private IReadOnlyList<DatabaseObjectItem> _objects = [];
    private readonly HashSet<DatabaseObjectKind> _collapsed = [];
    private string? _selected;
    public event Action<DatabaseObjectItem, bool>? ObjectOpened;
    public event Action<DatabaseObjectItem, string>? ObjectCommand;
    public event Action? CollapseRequested;
    public NavigationPane()
    {
        var root = OfficeVisuals.Grid("36,38,*"); root.Background = OfficeVisuals.Brush("F9F9F9");
        var header = OfficeVisuals.Grid("*", "*,30");
        var title = OfficeVisuals.Text("All Access Objects", 14, "444444", true); title.Margin = new(12, 0, 0, 0); OfficeVisuals.Add(header, title);
        OfficeVisuals.Add(header, OfficeVisuals.Button("«", () => CollapseRequested?.Invoke(), automationId: "NavigationCollapse"), column: 1);
        OfficeVisuals.Add(root, header);
        _search.Margin = new(8, 2, 8, 8); AutomationProperties.SetAutomationId(_search, "ObjectSearch"); AutomationProperties.SetName(_search, "Search database objects");
        _search.TextChanged += (_, _) => Build(); OfficeVisuals.Add(root, _search, 1);
        OfficeVisuals.Add(root, new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 2);
        Content = OfficeVisuals.Border(root, "F9F9F9", "C3C8CC", new(0, 0, 1, 0));
    }
    public void SetObjects(DatabaseDocument document)
    {
        _objects = document.Tables.Select(t => new DatabaseObjectItem(DatabaseObjectKind.Table, t.Name))
            .Concat(document.Queries.Select(q => new DatabaseObjectItem(DatabaseObjectKind.Query, q.Name)))
            .Concat(document.Forms.Select(f => new DatabaseObjectItem(DatabaseObjectKind.Form, f.Name)))
            .Concat(document.Reports.Select(r => new DatabaseObjectItem(DatabaseObjectKind.Report, r.Name)))
            .Concat(document.Macros.Select(m => new DatabaseObjectItem(DatabaseObjectKind.Macro, m.Name))).ToList();
        Build();
    }
    public void Select(DatabaseObjectItem? item) { _selected = item?.Key; Build(); }
    public void FocusSearch() => _search.Focus(FocusState.Programmatic);
    private void Build()
    {
        _items.Children.Clear();
        foreach (var kind in new[] { DatabaseObjectKind.Table, DatabaseObjectKind.Query, DatabaseObjectKind.Form, DatabaseObjectKind.Report, DatabaseObjectKind.Macro })
        {
            var matches = _objects.Where(o => o.Kind == kind && o.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0 && _search.Text.Length != 0) continue;
            var title = kind switch { DatabaseObjectKind.Query => "Queries", _ => kind + "s" };
            var header = OfficeVisuals.Button((_collapsed.Contains(kind) ? "▸  " : "▾  ") + title, () => { if (!_collapsed.Add(kind)) _collapsed.Remove(kind); Build(); });
            header.HorizontalAlignment = HorizontalAlignment.Stretch; header.HorizontalContentAlignment = HorizontalAlignment.Left;
            header.Background = OfficeVisuals.Brush("E8E8E8"); header.Height = 29; header.Margin = new(0, 5, 0, 2); header.Padding = new(10, 3, 6, 3); _items.Children.Add(header);
            if (_collapsed.Contains(kind) && _search.Text.Length == 0) continue;
            foreach (var item in matches)
            {
                var button = OfficeVisuals.Button(item.Name, () => { _selected = item.Key; ObjectOpened?.Invoke(item, false); Build(); }, item.Icon, "Object_" + item.Key);
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Height = 30; button.Margin = new(5, 0, 5, 1); button.Padding = new(9, 4, 6, 4);
                if (item.Key == _selected) { button.Background = OfficeVisuals.Brush("F5DFB4"); button.BorderBrush = OfficeVisuals.Brush("E8BC69"); }
                var menu = new MenuFlyout();
                foreach (var command in new[] { "Open", "Design View", "Rename", "Delete" })
                {
                    var entry = new MenuFlyoutItem { Text = command };
                    entry.Click += (_, _) => { if (command is "Open" or "Design View") ObjectOpened?.Invoke(item, command == "Design View"); else ObjectCommand?.Invoke(item, command); };
                    menu.Items.Add(entry);
                }
                button.ContextFlyout = menu; _items.Children.Add(button);
            }
        }
    }
}
