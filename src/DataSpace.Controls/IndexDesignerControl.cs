namespace DataSpace.Controls;

/// <summary>Reusable ordered index/field editor. The host chooses when to commit its detached draft.</summary>
public sealed class IndexDesignerControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private readonly IndexDesign _design;
    private readonly string[] _fields;
    private readonly StackPanel _indexes = new() { Spacing = 12 };
    private string _baseline;
    public bool HasPendingChanges => Fingerprint() != _baseline;
    public IndexDesignerControl(DatabaseWorkspace workspace, string tableName)
    {
        _workspace = workspace; _design = new(workspace.Document, tableName);
        _fields = workspace.Document.Table(tableName).Fields.Select(f => f.Name).ToArray(); _baseline = Fingerprint();
        var panel = new StackPanel { Spacing = 10 };
        var help = OfficeVisuals.Text("Add ordered fields to build a composite index. Unique indexes validate all existing records. Primary keys are configured in Field Properties.", 12, "666666"); help.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(help);
        panel.Children.Add(OfficeVisuals.Button("Add Index", () =>
        {
            _design.Indexes.Add(new() { Name = Names.Available("Index", _design.Indexes.Select(i => i.Name)), Fields = [_fields[0]] }); Build();
        }, "new", "AddIndex"));
        panel.Children.Add(_indexes);
        var scroll = EditorVisuals.Scroll(panel); scroll.MaxHeight = 460; Content = scroll; MinWidth = 520;
        AutomationProperties.SetName(this, "Table indexes"); Build();
    }
    private string Fingerprint() => FieldValues.Key(_design.Indexes.Select(i => FieldValues.Key(new[] { i.Name, i.Unique.ToString(), FieldValues.Key(i.Fields) })));
    private void Build()
    {
        _indexes.Children.Clear();
        foreach (var index in _design.Indexes)
        {
            var panel = new StackPanel { Spacing = 5 };
            var name = OfficeVisuals.Input(index.Name, width: 210); AutomationProperties.SetName(name, "Index name " + index.Name);
            name.TextChanged += (_, _) => index.Name = name.Text;
            panel.Children.Add(OfficeVisuals.Row(name, EditorVisuals.Check("Unique", index.Unique, value => index.Unique = value), OfficeVisuals.Button("Remove Index", () => { _design.Indexes.Remove(index); Build(); }, "delete")));
            for (var position = 0; position < index.Fields.Count; position++)
            {
                var current = position;
                var field = OfficeVisuals.Combo(_fields, index.Fields[position], 240); AutomationProperties.SetName(field, $"Index {index.Name} field {position + 1}");
                field.SelectionChanged += (_, _) => { if (field.SelectedItem is string value && current < index.Fields.Count) index.Fields[current] = value; };
                void Move(int delta)
                {
                    var next = current + delta; if (next < 0 || next >= index.Fields.Count) return;
                    (index.Fields[current], index.Fields[next]) = (index.Fields[next], index.Fields[current]); Build();
                }
                panel.Children.Add(OfficeVisuals.Row(OfficeVisuals.Text((position + 1).ToString()), field,
                    OfficeVisuals.Button("Up", () => Move(-1)), OfficeVisuals.Button("Down", () => Move(1)),
                    OfficeVisuals.Button("Remove Field", () => { index.Fields.RemoveAt(current); Build(); })));
            }
            panel.Children.Add(OfficeVisuals.Button("Add Index Field", () =>
            {
                var next = _fields.FirstOrDefault(f => !index.Fields.Contains(f, StringComparer.OrdinalIgnoreCase)); if (next is not null) { index.Fields.Add(next); Build(); }
            }, "new"));
            _indexes.Children.Add(OfficeVisuals.Border(panel, padding: new(10)));
        }
    }
    public void Commit() { if (!HasPendingChanges) return; _design.Apply(_workspace); _baseline = Fingerprint(); }
    public void Dispose() { }
}
