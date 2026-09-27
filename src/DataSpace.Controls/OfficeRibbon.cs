namespace DataSpace.Controls;

public sealed record RibbonCommand(string Id, string Label, string Icon, bool Large = false, string? Shortcut = null, bool Enabled = true);
public sealed record RibbonGroup(string Label, IReadOnlyList<RibbonCommand> Commands);
public sealed record RibbonTab(string Id, string Label, IReadOnlyList<RibbonGroup> Groups, bool Contextual = false);

/// <summary>A standalone, data-driven Office ribbon with custom groups, command routing and collapse behavior.</summary>
public sealed class OfficeRibbon : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly ScrollViewer _groupScroll;
    private readonly Dictionary<string, List<Button>> _commandButtons = new(StringComparer.Ordinal);
    private IReadOnlyList<RibbonTab> _definitions = [];
    private string _selected = "home";
    public event Action<string>? CommandInvoked;
    public event Action<string>? TabSelected;
    public string SelectedTab => _selected;
    public bool IsCollapsed { get; private set; }
    public OfficeRibbon()
    {
        var root = OfficeVisuals.Grid("32,Auto"); root.Background = OfficeVisuals.Brush("F7F7F7");
        var tabGrid = OfficeVisuals.Grid("*", "*,32");
        var tabScroll = new ScrollViewer { Content = _tabs, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        OfficeVisuals.Add(tabGrid, tabScroll);
        var collapse = OfficeVisuals.Button("⌃", ToggleCollapsed, automationId: "RibbonCollapse"); ToolTipService.SetToolTip(collapse, "Collapse or expand the ribbon (Ctrl+F1)");
        OfficeVisuals.Add(tabGrid, collapse, column: 1); OfficeVisuals.Add(root, tabGrid);
        _groupScroll = new ScrollViewer { Content = _groups, Height = 102, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        OfficeVisuals.Add(root, OfficeVisuals.Border(_groupScroll, "FAFAFA", "C8CDD1", new(0, 0, 0, 1)), 1); Content = root;
        AutomationProperties.SetName(this, "Ribbon");
    }
    public void SetTabs(IReadOnlyList<RibbonTab> tabs)
    {
        _definitions = tabs;
        if (!tabs.Any(t => t.Id == _selected)) _selected = tabs.FirstOrDefault(t => t.Id != "file")?.Id ?? "";
        BuildTabs(); BuildGroups();
    }
    public void SelectTab(string id)
    {
        if (id == "file") { CommandInvoked?.Invoke("file"); return; }
        if (!_definitions.Any(t => t.Id == id)) return;
        _selected = id; IsCollapsed = false; _groupScroll.Visibility = Visibility.Visible;
        BuildTabs(); BuildGroups(); TabSelected?.Invoke(id);
    }
    public void ToggleCollapsed() { IsCollapsed = !IsCollapsed; _groupScroll.Visibility = IsCollapsed ? Visibility.Collapsed : Visibility.Visible; }
    public void SetCommandEnabled(string id, bool enabled)
    { if (_commandButtons.TryGetValue(id, out var buttons)) foreach (var button in buttons) button.IsEnabled = enabled; }
    private void BuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var tab in _definitions)
        {
            var button = OfficeVisuals.Button(tab.Label, () => SelectTab(tab.Id), automationId: "RibbonTab_" + tab.Id);
            button.MinWidth = tab.Id == "file" ? 58 : 66; button.Height = 32; button.Padding = new(16, 4, 16, 4);
            button.Background = OfficeVisuals.Brush(tab.Id == "file" ? "A4373A" : tab.Id == _selected ? "FAFAFA" : tab.Contextual ? "E8F0EA" : "F7F7F7");
            button.Foreground = OfficeVisuals.Brush(tab.Id == "file" ? "FFFFFF" : tab.Id == _selected ? "A4373A" : "252525");
            button.BorderBrush = OfficeVisuals.Brush(tab.Id == _selected ? "C8CDD1" : "00000000"); button.BorderThickness = tab.Id == _selected ? new(1, 1, 1, 0) : new(0);
            button.DoubleTapped += (_, _) => ToggleCollapsed(); _tabs.Children.Add(button);
        }
    }
    private void BuildGroups()
    {
        _groups.Children.Clear(); _commandButtons.Clear();
        var tab = _definitions.FirstOrDefault(t => t.Id == _selected); if (tab is null) return;
        foreach (var group in tab.Groups)
        {
            var container = OfficeVisuals.Grid("*,20"); container.Margin = new(4, 4, 0, 0); container.MinWidth = 62;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(3, 0, 7, 0) };
            StackPanel? smallColumn = null; var smallCount = 0;
            foreach (var command in group.Commands)
            {
                var button = OfficeVisuals.Button(command.Label, () => CommandInvoked?.Invoke(command.Id), automationId: "Command_" + command.Id);
                var content = new StackPanel { Orientation = command.Large ? Orientation.Vertical : Orientation.Horizontal, Spacing = command.Large ? 5 : 6, HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new OfficeIcon(command.Icon) { Width = command.Large ? 30 : 16, Height = command.Large ? 30 : 16, HorizontalAlignment = HorizontalAlignment.Center });
                var text = OfficeVisuals.Text(command.Label, 11.5); text.TextAlignment = TextAlignment.Center;
                content.Children.Add(text); button.Content = content; button.IsEnabled = command.Enabled;
                button.Height = command.Large ? 75 : 25; button.Padding = command.Large ? new(7, 5, 7, 4) : new(4, 2, 5, 2);
                button.HorizontalContentAlignment = command.Large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                ToolTipService.SetToolTip(button, command.Label + (command.Shortcut is null ? "" : " (" + command.Shortcut + ")"));
                if (!_commandButtons.TryGetValue(command.Id, out var buttons)) _commandButtons[command.Id] = buttons = [];
                buttons.Add(button);
                if (command.Large) { row.Children.Add(button); smallColumn = null; smallCount = 0; }
                else
                {
                    if (smallColumn is null || smallCount == 3) { smallColumn = new(); row.Children.Add(smallColumn); smallCount = 0; }
                    smallColumn.Children.Add(button); smallCount++;
                }
            }
            OfficeVisuals.Add(container, row);
            var label = OfficeVisuals.Text(group.Label, 10.5, "666666"); label.HorizontalAlignment = HorizontalAlignment.Center;
            OfficeVisuals.Add(container, label, 1);
            _groups.Children.Add(OfficeVisuals.Border(container, "FAFAFA", "D9D9D9", new(0, 0, 1, 0)));
        }
    }
}
