using DataSpace.Query;

namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
    private string? _ribbonContext;
    private readonly Dictionary<string, bool> _documentModes = new(StringComparer.Ordinal);
    private double _navigationWidth = 220;
    private void UpdateContextRibbon()
    {
        var context = _active is null ? "none" : _active.Kind + ":" + _design;
        if (_ribbonContext != context)
        {
            _ribbonContext = context;
            var tabs = OfficeCommandCatalog.Create().ToList(); var tab = OfficeContextCatalog.Create(_active?.Kind, _design);
            if (tab is not null) tabs.Add(tab); _ribbon.SetTabs(tabs);
        }
        foreach (var command in new[] { "copy", "paste", "selectAll", "totals", "ascending", "descending", "filter", "clearFilter", "find", "replace", "columns", "formatDatasheet", "appendRecords", "exportJson", "exportSqlite" })
            _ribbon.SetCommandEnabled(command, _sheet is not null);
        _ribbon.SetCommandEnabled("view", _active is not null);
        _ribbon.SetCommandEnabled("closeObject", _active is not null);
        _ribbon.SetCommandEnabled("closeAll", _documents.Count > 0);
    }
    private void ActivateDocument(DatabaseObjectItem item) => OpenObject(item, _documentModes.GetValueOrDefault(item.Key));
    private void CycleDocument(bool backwards)
    {
        CommitActive(); if (_documents.Count == 0) return;
        var at = _documents.FindIndex(item => item.Key == _active?.Key);
        ActivateDocument(_documents[(at + (backwards ? -1 : 1) + _documents.Count) % _documents.Count]);
    }
    private void CloseDocumentsExcept(DatabaseObjectItem? keep)
    {
        CommitActive();
        if (keep is not null && !_documents.Any(item => item.Key == keep.Key)) return;
        // Do not instantiate intermediate editors while closing a batch. Opening a
        // report/query can be expensive or fail, and is not part of a close command.
        var retainActive = keep is not null && _active?.Key == keep.Key;
        if (!retainActive) { DisposeActive(); _active = null; }
        _documents.RemoveAll(item => item.Key != keep?.Key);
        foreach (var key in _documentModes.Keys.Where(key => key != keep?.Key).ToArray()) _documentModes.Remove(key);
        if (keep is not null && !retainActive) ActivateDocument(keep);
        else if (keep is null) EmptyView();
        UpdateChrome();
    }
    private void AddNavigationSplitter()
    {
        var splitter = new Border { Width = 5, Background = OfficeVisuals.Brush("00000000"), HorizontalAlignment = HorizontalAlignment.Right };
        double origin = 0, originalWidth = 0; var dragging = false;
        splitter.PointerPressed += (_, e) =>
        {
            if (_navigation.Visibility != Visibility.Visible || !e.GetCurrentPoint(_body).Properties.IsLeftButtonPressed) return;
            origin = e.GetCurrentPoint(_body).Position.X; originalWidth = _navigationWidth; dragging = splitter.CapturePointer(e.Pointer); e.Handled = true;
        };
        splitter.PointerMoved += (_, e) => { if (dragging) { _navigationWidth = Math.Clamp(originalWidth + e.GetCurrentPoint(_body).Position.X - origin, 160, 480); _body.ColumnDefinitions[0].Width = new(_navigationWidth); e.Handled = true; } };
        splitter.PointerReleased += (_, e) => { dragging = false; splitter.ReleasePointerCapture(e.Pointer); };
        splitter.PointerCaptureLost += (_, _) => dragging = false;
        OfficeVisuals.Add(_body, splitter);
    }
    private async Task ExecuteContextAsync(string id)
    {
        if (id == "designView" || id == "datasheetView") { if (_active is not null) OpenObject(_active, id == "designView"); return; }
        if (_editor is TableDesignerControl table)
        {
            if (id == "propertySheet") table.ToggleProperties(); else await table.ExecuteRibbonAsync(id);
        }
        else if (_editor is QueryEditorControl query)
        {
            switch (id)
            {
                case "queryDesignView": query.SwitchView(QueryEditorView.Design); break;
                case "querySqlView": query.SwitchView(QueryEditorView.Sql); break;
                case "queryResults": query.SwitchView(QueryEditorView.Datasheet); break;
                case "queryRun": await query.RunAsync(); break;
                case "queryCrosstab": await query.ShowCrosstabBuilderAsync(); break;
                case "queryDuplicates": await query.ShowFindBuilderAsync(FindQueryKind.Duplicates); break;
                case "queryUnmatched": await query.ShowFindBuilderAsync(FindQueryKind.Unmatched); break;
            }
        }
        else if (_editor is FormEditorControl form)
        {
            if (id == "formTabOrder") await form.EditTabOrderAsync(); else form.ExecuteRibbon(id);
        }
        else if (_editor is ReportPreviewControl report) report.ExecuteRibbon(id);
    }
}
