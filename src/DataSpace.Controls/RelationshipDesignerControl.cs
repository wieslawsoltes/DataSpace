namespace DataSpace.Controls;

/// <summary>Skia relationship diagram with draggable table cards and validated referential-integrity authoring.</summary>
public sealed class RelationshipDesignerControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private DatabaseDocument _draft;
    private long _revision;
    private readonly RelationshipRenderer _renderer = new();
    private readonly RelationshipViewState _state = new();
    private readonly SkiaSurface _surface = new() { Width = 2400, Height = 1600 };
    private readonly StackPanel _list = new() { Spacing = 5 };
    private TableDefinition? _dragged;
    private Point _start;
    private double _x, _y;
    public bool HasPendingChanges { get; private set; }
    public event Action<string>? Error;
    public RelationshipDesignerControl(DatabaseWorkspace workspace)
    {
        _workspace = workspace; _draft = DocumentCodec.Clone(workspace.Document); _revision = workspace.Document.Revision;
        var root = OfficeVisuals.Grid("*", "*,320");
        _surface.Painter = (canvas, width, height) => _renderer.Draw(canvas, width, height, _draft, _state);
        _surface.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(_surface); if (!point.Properties.IsLeftButtonPressed) return;
            var hit = _renderer.HitTest(_draft, _state, (float)point.Position.X, (float)point.Position.Y);
            _state.SelectedTable = hit.Table;
            if (hit.Header && hit.Table is not null) { _dragged = _draft.Table(hit.Table); _start = point.Position; _x = _dragged.DiagramX; _y = _dragged.DiagramY; _surface.CapturePointer(e.Pointer); }
            _surface.Invalidate(); e.Handled = true;
        };
        _surface.PointerMoved += (_, e) =>
        {
            if (_dragged is null) return;
            var point = e.GetCurrentPoint(_surface).Position;
            _dragged.DiagramX = Math.Clamp(_x + point.X - _start.X, 0, 2100); _dragged.DiagramY = Math.Clamp(_y + point.Y - _start.Y, 0, 1300);
            HasPendingChanges = true; _surface.Invalidate();
        };
        _surface.PointerReleased += (_, e) => { _dragged = null; _surface.ReleasePointerCapture(e.Pointer); };
        _surface.PointerCaptureLost += (_, _) => _dragged = null;
        OfficeVisuals.Add(root, EditorVisuals.Scroll(_surface));
        var panel = new StackPanel { Spacing = 8, Margin = new(14) }; panel.Children.Add(OfficeVisuals.Text("Edit Relationships", 17, bold: true));
        var parent = OfficeVisuals.Combo(_draft.Tables.Select(t => t.Name)); var child = OfficeVisuals.Combo(_draft.Tables.Select(t => t.Name));
        var parentField = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; var childField = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Fields(ComboBox table, ComboBox field)
        {
            field.Items.Clear(); if (table.SelectedItem is string name) foreach (var item in _draft.Table(name).Fields) field.Items.Add(item.Name);
            if (field.Items.Count > 0) field.SelectedIndex = 0;
        }
        Fields(parent, parentField); Fields(child, childField);
        parent.SelectionChanged += (_, _) => Fields(parent, parentField); child.SelectionChanged += (_, _) => Fields(child, childField);
        EditorVisuals.Labeled(panel, "Primary table", parent); EditorVisuals.Labeled(panel, "Primary field", parentField);
        EditorVisuals.Labeled(panel, "Related table", child); EditorVisuals.Labeled(panel, "Related field", childField);
        var enforce = true; var cascadeUpdate = false; var cascadeDelete = false;
        panel.Children.Add(EditorVisuals.Check("Enforce Referential Integrity", true, value => enforce = value));
        panel.Children.Add(EditorVisuals.Check("Cascade Update Related Fields", false, value => cascadeUpdate = value));
        panel.Children.Add(EditorVisuals.Check("Cascade Delete Related Records", false, value => cascadeDelete = value));
        panel.Children.Add(OfficeVisuals.Button("Create Relationship", () =>
        {
            RelationshipDefinition? relation = null;
            try
            {
                if (parent.SelectedItem is not string p || child.SelectedItem is not string c || parentField.SelectedItem is not string pf || childField.SelectedItem is not string cf) throw new DataSpaceException("Select both tables and fields.");
                if (Names.Equal(p, c) && Names.Equal(pf, cf)) throw new DataSpaceException("A field cannot relate to itself.");
                relation = new() { Name = Names.Available("Relationship", _draft.Relationships.Select(r => r.Name)), ParentTable = p, ParentField = pf, ChildTable = c, ChildField = cf, EnforceIntegrity = enforce, CascadeUpdate = cascadeUpdate, CascadeDelete = cascadeDelete };
                _draft.Relationships.Add(relation); SchemaValidator.Validate(_draft);
                HasPendingChanges = true; BuildList(); _surface.Invalidate();
            }
            catch (Exception error) { if (relation is not null) _draft.Relationships.Remove(relation); Error?.Invoke(error.Message); }
        }, "relationships"));
        panel.Children.Add(OfficeVisuals.Text("Existing relationships", 14, bold: true)); panel.Children.Add(_list);
        OfficeVisuals.Add(root, OfficeVisuals.Border(EditorVisuals.Scroll(panel), "F7F7F7"), column: 1); Content = root; BuildList();
    }
    private void BuildList()
    {
        _list.Children.Clear();
        foreach (var relation in _draft.Relationships)
        {
            var text = OfficeVisuals.Text(relation.ParentTable + "." + relation.ParentField + " → " + relation.ChildTable + "." + relation.ChildField, 11); text.TextWrapping = TextWrapping.Wrap;
            var remove = OfficeVisuals.Button("Remove", () => { _draft.Relationships.Remove(relation); HasPendingChanges = true; BuildList(); _surface.Invalidate(); });
            _list.Children.Add(OfficeVisuals.Border(OfficeVisuals.Stack(text, remove), padding: new(8)));
        }
    }
    public void Commit()
    {
        if (!HasPendingChanges) return;
        var snapshot = DocumentCodec.Clone(_draft);
        _workspace.Edit("Edit relationships", document =>
        {
            foreach (var table in snapshot.Tables) { var target = document.Table(table.Name); target.DiagramX = table.DiagramX; target.DiagramY = table.DiagramY; }
            document.Relationships = snapshot.Relationships;
        }, _revision);
        _revision = _workspace.Document.Revision; HasPendingChanges = false; _draft = DocumentCodec.Clone(_workspace.Document); BuildList(); _surface.Invalidate();
    }
    public void Dispose() => _renderer.Dispose();
}
