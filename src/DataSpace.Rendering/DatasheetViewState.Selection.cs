namespace DataSpace.Rendering;

public sealed partial class DatasheetViewState
{
    /// <summary>Column range selection is metadata; it never materializes the selected rows.</summary>
    public bool WholeColumnSelection { get; set; }

    public void SelectColumns(int column, int columnCount, bool extend = false)
    {
        if (columnCount < 1) { WholeColumnSelection = false; return; }
        column = Math.Clamp(column, 0, columnCount - 1);
        if (!extend || !WholeColumnSelection) AnchorColumn = column;
        AnchorColumn = Math.Clamp(AnchorColumn, 0, columnCount - 1);
        SelectedColumn = column; WholeColumnSelection = true;
    }

    public (int First, int Last) SelectedColumns(int columnCount)
    {
        if (columnCount < 1) return (0, -1);
        var first = WholeColumnSelection ? Math.Min(AnchorColumn, SelectedColumn) : SelectedColumn;
        var last = WholeColumnSelection ? Math.Max(AnchorColumn, SelectedColumn) : SelectedColumn;
        return (Math.Clamp(first, 0, columnCount - 1), Math.Clamp(last, 0, columnCount - 1));
    }

    public (int First, int Last) SelectedRows(int recordCount)
    {
        if (recordCount < 1) return (0, -1);
        return WholeColumnSelection ? (0, recordCount - 1) :
            (Math.Clamp(Math.Min(AnchorRow, SelectedRow), 0, recordCount - 1), Math.Clamp(Math.Max(AnchorRow, SelectedRow), 0, recordCount - 1));
    }
}
