using SkiaSharp;
using SkiaSharp.Views.Windows;
using Uno.WinUI.Graphics2DSK;
using Windows.UI;

namespace DataSpace.Controls;

public static class OfficeVisuals
{
    public static SolidColorBrush Brush(string color)
    {
        var parsed = SKColor.Parse(color); return new(Color.FromArgb(parsed.Alpha, parsed.Red, parsed.Green, parsed.Blue));
    }
    public static TextBlock Text(string text, double size = 13, string color = "252525", bool bold = false)
        => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(bold ? 600 : 400) }, VerticalAlignment = VerticalAlignment.Center };
    public static void Style(Control control, string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style) control.Style = style;
    }
    public static Button Button(string label, Action action, string? icon = null, string? automationId = null)
    {
        var button = new Button { Content = label, MinWidth = 0, MinHeight = 25, Padding = new(7, 3, 7, 3), FontSize = 12 };
        Style(button, "OfficeButtonStyle");
        if (icon is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            panel.Children.Add(new OfficeIcon(icon) { Width = 17, Height = 17 }); panel.Children.Add(Text(label, 12)); button.Content = panel;
        }
        AutomationProperties.SetName(button, label); if (automationId is not null) AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action(); return button;
    }
    public static TextBox Input(string value = "", string placeholder = "", double width = double.NaN)
    {
        var input = new TextBox { Text = value, PlaceholderText = placeholder, Width = width, MinHeight = 28, FontSize = 13, Padding = new(6, 3, 6, 3) };
        Style(input, "OfficeTextBoxStyle"); return input;
    }
    public static ComboBox Combo(IEnumerable<string> items, string? selected = null, double width = double.NaN)
    {
        var box = new ComboBox { Width = width, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        Style(box, "OfficeComboBoxStyle"); foreach (var item in items) box.Items.Add(item);
        box.SelectedItem = selected; if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0; return box;
    }
    public static Border Border(UIElement child, string background = "FFFFFF", string border = "D0D0D0", Thickness? thickness = null, Thickness? padding = null)
        => new() { Child = child, Background = Brush(background), BorderBrush = Brush(border), BorderThickness = thickness ?? new(1), Padding = padding ?? new(0) };
    public static Grid Grid(string rows = "*", string columns = "*")
    {
        static GridLength Length(string value) => value == "Auto" ? GridLength.Auto : value.EndsWith('*') ? new GridLength(value.Length == 1 ? 1 : double.Parse(value[..^1], FieldValues.Culture), GridUnitType.Star) : new GridLength(double.Parse(value, FieldValues.Culture));
        var grid = new Grid(); foreach (var row in rows.Split(',')) grid.RowDefinitions.Add(new() { Height = Length(row) });
        foreach (var column in columns.Split(',')) grid.ColumnDefinitions.Add(new() { Width = Length(column) }); return grid;
    }
    public static void Add(Grid grid, UIElement element, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    { Microsoft.UI.Xaml.Controls.Grid.SetRow(element, row); Microsoft.UI.Xaml.Controls.Grid.SetColumn(element, column); Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(element, rowSpan); Microsoft.UI.Xaml.Controls.Grid.SetColumnSpan(element, columnSpan); grid.Children.Add(element); }
    public static StackPanel Stack(params UIElement[] children)
    { var panel = new StackPanel { Spacing = 8 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params UIElement[] children)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static bool ControlDown => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    public static bool ShiftDown => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}

public sealed class OfficeIcon : UserControl
{
    private readonly SkiaSurface _surface = new();
    private DrawingResources? _drawing;
    private SKColor _color = SKColor.Parse("546C7C");
    public string Icon { get; }
    public SKColor Color { get => _color; set { _color = value; _surface.Invalidate(); } }
    public OfficeIcon(string icon)
    {
        Icon = icon; Width = 24; Height = 24; IsHitTestVisible = false; Content = _surface;
        _surface.Painter = (canvas, width, height) =>
        {
            _drawing ??= new(); IconRenderer.Draw(canvas, Icon, new(0, 0, width, height), Color, _drawing);
        };
        Unloaded += (_, _) => { _drawing?.Dispose(); _drawing = null; };
        Loaded += (_, _) => _surface.Invalidate();
    }
}

/// <summary>Logical-unit Skia drawing on Uno's shared renderer, with a DPI-correct fallback for other backends.</summary>
public sealed class SkiaSurface : UserControl
{
    private readonly DirectCanvas? _direct;
    private readonly SKXamlCanvas? _fallback;
    private Action<SKCanvas, float, float>? _painter;
    public bool UsesSharedCanvas => _direct is not null;
    public Action<SKCanvas, float, float>? Painter { get => _painter; set { _painter = value; Invalidate(); } }
    public SkiaSurface()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        // Drawing children do not participate in pointer hit testing. A non-null
        // transparent brush gives their full logical bounds a hit target whose
        // pointer/gesture events bubble to this reusable surface.
        var inputRoot = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        Content = inputRoot;
        if (SKCanvasElement.IsSupportedOnCurrentPlatform())
        {
            _direct = new DirectCanvas(this) { IsHitTestVisible = false };
            inputRoot.Children.Add(_direct);
        }
        else
        {
            _fallback = new SKXamlCanvas { IsHitTestVisible = false };
            inputRoot.Children.Add(_fallback);
            _fallback.PaintSurface += (_, e) =>
            {
                if (ActualWidth <= 0 || ActualHeight <= 0) return;
                var canvas = e.Surface.Canvas; canvas.Clear(SKColors.Transparent);
                canvas.Save();
                try { canvas.Scale(e.Info.Width / (float)ActualWidth, e.Info.Height / (float)ActualHeight); _painter?.Invoke(canvas, (float)ActualWidth, (float)ActualHeight); }
                finally { canvas.Restore(); }
            };
        }
        SizeChanged += (_, _) => Invalidate(); Loaded += (_, _) => Invalidate();
    }
    public void Invalidate() { _direct?.Invalidate(); _fallback?.Invalidate(); }
    private sealed class DirectCanvas(SkiaSurface owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            canvas.Save();
            try { canvas.ClipRect(new SKRect(0, 0, (float)area.Width, (float)area.Height)); owner._painter?.Invoke(canvas, (float)area.Width, (float)area.Height); }
            finally { canvas.Restore(); }
        }
    }
}
