using DataSpace.Query;
using DataSpace.Storage;

namespace DataSpace.Controls;

/// <summary>Reusable Access-style workspace. File dialogs and optimistic persistence are supplied by the host.</summary>
public sealed partial class DatabaseWorkspaceView : UserControl, IDisposable
{
    private readonly OfficeRibbon _ribbon = new();
    private readonly NavigationPane _navigation = new();
    private readonly DocumentTabStrip _tabs = new();
    private readonly RecordNavigator _navigator = new();
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _title = OfficeVisuals.Text("DataSpace", 13, "FFFFFF");
    private readonly TextBlock _status = OfficeVisuals.Text("Ready", 11, "555555");
    private readonly TextBlock _error = OfficeVisuals.Text("", 12, "9C252A");
    private readonly Border _errorBar;
    private readonly Border _backstage = new() { Visibility = Visibility.Collapsed };
    private readonly Grid _body = OfficeVisuals.Grid("*", "220,*");
    private readonly List<DatabaseObjectItem> _documents = [];
    private readonly Button _expandNavigation;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private string _pendingSearch = "";
    private DatabaseObjectItem? _active;
    private DatasheetControl? _sheet;
    private IDatabaseEditor? _editor;
    private bool _design, _busy, _descending;
    private string _filter = "", _search = "";
    private string? _sortField;
    public DatabaseWorkspace Workspace { get; }
    public Func<Task>? SaveDatabaseAsync { get; set; }
    public Func<string, Task<string?>>? ImportTextAsync { get; set; }
    public Func<string, string, byte[], Task>? ExportFileAsync { get; set; }
    public Func<bool>? StorageIsDirty { get; set; }
    public string? ActiveObjectKey => _active?.Key;
    public bool HasPendingChanges => _editor?.HasPendingChanges == true;

