namespace DataSpace.Controls;

public sealed partial class DatasheetControl
{
    public event Action<string>? LayoutCommand;
    public void ApplyLayout(DatasheetLayout layout, IReadOnlyList<FieldDefinition> visible)
    {
        layout.Validate(); ViewState.FrozenColumnCount = layout.FrozenCount(visible);
        ViewState.RowHeight = (float)layout.RowHeight; ViewState.FontSize = (float)layout.FontSize;
        ViewState.Bold = layout.Bold; ViewState.AlternateRows = layout.AlternateRows;
        ViewState.HorizontalGridLines = layout.HorizontalGridLines; ViewState.VerticalGridLines = layout.VerticalGridLines;
    }
    public void BestFitSelectedColumn()
    {
        if (!FinishEdit(false) || SelectedField is not { } field) return;
        ColumnWidthChanged?.Invoke(field.Name, _renderer.MeasureColumn(field, _records, ViewState));
    }
    public void FocusGrid() => Focus(FocusState.Programmatic);
}
