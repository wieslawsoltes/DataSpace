namespace DataSpace.Controls;

public sealed partial class DatasheetControl
{
    /// <summary>Select adjacent displayed columns without enumerating their record values.</summary>
    public void SelectColumns(int column, bool extend = false)
    {
        if (!FinishEdit(false) || _fields.Count == 0) return;
        ViewState.SelectColumns(column, _fields.Count, extend);
        EnsureVisible(); _surface.Invalidate(); SelectionChanged?.Invoke();
        var (first, last) = ViewState.SelectedColumns(_fields.Count);
        AutomationProperties.SetName(this, $"Selected {last - first + 1} field(s): {_fields[first].DisplayName} through {_fields[last].DisplayName}. All {_records.Count:N0} records. Shift+Left or Shift+Right extends the field selection.");
    }

    public string[] SelectedFieldNames()
    {
        var (first, last) = ViewState.SelectedColumns(_fields.Count);
        if (last < first) return [];
        var result = new string[last - first + 1];
        for (var index = 0; index < result.Length; index++) result[index] = _fields[first + index].Name;
        return result;
    }

    private void DeleteSelectedRecords()
    {
        if (ViewState.ReadOnly) return;
        if (ViewState.WholeColumnSelection)
        { Error?.Invoke("Select record rows, not whole columns, before deleting records."); return; }
        DeleteRecordsRequested?.Invoke(SelectedRecordIds());
    }
}