    public DatabaseWorkspaceView(DatabaseWorkspace workspace)
    {
        Workspace = workspace;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = OfficeVisuals.Grid("36,Auto,Auto,*,25"); root.Background = OfficeVisuals.Brush("FFFFFF");
        var titlebar = OfficeVisuals.Grid("*", "180,*,190"); titlebar.Background = OfficeVisuals.Brush("A4373A");
        OfficeVisuals.Add(titlebar, OfficeVisuals.Row(OfficeVisuals.Text("  DataSpace", 14, "FFFFFF", true), Quick("▣", "Save", "save"), Quick("↶", "Undo", "undo"), Quick("↷", "Redo", "redo")));
        _title.HorizontalAlignment = HorizontalAlignment.Center; OfficeVisuals.Add(titlebar, _title, column: 1);
        var edition = OfficeVisuals.Text("Local database workspace  ", 11, "FFFFFF"); edition.HorizontalAlignment = HorizontalAlignment.Right; OfficeVisuals.Add(titlebar, edition, column: 2);
        OfficeVisuals.Add(root, titlebar); _ribbon.SetTabs(OfficeCommandCatalog.Create()); _ribbon.CommandInvoked += Execute; OfficeVisuals.Add(root, _ribbon, 1);
        _error.TextWrapping = TextWrapping.Wrap; _error.Margin = new(10, 7, 10, 7);
        _errorBar = OfficeVisuals.Border(_error, "FFF1DF", "E9CDA0", new(0, 0, 0, 1)); _errorBar.Visibility = Visibility.Collapsed; OfficeVisuals.Add(root, _errorBar, 2);
        var documents = OfficeVisuals.Grid("33,*,Auto"); OfficeVisuals.Add(documents, _tabs); OfficeVisuals.Add(documents, _content, 1); OfficeVisuals.Add(documents, _navigator, 2);
        OfficeVisuals.Add(_body, _navigation); OfficeVisuals.Add(_body, documents, column: 1);
        _expandNavigation = OfficeVisuals.Button("»", ToggleNavigation); _expandNavigation.Visibility = Visibility.Collapsed; _expandNavigation.VerticalAlignment = VerticalAlignment.Top; OfficeVisuals.Add(_body, _expandNavigation);
        OfficeVisuals.Add(root, _body, 3); _status.Margin = new(10, 0, 0, 0); OfficeVisuals.Add(root, OfficeVisuals.Border(_status, "F3F3F3", thickness: new(0, 1, 0, 0)), 4);
        OfficeVisuals.Add(root, _backstage, rowSpan: 5); Content = root;
        _navigation.CollapseRequested += ToggleNavigation;
        _navigation.ObjectOpened += (item, design) => Try(() => OpenObject(item, design));
        _navigation.ObjectCommand += (item, command) => _ = GuardAsync(() => ObjectCommandAsync(item, command));
        _tabs.Selected += item => Try(() => OpenObject(item)); _tabs.Closed += item => Try(() => CloseObject(item));
        _navigator.Navigate += index => _sheet?.SelectCell(index, _sheet.ViewState.SelectedColumn);
        _navigator.NewRecord += () => Execute("newRecord");
        _navigator.SearchChanged += text => { _pendingSearch = text; _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Try(() => { CommitActive(); _search = _pendingSearch; RefreshTable(); }); };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.F6) { _navigation.FocusSearch(); e.Handled = true; return; }
            if (!OfficeVisuals.ControlDown) return;
            var command = e.Key switch { VirtualKey.S => "save", VirtualKey.Z => "undo", VirtualKey.Y => "redo", VirtualKey.F => "find", VirtualKey.O => "open", VirtualKey.F1 => "collapseRibbon", _ => null };
            if (command is not null) { Execute(command); e.Handled = true; }
        };
        Workspace.Changed += OnChanged; UpdateChrome();
        if (Workspace.Document.Tables.FirstOrDefault() is { } first) OpenObject(new(DatabaseObjectKind.Table, first.Name)); else EmptyView();
        AutomationProperties.SetAutomationId(this, "DataSpaceWorkspace");
    }
    private Button Quick(string glyph, string name, string command)
    {
        var button = OfficeVisuals.Button(glyph, () => Execute(command)); button.Foreground = OfficeVisuals.Brush("FFFFFF"); button.BorderThickness = new(0); button.Padding = new(4); button.MinWidth = 24;
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); return button;
    }
    private void ToggleNavigation()
    {
        var collapse = _navigation.Visibility == Visibility.Visible;
        _navigation.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible; _expandNavigation.Visibility = collapse ? Visibility.Visible : Visibility.Collapsed;
        _body.ColumnDefinitions[0].Width = new(collapse ? 28 : 220);
    }
    private void OnChanged(object? sender, EventArgs e)
    {
        UpdateChrome();
        if (_sheet is not null && _active is not null && Workspace.Document.Tables.Any(t => Names.Equal(t.Name, _active.Name))) Try(RefreshTable);
    }
    public void UpdateChrome()
    {
        _title.Text = Workspace.Document.Name + " : Database — DataSpace" + (StorageIsDirty?.Invoke() == true ? " *" : "");
        _navigation.SetObjects(Workspace.Document); _navigation.Select(_active); _tabs.SetDocuments(_documents, _active?.Key);
        _ribbon.SetCommandEnabled("undo", Workspace.CanUndo); _ribbon.SetCommandEnabled("redo", Workspace.CanRedo); _ribbon.SetCommandEnabled("exportPdf", _editor is ReportPreviewControl);
    }
    public void ShowStatus(string message) { _status.Text = message; UpdateChrome(); }
    public void ShowError(string message) { _error.Text = message; _errorBar.Visibility = Visibility.Visible; _status.Text = "Operation not completed"; }
    private void Try(Action action) { try { action(); } catch (Exception error) { ShowError(error.Message); } }
    private async Task GuardAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; _errorBar.Visibility = Visibility.Collapsed;
        try { await action(); } catch (Exception error) { ShowError(error.Message); }
        finally { _busy = false; UpdateChrome(); }
    }
    public void CommitActive()
    {
        if (_sheet?.FinishEdit(false) == false) throw new DataSpaceException("Correct the cell value or press Escape before continuing.");
        _editor?.Commit();
    }
    public void OpenObject(DatabaseObjectItem item, bool design = false)
    {
        CommitActive(); if (_active?.Key == item.Key && _design == design) return;
        FrameworkElement view = item.Kind switch
        {
            DatabaseObjectKind.Table when design => new TableDesignerControl(Workspace, item.Name),
            DatabaseObjectKind.Table => CreateDatasheet(item.Name),
            DatabaseObjectKind.Query => new QueryEditorControl(Workspace, item.Name, design),
            DatabaseObjectKind.Form => new FormEditorControl(Workspace, item.Name, design),
            DatabaseObjectKind.Report => new ReportPreviewControl(Workspace, item.Name, design),
            DatabaseObjectKind.Relationships => new RelationshipDesignerControl(Workspace),
            DatabaseObjectKind.Macro => new MacroDesignerControl(Workspace, item.Name),
            _ => throw new DataSpaceException("Unknown database object.")
        };
        DisposeActive(); _active = item; _design = design; _editor = view as IDatabaseEditor; _sheet = view as DatasheetControl;
        _filter = ""; _search = ""; _pendingSearch = ""; _sortField = null; _descending = false;
        if (view is QueryEditorControl query) { query.ConfirmActionAsync = ConfirmAsync; query.Error += ShowError; }
        if (view is FormEditorControl form) form.Error += ShowError;
        if (view is ReportPreviewControl report) report.Error += ShowError;
        if (view is RelationshipDesignerControl relationships) relationships.Error += ShowError;
        if (view is MacroDesignerControl macro) macro.RunRequested += () => _ = GuardAsync(() => RunMacroAsync(item.Name));
        if (!_documents.Any(d => d.Key == item.Key)) _documents.Add(item);
        _content.Content = view; _navigator.Visibility = _sheet is null ? Visibility.Collapsed : Visibility.Visible;
        if (_sheet is not null) RefreshTable();
        _status.Text = item.Name + (design ? " — Design View" : " — " + item.Kind + " View"); UpdateChrome();
    }
    private DatasheetControl CreateDatasheet(string tableName)
    {
        Workspace.Document.Table(tableName); var sheet = new DatasheetControl();
        sheet.CommitEdits = edits =>
        {
            try
            {
                Workspace.UpdateRecords("Edit records", tableName, edits.Select(e => new RecordEdit(e.RecordId, e.Field, e.Value)).ToArray()); return true;
            }
            catch (Exception error) { ShowError(error.Message); return false; }
        };
        sheet.NewRecordRequested += () => Execute("newRecord"); sheet.DeleteRecordsRequested += ids => _ = GuardAsync(() => DeleteRecordsAsync(ids));
        sheet.SortRequested += (field, descending) => Try(() => { CommitActive(); _sortField = field; _descending = descending; RefreshTable(); });
        sheet.ColumnWidthChanged += (field, width) => Try(() => Workspace.Edit("Resize column", document => document.Table(tableName).Field(field).Width = width));
        sheet.SelectionChanged += () => _navigator.Update(sheet.ViewState.SelectedRow, sheet.Records.Count, _filter.Length > 0 || _search.Length > 0);
        sheet.Error += ShowError; return sheet;
    }
    private void RefreshTable()
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) return;
        var result = TableView.Open(Workspace.Document, _active.Name, _filter, _sortField, _descending, _search);
        _sheet.ViewState.SortField = _sortField; _sheet.ViewState.SortDescending = _descending;
        _sheet.SetData(result.Fields, result); _navigator.Update(_sheet.ViewState.SelectedRow, result.Count, _filter.Length > 0 || _search.Length > 0);
    }
    private void DisposeActive() { _searchTimer.Stop(); if (_content.Content is IDisposable disposable) disposable.Dispose(); _content.Content = null; _sheet = null; _editor = null; }
    private void EmptyView()
    {
        var panel = OfficeVisuals.Stack(OfficeVisuals.Text("Build your database", 27, "A4373A"), OfficeVisuals.Text("Create a table, import a CSV file, or open a DataSpace database.", 14, "666666"), OfficeVisuals.Button("Create Table", () => Execute("newTable"), "table"));
        panel.Margin = new(45); panel.HorizontalAlignment = HorizontalAlignment.Left; panel.VerticalAlignment = VerticalAlignment.Top; _content.Content = panel; _navigator.Visibility = Visibility.Collapsed;
    }
    private void CloseObject(DatabaseObjectItem item)
    {
        if (_active?.Key == item.Key) { CommitActive(); DisposeActive(); _active = null; }
        _documents.RemoveAll(d => d.Key == item.Key);
        if (_active is null) { if (_documents.LastOrDefault() is { } next) OpenObject(next); else EmptyView(); } UpdateChrome();
    }
    private void ReopenActive()
    {
        var item = _active; var design = _design; DisposeActive(); _active = null;
        _documents.RemoveAll(d => d.Kind != DatabaseObjectKind.Relationships && !Objects().Any(o => o.Key == d.Key));
        if (item is not null && (item.Kind == DatabaseObjectKind.Relationships || Objects().Any(o => o.Key == item.Key))) OpenObject(item, design);
        else { EmptyView(); UpdateChrome(); }
    }
    private IEnumerable<DatabaseObjectItem> Objects() => Workspace.Document.Tables.Select(t => new DatabaseObjectItem(DatabaseObjectKind.Table, t.Name))
        .Concat(Workspace.Document.Queries.Select(q => new DatabaseObjectItem(DatabaseObjectKind.Query, q.Name)))
        .Concat(Workspace.Document.Forms.Select(f => new DatabaseObjectItem(DatabaseObjectKind.Form, f.Name)))
        .Concat(Workspace.Document.Reports.Select(r => new DatabaseObjectItem(DatabaseObjectKind.Report, r.Name)))
        .Concat(Workspace.Document.Macros.Select(m => new DatabaseObjectItem(DatabaseObjectKind.Macro, m.Name)));
    private string SourceTable() => _active?.Kind == DatabaseObjectKind.Table ? _active.Name : _editor is FormEditorControl form ? form.Source : Workspace.Document.Tables.FirstOrDefault()?.Name ?? throw new DataSpaceException("Create a table first.");
    public void ReplaceDocument(DatabaseDocument document)
    {
        var validated = DocumentCodec.Clone(document); DisposeActive(); _active = null; _documents.Clear(); Workspace.Replace(validated);
        if (Workspace.Document.Tables.FirstOrDefault() is { } first) OpenObject(new(DatabaseObjectKind.Table, first.Name)); else EmptyView(); UpdateChrome();
    }
    public void Dispose() { _searchTimer.Stop(); Workspace.Changed -= OnChanged; DisposeActive(); }
}
