namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
    private async Task ConfigureDatasheetAsync(string command)
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) throw new DataSpaceException("Open a table datasheet first.");
        var snapshot = Workspace.Document; var name = _active.Name; var table = snapshot.Table(name); var layout = table.Datasheet.Copy();
        var selected = _sheet.SelectedField?.Name;
        if (command == "hideFields")
        {
            layout = DatasheetFieldCommands.Hide(table.Fields, layout, _sheet.SelectedFieldNames());
        }
        else if (command == "freezeFields")
        {
            layout = DatasheetFieldCommands.Freeze(table.Fields, layout, _sheet.SelectedFieldNames());
        }
        else if (command == "unfreezeFields")
        {
            layout.ColumnOrder = layout.VisibleFields(table.Fields).Select(f => f.Name).Concat(layout.ColumnOrder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            layout.FrozenFields.Clear();
        }
        else if (command == "columnWidth")
        {
            if (selected is null) return;
            var value = await PromptAsync("Column Width", "Column width", table.Field(selected).Width.ToString(FieldValues.Culture));
            if (value is null) return;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, FieldValues.Culture, out var width)) throw new DataSpaceException("Enter a numeric column width.");
            if (!ReferenceEquals(snapshot, Workspace.Document)) throw new DataSpaceException("The database changed. Reopen the dialog.");
            Workspace.ConfigureDatasheet(name, layout, selected, width, snapshot.Revision); return;
        }
        else
        {
            using var editor = new DatasheetOptionsControl(table.Fields, layout, selected); var error = new ValidationMessageControl();
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Datasheet Layout", Content = OfficeVisuals.Stack(editor, error), PrimaryButtonText = "Apply Layout", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            DatasheetLayout? captured = null;
            dialog.PrimaryButtonClick += (_, e) => { try { captured = editor.Capture(); } catch (Exception exception) { error.Text = exception.Message; e.Cancel = true; } };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || captured is null) return; layout = captured;
        }
        if (!ReferenceEquals(snapshot, Workspace.Document)) throw new DataSpaceException("The database changed. Reopen the dialog.");
        Workspace.ConfigureDatasheet(name, layout, expectedRevision: snapshot.Revision);
        ShowStatus("Datasheet layout applied. Save to retain it; Undo restores the previous layout.");
    }
    private async Task FindReplaceAsync(bool replace)
    {
        if (_sheet is null || _active?.Kind != DatabaseObjectKind.Table) throw new DataSpaceException("Open a table datasheet to find or replace values.");
        var name = _active.Name; var snapshot = Workspace.Document; TableSearchHit? hit = null;
        using var editor = new FindReplaceControl(_sheet.Fields.Select(f => f.Name), _sheet.SelectedField?.Name, replace);
        void CheckSnapshot() { if (!ReferenceEquals(snapshot, Workspace.Document)) throw new DataSpaceException("The database changed. Reopen Find and Replace."); }
        void Find()
        {
            CheckSnapshot();
            hit = TableTextSearch.FindNext(_sheet.Fields, _sheet.Records, editor.Options, hit?.Row ?? -1, hit?.Column ?? -1, editor.OnlyField);
            if (hit is not null) _sheet.SelectCell(hit.Row, hit.Column);
            editor.Status.Text = hit is null ? "No matching value in this view." : $"Found {hit.Field}, record {hit.Row + 1:N0}. Search wraps within this view.";
        }
        editor.FindRequested += () => { try { editor.Error.Text = ""; Find(); } catch (Exception error) { editor.Error.Text = error.Message; } };
        editor.ReplaceRequested += all =>
        {
            try
            {
                editor.Error.Text = ""; CheckSnapshot();
                if (!all && hit is null) Find();
                if (!all && hit is null) return;
                var rows = all ? _sheet.Records : new[] { _sheet.Records[hit!.Row] };
                var fields = all ? _sheet.Fields : new[] { _sheet.Fields[hit!.Column] };
                var edits = TableTextSearch.Replacements(fields, rows, editor.Options, editor.Replacement, editor.OnlyField);
                Workspace.UpdateRecords(all ? "Replace all" : "Replace value", name, edits, snapshot.Revision);
                snapshot = Workspace.Document; hit = null;
                editor.Status.Text = $"Replaced {edits.Length:N0} cell(s). Undo restores this operation.";
            }
            catch (Exception error) { editor.Error.Text = error.Message; }
        };
        await new ContentDialog { XamlRoot = XamlRoot, Title = "Find and Replace", Content = editor, CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close }.ShowAsync();
        _sheet?.FocusGrid();
    }
}
