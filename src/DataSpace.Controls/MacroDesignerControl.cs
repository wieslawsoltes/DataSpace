namespace DataSpace.Controls;

/// <summary>Editable, ordered macro actions. Execution is delegated to a host, never evaluated as arbitrary code.</summary>
public sealed class MacroDesignerControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private readonly string _name;
    private readonly List<MacroStep> _steps;
    private readonly StackPanel _rows = new() { Spacing = 8, Margin = new(16) };
    private long _revision;
    public bool HasPendingChanges { get; private set; }
    public event Action? RunRequested;
    public MacroDesignerControl(DatabaseWorkspace workspace, string name)
    {
        _workspace = workspace; _name = name; _revision = workspace.Document.Revision;
        _steps = workspace.Document.Macros.First(m => Names.Equal(m.Name, name)).Steps.Select(s => new MacroStep { Action = s.Action, Argument = s.Argument }).ToList();
        var root = OfficeVisuals.Grid("Auto,*");
        var toolbar = OfficeVisuals.Row(OfficeVisuals.Button("Add Action", () => { _steps.Add(new()); HasPendingChanges = true; Build(); }, "new"), OfficeVisuals.Button("Run Macro", () => RunRequested?.Invoke(), "macro")); toolbar.Margin = new(10);
        OfficeVisuals.Add(root, toolbar); OfficeVisuals.Add(root, EditorVisuals.Scroll(_rows), 1); Content = root; Build();
    }
    private void Build()
    {
        _rows.Children.Clear();
        foreach (var step in _steps)
        {
            var type = OfficeVisuals.Combo(Enum.GetNames<MacroActionKind>(), step.Action.ToString(), 220);
            type.SelectionChanged += (_, _) => { if (type.SelectedItem is string value) { step.Action = Enum.Parse<MacroActionKind>(value); HasPendingChanges = true; } };
            var argument = OfficeVisuals.Input(step.Argument, "Object name or filter expression", 350);
            argument.TextChanged += (_, _) => { step.Argument = argument.Text; HasPendingChanges = true; };
            var row = OfficeVisuals.Row(OfficeVisuals.Text((_steps.IndexOf(step) + 1).ToString(), 14), type, argument,
                OfficeVisuals.Button("↑", () => Move(step, -1)), OfficeVisuals.Button("↓", () => Move(step, 1)),
                OfficeVisuals.Button("Remove", () => { _steps.Remove(step); HasPendingChanges = true; Build(); }, "delete"));
            _rows.Children.Add(OfficeVisuals.Border(row, padding: new(10)));
        }
        var help = OfficeVisuals.Text("OpenObject accepts Table:Customers, Query:Query1, Form:Form1, Report:Report1, or an unambiguous object name. ApplyFilter accepts a WHERE expression. Macros run only when explicitly requested; imported macros never auto-run.", 12, "666666");
        help.TextWrapping = TextWrapping.Wrap; help.MaxWidth = 850; help.HorizontalAlignment = HorizontalAlignment.Left; _rows.Children.Add(help);
    }
    private void Move(MacroStep step, int delta)
    {
        var index = _steps.IndexOf(step); var next = index + delta; if (next < 0 || next >= _steps.Count) return;
        _steps.RemoveAt(index); _steps.Insert(next, step); HasPendingChanges = true; Build();
    }
    public void Commit()
    {
        if (!HasPendingChanges) return;
        _workspace.Edit("Edit macro", document => document.Macros.First(m => Names.Equal(m.Name, _name)).Steps = _steps.Select(s => new MacroStep { Action = s.Action, Argument = s.Argument }).ToList(), _revision);
        _revision = _workspace.Document.Revision; HasPendingChanges = false;
    }
    public void Dispose() { }
}
