using Microsoft.UI.Xaml.Automation.Peers;

namespace DataSpace.Controls;

/// <summary>A visible, explicitly named live-region validation message. Empty messages have no semantic node.</summary>
public sealed class ValidationMessageControl : UserControl
{
    private readonly StackPanel _host = new();
    private string _text = "";

    public ValidationMessageControl()
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
            // Create the semantic text node with its actual message. Updating a
            // TextBlock which entered the Skia tree empty can leave no text peer.
            var text = OfficeVisuals.Text(message, 12, "9C252A");
            text.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetName(text, message);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
            _host.Children.Add(text);
        }
    }
}
