namespace DataSpace.Controls;

public enum DatabaseObjectKind { Table, Query, Form, Report, Macro, Relationships }
public sealed record DatabaseObjectItem(DatabaseObjectKind Kind, string Name)
{
    public string Key => Kind + ":" + Name;
    public string Icon => Kind switch { DatabaseObjectKind.Table => "table", DatabaseObjectKind.Query => "query", DatabaseObjectKind.Form => "form", DatabaseObjectKind.Report => "report", DatabaseObjectKind.Macro => "macro", _ => "relationships" };
}

public sealed class NavigationPane : UserControl, IDisposable
{
    private readonly StackPanel _items = new() { Spacing = 0 };
    private readonly TextBox _search = OfficeVisuals.Input(placeholder: "Search...");
    private IReadOnlyList<DatabaseObjectItem> _objects = [];
    private readonly HashSet<DatabaseObjectKind> _collapsed = [];
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private string? _selected;
    private DatabaseObjectKind? _kind;
    private bool _descending, _showHidden;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Button _category;
    public bool OpenOnSingleClick { get; set; } = true;
    public event Action<DatabaseObjectItem, bool>? ObjectOpened;
    public event Action<DatabaseObjectItem, string>? ObjectCommand;
    public event Action? CollapseRequested;
    public NavigationPane()
    {
        var root = OfficeVisuals.Grid("36,38,*"); root.Background = OfficeVisuals.Brush("F9F9F9");
        var header = OfficeVisuals.Grid("*", "*,30");
        _category = OfficeVisuals.Button("All Access Objects ▾", ShowNavigationMenu, automationId: "NavigationCategories");
        _category.HorizontalAlignment = HorizontalAlignment.Stretch; _category.HorizontalContentAlignment = HorizontalAlignment.Left; OfficeVisuals.Add(header, _category);
        OfficeVisuals.Add(header, OfficeVisuals.Button("«", () => CollapseRequested?.Invoke(), automationId: "NavigationCollapse"), column: 1);
        OfficeVisuals.Add(root, header);
        _search.Margin = new(8, 2, 8, 8); AutomationProperties.SetAutomationId(_search, "ObjectSearch"); AutomationProperties.SetName(_search, "Search database objects");
        _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Build(); }; OfficeVisuals.Add(root, _search, 1);
        OfficeVisuals.Add(root, new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 2);
        Content = OfficeVisuals.Border(root, "F9F9F9", "C3C8CC", new(0, 0, 1, 0));
    }
    public void SetObjects(DatabaseDocument document)
    {
        var objects = document.Tables.Select(t => new DatabaseObjectItem(DatabaseObjectKind.Table, t.Name))
            .Concat(document.Queries.Select(q => new DatabaseObjectItem(DatabaseObjectKind.Query, q.Name)))
            .Concat(document.Forms.Select(f => new DatabaseObjectItem(DatabaseObjectKind.Form, f.Name)))
            .Concat(document.Reports.Select(r => new DatabaseObjectItem(DatabaseObjectKind.Report, r.Name)))
            .Concat(document.Macros.Select(m => new DatabaseObjectItem(DatabaseObjectKind.Macro, m.Name))).ToArray();
        if (_objects.SequenceEqual(objects)) return;
        _objects = objects; Build();
    }
    public void Select(DatabaseObjectItem? item)
    {
        if (_selected == item?.Key) return;
        var old = _selected; _selected = item?.Key;
        if (old is not null && _buttons.TryGetValue(old, out var previous)) Style(previous, false);
        if (_selected is not null && _buttons.TryGetValue(_selected, out var current)) Style(current, true);
    }
    private static void Style(Button button, bool selected)
    {
        button.Background = OfficeVisuals.Brush(selected ? "F5DFB4" : "00000000");
        button.BorderBrush = OfficeVisuals.Brush(selected ? "E8BC69" : "00000000");
    }
    public void FocusSearch() => _search.Focus(FocusState.Programmatic);
    private void ShowNavigationMenu()
    {
        var menu = new MenuFlyout();
        void Apply(Action action)
        {
            // Rebuilding the object list while its native menu still owns focus
            // can leave a stale popup peer as the target of the next key gesture.
            menu.Hide();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded) return;
                action();
                _category.Focus(FocusState.Programmatic);
            });
        }
        void Item(string name, Action action) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => Apply(action); menu.Items.Add(item); }
        void Category(DatabaseObjectKind? kind, string name) => Item(name, () => { _kind = kind; _category.Content = name + " ▾"; AutomationProperties.SetName(_category, name + " ▾"); Build(); });
        Category(null, "All Access Objects");
        foreach (var kind in new[] { DatabaseObjectKind.Table, DatabaseObjectKind.Query, DatabaseObjectKind.Form, DatabaseObjectKind.Report, DatabaseObjectKind.Macro })
            Category(kind, kind == DatabaseObjectKind.Query ? "Queries" : kind + "s");
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Sort A to Z", () => { _descending = false; Build(); }); Item("Sort Z to A", () => { _descending = true; Build(); });
        Item("Expand All Groups", () => { _collapsed.Clear(); Build(); });
        Item("Collapse All Groups", () => { foreach (var kind in Enum.GetValues<DatabaseObjectKind>()) _collapsed.Add(kind); Build(); });
        var hidden = new ToggleMenuFlyoutItem { Text = "Show Hidden Objects", IsChecked = _showHidden };
        hidden.Click += (_, _) => { var show = hidden.IsChecked; Apply(() => { _showHidden = show; Build(); }); }; menu.Items.Add(hidden);
        var single = new ToggleMenuFlyoutItem { Text = "Single-click to open", IsChecked = OpenOnSingleClick };
        single.Click += (_, _) => { var open = single.IsChecked; Apply(() => OpenOnSingleClick = open); }; menu.Items.Add(single);
        menu.ShowAt(_category);
    }
    public void Dispose() { _searchTimer.Stop(); _items.Children.Clear(); _buttons.Clear(); }
    private void Build()
    {
        _items.Children.Clear(); _buttons.Clear();
        foreach (var kind in new[] { DatabaseObjectKind.Table, DatabaseObjectKind.Query, DatabaseObjectKind.Form, DatabaseObjectKind.Report, DatabaseObjectKind.Macro })
        {
            if (_kind is not null && _kind != kind) continue;
            var source = _objects.Where(o => o.Kind == kind && (_showHidden || !_hidden.Contains(o.Key)) && o.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
            var matches = (_descending ? source.OrderByDescending(o => o.Name, StringComparer.OrdinalIgnoreCase) : source.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0 && _search.Text.Length != 0) continue;
            var title = kind switch { DatabaseObjectKind.Query => "Queries", _ => kind + "s" };
            var header = OfficeVisuals.Button((_collapsed.Contains(kind) ? "▸  " : "▾  ") + title, () => { if (!_collapsed.Add(kind)) _collapsed.Remove(kind); Build(); });
            header.HorizontalAlignment = HorizontalAlignment.Stretch; header.HorizontalContentAlignment = HorizontalAlignment.Left;
            header.Background = OfficeVisuals.Brush("E8E8E8"); header.Height = 29; header.Margin = new(0, 5, 0, 2); header.Padding = new(10, 3, 6, 3); _items.Children.Add(header);
            if (_collapsed.Contains(kind) && _search.Text.Length == 0) continue;
            foreach (var item in matches)
            {
                var button = OfficeVisuals.Button(item.Name, () => { Select(item); if (OpenOnSingleClick) ObjectOpened?.Invoke(item, false); }, item.Icon, "Object_" + item.Key);
                button.Opacity = _hidden.Contains(item.Key) ? .55 : 1;
                button.DoubleTapped += (_, e) => { ObjectOpened?.Invoke(item, false); e.Handled = true; };
                button.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && !OpenOnSingleClick) { ObjectOpened?.Invoke(item, false); e.Handled = true; } };
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Height = 30; button.Margin = new(5, 0, 5, 1); button.Padding = new(9, 4, 6, 4);
                Style(button, item.Key == _selected); _buttons[item.Key] = button;
                var menu = new MenuFlyout();
                foreach (var command in new[] { "Open", "Design View", "Rename", "Delete" })
                {
                    var entry = new MenuFlyoutItem { Text = command };
                    entry.Click += (_, _) => { if (command is "Open" or "Design View") ObjectOpened?.Invoke(item, command == "Design View"); else ObjectCommand?.Invoke(item, command); };
                    menu.Items.Add(entry);
                }
                var hide = new MenuFlyoutItem { Text = _hidden.Contains(item.Key) ? "Unhide Object" : "Hide Object" };
                hide.Click += (_, _) => { if (!_hidden.Add(item.Key)) _hidden.Remove(item.Key); Build(); }; menu.Items.Add(hide);
                button.ContextFlyout = menu; _items.Children.Add(button);
            }
        }
    }
}
