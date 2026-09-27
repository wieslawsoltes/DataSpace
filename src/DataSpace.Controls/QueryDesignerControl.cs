using DataSpace.Query;
using SkiaSharp;

namespace DataSpace.Controls;

/// <summary>Reusable SELECT workspace: draggable sources, joins, properties and an editable QBE grid.</summary>
public sealed class QueryDesignerControl : UserControl, IDisposable
{
    private readonly DatabaseDocument _document;
    private readonly Canvas _cards = new() { Width = 2200, Height = 900 };
    private readonly SkiaSurface _lines = new() { Width = 2200, Height = 900, IsHitTestVisible = false };
    private readonly DrawingResources _drawing = new();
    private readonly Grid _scene = new() { Background = OfficeVisuals.Brush("E9E9E9") };
    private string _topText;
    private string _baseline = "";
    private readonly StackPanel _properties = new() { Spacing = 5, Margin = new(10) };
    private readonly QueryDesignGrid _grid;
    private readonly TextBlock _message = OfficeVisuals.Text("Double-click a field to add it; drag table headers to arrange sources.", 11, "666666");
    private QueryDesignSource? _selected;
    private (string Alias, string Field)? _joinStart;
    private bool _disposed, _invalidTop;
    public QueryDesign Design { get; }
    public bool HasPendingChanges => Fingerprint() != _baseline;
    public event Action? Changed;
    public QueryDesignerControl(DatabaseDocument document, QueryDesign design)
    {
        _document = document; Design = design; _topText = design.Top?.ToString() ?? "";
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = OfficeVisuals.Grid("Auto,*,Auto,Auto", "*,270");
        var tables = OfficeVisuals.Combo(document.Tables.Select(t => t.Name), width: 190);
        var toolbar = OfficeVisuals.Row(tables,
            OfficeVisuals.Button("Add Table", () => { if (tables.SelectedItem is string name) { _selected = Design.AddTable(document, name); ChangedDesign(); BuildCards(); BuildProperties(); } }, "table"),
            OfficeVisuals.Button("Add Column", () => { Design.Columns.Add(new() { Expression = "1" }); _grid.Rebuild(); ChangedDesign(); }, "new"),
            OfficeVisuals.Button("Add OR Row", () => _grid.AddCriteriaRow()), OfficeVisuals.Button("Totals", () => _grid.ToggleTotals(), "totals"));
        toolbar.Margin = new(8); OfficeVisuals.Add(root, EditorVisuals.Scroll(toolbar), columnSpan: 2);
        _scene.Children.Add(_lines); _scene.Children.Add(_cards); _lines.Painter = DrawJoins;
        OfficeVisuals.Add(root, EditorVisuals.Scroll(_scene), 1); OfficeVisuals.Add(root, OfficeVisuals.Border(EditorVisuals.Scroll(_properties), "F7F7F7"), 1, 1);
        _grid = new QueryDesignGrid(design); _grid.Changed += ChangedDesign;
        var gridScroll = EditorVisuals.Scroll(_grid); gridScroll.MaxHeight = 350;
        OfficeVisuals.Add(root, OfficeVisuals.Border(gridScroll, "FFFFFF"), 2, columnSpan: 2);
        _message.Margin = new(8, 5, 8, 5); _message.TextWrapping = TextWrapping.Wrap; OfficeVisuals.Add(root, _message, 3, columnSpan: 2);
        Content = root; BuildCards(); BuildProperties(); MarkCommitted(); AutomationProperties.SetAutomationId(this, "QueryDesigner");
    }
    private string Fingerprint() => FieldValues.Key(new[]
    {
        _topText, Design.Top?.ToString(), Design.Offset.ToString(), Design.Distinct.ToString(), Design.Where, Design.Having,
        FieldValues.Key(Design.Sources.Select(s => FieldValues.Key(new[] { s.Table, s.Alias, s.Join.ToString(), s.Condition, s.X.ToString("R", FieldValues.Culture), s.Y.ToString("R", FieldValues.Culture) }))),
        FieldValues.Key(Design.Columns.Select(c => FieldValues.Key(new[] { c.Expression, c.Alias, c.Show.ToString(), c.Total.ToString(), c.Sort.ToString(), c.SortPriority.ToString(), FieldValues.Key(c.Criteria) })))
    });
    public string ToSql()
    {
        if (_invalidTop) throw new DataSpaceException("Top Values must be blank or a non-negative integer."); return Design.ToSql();
    }
    public void MarkCommitted() => _baseline = Fingerprint();
    private void ChangedDesign() { Changed?.Invoke(); _lines.Invalidate(); }
    private void BuildCards()
    {
        _cards.Children.Clear(); ResizeScene();
        foreach (var source in Design.Sources)
        {
            var content = new StackPanel();
            var card = OfficeVisuals.Border(content, "FFFFFF", _selected == source ? "A4373A" : "7F8990"); card.Width = 220;
            var header = OfficeVisuals.Border(OfficeVisuals.Text(source.Alias + (source.Alias != source.Table ? " : " + source.Table : ""), 12, "FFFFFF", true), "727E86", thickness: new(0), padding: new(8));
            Point origin = default; double x = 0, y = 0; var dragging = false;
            header.PointerPressed += (_, e) =>
            {
                var point = e.GetCurrentPoint(_cards); if (!point.Properties.IsLeftButtonPressed) return;
                _selected = source; BuildProperties(); origin = point.Position; x = source.X; y = source.Y; dragging = true; header.CapturePointer(e.Pointer); e.Handled = true;
            };
            header.PointerMoved += (_, e) =>
            {
                if (!dragging) return; var point = e.GetCurrentPoint(_cards).Position;
                source.X = Math.Clamp(x + point.X - origin.X, 0, 9780); source.Y = Math.Clamp(y + point.Y - origin.Y, 0, 9750);
                Canvas.SetLeft(card, source.X); Canvas.SetTop(card, source.Y); ResizeScene(); ChangedDesign(); e.Handled = true;
            };
            header.PointerReleased += (_, e) => { dragging = false; header.ReleasePointerCapture(e.Pointer); }; header.PointerCaptureLost += (_, _) => dragging = false;
            content.Children.Add(header); var list = new StackPanel();
            var table = _document.Tables.FirstOrDefault(t => Names.Equal(t.Name, source.Table));
            foreach (var field in new[] { "*" }.Concat(table?.Fields.Select(f => f.Name) ?? []))
            {
                var button = OfficeVisuals.Button(field, () => SelectJoinField(source, field));
                button.Height = 27; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
                AutomationProperties.SetName(button, source.Alias + "." + field);
                button.DoubleTapped += (_, e) => { _joinStart = null; Design.AddField(source.Alias, field); _grid.Rebuild(); ChangedDesign(); e.Handled = true; }; list.Children.Add(button);
            }
            var fields = EditorVisuals.Scroll(list); fields.MaxHeight = 194; content.Children.Add(fields);
            Canvas.SetLeft(card, source.X); Canvas.SetTop(card, source.Y); _cards.Children.Add(card);
        }
        _lines.Invalidate();
    }
    private void ResizeScene()
    {
        _scene.Width = _cards.Width = _lines.Width = Math.Max(1200, Design.Sources.Select(s => s.X + 320).DefaultIfEmpty(0).Max());
        _scene.Height = _cards.Height = _lines.Height = Math.Max(600, Design.Sources.Select(s => s.Y + 300).DefaultIfEmpty(0).Max());
    }
    private void SelectJoinField(QueryDesignSource source, string field)
    {
        _selected = source; BuildProperties(); if (field == "*") { _joinStart = null; return; }
        if (_joinStart is { } start && !Names.Equal(start.Alias, source.Alias))
        {
            var previous = Design.Sources.FindIndex(s => Names.Equal(s.Alias, start.Alias)); var current = Design.Sources.IndexOf(source);
            var later = Design.Sources[Math.Max(previous, current)];
            var condition = QueryDesign.Field(start.Alias, start.Field) + " = " + QueryDesign.Field(source.Alias, field);
            if (later.Join == QueryJoinKind.Cross) { later.Join = QueryJoinKind.Inner; later.Condition = condition; } else later.Condition = "(" + later.Condition + ") AND (" + condition + ")";
            _joinStart = null; ChangedDesign(); BuildProperties(); _message.Text = "Join added. Select a source header to edit its type or ON expression.";
        }
        else { _joinStart = (source.Alias, field); _message.Text = "Select a field in another source to join with " + source.Alias + "." + field + "; double-click to add a result column."; }
    }
    private void DrawJoins(SKCanvas canvas, float width, float height)
    {
        for (var i = 1; i < Design.Sources.Count; i++)
        {
            var source = Design.Sources[i]; if (source.Join == QueryJoinKind.Cross) continue;
            var earlier = Design.Sources.Take(i).LastOrDefault(s => source.Condition.Contains(Names.Quote(s.Alias) + ".", StringComparison.OrdinalIgnoreCase)) ?? Design.Sources[i - 1];
            var x1 = (float)(earlier.X + 220); var y1 = (float)(earlier.Y + 48); var x2 = (float)source.X; var y2 = (float)(source.Y + 48);
            var color = SKColor.Parse("586E7C"); var middle = (x1 + x2) / 2;
            _drawing.Line(canvas, x1, y1, middle, y1, color, 2); _drawing.Line(canvas, middle, y1, middle, y2, color, 2); _drawing.Line(canvas, middle, y2, x2, y2, color, 2);
            if (source.Join == QueryJoinKind.Left) { _drawing.Line(canvas, x2 - 8, y2 - 4, x2, y2, color, 2); _drawing.Line(canvas, x2 - 8, y2 + 4, x2, y2, color, 2); }
        }
    }
    private void BuildProperties()
    {
        _properties.Children.Clear(); _properties.Children.Add(OfficeVisuals.Text("Query Properties", 16, bold: true));
        _properties.Children.Add(EditorVisuals.Check("Unique Values (DISTINCT)", Design.Distinct, value => { Design.Distinct = value; ChangedDesign(); }));
        var top = OfficeVisuals.Input(_topText, "All records");
        top.TextChanged += (_, _) =>
        {
            if (_topText == top.Text) return;
            _topText = top.Text; _invalidTop = false;
            if (top.Text.Length == 0) Design.Top = null;
            else if (int.TryParse(top.Text, out var value) && value >= 0) Design.Top = value;
            else { _invalidTop = true; _message.Text = "Top Values must be blank or a non-negative integer."; } ChangedDesign();
        };
        EditorVisuals.Labeled(_properties, "Top Values", top);
        void Text(string label, string value, Action<string> setter)
        {
            var previous = value; var input = EditorVisuals.Multiline(value); input.MaxHeight = 110;
            input.TextChanged += (_, _) => { if (input.Text == previous) return; previous = input.Text; setter(previous); ChangedDesign(); }; EditorVisuals.Labeled(_properties, label, input);
        }
        Text("WHERE (before grouping)", Design.Where, value => Design.Where = value); Text("HAVING (after grouping)", Design.Having, value => Design.Having = value);
        var help = OfficeVisuals.Text("Imported WHERE/HAVING expressions are preserved here. Grid criteria are additional conditions.", 11, "666666"); help.TextWrapping = TextWrapping.Wrap; _properties.Children.Add(help);
        if (_selected is not { } source) return; _properties.Children.Add(OfficeVisuals.Text("Source: " + source.Alias, 14, bold: true));
        if (Design.Sources.IndexOf(source) > 0)
        {
            var join = OfficeVisuals.Combo(Enum.GetNames<QueryJoinKind>(), source.Join.ToString());
            join.SelectionChanged += (_, _) => { if (join.SelectedItem is string value && value != source.Join.ToString()) { source.Join = Enum.Parse<QueryJoinKind>(value); ChangedDesign(); } };
            EditorVisuals.Labeled(_properties, "Join Type", join); Text("ON expression", source.Condition, value => source.Condition = value);
        }
        _properties.Children.Add(OfficeVisuals.Button("Remove Source", () =>
        {
            Design.Sources.Remove(source); _selected = null; _joinStart = null; ChangedDesign(); BuildCards(); BuildProperties();
            _message.Text = "Source removed. Review any remaining field and join references before running.";
        }, "delete"));
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lines.Painter = null; _drawing.Dispose(); }
}

