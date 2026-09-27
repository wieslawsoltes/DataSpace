using DataSpace.Query;

namespace DataSpace.Controls;

/// <summary>Standalone native Uno crosstab authoring form. ReadDefinition reads current inputs, not delayed text events.</summary>
public sealed class CrosstabBuilderControl : UserControl
{
    private readonly DatabaseDocument _document;
    private readonly ComboBox _source, _aggregate;
    private readonly TextBox _column, _value, _headings, _where;
    private readonly CheckBox _totals;
    private readonly StackPanel _fields = new() { Spacing = 0 };
    private readonly List<(string Field, CheckBox Input)> _rows = [];
    private string? _loadedSource;
    public CrosstabBuilderControl(DatabaseDocument document, CrosstabDesign? design = null)
    {
        _document = document;
        _source = OfficeVisuals.Combo(document.Tables.Select(table => table.Name), design?.Source);
        _aggregate = OfficeVisuals.Combo(Enum.GetNames<CrosstabAggregate>(), (design?.Aggregate ?? CrosstabAggregate.Count).ToString());
        _column = OfficeVisuals.Input(design?.ColumnExpression ?? "", "Field or expression");
        _value = OfficeVisuals.Input(design?.ValueExpression ?? "*", "Field or * for Count");
        _headings = OfficeVisuals.Input(design?.FixedHeadings ?? "", "Example: 'New', 'Processing', 'Shipped'");
        _where = OfficeVisuals.Input(design?.Where ?? "", "Optional WHERE expression");
        _totals = new CheckBox { Content = "Include row totals", IsChecked = design?.ShowRowTotals == true, FontSize = 12 };
        AutomationProperties.SetName(_totals, "Include row totals");
        var panel = new StackPanel { Spacing = 6, MinWidth = 430 };
        EditorVisuals.Labeled(panel, "Crosstab source", _source);
        panel.Children.Add(OfficeVisuals.Text("Row headings", 12, "555555"));
        var rows = EditorVisuals.Scroll(_fields); rows.Height = 120; panel.Children.Add(rows);
        EditorVisuals.Labeled(panel, "Column heading expression", _column);
        var values = OfficeVisuals.Grid("*", "*,*");
        var left = new StackPanel { Spacing = 4, Margin = new(0, 0, 8, 0) }; var right = new StackPanel { Spacing = 4 };
        EditorVisuals.Labeled(left, "Crosstab aggregate", _aggregate); EditorVisuals.Labeled(right, "Value expression", _value);
        OfficeVisuals.Add(values, left); OfficeVisuals.Add(values, right, column: 1); panel.Children.Add(values);
        EditorVisuals.Labeled(panel, "Fixed column headings", _headings); EditorVisuals.Labeled(panel, "Crosstab filter", _where);
        panel.Children.Add(_totals);
        var note = OfficeVisuals.Text("Generate SQL explicitly replaces this query's SQL. Cancel keeps it unchanged. Leave fixed headings empty to discover columns from the data.", 11, "666666");
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var scroll = EditorVisuals.Scroll(panel); scroll.MaxHeight = 560; Content = scroll;
        BuildFields(design?.RowFields);
        _source.SelectionChanged += (_, _) => { if (_loadedSource != _source.SelectedItem as string) BuildFields(null); };
    }
    private void BuildFields(IReadOnlyList<string>? selected)
    {
        _fields.Children.Clear(); _rows.Clear(); _loadedSource = _source.SelectedItem as string;
        if (_loadedSource is null) return;
        var table = _document.Table(_loadedSource);
        var fields = selected is null ? table.Fields : selected.Select(table.Field).Concat(table.Fields.Where(field => !selected.Any(name => Names.Equal(name, field.Name)))).ToList();
        foreach (var field in fields)
        {
            var input = new CheckBox { Content = field.DisplayName, IsChecked = selected?.Any(name => Names.Equal(name, field.Name)) == true, MinHeight = 25, FontSize = 12 };
            AutomationProperties.SetName(input, "Row heading " + field.Name); _fields.Children.Add(input); _rows.Add((field.Name, input));
        }
        if (selected is null) _column.Text = table.Fields.Count > 1 ? Names.Quote(table.Fields[1].Name) : "";
    }
    public CrosstabDesign ReadDefinition() => new()
    {
        Source = _source.SelectedItem as string ?? throw new DataSpaceException("Select a source table."),
        RowFields = _rows.Where(row => row.Input.IsChecked == true).Select(row => row.Field).ToList(),
        Aggregate = Enum.Parse<CrosstabAggregate>(_aggregate.SelectedItem as string ?? "Count"),
        ColumnExpression = _column.Text, ValueExpression = _value.Text, FixedHeadings = _headings.Text, Where = _where.Text,
        ShowRowTotals = _totals.IsChecked == true
    };
    public string ToSql() => ReadDefinition().ToSql(_document);
}
