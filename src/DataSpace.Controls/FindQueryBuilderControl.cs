using DataSpace.Query;

namespace DataSpace.Controls;

/// <summary>Native Uno duplicate/unmatched authoring without application-shell dependencies.</summary>
public sealed class FindQueryBuilderControl : UserControl
{
    private readonly DatabaseDocument _document;
    private readonly FindQueryKind _kind;
    private readonly ComboBox _source, _related;
    private readonly CheckBox _summary, _nulls;
    private readonly StackPanel _matches = new() { Spacing = 3 }, _outputs = new() { Spacing = 0 };
    private readonly List<(string Field, CheckBox Selected, ComboBox Related)> _keys = [];
    private readonly List<(string Field, CheckBox Selected)> _columns = [];
    private string? _loadedSource, _loadedRelated;

    public FindQueryBuilderControl(DatabaseDocument document, FindQueryKind kind)
    {
        _document = document; _kind = kind;
        _source = OfficeVisuals.Combo(document.Tables.Select(t => t.Name));
        _related = OfficeVisuals.Combo(document.Tables.Select(t => t.Name), document.Tables.Skip(1).FirstOrDefault()?.Name);
        _summary = new CheckBox { Content = "Show duplicate groups and counts", IsChecked = true };
        _nulls = new CheckBox { Content = "Include duplicate null keys", IsChecked = true };
        AutomationProperties.SetName(_summary, "Show duplicate groups and counts");
        AutomationProperties.SetName(_nulls, "Include duplicate null keys");
        var panel = new StackPanel { Spacing = 7, Width = 430 };
        EditorVisuals.Labeled(panel, "Find source table", _source);
        if (kind == FindQueryKind.Unmatched) EditorVisuals.Labeled(panel, "Find related table", _related);
        panel.Children.Add(OfficeVisuals.Text(kind == FindQueryKind.Unmatched ? "Select source keys and their matching related fields" : "Select fields which must have duplicate values", 12));
        var matchScroll = EditorVisuals.Scroll(_matches); matchScroll.Height = 160; panel.Children.Add(matchScroll);
        if (kind == FindQueryKind.Duplicates) { panel.Children.Add(_summary); panel.Children.Add(_nulls); }
        panel.Children.Add(OfficeVisuals.Text("Detail output fields (none selected means all)", 12));
        var outputScroll = EditorVisuals.Scroll(_outputs); outputScroll.Height = 100; panel.Children.Add(outputScroll);
        var note = OfficeVisuals.Text(kind == FindQueryKind.Duplicates
            ? "Summary uses grouped counts. Detail mode returns each duplicated record and uses bounded correlated queries. Output fields apply to detail mode only."
            : "Null source keys are unmatched. One-key queries use a cached membership set; multiple keys use a null-safe left-join test. No records are modified.", 11, "666666");
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var scroll = EditorVisuals.Scroll(panel); scroll.MaxHeight = 560; Content = scroll;
        BuildFields();
        _source.SelectionChanged += (_, _) => { if (_source.SelectedItem as string != _loadedSource) BuildFields(); };
        _related.SelectionChanged += (_, _) => { if (_related.SelectedItem as string != _loadedRelated) BuildFields(); };
    }
    private void BuildFields()
    {
        _matches.Children.Clear(); _outputs.Children.Clear(); _keys.Clear(); _columns.Clear();
        _loadedSource = _source.SelectedItem as string; _loadedRelated = _related.SelectedItem as string;
        if (_loadedSource is null) return;
        var table = _document.Table(_loadedSource);
        var related = _loadedRelated is null ? null : _document.Table(_loadedRelated);
        foreach (var field in table.Fields)
        {
            var selected = new CheckBox { Content = field.DisplayName, MinHeight = 28, Width = 220, FontSize = 12 };
            AutomationProperties.SetName(selected, "Match field " + field.Name);
            var match = OfficeVisuals.Combo(related?.Fields.Select(f => f.Name) ?? [], field.Name, 190);
            AutomationProperties.SetName(match, "Related field for " + field.Name);
            _matches.Children.Add(_kind == FindQueryKind.Unmatched ? OfficeVisuals.Row(selected, match) : selected);
            _keys.Add((field.Name, selected, match));
            var output = new CheckBox { Content = field.DisplayName, MinHeight = 25, FontSize = 12 };
            AutomationProperties.SetName(output, "Output field " + field.Name); _outputs.Children.Add(output); _columns.Add((field.Name, output));
        }
    }
    public FindQueryDesign ReadDefinition() => new()
    {
        Kind = _kind, Source = _source.SelectedItem as string ?? throw new DataSpaceException("Select a source table."),
        RelatedSource = _related.SelectedItem as string ?? "",
        MatchFields = _keys.Where(k => k.Selected.IsChecked == true).Select(k => k.Field).ToList(),
        RelatedFields = _keys.Where(k => k.Selected.IsChecked == true).Select(k => k.Related.SelectedItem as string ?? "").ToList(),
        OutputFields = _columns.Where(c => c.Selected.IsChecked == true).Select(c => c.Field).ToList(),
        SummaryOnly = _summary.IsChecked == true, IncludeNullKeys = _nulls.IsChecked == true
    };
    public string ToSql() => ReadDefinition().ToSql(_document);
}
