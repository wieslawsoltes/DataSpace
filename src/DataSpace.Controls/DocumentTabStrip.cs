namespace DataSpace.Controls;

public sealed class DocumentTabStrip : UserControl
{
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private DatabaseObjectItem[] _documents = [];
    private string? _selected;
    public event Action<DatabaseObjectItem>? Selected;
    public event Action<DatabaseObjectItem>? Closed;
    public DocumentTabStrip()
    {
        Height = 33;
        Content = OfficeVisuals.Border(new ScrollViewer { Content = _panel, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, "E7E7E7", "C8CDD1", new(0, 0, 0, 1));
    }
    public void SetDocuments(IReadOnlyList<DatabaseObjectItem> documents, string? selectedKey)
    {
        if (_selected == selectedKey && _documents.SequenceEqual(documents)) return;
        _documents = documents.ToArray(); _selected = selectedKey; _panel.Children.Clear();
        foreach (var document in _documents)
        {
            var selected = document.Key == selectedKey;
            var tab = OfficeVisuals.Grid("*", "*,25"); tab.MinWidth = 130; tab.Height = 32;
            tab.Background = OfficeVisuals.Brush(selected ? "FFFFFF" : "ECECEC");
            var open = OfficeVisuals.Button(document.Name, () => Selected?.Invoke(document), document.Icon, "Document_" + document.Key);
            open.Padding = new(11, 4, 8, 4); open.HorizontalAlignment = HorizontalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Left;
            var close = OfficeVisuals.Button("×", () => Closed?.Invoke(document)); close.Padding = new(3); close.FontSize = 17;
            AutomationProperties.SetName(close, "Close " + document.Name);
            OfficeVisuals.Add(tab, open); OfficeVisuals.Add(tab, close, column: 1);
            _panel.Children.Add(OfficeVisuals.Border(tab, selected ? "FFFFFF" : "ECECEC", selected ? "DFA63D" : "CACACA", new(0, selected ? 2 : 0, 1, 0)));
        }
    }
}
