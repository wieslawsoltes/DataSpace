using DataSpace.Query;

namespace DataSpace.Controls;

/// <summary>SQL editor, explicit SELECT builder, parameter editor and read-only result datasheet.</summary>
public sealed class QueryEditorControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private readonly string _name;
    private readonly QueryEngine _engine = new();
    private readonly TextBox _sql;
    private readonly TextBox _parameters;
    private readonly DatasheetControl _results = new();
    private readonly TextBlock _status = OfficeVisuals.Text("Ready", 11, "666666");
    private string _baselineSql;
    private string _baselineParameters;
    public Func<string, Task<bool>>? ConfirmActionAsync { get; set; }
    public event Action<string>? Error;
    public bool HasPendingChanges => _sql.Text != _baselineSql || _parameters.Text != _baselineParameters;
    public QueryResult? LastResult { get; private set; }
    public QueryEditorControl(DatabaseWorkspace workspace, string name)
    {
        _workspace = workspace; _name = name;
        var query = workspace.Document.Queries.First(q => Names.Equal(q.Name, name));
        _baselineSql = query.Sql; _baselineParameters = string.Join("\n", query.Parameters.Select(p => p.Key + "=" + p.Value));
        _sql = OfficeVisuals.Input(query.Sql); _sql.AcceptsReturn = true; _sql.TextWrapping = TextWrapping.Wrap;
        _sql.FontFamily = new FontFamily("Consolas, monospace"); _sql.FontSize = 14; _sql.Margin = new(8);
        AutomationProperties.SetAutomationId(_sql, "SqlEditor"); AutomationProperties.SetName(_sql, "SQL statement");
        _parameters = OfficeVisuals.Input(_baselineParameters, "Parameter=value, one per line"); _parameters.AcceptsReturn = true; _parameters.Margin = new(8); _parameters.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_parameters, "Query parameters");
        var root = OfficeVisuals.Grid("Auto,200,Auto,*,24");
        var toolbar = OfficeVisuals.Row(OfficeVisuals.Button("Run", async () => await RunAsync(), "query", "RunQuery"), OfficeVisuals.Text("SELECT results are read-only. Action queries require confirmation.", 11, "666666")); toolbar.Margin = new(8);
        OfficeVisuals.Add(root, toolbar);
        var editors = OfficeVisuals.Grid("*", "3*,*"); OfficeVisuals.Add(editors, _sql); OfficeVisuals.Add(editors, _parameters, column: 1); OfficeVisuals.Add(root, editors, 1);
        var builder = new StackPanel { Spacing = 6, Margin = new(8) };
        var source = OfficeVisuals.Combo(workspace.Document.Tables.Select(t => t.Name), width: 190);
        var fields = OfficeVisuals.Input("*", "Field names separated by commas", 280);
        var where = OfficeVisuals.Input("", "WHERE expression", 230);
        var order = OfficeVisuals.Input("", "ORDER BY expression", 190);
        var generate = OfficeVisuals.Button("Generate SELECT", () =>
        {
            try
            {
                if (source.SelectedItem is not string tableName) throw new DataSpaceException("Create a table first.");
                var table = _workspace.Document.Table(tableName);
                var selected = fields.Text.Trim() == "*" ? "*" : string.Join(", ", fields.Text.Split(',').Select(f => Names.Quote(table.Field(f.Trim()).Name)));
                var sql = "SELECT " + selected + " FROM " + Names.Quote(tableName);
                if (!string.IsNullOrWhiteSpace(where.Text)) sql += " WHERE " + where.Text;
                if (!string.IsNullOrWhiteSpace(order.Text)) sql += " ORDER BY " + order.Text;
                sql += ";"; _engine.IsReadOnly(sql); _sql.Text = sql;
            }
            catch (Exception error) { Error?.Invoke(error.Message); }
        });
        builder.Children.Add(OfficeVisuals.Text("SELECT builder — Generate explicitly replaces the SQL editor contents.", 11, "666666"));
        builder.Children.Add(EditorVisuals.Scroll(OfficeVisuals.Row(source, fields, where, order, generate)));
        OfficeVisuals.Add(root, builder, 2); OfficeVisuals.Add(root, _results, 3); _status.Margin = new(8, 0, 0, 0); OfficeVisuals.Add(root, _status, 4);
        Content = root; _results.Error += message => Error?.Invoke(message);
    }
    private Dictionary<string, string?> ReadParameters()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in _parameters.Text.Replace("\r", "").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var separator = line.IndexOf('=');
            if (separator < 1) throw new DataSpaceException("Use name=value for each query parameter.");
            var name = line[..separator].Trim(); Names.Validate(name);
            if (!result.TryAdd(name, line[(separator + 1)..])) throw new DataSpaceException("Duplicate query parameter: " + name);
        }
        return result;
    }
    public void Commit()
    {
        if (!HasPendingChanges) return;
        _engine.IsReadOnly(_sql.Text); var parameters = ReadParameters();
        var current = _workspace.Document.Queries.First(q => Names.Equal(q.Name, _name));
        if (current.Sql != _baselineSql) throw new DataSpaceException("This query was changed elsewhere. Copy your SQL before reopening it.");
        _workspace.Edit("Save query", document =>
        {
            var query = document.Queries.First(q => Names.Equal(q.Name, _name)); query.Sql = _sql.Text; query.Parameters = parameters;
        });
        _baselineSql = _sql.Text; _baselineParameters = _parameters.Text;
    }
    public async Task RunAsync()
    {
        try
        {
            Commit();
            if (!_engine.IsReadOnly(_sql.Text) && (ConfirmActionAsync is null || !await ConfirmActionAsync("Run this action query? It may insert, update, delete records or change the schema. The whole action is undoable."))) return;
            var parameters = ReadParameters().ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase);
            LastResult = _engine.Execute(_workspace, _sql.Text, parameters);
            _results.SetData(LastResult.Fields, LastResult.Records, true);
            _status.Text = LastResult.IsAction ? $"{LastResult.AffectedRecords:N0} record(s) affected" : $"{LastResult.Records.Count:N0} records · {LastResult.Duration.TotalMilliseconds:N1} ms";
        }
        catch (Exception error) { _status.Text = error.Message; Error?.Invoke(error.Message); }
    }
    public void Dispose() => _results.Dispose();
}
