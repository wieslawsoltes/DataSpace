namespace DataSpace.Controls;

public sealed partial class FormEditorControl
{
    public async Task EditTabOrderAsync()
    {
        if (!_design) return;
        using var editor = new FormTabOrderControl(_form);
        var error = new ValidationMessageControl();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Tab Order", Content = OfficeVisuals.Stack(editor, error),
            PrimaryButtonText = "Apply Tab Order", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try { if (editor.Apply(_form)) { HasPendingChanges = true; BuildProperties(); } }
            catch (Exception exception) { e.Cancel = true; error.Text = exception.Message; }
        };
        try { await dialog.ShowAsync(); }
        finally { dialog.Content = null; }
    }
}
