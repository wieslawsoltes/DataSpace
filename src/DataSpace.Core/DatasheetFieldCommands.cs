namespace DataSpace.Core;

/// <summary>Pure, atomic presentation commands for adjacent displayed fields, independent of Uno.</summary>
public static class DatasheetFieldCommands
{
    public static DatasheetLayout Hide(IReadOnlyList<FieldDefinition> fields, DatasheetLayout layout, IReadOnlyList<string> selected)
    {
        var names = Resolve(fields, layout, selected);
        if (names.Length >= layout.VisibleFields(fields).Length) throw new DataSpaceException("Keep at least one field visible.");
        var copy = layout.Copy();
        foreach (var name in names) if (!copy.HiddenFields.Contains(name, StringComparer.OrdinalIgnoreCase)) copy.HiddenFields.Add(name);
        copy.Validate(); return copy;
    }

    public static DatasheetLayout Freeze(IReadOnlyList<FieldDefinition> fields, DatasheetLayout layout, IReadOnlyList<string> selected)
    {
        var names = Resolve(fields, layout, selected); var copy = layout.Copy();
        foreach (var name in names) if (!copy.FrozenFields.Contains(name, StringComparer.OrdinalIgnoreCase)) copy.FrozenFields.Add(name);
        copy.Validate(); return copy;
    }

    private static string[] Resolve(IReadOnlyList<FieldDefinition> fields, DatasheetLayout layout, IReadOnlyList<string> selected)
    {
        ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(layout); ArgumentNullException.ThrowIfNull(selected);
        if (selected.Count is < 1 or > 256) throw new DataSpaceException("Select 1–256 displayed fields.");
        var visible = layout.VisibleFields(fields);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < selected.Count; index++)
        {
            var name = selected[index]; Names.Validate(name);
            if (!set.Add(name) || !visible.Any(field => Names.Equal(field.Name, name)))
                throw new DataSpaceException("Select each displayed field only once.");
        }
        // Use displayed order even when the selection was extended to the left.
        return visible.Where(field => set.Contains(field.Name)).Select(field => field.Name).ToArray();
    }
}
