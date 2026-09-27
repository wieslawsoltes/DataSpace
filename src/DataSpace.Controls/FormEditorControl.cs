namespace DataSpace.Controls;

/// <summary>Bound record form and drag/resize form designer with a detached property sheet.</summary>
public sealed class FormEditorControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private FormDefinition _form;
    private long _revision;
    private readonly bool _design;
    private readonly FormLayoutRenderer _renderer = new();
    private readonly SkiaSurface _surface = new();
    private readonly StackPanel _properties = new() { Spacing = 6, Margin = new(14) };
    private readonly Grid _root = OfficeVisuals.Grid("Auto,*,30", "*,285");
    private readonly RecordNavigator _navigator = new();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl _recordHost = new();
    private LayoutControl? _selected;
    private string? _recordId;
    private int _recordIndex;
    private bool _dragging;
    private bool _resizing;
    private Point _start;
    private (double X, double Y, double Width, double Height) _original;
    private float _zoom = 1;
    public bool HasPendingChanges { get; private set; }
    public event Action<string>? Error;
    public string Source => _form.Source;
    public FormEditorControl(DatabaseWorkspace workspace, string name, bool design)
    {
        _workspace = workspace; _design = design; _revision = workspace.Document.Revision;
        _form = Copy(workspace.Document.Forms.First(f => Names.Equal(f.Name, name)));
        if (design) BuildDesigner(); else BuildRecordHost();
        Content = _root;
    }
    private static FormDefinition Copy(FormDefinition form) => new()
    {
        Name = form.Name, Source = form.Source, Title = form.Title, Width = form.Width, Height = form.Height,
        Controls = form.Controls.Select(c => new LayoutControl { Id = c.Id, Kind = c.Kind, Field = c.Field, Caption = c.Caption, X = c.X, Y = c.Y, Width = c.Width, Height = c.Height, FontSize = c.FontSize }).ToList()
    };
    private void BuildRecordHost()
    {
        var title = OfficeVisuals.Text("Form View · " + _form.Source, 12, "666666"); title.Margin = new(10);
        OfficeVisuals.Add(_root, title, columnSpan: 2); OfficeVisuals.Add(_root, EditorVisuals.Scroll(_recordHost), 1, columnSpan: 2);
        OfficeVisuals.Add(_root, _navigator, 2, columnSpan: 2);
        _navigator.Navigate += index =>
        {
            try { Commit(); _recordIndex = index; BuildRecord(); }
            catch (Exception error) { Error?.Invoke(error.Message); }
        };
        _navigator.SearchChanged += text =>
        {
            try
            {
                Commit(); var table = _workspace.Document.Table(_form.Source);
                var found = table.Records.FindIndex(row => row.Values.Values.Any(v => v?.Contains(text, StringComparison.OrdinalIgnoreCase) == true));
                if (found >= 0) { _recordIndex = found; BuildRecord(); }
            }
            catch (Exception error) { Error?.Invoke(error.Message); }
        };
        _navigator.NewRecord += () => Error?.Invoke("Use Home > New to enter all required fields before adding a record.");
        BuildRecord();
    }
    private void BuildRecord()
    {
        var table = _workspace.Document.Table(_form.Source);
        _recordIndex = Math.Clamp(_recordIndex, 0, Math.Max(0, table.Records.Count - 1));
        var record = table.Records.ElementAtOrDefault(_recordIndex); _recordId = record?.Id;
        _values.Clear(); _revision = _workspace.Document.Revision;
        _navigator.Update(_recordIndex, table.Records.Count, false);
        var canvas = new Canvas { Width = _form.Width, Height = _form.Height, Background = OfficeVisuals.Brush("FAFAFA") };
        var header = OfficeVisuals.Border(OfficeVisuals.Text(string.IsNullOrWhiteSpace(_form.Title) ? _form.Name : _form.Title, 25, "A4373A"), "EDF1F3", padding: new(34, 14, 0, 14));
        header.Width = _form.Width; header.Height = 66; canvas.Children.Add(header);
        if (record is null)
        {
            var empty = OfficeVisuals.Text("No records. Choose Home > New to add a record.", 14, "666666"); Canvas.SetLeft(empty, 35); Canvas.SetTop(empty, 100); canvas.Children.Add(empty); _recordHost.Content = canvas; return;
        }
        foreach (var layout in _form.Controls)
        {
            FrameworkElement element;
            if (layout.Kind is LayoutControlKind.TextBox or LayoutControlKind.CheckBox)
            {
                var field = table.Field(layout.Field);
                var caption = OfficeVisuals.Text(layout.Caption, 12); Canvas.SetLeft(caption, layout.X); Canvas.SetTop(caption, Math.Max(0, layout.Y - 23)); canvas.Children.Add(caption);
                if (layout.Kind == LayoutControlKind.CheckBox)
                {
                    var input = EditorVisuals.Check("", FieldValues.Parse(field, record[field.Name]) is true, value => { _values[field.Name] = value.ToString(); HasPendingChanges = true; });
                    element = input;
                }
                else
                {
                    var input = OfficeVisuals.Input(record[field.Name] ?? ""); input.IsReadOnly = field.Type == FieldType.AutoNumber; input.FontSize = layout.FontSize;
                    input.TextChanged += (_, _) => { _values[field.Name] = input.Text; HasPendingChanges = true; }; element = input;
                }
                AutomationProperties.SetName(element, layout.Caption);
            }
            else element = OfficeVisuals.Text(layout.Caption, layout.FontSize, bold: layout.Kind == LayoutControlKind.Heading);
            element.Width = layout.Width; element.Height = layout.Height; Canvas.SetLeft(element, layout.X); Canvas.SetTop(element, layout.Y); canvas.Children.Add(element);
        }
        _recordHost.Content = canvas;
    }
    private void BuildDesigner()
    {
        var fields = OfficeVisuals.Combo(_workspace.Document.Table(_form.Source).Fields.Select(f => f.Name), width: 190);
        var toolbar = OfficeVisuals.Row(fields,
            OfficeVisuals.Button("Text Box", () => Add(LayoutControlKind.TextBox, fields.SelectedItem as string)),
            OfficeVisuals.Button("Check Box", () => Add(LayoutControlKind.CheckBox, fields.SelectedItem as string)),
            OfficeVisuals.Button("Label", () => Add(LayoutControlKind.Label, null)),
            OfficeVisuals.Button("Heading", () => Add(LayoutControlKind.Heading, null)),
            OfficeVisuals.Button("Delete", DeleteSelected));
        toolbar.Margin = new(8); OfficeVisuals.Add(_root, EditorVisuals.Scroll(toolbar), columnSpan: 2);
        _surface.Width = _form.Width; _surface.Height = _form.Height; _surface.IsTabStop = true;
        _surface.Painter = (canvas, width, height) => _renderer.Draw(canvas, width, height, _form, _selected?.Id, _zoom);
        _surface.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(_surface); if (!point.Properties.IsLeftButtonPressed) return;
            _surface.Focus(FocusState.Pointer); _start = new(point.Position.X / _zoom, point.Position.Y / _zoom);
            _selected = _renderer.HitTest(_form, (float)_start.X, (float)_start.Y);
            if (_selected is not null)
            {
                _original = (_selected.X, _selected.Y, _selected.Width, _selected.Height);
                _resizing = Math.Abs(_start.X - (_selected.X + _selected.Width)) < 9 && Math.Abs(_start.Y - (_selected.Y + _selected.Height)) < 9;
                _dragging = true; _surface.CapturePointer(e.Pointer);
            }
            BuildProperties(); _surface.Invalidate(); e.Handled = true;
        };
        _surface.PointerMoved += (_, e) =>
        {
            if (!_dragging || _selected is null) return;
            var p = e.GetCurrentPoint(_surface).Position; var dx = p.X / _zoom - _start.X; var dy = p.Y / _zoom - _start.Y;
            static double Snap(double v) => Math.Round(v / 10) * 10;
            if (_resizing) { _selected.Width = Math.Clamp(Snap(_original.Width + dx), 20, 10000); _selected.Height = Math.Clamp(Snap(_original.Height + dy), 20, 10000); }
            else { _selected.X = Math.Clamp(Snap(_original.X + dx), 0, 10000); _selected.Y = Math.Clamp(Snap(_original.Y + dy), 0, 10000); }
            HasPendingChanges = true; _surface.Invalidate(); e.Handled = true;
        };
        _surface.PointerReleased += (_, e) => { _dragging = false; _surface.ReleasePointerCapture(e.Pointer); BuildProperties(); };
        _surface.PointerCaptureLost += (_, _) => _dragging = false;
        _surface.KeyDown += (_, e) => { if (e.Key == VirtualKey.Delete) { DeleteSelected(); e.Handled = true; } };
        OfficeVisuals.Add(_root, EditorVisuals.Scroll(_surface), 1);
        OfficeVisuals.Add(_root, OfficeVisuals.Border(EditorVisuals.Scroll(_properties), "F7F7F7", thickness: new(1, 0, 0, 0)), 1, 1);
        var zoom = new Slider { Minimum = 50, Maximum = 150, Value = 100, Width = 180 };
        zoom.ValueChanged += (_, _) => { _zoom = (float)zoom.Value / 100; _surface.Width = _form.Width * _zoom; _surface.Height = _form.Height * _zoom; _surface.Invalidate(); };
        var footer = OfficeVisuals.Row(OfficeVisuals.Text("Drag controls; drag the lower-right corner to resize. Grid: 10 px.", 11, "666666"), zoom);
        footer.Margin = new(8, 0, 0, 0); OfficeVisuals.Add(_root, footer, 2, columnSpan: 2); BuildProperties();
    }
    private void Add(LayoutControlKind kind, string? fieldName)
    {
        if (kind is LayoutControlKind.TextBox or LayoutControlKind.CheckBox)
        {
            if (fieldName is null) return;
            var field = _workspace.Document.Table(_form.Source).Field(fieldName);
            if (kind == LayoutControlKind.CheckBox && field.Type != FieldType.YesNo) { Error?.Invoke("A Check Box must bind to a YesNo field."); return; }
        }
        _selected = new LayoutControl { Kind = kind, Field = fieldName ?? "", Caption = fieldName ?? kind.ToString(), X = 40, Y = 110 + _form.Controls.Count % 6 * 60, Width = 280, Height = 32 };
        _form.Controls.Add(_selected); HasPendingChanges = true; BuildProperties(); _surface.Invalidate();
    }
    private void DeleteSelected() { if (_selected is null) return; _form.Controls.Remove(_selected); _selected = null; HasPendingChanges = true; BuildProperties(); _surface.Invalidate(); }
    private void BuildProperties()
    {
        _properties.Children.Clear(); _properties.Children.Add(OfficeVisuals.Text("Property Sheet", 17, bold: true));
        var title = OfficeVisuals.Input(_form.Title); title.TextChanged += (_, _) => { _form.Title = title.Text; HasPendingChanges = true; _surface.Invalidate(); };
        EditorVisuals.Labeled(_properties, "Form Caption", title);
        void Number(string label, double value, double minimum, double maximum, Action<double> assign)
        {
            var input = new NumberBox { Value = value, Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            input.ValueChanged += (_, _) => { if (double.IsFinite(input.Value)) { assign(input.Value); HasPendingChanges = true; _surface.Width = _form.Width * _zoom; _surface.Height = _form.Height * _zoom; _surface.Invalidate(); } };
            EditorVisuals.Labeled(_properties, label, input);
        }
        Number("Form Width", _form.Width, 100, 10000, value => _form.Width = value);
        Number("Form Height", _form.Height, 100, 10000, value => _form.Height = value);
        if (_selected is not { } control) return;
        _properties.Children.Add(OfficeVisuals.Text(control.Kind.ToString(), 13, "A4373A", true));
        var caption = OfficeVisuals.Input(control.Caption); caption.TextChanged += (_, _) => { control.Caption = caption.Text; HasPendingChanges = true; _surface.Invalidate(); }; EditorVisuals.Labeled(_properties, "Caption", caption);
        Number("Left", control.X, 0, 10000, value => control.X = value); Number("Top", control.Y, 0, 10000, value => control.Y = value);
        Number("Width", control.Width, 16, 10000, value => control.Width = value); Number("Height", control.Height, 16, 10000, value => control.Height = value);
        Number("Font Size", control.FontSize, 6, 120, value => control.FontSize = value);
    }
    public void Commit()
    {
        if (!HasPendingChanges) return;
        if (_design)
        {
            var snapshot = Copy(_form);
            _workspace.Edit("Edit form layout", document => { var index = document.Forms.FindIndex(f => Names.Equal(f.Name, _form.Name)); if (index < 0) throw new DataSpaceException("The form was removed."); document.Forms[index] = snapshot; }, _revision);
        }
        else if (_recordId is { } id)
        {
            var values = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
            _workspace.Edit("Edit form record", document => RecordOperations.Update(document, _form.Source, id, values), _revision);
        }
        HasPendingChanges = false; _revision = _workspace.Document.Revision;
        if (!_design) BuildRecord();
    }
    public void Dispose() => _renderer.Dispose();
}
