using DataSpace.DataSources;

namespace DataSpace.Controls;

/// <summary>Access-style Get External Data workspace: source selection, bounded read-only preview and explicit local-copy import.</summary>
public sealed class ExternalDataControl : UserControl, IAsyncDisposable
{
    private readonly Func<string, Task<string?>>? _pickText;
    private readonly Func<Task<IDataSource?>>? _pickSqlite;
    private readonly ComboBox _provider = OfficeVisuals.Combo(["JSON file", "SQLite file", "JSON URL", "PostgreSQL", "MySQL / MariaDB", "SQL Server"]);
    private readonly TextBox _address = OfficeVisuals.Input("", "https://server.example/");
    private readonly PasswordBox _token = new() { PlaceholderText = "Session-only gateway token", MinHeight = 30 };
    private readonly TextBox _pointer = OfficeVisuals.Input("", "Optional: /data/items");
    private readonly TextBox _name = OfficeVisuals.Input("Imported");
    private readonly ComboBox _sourceList = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    private readonly ComboBox _tables = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    private readonly NumberBox _maximum = new() { Value = 10000, Minimum = 1, Maximum = SourceLimits.MaxImportRows, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly DatasheetControl _preview = new();
    private readonly ValidationMessageControl _error = new();
    private readonly StatusMessageControl _status = new() { Text = "Choose a source to preview its tables." };
    private readonly TextBlock _hint = OfficeVisuals.Text("", 12, "666666");
    private readonly Button _connect, _previous, _next, _refresh, _import, _cancel;
    private readonly StackPanel _connectionFields = new() { Spacing = 7 };
    private readonly StackPanel _pointerFields = new() { Spacing = 7 };
    private readonly StackPanel _tokenFields = new() { Spacing = 7 };
    private readonly StackPanel _catalogFields = new() { Spacing = 7 };
    private IDataSource? _source;
    private SourcePager? _pager;
    private CancellationTokenSource? _operation;
    private int _offset;
    private bool _busy, _changing, _disposed, _more;
    public TableDefinition? ImportedTable { get; private set; }
    public event Action? ImportCompleted;
    public bool IsBusy => _busy;
    public Func<string, bool>? NameAvailable { get; set; }

    public ExternalDataControl(Func<string, Task<string?>>? pickText, Func<Task<IDataSource?>>? pickSqlite, string initialProvider = "JSON file")
    {
        _pickText = pickText; _pickSqlite = pickSqlite;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "ExternalDataWorkspace");
        var root = OfficeVisuals.Grid("Auto,*,Auto", "260,*"); root.Background = OfficeVisuals.Brush("FFFFFF");
        var heading = OfficeVisuals.Stack(OfficeVisuals.Text("Get External Data", 24, "A4373A"), OfficeVisuals.Text("Choose a data source, preview its tables, and import an editable copy.", 13, "666666"));
        heading.Margin = new(18, 8, 18, 16); OfficeVisuals.Add(root, heading, columnSpan: 2);
        var left = new StackPanel { Spacing = 9, Margin = new(14) };
        left.Children.Add(OfficeVisuals.Text("Data source", 15, bold: true));
        EditorVisuals.Labeled(left, "Source type", _provider);
        AutomationProperties.SetAutomationId(_provider, "SourceProvider");
        EditorVisuals.Labeled(_connectionFields, "Endpoint URL", _address); left.Children.Add(_connectionFields);
        EditorVisuals.Labeled(_tokenFields, "Gateway access token", _token); left.Children.Add(_tokenFields);
        EditorVisuals.Labeled(_pointerFields, "JSON Pointer", _pointer); left.Children.Add(_pointerFields);
        AutomationProperties.SetAutomationId(_address, "SourceEndpoint"); AutomationProperties.SetAutomationId(_pointer, "SourceJsonPointer"); AutomationProperties.SetAutomationId(_token, "SourceAccessToken");
        _hint.TextWrapping = TextWrapping.Wrap; left.Children.Add(_hint);
        _connect = OfficeVisuals.Button("Browse / Connect", () => Run(ConnectAsync), "open", "SourceConnect"); left.Children.Add(_connect);
        EditorVisuals.Labeled(_catalogFields, "Available databases", _sourceList); left.Children.Add(_catalogFields); AutomationProperties.SetAutomationId(_sourceList, "GatewaySources");
        EditorVisuals.Labeled(left, "Tables", _tables); AutomationProperties.SetAutomationId(_tables, "SourceTables");
        left.Children.Add(OfficeVisuals.Text("Import options", 15, bold: true));
        EditorVisuals.Labeled(left, "Local table name", _name); AutomationProperties.SetAutomationId(_name, "SourceImportName");
        EditorVisuals.Labeled(left, "Maximum imported records", _maximum);
        OfficeVisuals.Add(root, OfficeVisuals.Border(new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "F5F5F5", thickness: new(0, 0, 1, 0)), 1);
        var right = OfficeVisuals.Grid("Auto,Auto,*,Auto"); right.Margin = new(14, 0, 0, 0);
        var label = OfficeVisuals.Text("Read-only preview", 15, bold: true); label.Margin = new(0, 0, 0, 8); OfficeVisuals.Add(right, label);
        OfficeVisuals.Add(right, _error, 1); OfficeVisuals.Add(right, _preview, 2);
        _previous = OfficeVisuals.Button("◀ Previous", () => Run(ct => PageAsync(Math.Max(0, _offset - 200), ct)), automationId: "SourcePrevious");
        _next = OfficeVisuals.Button("Next ▶", () => Run(ct => PageAsync(_offset + 200, ct)), automationId: "SourceNext");
        _refresh = OfficeVisuals.Button("Refresh", () => Run(async ct => { _pager?.Invalidate(); await PageAsync(0, ct); }), "refresh", "SourceRefresh");
        var navigation = OfficeVisuals.Row(_previous, _next, _refresh); navigation.Margin = new(0, 8, 0, 0); OfficeVisuals.Add(right, navigation, 3);
        OfficeVisuals.Add(root, right, 1, 1);
        var footer = OfficeVisuals.Grid("Auto,Auto", "*,Auto,Auto"); footer.Margin = new(14, 12, 0, 0);
        OfficeVisuals.Add(footer, _status);
        _cancel = OfficeVisuals.Button("Cancel operation", () => _operation?.Cancel()); OfficeVisuals.Add(footer, _cancel, column: 1);
        _import = OfficeVisuals.Button("Import table", () => Run(ImportAsync), "table", "SourceImport"); OfficeVisuals.Add(footer, _import, column: 2);
        var note = OfficeVisuals.Text("Import creates a local snapshot. Source records are never modified. Online credentials stay on the gateway; this token is not saved.", 11, "666666"); note.TextWrapping = TextWrapping.Wrap; note.Margin = new(0, 8, 0, 0);
        OfficeVisuals.Add(footer, note, 1, columnSpan: 3); OfficeVisuals.Add(root, footer, 2, columnSpan: 2);
        Content = root;
        _provider.SelectedItem = initialProvider;
        _provider.SelectionChanged += (_, _) => { if (!_busy) { SetMode(); _ = ClearSourceAsync(); } };
        _sourceList.SelectionChanged += (_, _) => { if (!_changing && !_busy && _sourceList.SelectedItem is GatewaySource source) Run(ct => UseSourceAsync(new GatewayDataSource(_address.Text, _token.Password, source), ct)); };
        _tables.SelectionChanged += (_, _) => { if (!_changing && !_busy) Run(ct => PageAsync(0, ct)); };
        SetMode(); UpdateButtons();
    }
    private void SetMode()
    {
        var mode = _provider.SelectedItem as string ?? "JSON file"; var remote = mode is "PostgreSQL" or "MySQL / MariaDB" or "SQL Server";
        _connectionFields.Visibility = mode == "JSON URL" || remote ? Visibility.Visible : Visibility.Collapsed;
        _tokenFields.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
        _pointerFields.Visibility = mode.StartsWith("JSON", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        _catalogFields.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
        _hint.Text = remote ? "Connect to a DataSpace gateway configured for your database. Database connection strings are entered on the server, not here." : mode == "SQLite file" ? "Open a local SQLite file (up to 16 MiB). The source is read-only; imports and exports create separate copies." : "Use an array of objects. Nested values and non-integer numbers are preserved as text; the optional pointer selects a nested array.";
    }
    private async void Run(Func<CancellationToken, Task> action)
    {
        if (_busy || _disposed) return; _busy = true; _error.Text = ""; _operation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); UpdateButtons();
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { if (!_disposed) SetStatus("Operation cancelled. No partial table was imported."); }
        catch (Exception error) { if (!_disposed) { _error.Text = error.Message; SetStatus("Operation not completed."); } }
        finally { _operation.Dispose(); _operation = null; _busy = false; if (!_disposed) UpdateButtons(); }
    }
    private async Task ConnectAsync(CancellationToken ct)
    {
        var mode = _provider.SelectedItem as string ?? "JSON file";
        SetStatus("Opening source…");
        if (mode == "JSON file")
        {
            var json = _pickText is null ? throw new DataSpaceException("File import is not configured by the host.") : await _pickText("json");
            ct.ThrowIfCancellationRequested(); if (json is null) { SetStatus("No file selected."); return; }
            await UseSourceAsync(new JsonDataSource(json, "JSON file", _pointer.Text), ct);
        }
        else if (mode == "SQLite file")
        {
            var source = _pickSqlite is null ? throw new DataSpaceException("SQLite support is not configured by the host.") : await _pickSqlite();
            if (source is not null) await UseSourceAsync(source, ct); else SetStatus("No file selected.");
        }
        else if (mode == "JSON URL")
        {
            using var client = SourceHttp.CreateClient(); await UseSourceAsync(await SourceHttp.OpenJsonAsync(client, _address.Text, _pointer.Text, ct), ct);
        }
        else
        {
            await ClearSourceAsync();
            var sources = await GatewayDataSource.DiscoverAsync(_address.Text, _token.Password, ct); if (_disposed) return;
            var preferred = mode == "PostgreSQL" ? "postgresql" : mode == "SQL Server" ? "sqlserver" : "mysql";
            _changing = true; _sourceList.Items.Clear(); foreach (var source in sources) _sourceList.Items.Add(source);
            _sourceList.SelectedItem = sources.FirstOrDefault(s => s.Provider.Equals(preferred, StringComparison.OrdinalIgnoreCase) || preferred == "mysql" && s.Provider.Equals("mariadb", StringComparison.OrdinalIgnoreCase)) ?? sources.FirstOrDefault(); _changing = false;
            if (_sourceList.SelectedItem is not GatewaySource selected) throw new DataSpaceException("The gateway exposes no databases.");
            await UseSourceAsync(new GatewayDataSource(_address.Text, _token.Password, selected), ct);
        }
    }
    private async Task UseSourceAsync(IDataSource source, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested(); var tables = await source.GetTablesAsync(ct); ct.ThrowIfCancellationRequested();
            if (_disposed) { await source.DisposeAsync(); return; }
            await ClearSourceAsync(); _source = source; _pager = new(source);
            _changing = true; foreach (var table in tables) _tables.Items.Add(table); if (_tables.Items.Count > 0) _tables.SelectedIndex = 0; _changing = false;
            await PageAsync(0, ct);
        }
        catch { if (!ReferenceEquals(_source, source)) await source.DisposeAsync(); throw; }
    }
    private async Task PageAsync(int offset, CancellationToken ct)
    {
        if (_pager is null || _tables.SelectedItem is not SourceTable table) return;
        SetStatus("Reading table page…"); var page = await _pager.ReadAsync(new(table.Id, offset, 200), ct); ct.ThrowIfCancellationRequested(); if (_disposed) return;
        _offset = offset; _more = page.HasMore;
        _preview.SetData(SourceImport.Fields(page.Columns), SourceImport.Records(page), true);
        SetStatus($"{_source!.DisplayName} · records {(page.Rows.Length == 0 ? 0 : offset + 1):N0}–{offset + page.Rows.Length:N0} · {_pager.Reads} page reads / {_pager.CacheHits} cache hits");
    }
    private async Task ImportAsync(CancellationToken ct)
    {
        if (_source is null || _tables.SelectedItem is not SourceTable table) return;
        if (!double.IsFinite(_maximum.Value)) throw new DataSpaceException("Enter a maximum record count.");
        var name = _name.Text; Names.Validate(name);
        if (NameAvailable?.Invoke(name) == false) throw new DataSpaceException("A local table already has that name. Choose another name.");
        SetStatus("Importing complete table into a detached local copy…");
        var imported = await SourceImport.ReadTableAsync(_source, table, name, (int)_maximum.Value, cancellationToken: ct);
        ct.ThrowIfCancellationRequested(); if (_disposed) return; ImportedTable = imported; ImportCompleted?.Invoke();
    }
    private async Task ClearSourceAsync()
    {
        var source = _source; _source = null; _pager = null; _offset = 0; _more = false;
        _changing = true; _tables.Items.Clear(); _changing = false;
        if (!_disposed) _preview.SetData([], [], true);
        if (source is not null) await source.DisposeAsync();
        if (!_disposed) UpdateButtons();
    }
    private void SetStatus(string value) => _status.Text = value;
    private void UpdateButtons()
    {
        _provider.IsEnabled = _connect.IsEnabled = _address.IsEnabled = _token.IsEnabled = _pointer.IsEnabled = !_busy;
        _sourceList.IsEnabled = _tables.IsEnabled = !_busy;
        _cancel.IsEnabled = _busy; _import.IsEnabled = !_busy && _source is not null && _tables.SelectedItem is SourceTable;
        _previous.IsEnabled = !_busy && _offset > 0; _next.IsEnabled = !_busy && _more; _refresh.IsEnabled = !_busy && _source is not null;
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _operation?.Cancel(); _token.Password = "";
        await ClearSourceAsync(); _preview.Dispose();
    }
}
