using DataSpace.Query;

namespace DataSpace.Controls;

public sealed partial class QueryEditorControl
{
    public async Task ShowFindBuilderAsync(FindQueryKind kind)
    {
        if (_disposed || _running || _dialogOpen) return;
        _dialogOpen = true;
        try
        {
            SynchronizeDesign();
            var builder = new FindQueryBuilderControl(_workspace.Document, kind);
            var errorText = OfficeVisuals.Text("", 12, "9C252A"); errorText.TextWrapping = TextWrapping.Wrap;
            var dialog = new ContentDialog { XamlRoot = XamlRoot,
                Title = kind == FindQueryKind.Duplicates ? "Find Duplicates Query" : "Find Unmatched Query",
                Content = OfficeVisuals.Stack(errorText, builder), PrimaryButtonText = "Generate SQL", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None };
            // A rejected primary click must not fall through to a default Close
            // action when Enter bubbles through the native ContentDialog.
            string? generated = null;
            dialog.PrimaryButtonClick += (_, e) =>
            {
                try { generated = builder.ToSql(); }
                catch (Exception error) { e.Cancel = true; errorText.Text = error.Message; }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || generated is null || _disposed) return;
            _designer?.Dispose(); _designer = null; _state = null; _sql.Text = generated;
            _host.Content = _sqlView; _view = QueryEditorView.Sql;
            _status.Text = "Find query generated. Run to preview; save to retain the SQL.";
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { _dialogOpen = false; }
    }
}
