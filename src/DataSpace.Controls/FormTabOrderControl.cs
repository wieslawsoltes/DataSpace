namespace DataSpace.Controls;

/// <summary>Independent native form tab-order editor: moves, spatial Auto Order and Tab Stop.</summary>
public sealed class FormTabOrderControl : UserControl, IDisposable
{
    private readonly FormTabOrderDraft _draft;
    private readonly ListBox _list = new() { Height = 230, MinWidth = 400 };
    private readonly CheckBox _stop = new() { Content = "Tab Stop", IsChecked = true };
    private readonly Button _up, _down;
    private bool _loading;
    public FormTabOrderControl(FormDefinition form)
    {
        _draft = new(form);
        var panel = new StackPanel { Spacing = 10 };
        var help = OfficeVisuals.Text("Choose a control and move it in the tab sequence. Auto Order arranges controls from top to bottom, then left to right. Clearing Tab Stop skips keyboard traversal, not mouse access.", 13);
        help.TextWrapping = TextWrapping.Wrap; help.MaxWidth = 520;
        panel.Children.Add(help);
        AutomationProperties.SetName(_list, "Control tab order");
        panel.Children.Add(_list);
        _up = OfficeVisuals.Button("Move Up", () => Move(-1));
        _down = OfficeVisuals.Button("Move Down", () => Move(1));
        panel.Children.Add(OfficeVisuals.Row(_up, _down, OfficeVisuals.Button("Auto Order", () =>
        {
            var id = SelectedId; _draft.AutoOrder(); Refresh(id);
        })));
        AutomationProperties.SetName(_stop, "Tab Stop"); panel.Children.Add(_stop);
        Content = panel;
        _list.SelectionChanged += (_, _) => { if (!_loading) UpdateSelection(); };
        _stop.Checked += (_, _) => ChangeStop(true); _stop.Unchecked += (_, _) => ChangeStop(false);
        Refresh(_draft.Entries.FirstOrDefault()?.Id);
    }
    private string? SelectedId => _list.SelectedIndex >= 0 ? _draft.Entries[_list.SelectedIndex].Id : null;
    private void ChangeStop(bool value)
    {
        if (_loading || SelectedId is not { } id) return;
        _draft.SetTabStop(id, value);
    }
    private void Move(int delta)
    {
        if (SelectedId is not { } id) return;
        var index = _list.SelectedIndex + delta;
        if (index < 0 || index >= _draft.Entries.Count) return;
        _draft.MoveTo(id, index); Refresh(id);
    }
    private void Refresh(string? selected)
    {
        _loading = true; _list.Items.Clear();
        foreach (var entry in _draft.Entries) _list.Items.Add((_list.Items.Count + 1) + ". " + entry.Caption);
        _list.SelectedIndex = selected is null ? -1 : _draft.Entries.ToList().FindIndex(entry => entry.Id == selected);
        _loading = false; UpdateSelection();
    }
    private void UpdateSelection()
    {
        _loading = true;
        var index = _list.SelectedIndex; _stop.IsEnabled = index >= 0;
        _stop.IsChecked = index >= 0 && _draft.Entries[index].TabStop;
        _up.IsEnabled = index > 0; _down.IsEnabled = index >= 0 && index < _draft.Entries.Count - 1;
        _loading = false;
    }
    public bool Apply(FormDefinition form) => _draft.Apply(form);
    public void Dispose() { _loading = true; _list.Items.Clear(); Content = null; }
}
