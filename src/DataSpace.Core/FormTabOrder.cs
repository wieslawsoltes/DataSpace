namespace DataSpace.Core;

/// <summary>Tab sequence for the supported bound controls; drawing order is independent.</summary>
public static class FormTabOrder
{
    public static bool IsInput(LayoutControl control) => control.Kind is LayoutControlKind.TextBox or LayoutControlKind.CheckBox;

    public static void Validate(FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Controls is null || form.Controls.Count > 512) throw new DataSpaceException("A form supports at most 512 controls.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in form.Controls)
        {
            if (control is null || string.IsNullOrWhiteSpace(control.Id) || control.Id.Length > 128 || !identities.Add(control.Id))
                throw new DataSpaceException("Form control identities must be present and unique.");
            if (control.TabIndex is < -1 or > 32767) throw new DataSpaceException("Tab Index must be -1 (creation order) or 0–32767.");
        }
    }

    /// <summary>Includes skipped controls unless activeOnly is true; duplicate indices have stable creation-order ties.</summary>
    public static IReadOnlyList<LayoutControl> Ordered(FormDefinition form, bool activeOnly = false)
    {
        Validate(form);
        return form.Controls.Select((control, index) => (control, index))
            .Where(item => IsInput(item.control) && (!activeOnly || item.control.TabStop))
            .OrderBy(item => item.control.TabIndex < 0 ? item.index : item.control.TabIndex)
            .ThenBy(item => item.index).Select(item => item.control).ToArray();
    }
}

public sealed record FormTabEntry(string Id, string Caption, double X, double Y, bool TabStop);

/// <summary>Detached bounded tab-order draft. Applying it changes only tab indices/stops, never control geometry or draw order.</summary>
public sealed class FormTabOrderDraft
{
    private readonly List<FormTabEntry> _entries;
    private readonly Baseline[] _baseline;
    private readonly IReadOnlyList<FormTabEntry> _view;
    public IReadOnlyList<FormTabEntry> Entries => _view;
    private sealed record Baseline(string Id, LayoutControlKind Kind, int Index, bool Stop, double X, double Y);
    private static Baseline[] Capture(FormDefinition form) => form.Controls.Select(c => new Baseline(c.Id, c.Kind, c.TabIndex, c.TabStop, c.X, c.Y)).ToArray();

    public FormTabOrderDraft(FormDefinition form)
    {
        _entries = FormTabOrder.Ordered(form).Select(c => new FormTabEntry(c.Id, c.Caption, c.X, c.Y, c.TabStop)).ToList();
        _view = _entries.AsReadOnly(); _baseline = Capture(form);
    }
    public void MoveTo(string id, int index)
    {
        var current = Find(id);
        if (index < 0 || index >= _entries.Count) throw new DataSpaceException("The tab-order position is outside the control list.");
        var entry = _entries[current]; _entries.RemoveAt(current); _entries.Insert(index, entry);
    }
    public void SetTabStop(string id, bool value)
    {
        var index = Find(id); _entries[index] = _entries[index] with { TabStop = value };
    }
    public void AutoOrder()
    {
        if (_entries.Any(e => !double.IsFinite(e.X) || !double.IsFinite(e.Y))) throw new DataSpaceException("Cannot order controls with invalid positions.");
        var ordered = _entries.OrderBy(e => e.Y).ThenBy(e => e.X).ToArray();
        _entries.Clear(); _entries.AddRange(ordered);
    }
    private int Find(string id)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        return index >= 0 ? index : throw new DataSpaceException("The tab-order control does not exist.");
    }
    /// <summary>Rejects a stale draft before mutating any setting. The caller controls the workspace transaction.</summary>
    public bool Apply(FormDefinition form)
    {
        FormTabOrder.Validate(form);
        if (!_baseline.SequenceEqual(Capture(form))) throw new DataSpaceException("The form changed. Reopen Tab Order before applying this draft.");
        var targets = form.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        var changed = false;
        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index]; var control = targets[entry.Id];
            changed |= control.TabIndex != index || control.TabStop != entry.TabStop;
            control.TabIndex = index; control.TabStop = entry.TabStop;
        }
        return changed;
    }
}
