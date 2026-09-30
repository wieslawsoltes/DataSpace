namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
    private async Task AppendCurrentViewAsync()
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) throw new DataSpaceException("Open a local table datasheet first. Import external data to a local copy before appending it.");
        var snapshot = Workspace.Document;
        var sourceRows = _sheet.Records; // The view retains its original document; self-appends cannot grow enumeration.
        var sourceNames = _sheet.Fields.Select(field => field.Name).ToArray();
        if (sourceRows.Count == 0) throw new DataSpaceException("The current view has no records to append.");
        using var editor = new AppendTableControl(snapshot.Tables, _sheet.Fields, _active.Name, sourceRows.Count);
        var error = new ValidationMessageControl();
        var panel = OfficeVisuals.Stack(editor, error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Append Records", Content = panel,
            PrimaryButtonText = "Append records", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        string? destination = null; var count = 0;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                // Revision alone cannot detect a replaced database whose revision happens to match.
                if (!ReferenceEquals(snapshot, Workspace.Document)) throw new DataSpaceException("The database changed. Reopen the append dialog before continuing.");
                var mapping = editor.CaptureMapping();
                var names = mapping.SourceOrdinals.Select(index => sourceNames[index]).ToArray();
                IEnumerable<IReadOnlyList<string?>> Rows()
                {
                    foreach (var row in sourceRows) yield return names.Select(name => row[name]).ToArray();
                }
                destination = editor.DestinationTable;
                count = Workspace.AppendRecords("Append records", destination, mapping.Columns, Rows(), snapshot.Revision);
            }
            catch (Exception exception) { e.Cancel = true; destination = null; error.Text = exception.Message; }
        };
        await dialog.ShowAsync();
        if (destination is not null)
        {
            OpenObject(new(DatabaseObjectKind.Table, destination));
            ShowStatus($"Appended {count:N0} records into {destination}. Save to retain the changes; Undo reverts the complete append.");
        }
    }
}
