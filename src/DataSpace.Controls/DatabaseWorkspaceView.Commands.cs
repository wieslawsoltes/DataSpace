using System.Text;
using DataSpace.Query;
using DataSpace.Storage;

namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
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
        var old = _filter; _filter = filter; try { RefreshTable(); } catch { _filter = old; throw; }
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
                case MacroActionKind.GoToFirstRecord: if (_sheet is null) throw new DataSpaceException("Record navigation requires an open datasheet."); _sheet.SelectCell(0, 0); break;
                case MacroActionKind.GoToLastRecord: if (_sheet is null) throw new DataSpaceException("Record navigation requires an open datasheet."); _sheet.SelectCell(_sheet.Records.Count - 1, 0); break;
                case MacroActionKind.SaveDatabase: if (SaveDatabaseAsync is null) throw new DataSpaceException("No storage adapter is configured."); await SaveDatabaseAsync(); break;
                default: throw new DataSpaceException("Unsupported macro action.");
            }
        }
        ShowStatus("Macro completed: " + name);
    }
}
