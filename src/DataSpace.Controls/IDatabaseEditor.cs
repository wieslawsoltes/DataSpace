namespace DataSpace.Controls;

/// <summary>A staged editor. Commit must throw on validation failure and retain the user's draft.</summary>
public interface IDatabaseEditor : IDisposable
{
    bool HasPendingChanges { get; }
    void Commit();
}

internal static class EditorVisuals
{
    public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    public static void Labeled(Panel panel, string label, FrameworkElement input)
    {
        panel.Children.Add(OfficeVisuals.Text(label, 12, "555555"));
        AutomationProperties.SetName(input, label);
        panel.Children.Add(input);
    }
    public static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var control = new CheckBox { Content = text, IsChecked = value, MinHeight = 28, FontSize = 12 };
        control.Checked += (_, _) => changed(true); control.Unchecked += (_, _) => changed(false);
        return control;
    }
}
