using System.Text;
using DataSpace.Query;
using DataSpace.Storage;

namespace DataSpace.Controls;

/// <summary>A reusable Access-style database workspace. Persistence and file dialogs are injected by the host.</summary>
public sealed class DatabaseWorkspaceView : UserControl, IDisposable
{
    private readonly OfficeRibbon _ribbon = new();
    private readonly NavigationPane _navigation = new();
    private readonly DocumentTabStrip _tabs = new();
    private readonly RecordNavigator _navigator = new();
    private readonly ContentControl _content = new();
    private readonly TextBlock _title = OfficeVisuals.Text("DataSpace", 13, "FFFFFF");
    private readonly TextBlock _status = OfficeVisuals.Text("Ready", 11, "555555");
    private readonly TextBlock _error = OfficeVisuals.Text("", 12, "9C252A");
    private readonly Border _errorBar;
    private readonly Border _backstage = new() { Visibility = Visibility.Collapsed };
    private readonly Grid _body = OfficeVisuals.Grid("*", "220,*");
    private readonly List<DatabaseObjectItem> _documents = [];
    private readonly QueryEngine _engine = new();
    private readonly Button _expandNavigation;
    private DatabaseObjectItem? _active;
    private DatasheetControl? _sheet;
    private IDatabaseEditor? _editor;
    private bool _design;
    private bool _busy;
    private string _filter = "";
    private string _search = "";
    private string? _sortField;
    private bool _descending;
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
        var root = OfficeVisuals.Grid("36,Auto,Auto,*,25"); root.Background = OfficeVisuals.Brush("FFFFFF");
        var titlebar = OfficeVisuals.Grid("*", "180,*,190"); titlebar.Background = OfficeVisuals.Brush("A4373A");
        var quick = OfficeVisuals.Row(OfficeVisuals.Text("  DataSpace", 14, "FFFFFF", true), Quick("▣", "Save", "save"), Quick("↶", "Undo", "undo"), Quick("↷", "Redo", "redo"));
        OfficeVisuals.Add(titlebar, quick); _title.HorizontalAlignment = HorizontalAlignment.Center; OfficeVisuals.Add(titlebar, _title, column: 1);
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
        _navigator.SearchChanged += text => Try(() => { CommitActive(); _search = text; RefreshTable(); });
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.F6) { _navigation.FocusSearch(); e.Handled = true; return; }
            if (!OfficeVisuals.ControlDown) return;
            var command = e.Key switch { VirtualKey.S => "save", VirtualKey.Z => "undo", VirtualKey.Y => "redo", VirtualKey.F => "find", VirtualKey.O => "open", VirtualKey.F1 => "collapseRibbon", _ => null };
            if (command is not null) { Execute(command); e.Handled = true; }
        };
        Workspace.Changed += OnChanged; UpdateChrome();
        var first = Workspace.Document.Tables.FirstOrDefault();
        if (first is not null) OpenObject(new(DatabaseObjectKind.Table, first.Name)); else EmptyView();
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
    private void OnChanged(object? sender, EventArgs e) { UpdateChrome(); if (_sheet is not null) Try(RefreshTable); }
    public void UpdateChrome()
    {
        _title.Text = Workspace.Document.Name + " : Database — DataSpace" + (StorageIsDirty?.Invoke() == true ? " *" : "");
        _navigation.SetObjects(Workspace.Document); _navigation.Select(_active); _tabs.SetDocuments(_documents, _active?.Key);
        _ribbon.SetCommandEnabled("undo", Workspace.CanUndo); _ribbon.SetCommandEnabled("redo", Workspace.CanRedo);
        _ribbon.SetCommandEnabled("exportPdf", _editor is ReportPreviewControl);
    }
    public void ShowStatus(string message) { _status.Text = message; UpdateChrome(); }
    public void ShowError(string message) { _error.Text = message; _errorBar.Visibility = Visibility.Visible; _status.Text = "Operation not completed"; }
    private void ClearError() => _errorBar.Visibility = Visibility.Collapsed;
    private void Try(Action action) { try { action(); } catch (Exception error) { ShowError(error.Message); } }
    private async Task GuardAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; ClearError();
        try { await action(); }
        catch (Exception error) { ShowError(error.Message); }
        finally { _busy = false; UpdateChrome(); }
    }
    public void CommitActive()
    {
        if (_sheet?.FinishEdit(false) == false) throw new DataSpaceException("Correct the cell value or press Escape before continuing.");
        _editor?.Commit();
    }
    public void OpenObject(DatabaseObjectItem item, bool design = false)
    {
        CommitActive();
        if (_active?.Key == item.Key && _design == design) return;
        FrameworkElement view = item.Kind switch
        {
            DatabaseObjectKind.Table when design => new TableDesignerControl(Workspace, item.Name),
            DatabaseObjectKind.Table => CreateDatasheet(item.Name),
            DatabaseObjectKind.Query => new QueryEditorControl(Workspace, item.Name),
            DatabaseObjectKind.Form => new FormEditorControl(Workspace, item.Name, design),
            DatabaseObjectKind.Report => new ReportPreviewControl(Workspace, item.Name, design),
            DatabaseObjectKind.Relationships => new RelationshipDesignerControl(Workspace),
            DatabaseObjectKind.Macro => new MacroDesignerControl(Workspace, item.Name),
            _ => throw new DataSpaceException("Unknown database object.")
        };
        DisposeActive(); _active = item; _design = design; _editor = view as IDatabaseEditor; _sheet = view as DatasheetControl;
        _filter = ""; _search = ""; _sortField = null; _descending = false;
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
        Workspace.Document.Table(tableName);
        var sheet = new DatasheetControl();
        sheet.CommitEdits = edits =>
        {
            try
            {
                Workspace.Edit("Edit records", document =>
                {
                    foreach (var group in edits.GroupBy(e => e.RecordId))
                        RecordOperations.Update(document, tableName, group.Key, group.ToDictionary(e => e.Field, e => e.Value, StringComparer.OrdinalIgnoreCase));
                }); return true;
            }
            catch (Exception error) { ShowError(error.Message); return false; }
        };
        sheet.NewRecordRequested += () => Execute("newRecord");
        sheet.DeleteRecordsRequested += ids => _ = GuardAsync(() => DeleteRecordsAsync(ids));
        sheet.SortRequested += (field, descending) => Try(() => { CommitActive(); _sortField = field; _descending = descending; RefreshTable(); });
        sheet.ColumnWidthChanged += (field, width) => Try(() => Workspace.Edit("Resize column", document => document.Table(tableName).Field(field).Width = width));
        sheet.SelectionChanged += () => _navigator.Update(sheet.ViewState.SelectedRow, sheet.Records.Count, _filter.Length > 0 || _search.Length > 0);
        sheet.Error += ShowError; return sheet;
    }
    private void RefreshTable()
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) return;
        var table = Workspace.Document.Table(_active.Name);
        var sql = "SELECT * FROM " + Names.Quote(table.Name);
        if (!string.IsNullOrWhiteSpace(_filter)) sql += " WHERE " + _filter;
        if (_sortField is not null) sql += " ORDER BY " + Names.Quote(table.Field(_sortField).Name) + (_descending ? " DESC" : " ASC");
        var result = _engine.Select(Workspace.Document, sql + ";");
        var records = string.IsNullOrWhiteSpace(_search) ? result.Records : result.Records.Where(r => r.Values.Values.Any(v => v?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        _sheet.ViewState.SortField = _sortField; _sheet.ViewState.SortDescending = _descending;
        // Datasheet column-drag previews never mutate the live document.
        _sheet.SetData(table.Fields.Select(TableSchemaDraft.Copy).ToArray(), records);
        _navigator.Update(_sheet.ViewState.SelectedRow, records.Count, _filter.Length > 0 || _search.Length > 0);
    }
    private void DisposeActive() { if (_content.Content is IDisposable disposable) disposable.Dispose(); _content.Content = null; _sheet = null; _editor = null; }
    private void EmptyView()
    {
        var panel = OfficeVisuals.Stack(OfficeVisuals.Text("Build your database", 27, "A4373A"), OfficeVisuals.Text("Create a table, import a CSV file, or open a DataSpace database.", 14, "666666"), OfficeVisuals.Button("Create Table", () => Execute("newTable"), "table"));
        panel.Margin = new(45); panel.HorizontalAlignment = HorizontalAlignment.Left; panel.VerticalAlignment = VerticalAlignment.Top; _content.Content = panel; _navigator.Visibility = Visibility.Collapsed;
    }
    private void CloseObject(DatabaseObjectItem item)
    {
        if (_active?.Key == item.Key) { CommitActive(); DisposeActive(); _active = null; }
        _documents.RemoveAll(d => d.Key == item.Key);
        if (_active is null) { if (_documents.LastOrDefault() is { } next) OpenObject(next); else EmptyView(); }
        UpdateChrome();
    }
    private void ReopenActive()
    {
        var item = _active; var design = _design; DisposeActive(); _active = null;
        if (item is not null && (item.Kind == DatabaseObjectKind.Relationships || Objects().Any(o => o.Key == item.Key))) OpenObject(item, design);
        else { _documents.RemoveAll(d => !Objects().Any(o => o.Key == d.Key)); EmptyView(); UpdateChrome(); }
    }
    private IEnumerable<DatabaseObjectItem> Objects() => Workspace.Document.Tables.Select(t => new DatabaseObjectItem(DatabaseObjectKind.Table, t.Name))
        .Concat(Workspace.Document.Queries.Select(q => new DatabaseObjectItem(DatabaseObjectKind.Query, q.Name)))
        .Concat(Workspace.Document.Forms.Select(f => new DatabaseObjectItem(DatabaseObjectKind.Form, f.Name)))
        .Concat(Workspace.Document.Reports.Select(r => new DatabaseObjectItem(DatabaseObjectKind.Report, r.Name)))
        .Concat(Workspace.Document.Macros.Select(m => new DatabaseObjectItem(DatabaseObjectKind.Macro, m.Name)));
    private string SourceTable() => _active?.Kind == DatabaseObjectKind.Table ? _active.Name : _editor is FormEditorControl form ? form.Source : Workspace.Document.Tables.FirstOrDefault()?.Name ?? throw new DataSpaceException("Create a table first.");
    private async void Execute(string id) => await GuardAsync(() => ExecuteCoreAsync(id));
    private async Task ExecuteCoreAsync(string id)
    {
        if (id == "collapseRibbon") { _ribbon.ToggleCollapsed(); return; }
        if (id == "find") { _navigator.FocusSearch(); return; }
        CommitActive();
        switch (id)
        {
            case "file": ShowBackstage(); break;
            case "save": if (SaveDatabaseAsync is null) throw new DataSpaceException("No storage adapter is configured."); await SaveDatabaseAsync(); ShowStatus("Database saved"); break;
            case "open": await OpenDatabaseAsync(); break;
            case "newDatabase": await NewDatabaseAsync(); break;
            case "view": if (_active is not null) OpenObject(_active, !_design); break;
            case "copy": if (_sheet is not null) await _sheet.CopyAsync(); break;
            case "paste": if (_sheet is not null) await _sheet.PasteAsync(); break;
            case "selectAll": _sheet?.SelectAll(); break;
            case "totals": _sheet?.ToggleTotals(); break;
            case "ascending": case "descending": if (_sheet?.SelectedField is { } field) { _sortField = field.Name; _descending = id == "descending"; RefreshTable(); } break;
            case "filter": if (_sheet is null) throw new DataSpaceException("Open a table datasheet first."); var filter = await PromptAsync("Filter records", "WHERE expression", _filter); if (filter is not null) SetFilter(filter); break;
            case "clearFilter": SetFilter(""); break;
            case "refresh": ReopenActive(); break;
            case "newRecord": await NewRecordAsync(); break;
            case "deleteRecord": if (_sheet is null) throw new DataSpaceException("Select records in a datasheet first."); await DeleteRecordsAsync(_sheet.SelectedRecordIds()); break;
            case "undo": Workspace.Undo(); ReopenActive(); break;
            case "redo": Workspace.Redo(); ReopenActive(); break;
            case "newTable": case "tableDesign": CreateObject(DatabaseObjectKind.Table, id == "tableDesign"); break;
            case "newQuery": CreateObject(DatabaseObjectKind.Query); break;
            case "newForm": CreateObject(DatabaseObjectKind.Form); break;
            case "newReport": CreateObject(DatabaseObjectKind.Report); break;
            case "newMacro": CreateObject(DatabaseObjectKind.Macro); break;
            case "relationships": OpenObject(new(DatabaseObjectKind.Relationships, "Relationships"), true); break;
            case "importCsv": await ImportCsvAsync(); break;
            case "exportCsv": await ExportCsvAsync(); break;
            case "exportDatabase": await ExportAsync(Workspace.Document.Name + ".dspace", "application/json", Encoding.UTF8.GetBytes(DocumentCodec.Serialize(Workspace.Document))); break;
            case "exportPdf": if (_editor is not ReportPreviewControl report) throw new DataSpaceException("Open a report first."); await ExportAsync((_active?.Name ?? "Report") + ".pdf", "application/pdf", report.ExportPdf()); break;
            case "validate": SchemaValidator.Validate(DocumentCodec.Clone(Workspace.Document)); ShowStatus("Database schema, field values, unique indexes and relationships are valid."); break;
            case "about": await MessageAsync("DataSpace", "Independent Access-style database workspace built with Uno Platform and SkiaSharp. Includes typed tables, managed SQL, forms, reports, relationships and explicit macros. DataSpace files are JSON; ACCDB/MDB, ACE/Jet, VBA and complete Microsoft Access parity are not implemented. Not affiliated with Microsoft."); break;
            case "shortcuts": await MessageAsync("Keyboard shortcuts", "Ctrl+S: Save\nCtrl+O: Open\nCtrl+Z / Ctrl+Y: Undo / Redo\nCtrl+F: Find records\nCtrl+F1: Collapse ribbon\nF6: Search objects\nDatasheet: arrows, Page Up/Down, Home/End, F2 or double-click to edit; Escape cancels editing; Ctrl+C / Ctrl+V copy and paste ranges."); break;
        }
    }
    private void SetFilter(string filter)
    {
        var old = _filter; _filter = filter;
        try { RefreshTable(); } catch { _filter = old; throw; }
    }
    private void CreateObject(DatabaseObjectKind kind, bool design = false)
    {
        var source = kind is DatabaseObjectKind.Table or DatabaseObjectKind.Macro ? Workspace.Document.Tables.FirstOrDefault()?.Name : SourceTable();
        string name = "";
        Workspace.Edit("Create " + kind, document => name = kind switch
        {
            DatabaseObjectKind.Table => ObjectFactory.CreateTable(document).Name,
            DatabaseObjectKind.Query => ObjectFactory.CreateQuery(document, source!).Name,
            DatabaseObjectKind.Form => ObjectFactory.CreateForm(document, source!).Name,
            DatabaseObjectKind.Report => ObjectFactory.CreateReport(document, source!).Name,
            DatabaseObjectKind.Macro => ObjectFactory.CreateMacro(document, source).Name,
            _ => throw new DataSpaceException("Unknown object type.")
        });
        OpenObject(new(kind, name), design);
    }
    private async Task NewRecordAsync()
    {
        var tableName = SourceTable(); var table = Workspace.Document.Table(tableName);
        var inputs = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        var panel = new StackPanel { Spacing = 8 }; var error = OfficeVisuals.Text("", 12, "9C252A"); error.TextWrapping = TextWrapping.Wrap; panel.Children.Add(error);
        foreach (var field in table.Fields.Where(f => f.Type is not FieldType.AutoNumber and not FieldType.Guid))
        {
            var input = OfficeVisuals.Input(field.DefaultValue ?? ""); inputs.Add(field.Name, input); EditorVisuals.Labeled(panel, field.DisplayName + (field.Required ? " *" : ""), input);
        }
        var scroll = EditorVisuals.Scroll(panel); scroll.MaxHeight = 520; scroll.MinWidth = 360;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "New record — " + tableName, Content = scroll, PrimaryButtonText = "Add record", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        string? recordId = null;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                var values = inputs.ToDictionary(p => p.Key, p => string.IsNullOrEmpty(p.Value.Text) ? null : p.Value.Text);
                Workspace.Edit("Add record", document => recordId = RecordOperations.Insert(document.Table(tableName), values).Id);
            }
            catch (Exception exception) { e.Cancel = true; error.Text = exception.Message; }
        };
        await dialog.ShowAsync();
        if (recordId is not null) { ReopenActive(); if (_sheet is not null) _sheet.SelectCell(_sheet.Records.ToList().FindIndex(r => r.Id == recordId), 0); }
    }
    private async Task DeleteRecordsAsync(IReadOnlyList<string> ids)
    {
        CommitActive(); if (ids.Count == 0) return;
        var table = SourceTable(); if (!await ConfirmAsync($"Delete {ids.Count:N0} selected record(s) from {table}? Enforced cascade-delete relationships may also remove related records. This operation can be undone.")) return;
        Workspace.Edit("Delete records", document => RecordOperations.Delete(document, table, ids));
    }
    private async Task ImportCsvAsync()
    {
        if (ImportTextAsync is null) throw new DataSpaceException("No file adapter is configured.");
        var text = await ImportTextAsync("csv"); if (text is null) return;
        var name = await PromptAsync("Import CSV", "Table name", Names.Available("Imported", Workspace.Document.Tables.Select(t => t.Name))); if (name is null) return;
        var table = CsvCodec.Import(name, text); Workspace.Edit("Import CSV", document => document.Tables.Add(table)); OpenObject(new(DatabaseObjectKind.Table, name));
    }
    private async Task ExportCsvAsync()
    {
        if (_sheet is not null) await ExportAsync((_active?.Name ?? "Table") + ".csv", "text/csv;charset=utf-8", Encoding.UTF8.GetBytes(CsvCodec.Export(_sheet.Fields, _sheet.Records)));
        else if (_editor is QueryEditorControl { LastResult: { IsAction: false } result }) await ExportAsync((_active?.Name ?? "Query") + ".csv", "text/csv;charset=utf-8", Encoding.UTF8.GetBytes(CsvCodec.Export(result.Fields, result.Records)));
        else throw new DataSpaceException("Open a table or run a SELECT query before exporting CSV.");
    }
    private Task ExportAsync(string name, string type, byte[] bytes) => ExportFileAsync?.Invoke(name, type, bytes) ?? throw new DataSpaceException("No export adapter is configured.");
    private async Task OpenDatabaseAsync()
    {
        if (ImportTextAsync is null) throw new DataSpaceException("No file adapter is configured.");
        var text = await ImportTextAsync("database"); if (text is null) return;
        var document = DocumentCodec.Deserialize(text);
        if ((StorageIsDirty?.Invoke() == true || HasPendingChanges) && !await ConfirmAsync("Replace the current database? Export or save your current changes before continuing.")) return;
        ReplaceDocument(document); ShowStatus("Opened " + document.Name + ". Save to keep it in this workspace.");
    }
    private async Task NewDatabaseAsync()
    {
        if (StorageIsDirty?.Invoke() == true && !await ConfirmAsync("Create a new database and replace the current workspace? Export or save changes first.")) return;
        var name = await PromptAsync("Blank database", "Database name", "Database1"); if (name is null) return; Names.Validate(name);
        var document = new DatabaseDocument { Name = name }; ObjectFactory.CreateTable(document); ReplaceDocument(document);
    }
    public void ReplaceDocument(DatabaseDocument document)
    {
        var validated = DocumentCodec.Clone(document); DisposeActive(); _active = null; _documents.Clear(); Workspace.Replace(validated);
        if (Workspace.Document.Tables.FirstOrDefault() is { } first) OpenObject(new(DatabaseObjectKind.Table, first.Name)); else EmptyView(); UpdateChrome();
    }
    private async Task ObjectCommandAsync(DatabaseObjectItem item, string command)
    {
        CommitActive();
        if (command == "Delete")
        {
            if (!await ConfirmAsync("Delete " + item.Kind + " '" + item.Name + "'? This operation can be undone.")) return;
            Workspace.Edit("Delete " + item.Kind, document =>
            {
                if (document.Macros.Any(m => m.Steps.Any(s => s.Action == MacroActionKind.OpenObject && (Names.Equal(s.Argument, item.Key) || Names.Equal(s.Argument, item.Name))))) throw new DataSpaceException("A macro references this object. Remove the dependency first.");
                switch (item.Kind)
                {
                    case DatabaseObjectKind.Table: if (document.Queries.Count > 0) throw new DataSpaceException("Review and remove saved SQL dependencies before deleting a table."); document.Tables.RemoveAll(t => Names.Equal(t.Name, item.Name)); break;
                    case DatabaseObjectKind.Query: document.Queries.RemoveAll(q => Names.Equal(q.Name, item.Name)); break;
                    case DatabaseObjectKind.Form: document.Forms.RemoveAll(f => Names.Equal(f.Name, item.Name)); break;
                    case DatabaseObjectKind.Report: document.Reports.RemoveAll(r => Names.Equal(r.Name, item.Name)); break;
                    case DatabaseObjectKind.Macro: document.Macros.RemoveAll(m => Names.Equal(m.Name, item.Name)); break;
                }
            });
            if (_active?.Key == item.Key) { DisposeActive(); _active = null; }
            _documents.RemoveAll(d => d.Key == item.Key); if (_active is null) { if (_documents.LastOrDefault() is { } next) OpenObject(next); else EmptyView(); }
        }
        else if (command == "Rename")
        {
            var name = await PromptAsync("Rename " + item.Kind, "Name", item.Name); if (name is null || name == item.Name) return; Names.Validate(name);
            Workspace.Edit("Rename " + item.Kind, document =>
            {
                switch (item.Kind)
                {
                    case DatabaseObjectKind.Table:
                        if (document.Queries.Count > 0) throw new DataSpaceException("Review and remove saved SQL dependencies before renaming a table.");
                        document.Table(item.Name).Name = name;
                        foreach (var relation in document.Relationships) { if (Names.Equal(relation.ParentTable, item.Name)) relation.ParentTable = name; if (Names.Equal(relation.ChildTable, item.Name)) relation.ChildTable = name; }
                        foreach (var form in document.Forms.Where(f => Names.Equal(f.Source, item.Name))) form.Source = name;
                        foreach (var report in document.Reports.Where(r => Names.Equal(r.Source, item.Name))) report.Source = name;
                        break;
                    case DatabaseObjectKind.Query: document.Queries.First(q => Names.Equal(q.Name, item.Name)).Name = name; foreach (var report in document.Reports.Where(r => Names.Equal(r.Source, item.Name))) report.Source = name; break;
                    case DatabaseObjectKind.Form: document.Forms.First(f => Names.Equal(f.Name, item.Name)).Name = name; break;
                    case DatabaseObjectKind.Report: document.Reports.First(r => Names.Equal(r.Name, item.Name)).Name = name; break;
                    case DatabaseObjectKind.Macro: document.Macros.First(m => Names.Equal(m.Name, item.Name)).Name = name; break;
                }
                foreach (var step in document.Macros.SelectMany(m => m.Steps).Where(s => s.Action == MacroActionKind.OpenObject))
                    if (Names.Equal(step.Argument, item.Key) || Names.Equal(step.Argument, item.Name)) step.Argument = item.Kind + ":" + name;
            });
            var replacement = new DatabaseObjectItem(item.Kind, name);
            var index = _documents.FindIndex(d => d.Key == item.Key); if (index >= 0) _documents[index] = replacement;
            if (_active?.Key == item.Key) { var design = _design; DisposeActive(); _active = null; OpenObject(replacement, design); }
        }
        UpdateChrome();
    }
    private async Task RunMacroAsync(string name)
    {
        CommitActive();
        var steps = Workspace.Document.Macros.First(m => Names.Equal(m.Name, name)).Steps.Select(s => new MacroStep { Action = s.Action, Argument = s.Argument }).ToArray();
        if (!await ConfirmAsync($"Run macro '{name}' with {steps.Length} action(s)?")) return;
        foreach (var step in steps)
        {
            switch (step.Action)
            {
                case MacroActionKind.OpenObject:
                    var matches = Objects().Where(o => Names.Equal(o.Key, step.Argument) || Names.Equal(o.Name, step.Argument)).ToArray();
                    if (matches.Length != 1) throw new DataSpaceException("Macro object is missing or ambiguous: " + step.Argument);
                    OpenObject(matches[0]); break;
                case MacroActionKind.ApplyFilter: if (_sheet is null) throw new DataSpaceException("ApplyFilter requires an open table datasheet."); SetFilter(step.Argument); break;
                case MacroActionKind.ClearFilter: SetFilter(""); break;
                case MacroActionKind.GoToFirstRecord: _sheet?.SelectCell(0, 0); break;
                case MacroActionKind.GoToLastRecord: if (_sheet is not null) _sheet.SelectCell(_sheet.Records.Count - 1, 0); break;
                case MacroActionKind.SaveDatabase: if (SaveDatabaseAsync is null) throw new DataSpaceException("No storage adapter is configured."); await SaveDatabaseAsync(); break;
                default: throw new DataSpaceException("Unsupported macro action.");
            }
        }
        ShowStatus("Macro completed: " + name);
    }
    private void ShowBackstage()
    {
        var root = OfficeVisuals.Grid("*", "210,*"); var menu = new StackPanel { Spacing = 8, Margin = new(18, 22, 18, 22) }; root.Background = OfficeVisuals.Brush("FFFFFF");
        void Item(string label, string command)
        {
            var button = OfficeVisuals.Button(label, () => { _backstage.Visibility = Visibility.Collapsed; Execute(command); }); button.Foreground = OfficeVisuals.Brush("FFFFFF"); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.FontSize = 18; button.Height = 48; menu.Children.Add(button);
        }
        var back = OfficeVisuals.Button("←  Back", () => _backstage.Visibility = Visibility.Collapsed); back.Foreground = OfficeVisuals.Brush("FFFFFF"); back.FontSize = 22; back.Margin = new(0, 0, 0, 18); menu.Children.Add(back);
        Item("New", "newDatabase"); Item("Open", "open"); Item("Save", "save"); Item("Export Database", "exportDatabase"); Item("About", "about");
        OfficeVisuals.Add(root, OfficeVisuals.Border(menu, "A4373A", thickness: new(0)));
        var info = OfficeVisuals.Stack(OfficeVisuals.Text("Database Information", 30, "A4373A"), OfficeVisuals.Text(Workspace.Document.Name, 23), OfficeVisuals.Text($"{Workspace.Document.Tables.Count} tables · {Workspace.Document.Queries.Count} queries · {Workspace.Document.Forms.Count} forms · {Workspace.Document.Reports.Count} reports", 14, "666666"), OfficeVisuals.Text("DataSpace format (.dspace)\nLocal browser or desktop storage. Export a separate file for backup.\nNative Microsoft Access database files are not supported.", 14, "666666"));
        info.Margin = new(48); info.VerticalAlignment = VerticalAlignment.Top; OfficeVisuals.Add(root, info, column: 1); _backstage.Child = root; _backstage.Visibility = Visibility.Visible;
    }
    public async Task<bool> ConfirmAsync(string message)
    {
        var content = OfficeVisuals.Text(message, 14); content.TextWrapping = TextWrapping.Wrap;
        return await new ContentDialog { XamlRoot = XamlRoot, Title = "DataSpace", Content = content, PrimaryButtonText = "Continue", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task<string?> PromptAsync(string title, string label, string value)
    {
        var input = OfficeVisuals.Input(value); input.MinWidth = 360; AutomationProperties.SetName(input, label);
        var result = await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = OfficeVisuals.Stack(OfficeVisuals.Text(label), input), PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary }.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task MessageAsync(string title, string message)
    {
        var content = OfficeVisuals.Text(message, 14); content.TextWrapping = TextWrapping.Wrap;
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "Close" }.ShowAsync();
    }
    public void Dispose() { Workspace.Changed -= OnChanged; DisposeActive(); }
}
