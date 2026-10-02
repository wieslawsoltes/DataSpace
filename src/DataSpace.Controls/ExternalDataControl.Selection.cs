namespace DataSpace.Controls;

public sealed partial class ExternalDataControl
{
    private void OnCommittedSelection(ComboBox selector, Action selected)
    {
        var pending = false;
        var committed = selector.SelectedItem;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        static bool ActivationKeyDown()
        {
            foreach (var key in new[] { VirtualKey.Enter, VirtualKey.Space, VirtualKey.Escape })
                if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) return true;
            return false;
        }
        void Reset()
        {
            timer.Stop(); pending = false; committed = selector.SelectedItem;
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
            timer.Stop();
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
        selector.DropDownClosed += (_, _) => QueueSelection();
        // A queued callback must never hold a removed dialog or activate a source
        // after disposal. No timer runs while the source chooser is idle.
        selector.Unloaded += (_, _) => Reset();
    }
}