/// <summary>Standalone native QBE grid: field expressions, aliases, grouping, sorting and OR rows.</summary>
public sealed class QueryDesignGrid : UserControl
{
    private readonly QueryDesign _design;
    private readonly Grid _cells = new();
    private bool _totals;
    private int _criteriaRows;
    public event Action? Changed;
    public QueryDesignGrid(QueryDesign design)
    {
        _design = design; _totals = design.Columns.Any(c => c.Total != QueryTotal.None);
        _criteriaRows = Math.Max(2, design.Columns.Select(c => c.Criteria.Count).DefaultIfEmpty(2).Max()); Content = _cells; Rebuild();
    }
    public void AddCriteriaRow()
    { if (_criteriaRows >= 32) return; _criteriaRows++; foreach (var column in _design.Columns) while (column.Criteria.Count < _criteriaRows) column.Criteria.Add(""); Rebuild(); Changed?.Invoke(); }
    public void ToggleTotals() { _totals = !_totals; Rebuild(); }
    public void Rebuild()
    {
        _cells.Children.Clear(); _cells.RowDefinitions.Clear(); _cells.ColumnDefinitions.Clear();
        var labels = new List<string> { "", "Field:", "Alias:", "Sort:", "Sort Order:", "Show:" }; if (_totals) labels.Add("Total:");
        labels.Add("Criteria:"); labels.AddRange(Enumerable.Repeat("or:", _criteriaRows - 1));
        foreach (var label in labels) _cells.RowDefinitions.Add(new() { Height = new(32) }); _cells.ColumnDefinitions.Add(new() { Width = new(90) });
        for (var row = 0; row < labels.Count; row++)
        { var text = OfficeVisuals.Text(labels[row], 12, bold: true); text.Margin = new(8, 0, 0, 0); OfficeVisuals.Add(_cells, OfficeVisuals.Border(text, "F1F1F1", thickness: new(0, 0, 1, 1)), row); }
        for (var index = 0; index < _design.Columns.Count; index++)
        {
            _cells.ColumnDefinitions.Add(new() { Width = new(225) }); var column = _design.Columns[index]; var cellColumn = index + 1;
            void Place(FrameworkElement element, int row, string label)
            {
                AutomationProperties.SetName(element, label + " column " + cellColumn); element.HorizontalAlignment = HorizontalAlignment.Stretch;
                OfficeVisuals.Add(_cells, OfficeVisuals.Border(element, thickness: new(0, 0, 1, 1)), row, cellColumn);
            }
            void Text(string value, int row, string label, Action<string> assign)
            {
                var previous = value; var input = OfficeVisuals.Input(value);
                input.TextChanged += (_, _) => { if (input.Text == previous) return; previous = input.Text; assign(previous); Changed?.Invoke(); }; Place(input, row, label);
            }
            var actions = OfficeVisuals.Row(OfficeVisuals.Text("Column " + cellColumn, 11), OfficeVisuals.Button("←", () => Move(column, -1)), OfficeVisuals.Button("→", () => Move(column, 1)), OfficeVisuals.Button("×", () => { _design.Columns.Remove(column); Rebuild(); Changed?.Invoke(); })); Place(actions, 0, "Column actions");
            Text(column.Expression, 1, "Field expression", value => column.Expression = value); Text(column.Alias, 2, "Output alias", value => column.Alias = value);
            var sort = OfficeVisuals.Combo(Enum.GetNames<QuerySort>(), column.Sort.ToString());
            sort.SelectionChanged += (_, _) => { if (sort.SelectedItem is string value && value != column.Sort.ToString()) { column.Sort = Enum.Parse<QuerySort>(value); Changed?.Invoke(); } }; Place(sort, 3, "Sort");
            var priority = new NumberBox { Value = column.SortPriority, Minimum = 0, Maximum = 256, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            priority.ValueChanged += (_, _) => { if (double.IsFinite(priority.Value) && column.SortPriority != (int)priority.Value) { column.SortPriority = (int)priority.Value; Changed?.Invoke(); } }; Place(priority, 4, "Sort order (zero uses column order)");
            Place(EditorVisuals.Check("", column.Show, value => { column.Show = value; Changed?.Invoke(); }), 5, "Show"); var first = 6;
            if (_totals)
            {
                var total = OfficeVisuals.Combo(Enum.GetNames<QueryTotal>(), column.Total.ToString());
                total.SelectionChanged += (_, _) => { if (total.SelectedItem is string value && value != column.Total.ToString()) { column.Total = Enum.Parse<QueryTotal>(value); Changed?.Invoke(); } }; Place(total, first++, "Total");
            }
            while (column.Criteria.Count < _criteriaRows) column.Criteria.Add("");
            for (var criteria = 0; criteria < _criteriaRows; criteria++)
            { var row = criteria; Text(column.Criteria[row], first + row, row == 0 ? "Criteria" : "Or " + row, value => column.Criteria[row] = value); }
        }
    }
    private void Move(QueryDesignColumn column, int delta)
    {
        var old = _design.Columns.IndexOf(column); var next = old + delta; if (next < 0 || next >= _design.Columns.Count) return;
        _design.Columns.RemoveAt(old); _design.Columns.Insert(next, column); Rebuild(); Changed?.Invoke();
    }
}
