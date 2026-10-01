namespace DataSpace.Controls;

public sealed partial class DatasheetControl
{
    private bool _contextMenuActive;
    private int _contextMenuGeneration;

    private void GuardContextMenuInput(MenuFlyout menu)
    {
        menu.Opened += (_, _) => { _contextMenuGeneration++; _contextMenuActive = true; };
        menu.Closed += (_, _) =>
        {
            var generation = _contextMenuGeneration;
            // The menu action can restore focus to this grid before its activation
            // key finishes routing. Do not reinterpret that key as an edit/toggle.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == _contextMenuGeneration) _contextMenuActive = false;
            });
        };
    }

    private bool IsGridKeyOrigin(object? source)
    {
        if (ReferenceEquals(source, this)) return true;
        for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            // Native text editors and popup items own their key gestures even if
            // they close, commit or change focus during the routed event.
            if (ReferenceEquals(current, _editor) || current is MenuFlyoutItemBase) return false;
            if (ReferenceEquals(current, _surface)) return true;
        }
        return false;
    }
}
