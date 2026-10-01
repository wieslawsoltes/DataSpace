using DataSpace.Query;

namespace DataSpace.Controls;

/// <summary>Paginated Skia preview and report property editor. Vector export uses the same page renderer.</summary>
public sealed class ReportPreviewControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private readonly ReportRenderer _renderer = new();
    private readonly QueryEngine _engine = new();
    private readonly ReportDefinition _report;
    private readonly SkiaSurface _surface = new();
    private readonly TextBlock _pageLabel = OfficeVisuals.Text("Page 1", 12);
    private readonly StackPanel _fieldList = new() { Spacing = 0 };
    private QueryResult _data = new();
    private long _revision;
    private int _page;
    private float _zoom = 1;
    private readonly Slider _zoomSlider = new() { Width = 180, Minimum = 50, Maximum = 180, Value = 100 };
    private readonly DateTime _generatedAt = DateTime.Now;
    public bool HasPendingChanges { get; private set; }
    public event Action<string>? Error;
    public ReportPreviewControl(DatabaseWorkspace workspace, string name, bool design)
    {
        _workspace = workspace; _revision = workspace.Document.Revision;
        var report = workspace.Document.Reports.First(r => Names.Equal(r.Name, name));
        _report = new() { Name = report.Name, Source = report.Source, Title = report.Title, Fields = report.Fields.ToList(), Landscape = report.Landscape, ShowTotals = report.ShowTotals };
        var root = OfficeVisuals.Grid("Auto,*", design ? "*,285" : "*");
        var toolbar = OfficeVisuals.Row(OfficeVisuals.Button("Previous Page", () => Navigate(-1)), _pageLabel, OfficeVisuals.Button("Next Page", () => Navigate(1)));
        var zoom = _zoomSlider;
        zoom.ValueChanged += (_, _) => { _zoom = (float)zoom.Value / 100; Invalidate(); }; toolbar.Children.Add(zoom); toolbar.Margin = new(10);
        OfficeVisuals.Add(root, toolbar, columnSpan: design ? 2 : 1);
        _surface.Margin = new(24); _surface.Painter = (canvas, _, _) => { canvas.Save(); canvas.Scale(_zoom); _renderer.DrawPage(canvas, _report, _data.Fields, _data.Records, _page, _generatedAt); canvas.Restore(); };
        var preview = EditorVisuals.Scroll(_surface); preview.Background = OfficeVisuals.Brush("D5D9DD"); OfficeVisuals.Add(root, preview, 1);
        if (design)
        {
            var properties = new StackPanel { Spacing = 8, Margin = new(14) }; properties.Children.Add(OfficeVisuals.Text("Report Properties", 17, bold: true));
            var title = OfficeVisuals.Input(_report.Title); title.TextChanged += (_, _) => { _report.Title = title.Text; HasPendingChanges = true; Invalidate(); }; EditorVisuals.Labeled(properties, "Title", title);
            var source = OfficeVisuals.Combo(workspace.Document.Tables.Select(t => t.Name).Concat(workspace.Document.Queries.Select(q => q.Name)), _report.Source);
            source.SelectionChanged += (_, _) =>
            {
                if (source.SelectedItem is not string value) return;
                var old = _report.Source; var fields = _report.Fields.ToList();
                try { _report.Source = value; _report.Fields.Clear(); LoadData(); HasPendingChanges = true; BuildFields(); Invalidate(); }
                catch (Exception error) { _report.Source = old; _report.Fields = fields; Error?.Invoke(error.Message); }
            };
            EditorVisuals.Labeled(properties, "Record Source", source);
            properties.Children.Add(EditorVisuals.Check("Landscape", _report.Landscape, value => { _report.Landscape = value; HasPendingChanges = true; Invalidate(); }));
            properties.Children.Add(OfficeVisuals.Text("Visible fields (none selected means all)", 11, "666666")); properties.Children.Add(_fieldList);
            OfficeVisuals.Add(root, OfficeVisuals.Border(EditorVisuals.Scroll(properties), "F7F7F7"), 1, 1);
        }
        Content = root; LoadData(); BuildFields(); Invalidate();
    }
    private void LoadData()
    {
        var table = _workspace.Document.Tables.FirstOrDefault(t => Names.Equal(t.Name, _report.Source));
        if (table is not null) _data = QueryResult.FromTable(table);
        else
        {
            var query = _workspace.Document.Queries.First(q => Names.Equal(q.Name, _report.Source));
            _data = _engine.Select(_workspace.Document, query.Sql, query.Parameters.ToDictionary(p => p.Key, p => (object?)p.Value));
        }
        foreach (var field in _report.Fields)
            if (!_data.Fields.Any(f => Names.Equal(f.Name, field))) throw new DataSpaceException("The report references a missing field: " + field);
        _page = 0;
    }
    private void BuildFields()
    {
        _fieldList.Children.Clear();
        foreach (var field in _data.Fields)
            _fieldList.Children.Add(EditorVisuals.Check(field.DisplayName, _report.Fields.Any(f => Names.Equal(f, field.Name)), value =>
            {
                _report.Fields.RemoveAll(f => Names.Equal(f, field.Name)); if (value) _report.Fields.Add(field.Name);
                HasPendingChanges = true; Invalidate();
            }));
    }
    private void Navigate(int delta) { _page = Math.Clamp(_page + delta, 0, ReportPageLayout.For(_report).PageCount(_data.Records.Count) - 1); Invalidate(); }
    private void Invalidate()
    {
        var layout = ReportPageLayout.For(_report); _page = Math.Clamp(_page, 0, layout.PageCount(_data.Records.Count) - 1);
        _surface.Width = layout.Width * _zoom; _surface.Height = layout.Height * _zoom;
        _pageLabel.Text = $"Page {_page + 1} of {layout.PageCount(_data.Records.Count)} · {_data.Records.Count:N0} records"; _surface.Invalidate();
    }
    public byte[] ExportPdf() { Commit(); return _renderer.ExportPdf(_report, _data.Fields, _data.Records, _generatedAt); }
    public void ExecuteRibbon(string command)
    {
        switch (command)
        {
            case "reportPrevious": Navigate(-1); break;
            case "reportNext": Navigate(1); break;
            case "reportPortrait": _report.Landscape = false; HasPendingChanges = true; Invalidate(); break;
            case "reportLandscape": _report.Landscape = true; HasPendingChanges = true; Invalidate(); break;
            case "reportZoomOut": _zoomSlider.Value = Math.Max(50, _zoomSlider.Value - 10); break;
            case "reportZoomIn": _zoomSlider.Value = Math.Min(180, _zoomSlider.Value + 10); break;
        }
    }
    public void Commit()
    {
        if (!HasPendingChanges) return;
        _workspace.Edit("Edit report", document =>
        {
            var report = document.Reports.First(r => Names.Equal(r.Name, _report.Name)); report.Source = _report.Source; report.Title = _report.Title;
            report.Fields = _report.Fields.ToList(); report.Landscape = _report.Landscape;
        }, _revision);
        HasPendingChanges = false; _revision = _workspace.Document.Revision;
    }
    public void Dispose() => _renderer.Dispose();
}
