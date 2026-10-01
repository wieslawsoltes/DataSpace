using DataSpace.Storage;
using Windows.ApplicationModel.DataTransfer;

namespace DataSpace.Controls;

public sealed record CellEdit(string RecordId, string Field, string? Value);

/// <summary>Two-axis virtualized datasheet; consumes lazy views without eagerly copying records.</summary>
public sealed partial class DatasheetControl : UserControl, IDisposable
{
    private readonly DatasheetRenderer _renderer = new();
    private readonly SkiaSurface _surface = new();
    private readonly Grid _viewport = new();
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Height = 14, MinWidth = 0 };
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Width = 14, MinHeight = 0 };
    private readonly TextBox _editor = OfficeVisuals.Input();
    private IReadOnlyList<FieldDefinition> _fields = [];
    private IReadOnlyList<Record> _records = [];
    private bool _editing, _committing, _selecting, _selectionCaptured;
    private int _resizeColumn = -1;
    private double _resizeStart, _originalWidth;
    private Point _selectionOrigin;
    private string? _editingRecord, _editingField;
    public DatasheetViewState ViewState { get; } = new();
    public IReadOnlyList<FieldDefinition> Fields => _fields;
    public IReadOnlyList<Record> Records => _records;
    public Func<IReadOnlyList<CellEdit>, bool>? CommitEdits { get; set; }
    public event Action? NewRecordRequested;
    public event Action<IReadOnlyList<string>>? DeleteRecordsRequested;
    public event Action<string, bool>? SortRequested;
    public event Action<string, double>? ColumnWidthChanged;
    public event Action? SelectionChanged;
    public event Action<string>? Error;
    public DatasheetControl()
    {
        IsTabStop = true; UseSystemFocusVisuals = true;
        AutomationProperties.SetName(this, "Datasheet. Use arrow keys to navigate, F2 to edit, and Control+C to copy.");
        AutomationProperties.SetAutomationId(this, "Datasheet");
        var root = OfficeVisuals.Grid("*,14", "*,14");
        _surface.Painter = (canvas, width, height) => _renderer.Draw(canvas, width, height, _fields, _records, ViewState);
        _viewport.Children.Add(_surface);
        _editor.Visibility = Visibility.Collapsed; _editor.HorizontalAlignment = HorizontalAlignment.Left; _editor.VerticalAlignment = VerticalAlignment.Top;
        _editor.BorderBrush = OfficeVisuals.Brush("DFA63D"); _editor.BorderThickness = new(2); _editor.Padding = new(6, 1, 4, 1); _editor.MinHeight = 0;
        AutomationProperties.SetAutomationId(_editor, "CellEditor"); _viewport.Children.Add(_editor);
        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { CancelEdit(); e.Handled = true; }
            else if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                if (FinishEdit()) Move(e.Key == VirtualKey.Enter ? 1 : 0, e.Key == VirtualKey.Tab ? (OfficeVisuals.ShiftDown ? -1 : 1) : 0, false);
                e.Handled = true;
            }
        };
        _editor.LostFocus += (_, _) => { if (_editing && !_committing) FinishEdit(false); };
        OfficeVisuals.Add(root, _viewport); OfficeVisuals.Add(root, _vertical, column: 1); OfficeVisuals.Add(root, _horizontal, row: 1);
        _horizontal.ValueChanged += (_, _) => { if (_editing && !FinishEdit(false)) return; ViewState.OffsetX = (float)_horizontal.Value; _surface.Invalidate(); };
        _vertical.ValueChanged += (_, _) => { if (_editing && !FinishEdit(false)) return; ViewState.OffsetY = (float)_vertical.Value; _surface.Invalidate(); };
        Content = root; _viewport.SizeChanged += (_, _) => UpdateScrollbars();
        _surface.PointerPressed += OnPressed; _surface.PointerMoved += OnMoved; _surface.PointerReleased += OnReleased;
        _surface.PointerCaptureLost += (_, _) => { _selecting = false; _selectionCaptured = false; _resizeColumn = -1; };
        _surface.DoubleTapped += (_, e) =>
        {
            var point = e.GetPosition(_surface);
            var hit = _renderer.HitTest(_fields, _records.Count, ViewState, (float)point.X, (float)point.Y);
            e.Handled = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded) return;
                if (hit.Kind == GridHitKind.ColumnResize) { ViewState.SelectedColumn = hit.Column; BestFitSelectedColumn(); }
                else if (hit.Kind == GridHitKind.Cell) BeginEdit();
            });
        };
        _surface.PointerWheelChanged += (_, e) =>
        {
            if (!FinishEdit(false)) return;
            var delta = e.GetCurrentPoint(_surface).Properties.MouseWheelDelta;
            if (OfficeVisuals.ShiftDown) _horizontal.Value = Math.Clamp(_horizontal.Value - delta, 0, _horizontal.Maximum);
            else _vertical.Value = Math.Clamp(_vertical.Value - delta * .65, 0, _vertical.Maximum);
            e.Handled = true;
        };
        KeyDown += OnKeyDown;
        var menu = new MenuFlyout();
        void Item(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Hide Fields", () => LayoutCommand?.Invoke("hideFields")); Item("Unhide Fields", () => LayoutCommand?.Invoke("columns"));
        Item("Freeze Fields", () => LayoutCommand?.Invoke("freezeFields")); Item("Unfreeze All Fields", () => LayoutCommand?.Invoke("unfreezeFields"));
        Item("Column Width", () => LayoutCommand?.Invoke("columnWidth")); Item("Best Fit", BestFitSelectedColumn);
        Item("Datasheet Formatting", () => LayoutCommand?.Invoke("formatDatasheet"));
        Item("Edit cell", () => BeginEdit()); Item("Copy", async () => await CopyAsync()); Item("Paste", async () => await PasteAsync());
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Sort A to Z", () => { if (_fields.Count > 0) SortRequested?.Invoke(_fields[ViewState.SelectedColumn].Name, false); });
        Item("Sort Z to A", () => { if (_fields.Count > 0) SortRequested?.Invoke(_fields[ViewState.SelectedColumn].Name, true); });
        Item("Delete record", () => { if (!ViewState.ReadOnly) DeleteRecordsRequested?.Invoke(SelectedRecordIds()); }); ContextFlyout = menu;
    }
    public void SetData(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> records, bool readOnly = false)
    {
        var selectedId = SelectedRecord?.Id; var selectedField = SelectedField?.Name;
        _fields = fields; _records = records; ViewState.ReadOnly = readOnly;
        var index = selectedId is null ? -1 : RecordIdentity.IndexOf(records, selectedId);
        ViewState.SelectedRow = Math.Clamp(index >= 0 ? index : ViewState.SelectedRow, 0, Math.Max(0, records.Count - 1));
        if (selectedField is not null) { var found = fields.ToList().FindIndex(f => Names.Equal(f.Name, selectedField)); if (found >= 0) ViewState.SelectedColumn = found; }
        ViewState.SelectedColumn = Math.Clamp(ViewState.SelectedColumn, 0, Math.Max(0, fields.Count - 1));
        ViewState.AnchorRow = Math.Clamp(ViewState.AnchorRow, 0, Math.Max(0, records.Count - 1));
        ViewState.AnchorColumn = Math.Clamp(ViewState.AnchorColumn, 0, Math.Max(0, fields.Count - 1));
        _renderer.InvalidateTotals(); UpdateScrollbars(); _surface.Invalidate(); SelectionChanged?.Invoke();
    }
    public Record? SelectedRecord => ViewState.SelectedRow >= 0 && ViewState.SelectedRow < _records.Count ? _records[ViewState.SelectedRow] : null;
    public FieldDefinition? SelectedField => ViewState.SelectedColumn >= 0 && ViewState.SelectedColumn < _fields.Count ? _fields[ViewState.SelectedColumn] : null;
    public void SetZoom(float zoom) { if (!FinishEdit(false)) return; ViewState.Zoom = Math.Clamp(zoom, .5f, 2); UpdateScrollbars(); _surface.Invalidate(); }
    public void ToggleTotals() { ViewState.ShowTotals = !ViewState.ShowTotals; UpdateScrollbars(); _surface.Invalidate(); }
    public void SelectCell(int row, int column, bool extend = false)
    {
        if (!FinishEdit(false)) return;
        ViewState.SelectedRow = Math.Clamp(row, 0, Math.Max(0, _records.Count - 1)); ViewState.SelectedColumn = Math.Clamp(column, 0, Math.Max(0, _fields.Count - 1));
        if (!extend) { ViewState.AnchorRow = ViewState.SelectedRow; ViewState.AnchorColumn = ViewState.SelectedColumn; }
        EnsureVisible(); _surface.Invalidate(); SelectionChanged?.Invoke();
        if (SelectedRecord is { } record && SelectedField is { } field) AutomationProperties.SetName(this, $"{field.DisplayName}: {FieldValues.Display(field, record[field.Name])}. Record {ViewState.SelectedRow + 1} of {_records.Count}. F2 to edit.");
    }
    private void Move(int rows, int columns, bool extend) => SelectCell(ViewState.SelectedRow + rows, ViewState.SelectedColumn + columns, extend);
    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!FinishEdit(false)) return;
        Focus(FocusState.Pointer); var point = e.GetCurrentPoint(_surface);
        var hit = _renderer.HitTest(_fields, _records.Count, ViewState, (float)point.Position.X, (float)point.Position.Y);
        if (!point.Properties.IsLeftButtonPressed)
        { if (point.Properties.IsRightButtonPressed && hit.Column >= 0) SelectCell(Math.Max(0, hit.Row), hit.Column); return; }
        if (hit.Kind == GridHitKind.ColumnResize)
        { _resizeColumn = hit.Column; _resizeStart = point.Position.X; _originalWidth = _fields[hit.Column].Width; _surface.CapturePointer(e.Pointer); }
        else if (hit.Kind == GridHitKind.ColumnHeader) SortRequested?.Invoke(_fields[hit.Column].Name, Names.Equal(ViewState.SortField, _fields[hit.Column].Name) && !ViewState.SortDescending);
        else if (hit.Kind == GridHitKind.NewRecord) { if (!ViewState.ReadOnly) NewRecordRequested?.Invoke(); }
        else if (hit.Kind == GridHitKind.Corner) SelectAll();
        else if (hit.Kind is GridHitKind.Cell or GridHitKind.RowHeader)
        {
            SelectCell(hit.Row, hit.Column < 0 ? 0 : hit.Column, OfficeVisuals.ShiftDown);
            if (hit.Kind == GridHitKind.RowHeader) { ViewState.AnchorColumn = 0; ViewState.SelectedColumn = Math.Max(0, _fields.Count - 1); _surface.Invalidate(); }
            _selecting = true; _selectionCaptured = false; _selectionOrigin = point.Position;
        }
        e.Handled = true;
    }
    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface); var position = point.Position;
        if (_resizeColumn >= 0)
        {
            _fields[_resizeColumn].Width = Math.Clamp(_originalWidth + (position.X - _resizeStart) / ViewState.Zoom, 40, 2000); UpdateScrollbars(); _surface.Invalidate(); e.Handled = true;
        }
        else if (_selecting)
        {
            if (!point.Properties.IsLeftButtonPressed) { _selecting = false; return; }
            if (!_selectionCaptured)
            {
                if (Math.Abs(position.X - _selectionOrigin.X) < 4 && Math.Abs(position.Y - _selectionOrigin.Y) < 4) return;
                _selectionCaptured = _surface.CapturePointer(e.Pointer);
            }
            var hit = _renderer.HitTest(_fields, _records.Count, ViewState, (float)position.X, (float)position.Y);
            if (hit.Kind == GridHitKind.Cell) SelectCell(hit.Row, hit.Column, true);
        }
    }
    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        var captured = _selectionCaptured || _resizeColumn >= 0;
        if (_resizeColumn >= 0)
        {
            var field = _fields[_resizeColumn]; var width = field.Width;
            field.Width = _originalWidth; _resizeColumn = -1; ColumnWidthChanged?.Invoke(field.Name, width);
        }
        _selecting = false; _selectionCaptured = false; if (captured) _surface.ReleasePointerCapture(e.Pointer);
    }
    public void BeginEdit(string? initialText = null)
    {
        if (ViewState.ReadOnly || SelectedRecord is not { } record || SelectedField is not { } field || field.Type == FieldType.AutoNumber) return;
        if (field.Type == FieldType.YesNo)
        { var value = FieldValues.Parse(field, record[field.Name]) is not true; CommitEdits?.Invoke([new(record.Id, field.Name, value.ToString())]); return; }
        EnsureVisible(); var bounds = _renderer.CellBounds(_fields, ViewState.SelectedRow, ViewState.SelectedColumn, ViewState);
        _editingRecord = record.Id; _editingField = field.Name; _editing = true;
        _editor.Margin = new(bounds.Left * ViewState.Zoom, bounds.Top * ViewState.Zoom, 0, 0);
        _editor.Width = bounds.Width * ViewState.Zoom; _editor.Height = bounds.Height * ViewState.Zoom; _editor.FontSize = ViewState.FontSize * ViewState.Zoom;
        _editor.Text = initialText ?? record[field.Name] ?? ""; _editor.Visibility = Visibility.Visible;
        AutomationProperties.SetName(_editor, "Edit " + field.DisplayName); _editor.Focus(FocusState.Programmatic);
        if (initialText is null) _editor.SelectAll(); else _editor.SelectionStart = _editor.Text.Length;
    }
    public bool FinishEdit(bool focus = true)
    {
        if (!_editing || _committing) return true; _committing = true;
        try
        {
            var index = _editingRecord is null ? -1 : RecordIdentity.IndexOf(_records, _editingRecord);
            var record = index < 0 ? null : _records[index]; var value = _editor.Text;
            if (record is not null && _editingField is { } field && value != (record[field] ?? "") && CommitEdits?.Invoke([new(record.Id, field, value)]) == false) return false;
            _editing = false; _editor.Visibility = Visibility.Collapsed;
            if (focus) Focus(FocusState.Programmatic); return true;
        }
        finally { _committing = false; }
    }
    public void CancelEdit() { _editing = false; _editor.Visibility = Visibility.Collapsed; Focus(FocusState.Programmatic); }
    public IReadOnlyList<string> SelectedRecordIds()
    {
        var first = Math.Max(0, Math.Min(ViewState.AnchorRow, ViewState.SelectedRow)); var last = Math.Min(_records.Count - 1, Math.Max(ViewState.AnchorRow, ViewState.SelectedRow));
        var ids = new string[Math.Max(0, last - first + 1)]; for (var i = 0; i < ids.Length; i++) ids[i] = _records[first + i].Id; return ids;
    }
    public void SelectAll()
    { ViewState.AnchorRow = 0; ViewState.AnchorColumn = 0; ViewState.SelectedRow = Math.Max(0, _records.Count - 1); ViewState.SelectedColumn = Math.Max(0, _fields.Count - 1); _surface.Invalidate(); SelectionChanged?.Invoke(); }
    public Task CopyAsync()
    {
        if (_records.Count == 0 || _fields.Count == 0) return Task.CompletedTask;
        try
        {
            var firstColumn = Math.Min(ViewState.AnchorColumn, ViewState.SelectedColumn); var lastColumn = Math.Max(ViewState.AnchorColumn, ViewState.SelectedColumn);
            var fields = _fields.Skip(firstColumn).Take(lastColumn - firstColumn + 1).ToArray();
            var firstRow = Math.Min(ViewState.AnchorRow, ViewState.SelectedRow); var lastRow = Math.Max(ViewState.AnchorRow, ViewState.SelectedRow);
            IEnumerable<Record> Rows() { for (var row = firstRow; row <= lastRow; row++) yield return _records[row]; }
            var data = new DataPackage(); data.SetText(CsvCodec.Export(fields, Rows(), new() { Delimiter = '\t', HasHeaders = false })); Clipboard.SetContent(data);
        }
        catch (Exception error) { Error?.Invoke("Clipboard copy failed: " + error.Message); }
        return Task.CompletedTask;
    }
    public async Task PasteAsync()
    {
        if (ViewState.ReadOnly || !FinishEdit(false)) return;
        try
        {
            var content = Clipboard.GetContent(); if (!content.Contains(StandardDataFormats.Text)) return;
            var rows = CsvCodec.Parse(await content.GetTextAsync(), new() { Delimiter = '\t', HasHeaders = false, MaximumRecords = 10000 });
            var edits = new List<CellEdit>(); var startRow = Math.Min(ViewState.SelectedRow, ViewState.AnchorRow); var startColumn = Math.Min(ViewState.SelectedColumn, ViewState.AnchorColumn);
            for (var row = 0; row < rows.Count; row++) for (var column = 0; column < rows[row].Count; column++)
            {
                if (startRow + row >= _records.Count || startColumn + column >= _fields.Count) throw new DataSpaceException("The pasted range extends beyond existing records or fields. Add records first.");
                var field = _fields[startColumn + column]; if (field.Type == FieldType.AutoNumber) throw new DataSpaceException("AutoNumber cells cannot be pasted over.");
                edits.Add(new(_records[startRow + row].Id, field.Name, rows[row][column]));
            }
            CommitEdits?.Invoke(edits);
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
    }
    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editing) return;
        if (OfficeVisuals.ControlDown)
        {
            if (e.Key == VirtualKey.C) { await CopyAsync(); e.Handled = true; }
            else if (e.Key == VirtualKey.V) { await PasteAsync(); e.Handled = true; }
            else if (e.Key == VirtualKey.A) { SelectAll(); e.Handled = true; }
            else if (e.Key == VirtualKey.Home) { SelectCell(0, 0); e.Handled = true; }
            else if (e.Key == VirtualKey.End) { SelectCell(_records.Count - 1, _fields.Count - 1); e.Handled = true; } return;
        }
        var extend = OfficeVisuals.ShiftDown;
        switch (e.Key)
        {
            case VirtualKey.Up: Move(-1, 0, extend); break; case VirtualKey.Down: Move(1, 0, extend); break;
            case VirtualKey.Left: Move(0, -1, extend); break; case VirtualKey.Right: Move(0, 1, extend); break;
            case VirtualKey.Home: SelectCell(ViewState.SelectedRow, 0, extend); break; case VirtualKey.End: SelectCell(ViewState.SelectedRow, _fields.Count - 1, extend); break;
            case VirtualKey.PageUp: Move(-(int)(_viewport.ActualHeight / (ViewState.RowHeight * ViewState.Zoom)), 0, extend); break;
            case VirtualKey.PageDown: Move((int)(_viewport.ActualHeight / (ViewState.RowHeight * ViewState.Zoom)), 0, extend); break;
            case VirtualKey.F2: case VirtualKey.Enter: BeginEdit(); break;
            case VirtualKey.Insert: if (!ViewState.ReadOnly) NewRecordRequested?.Invoke(); break;
            case VirtualKey.Delete: if (!ViewState.ReadOnly) DeleteRecordsRequested?.Invoke(SelectedRecordIds()); break;
            default: return;
        }
        e.Handled = true;
    }
    private void UpdateScrollbars()
    {
        var width = Math.Max(0, _viewport.ActualWidth / ViewState.Zoom - _renderer.Theme.RowHeaderWidth);
        var height = Math.Max(0, _viewport.ActualHeight / ViewState.Zoom - _renderer.Theme.ColumnHeaderHeight - (ViewState.ShowTotals ? ViewState.RowHeight : 0));
        _horizontal.Maximum = Math.Max(0, _renderer.ContentWidth(_fields) - _renderer.Theme.RowHeaderWidth - width);
        var scrollingWidth = Math.Max(0, width - (_renderer.FrozenEdge(_fields, ViewState) - _renderer.Theme.RowHeaderWidth));
        _horizontal.ViewportSize = scrollingWidth; _horizontal.SmallChange = 30; _horizontal.LargeChange = Math.Max(30, scrollingWidth);
        _vertical.Maximum = Math.Max(0, _renderer.ContentHeight(_records.Count, ViewState) - _renderer.Theme.ColumnHeaderHeight - height);
        _vertical.ViewportSize = height; _vertical.SmallChange = ViewState.RowHeight; _vertical.LargeChange = Math.Max(27, height);
        _horizontal.Value = Math.Clamp(_horizontal.Value, 0, _horizontal.Maximum); _vertical.Value = Math.Clamp(_vertical.Value, 0, _vertical.Maximum);
        ViewState.OffsetX = (float)_horizontal.Value; ViewState.OffsetY = (float)_vertical.Value; _surface.Invalidate();
    }
    private void EnsureVisible()
    {
        var bounds = _renderer.CellBounds(_fields, ViewState.SelectedRow, ViewState.SelectedColumn, ViewState);
        var width = _viewport.ActualWidth / ViewState.Zoom; var height = _viewport.ActualHeight / ViewState.Zoom;
        var leftEdge = ViewState.SelectedColumn < ViewState.FrozenColumnCount ? _renderer.Theme.RowHeaderWidth : _renderer.FrozenEdge(_fields, ViewState);
        if (ViewState.SelectedColumn >= ViewState.FrozenColumnCount && bounds.Left < leftEdge) _horizontal.Value = Math.Clamp(_horizontal.Value + bounds.Left - leftEdge, 0, _horizontal.Maximum);
        else if (ViewState.SelectedColumn >= ViewState.FrozenColumnCount && bounds.Right > width) _horizontal.Value = Math.Clamp(_horizontal.Value + bounds.Right - width, 0, _horizontal.Maximum);
        if (bounds.Top < _renderer.Theme.ColumnHeaderHeight) _vertical.Value = Math.Clamp(_vertical.Value + bounds.Top - _renderer.Theme.ColumnHeaderHeight, 0, _vertical.Maximum);
        else if (bounds.Bottom > height) _vertical.Value = Math.Clamp(_vertical.Value + bounds.Bottom - height, 0, _vertical.Maximum);
    }
    public void Dispose() => _renderer.Dispose();
}
