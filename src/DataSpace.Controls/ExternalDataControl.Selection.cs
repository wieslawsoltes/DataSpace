namespace DataSpace.Controls;

public sealed partial class ExternalDataControl
{
    private readonly HashSet<ComboBox> _settlingSelectors = [];

    internal void OnSourceDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_disposed || ImportedTable is not null) return;
        // A popup may close on key-down while ContentDialog processes its key-up.
        // Closing the chooser must consume that gesture, not close both layers.
        var selector = new[] { _provider, _sourceList, _tables }.FirstOrDefault(c => c.IsDropDownOpen)
            ?? _settlingSelectors.FirstOrDefault();
        if (selector is null) return;
        args.Cancel = true;
        selector.IsDropDownOpen = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && !_busy && IsLoaded && selector.IsEnabled) selector.Focus(FocusState.Programmatic);
        });
    }

    private void OnCommittedSelection(ComboBox selector, Action selected)
    {
        var pending = false;
        var committed = selector.SelectedItem;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        static bool ActivationKeyDown()
        {
            static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            return Down(VirtualKey.Enter) || Down(VirtualKey.Space) || Down(VirtualKey.Escape);
        }
        void Reset()
        {
            timer.Stop(); _settlingSelectors.Remove(selector); pending = false; committed = selector.SelectedItem;
        }
        void QueueSelection()
        {
            if (!pending || selector.IsDropDownOpen || _disposed || _changing || _busy) return;
            timer.Start();
        }
        timer.Tick += (_, _) =>
        {
            if (_disposed || !IsLoaded || _changing || _busy) { Reset(); return; }
            if (selector.IsDropDownOpen) { timer.Stop(); return; }
            // A dispatcher callback can run between key-down and key-up. Running
            // the source operation then focuses its Cancel button, which receives
            // the tail of the same Enter/Space gesture and cancels the new read.
            // Wait for physical key release, not an assumed dispatch delay.
            if (ActivationKeyDown()) return;
            timer.Stop(); _settlingSelectors.Remove(selector);
            if (!pending) return;
            pending = false;
            var next = selector.SelectedItem;
            if (Equals(next, committed)) return; // Escape restored the previous item.
            committed = next;
            selected();
        };
        selector.SelectionChanged += (_, _) =>
        {
            if (_disposed || _changing || _busy) { Reset(); return; }
            pending = true;
            QueueSelection();
        };
        selector.DropDownClosed += (_, _) =>
        {
            if (_disposed || _changing || _busy) { Reset(); return; }
            _settlingSelectors.Add(selector);
            // Also settle an unchanged/Escape-closed popup with no page to read.
            timer.Start();
        };
        // A queued callback must never hold a removed dialog or activate a source
        // after disposal. No timer runs while the source chooser is idle.
        selector.Unloaded += (_, _) => Reset();
    }
}
