using DataSpace.DataSources;
namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
    public Func<Task<IDataSource?>>? OpenSqliteAsync { get; set; }
    public Func<string, FieldDefinition[], Record[], CancellationToken, Task<byte[]>>? ExportSqliteTableAsync { get; set; }
    private async Task ExternalDataAsync(string provider)
    {
        await using var editor = new ExternalDataControl(ImportTextAsync, OpenSqliteAsync, provider);
        editor.NameAvailable = name => !Workspace.Document.Tables.Any(t => Names.Equal(t.Name, name));
        editor.Width = Math.Max(660, Math.Min(1080, XamlRoot.Size.Width - 100)); editor.Height = Math.Max(380, Math.Min(660, XamlRoot.Size.Height - 180));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "External Data", Content = editor, CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 1200d;
        editor.ImportCompleted += () => dialog.Hide();
        await dialog.ShowAsync();
        if (editor.ImportedTable is { } table)
        {
            Workspace.Edit("Import external table", document => document.Tables.Add(table));
            OpenObject(new(DatabaseObjectKind.Table, table.Name)); ShowStatus($"Imported {table.Records.Count:N0} records into {table.Name}. This is a local copy; save to retain it.");
        }
    }
    private async Task ExportSourceAsync(bool sqlite)
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) throw new DataSpaceException("Open a local table datasheet to export.");
        var fields = _sheet.Fields.ToArray();
        if (_sheet.Records.Count > SourceLimits.MaxImportRows) throw new DataSpaceException("Export is limited to 100,000 records.");
        var rows = _sheet.Records.ToArray(); var name = _active.Name;
        if (sqlite)
        {
            if (ExportSqliteTableAsync is null) throw new DataSpaceException("SQLite export is not configured by the host.");
            var bytes = await ExportSqliteTableAsync(name, fields, rows, CancellationToken.None);
            await ExportAsync(name + ".sqlite", "application/vnd.sqlite3", bytes);
        }
        else await ExportAsync(name + ".json", "application/json", SourceImport.ExportJson(fields, rows));
        ShowStatus("Exported current table view as a separate " + (sqlite ? "SQLite" : "JSON") + " file.");
    }
}
