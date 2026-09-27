using DataSpace.Query;

namespace DataSpace.Controls;

public enum QueryEditorView { Design, Sql, Datasheet }

/// <summary>Reusable query editor with non-destructive SQL/design switching and explicit action confirmation.</summary>
public sealed class QueryEditorControl : UserControl, IDatabaseEditor
{
    private readonly DatabaseWorkspace _workspace;
    private readonly string _name;
    private readonly QueryEngine _engine = new();
    private readonly TextBox _sql;
    private readonly TextBox _parameters;
    private readonly DatasheetControl _results = new();
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _status = OfficeVisuals.Text("Ready", 11, "666666");
    private readonly Grid _sqlView;
    private QueryDesignerControl? _designer;
    private string _baselineSql, _baselineParameters;
    private string? _state, _baselineState, _resultSql, _resultParameters;
    private QueryResult? _lastResult;
    private Dictionary<string, string?> _savedParameters;
    private QueryEditorView _view = QueryEditorView.Sql;
    private bool _running, _disposed;
    public Func<string, Task<bool>>? ConfirmActionAsync { get; set; }
    public event Action<string>? Error;
    public bool HasPendingChanges => _sql.Text != _baselineSql || _parameters.Text != _baselineParameters || _state != _baselineState || _designer?.HasPendingChanges == true;
    // Uno may deliver initial TextChanged after Loaded. Compare actual content,
    // not notification timing, so a delayed no-op event never discards a valid result.
    public QueryResult? LastResult => _sql.Text == _resultSql && _parameters.Text == _resultParameters && _designer?.HasPendingChanges != true ? _lastResult : null;
    public QueryEditorView View => _view;
    public QueryEditorControl(DatabaseWorkspace workspace, string name, bool design = false)
    {
        _workspace = workspace; _name = name;
        var query = workspace.Document.Queries.First(q => Names.Equal(q.Name, name));
        _baselineSql = query.Sql; _state = _baselineState = query.DesignerState;
        _savedParameters = new(query.Parameters, StringComparer.OrdinalIgnoreCase);
        _baselineParameters = string.Join("\n", query.Parameters.Select(p => p.Key + "=" + p.Value));
        _sql = OfficeVisuals.Input(query.Sql); _sql.AcceptsReturn = true; _sql.TextWrapping = TextWrapping.Wrap;
        _sql.FontFamily = new FontFamily("Consolas, monospace"); _sql.FontSize = 14; _sql.Margin = new(8);
        AutomationProperties.SetAutomationId(_sql, "SqlEditor"); AutomationProperties.SetName(_sql, "SQL statement");
        _parameters = OfficeVisuals.Input(_baselineParameters, "Parameter=value, one per line"); _parameters.AcceptsReturn = true; _parameters.Margin = new(8); _parameters.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_parameters, "Query parameters");
        var root = OfficeVisuals.Grid("Auto,*,26");
        var toolbar = OfficeVisuals.Row(
            OfficeVisuals.Button("Design View", () => TrySwitch(QueryEditorView.Design), "design", "QueryDesignView"),
            OfficeVisuals.Button("SQL View", () => TrySwitch(QueryEditorView.Sql), "query", "QuerySqlView"),
            OfficeVisuals.Button("Datasheet View", () => TrySwitch(QueryEditorView.Datasheet), "table", "QueryDatasheetView"),
            OfficeVisuals.Button("Run", async () => await RunAsync(), "query", "RunQuery")); toolbar.Margin = new(8);
        OfficeVisuals.Add(root, toolbar);
        _sqlView = OfficeVisuals.Grid("Auto,*", "3*,*");
        var sqlLabel = OfficeVisuals.Text("SQL statement", 12, bold: true); sqlLabel.Margin = new(8);
        var paramLabel = OfficeVisuals.Text("Parameters", 12, bold: true); paramLabel.Margin = new(8);
        OfficeVisuals.Add(_sqlView, sqlLabel); OfficeVisuals.Add(_sqlView, paramLabel, column: 1);
        OfficeVisuals.Add(_sqlView, _sql, 1); OfficeVisuals.Add(_sqlView, _parameters, 1, 1);
        _host.Content = _sqlView; OfficeVisuals.Add(root, _host, 1); _status.Margin = new(8, 0, 0, 0); OfficeVisuals.Add(root, _status, 2);
        Content = root; _results.Error += ShowError;
        var initial = true;
        Loaded += async (_, _) =>
        {
            if (!initial) return; initial = false;
            if (design) TrySwitch(QueryEditorView.Design);
            else { try { if (_engine.IsReadOnly(_sql.Text)) await RunAsync(); } catch (Exception error) { ShowError(error.Message); } }
        };
    }
    private void ShowError(string message) { _status.Text = message; Error?.Invoke(message); }
    private void TrySwitch(QueryEditorView view) { try { SwitchView(view); } catch (Exception error) { ShowError(error.Message); } }
    public void SwitchView(QueryEditorView view)
    {
        if (_view == view) return;
        SynchronizeDesign();
        if (view == QueryEditorView.Design)
        {
            var model = QueryDesign.Restore(_sql.Text, _state);
            var next = new QueryDesignerControl(_workspace.Document, model);
            _designer?.Dispose(); _designer = next; _host.Content = next;
        }
        else if (view == QueryEditorView.Sql) _host.Content = _sqlView;
        else
        {
            if (LastResult is null) throw new DataSpaceException("Run the current query before opening its result datasheet.");
            _host.Content = _results;
        }
        _view = view; _status.Text = view + " View";
    }
    private void SynchronizeDesign()
    {
        if (_view != QueryEditorView.Design || _designer?.HasPendingChanges != true) return;
        var sql = _designer.ToSql(); var state = _designer.Design.Serialize();
        _sql.Text = sql; _state = state; _designer.MarkCommitted();
    }
    private Dictionary<string, string?> ReadParameters()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in _parameters.Text.Replace("\r", "").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var separator = line.IndexOf('='); if (separator < 1) throw new DataSpaceException("Use name=value for each query parameter.");
            var name = line[..separator].Trim(); Names.Validate(name);
            if (!result.TryAdd(name, line[(separator + 1)..])) throw new DataSpaceException("Duplicate query parameter: " + name);
        }
        return result;
    }
    public void Commit()
    {
        SynchronizeDesign(); if (!HasPendingChanges) return;
        _engine.IsReadOnly(_sql.Text); var parameters = ReadParameters();
        var current = _workspace.Document.Queries.First(q => Names.Equal(q.Name, _name));
        if (current.Sql != _baselineSql || current.DesignerState != _baselineState || current.Parameters.Count != _savedParameters.Count ||
            current.Parameters.Any(p => !_savedParameters.TryGetValue(p.Key, out var saved) || saved != p.Value))
            throw new DataSpaceException("This query or its parameters changed elsewhere. Copy your SQL before reopening it.");
        _workspace.Edit("Save query", document =>
        {
            var query = document.Queries.First(q => Names.Equal(q.Name, _name)); query.Sql = _sql.Text; query.Parameters = parameters; query.DesignerState = _state;
        });
        _baselineSql = _sql.Text; _baselineParameters = _parameters.Text; _baselineState = _state; _savedParameters = new(parameters, StringComparer.OrdinalIgnoreCase);
    }
    public async Task RunAsync()
    {
        if (_running || _disposed) return; _running = true;
        try
        {
            Commit(); var sql = _sql.Text;
            if (!_engine.IsReadOnly(sql) && (ConfirmActionAsync is null || !await ConfirmActionAsync("Run this action query? It may insert, update, delete records or change the schema. The whole action is undoable."))) return;
            if (_disposed) return;
            var parameters = ReadParameters().ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase);
            _lastResult = null;
            var result = _engine.Execute(_workspace, sql, parameters);
            _lastResult = result; _resultSql = sql; _resultParameters = _parameters.Text;
            _results.SetData(result.Fields, result.Records, true); _host.Content = _results; _view = QueryEditorView.Datasheet;
            _status.Text = result.IsAction ? $"{result.AffectedRecords:N0} record(s) affected" : $"{result.Records.Count:N0} records · {result.Duration.TotalMilliseconds:N1} ms · {result.Statistics.HashJoins} hash join(s) · {result.Statistics.JoinComparisons:N0} comparisons";
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { _running = false; }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _designer?.Dispose(); _results.Dispose(); }
}
