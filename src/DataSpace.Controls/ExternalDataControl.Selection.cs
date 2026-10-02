namespace DataSpace.Controls;

public sealed partial class ExternalDataControl
{
    private void OnCommittedSelection(ComboBox selector, Action selected)
    {
        var pending = false;
        var generation = 0;
        void QueueSelection()
        {
            if (!pending || selector.IsDropDownOpen || _disposed || _changing || _busy) return;
            var expected = ++generation;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != expected || _disposed || _changing || _busy || selector.IsDropDownOpen) return;
                pending = false;
                selected();
            });
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (_disposed || _changing || _busy) return;
            pending = true;
            QueueSelection();
        };
        // Do not disable the selector, move focus or replace native input peers
        // from inside its open popup's selection/activation dispatch.
        selector.DropDownClosed += (_, _) => QueueSelection();
    }
}
