using Microsoft.UI.Xaml.Automation.Peers;

namespace DataSpace.Controls;

/// <summary>A visible polite status announcement that keeps Uno/Skia semantic text synchronized.</summary>
public sealed class StatusMessageControl : UserControl
{
    private readonly StackPanel _host = new();
    private string _text = "";

    public StatusMessageControl()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _host;
    }

    public string Text
    {
        get => _text;
        set
        {
            var message = value ?? "";
            if (_text == message) return;
            _text = message;
            _host.Children.Clear();
            if (message.Length == 0) return;
            // Some shared-canvas text peers retain the initial name after a text
            // update. A fresh, explicitly named node makes the visual status and
            // assistive-technology announcement describe the same completed work.
            var text = OfficeVisuals.Text(message, 12, "666666");
            text.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetName(text, message);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            _host.Children.Add(text);
        }
    }
}
