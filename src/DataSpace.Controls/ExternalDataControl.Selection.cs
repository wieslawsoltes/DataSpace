namespace DataSpace.Controls;

public sealed partial class ExternalDataControl
{
    private void OnCommittedSelection(ComboBox selector, Action selected)
    {
        var pending = false;
        var committed = selector.SelectedItem;
        var generation = 0;
        void QueueSelection()
        {
            if (!pending || selector.IsDropDownOpen || _disposed || _changing || _busy) return;
            var expected = ++generation;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != expected || _disposed || _changing || _busy || selector.IsDropDownOpen) return;
                pending = false;
                var next = selector.SelectedItem;
                if (Equals(next, committed)) return; // Escape can restore the original choice.
                committed = next;
                selected();
            });
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (_disposed || _changing || _busy)
            {
                // A reconnect/catalog reset invalidates previously queued input.
                generation++; pending = false; committed = selector.SelectedItem;
                return;
            }
            pending = true;
            QueueSelection();
        };
        // Do not disable the selector, move focus or replace native input peers
        // from inside its open popup's selection/activation dispatch.
        selector.DropDownClosed += (_, _) => QueueSelection();
    }
}
