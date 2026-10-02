namespace DataSpace.Controls;

public sealed partial class FormEditorControl
{
    public async Task EditTabOrderAsync()
    {
        if (!_design) return;
        var changed = false;
        using (var editor = new FormTabOrderControl(_form))
        {
            var error = new ValidationMessageControl();
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Tab Order", Content = OfficeVisuals.Stack(editor, error),
                PrimaryButtonText = "Apply Tab Order", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            dialog.PrimaryButtonClick += (_, e) =>
            {
                try { if (editor.Apply(_form)) { HasPendingChanges = true; changed = true; } }
                catch (Exception exception) { e.Cancel = true; error.Text = exception.Message; }
            };
            try { await dialog.ShowAsync(); }
            finally { dialog.Content = null; }
        }
        // Retire modal peers before rebuilding background properties or restoring
        // native designer focus. A Save during the modal handoff is not a commit.
        if (changed) BuildProperties();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded && _surface.IsLoaded) _surface.Focus(FocusState.Programmatic);
        });
    }
}
