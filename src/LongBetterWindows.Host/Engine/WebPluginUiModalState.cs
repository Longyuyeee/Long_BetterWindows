using System.Text.Json;
using System.Windows;

namespace LongBetterWindows.Host.Engine;

internal static class WebPluginUiModalState
{
    private const string MessageType = "long.ui-modal-state";

    private static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached(
            "IsOpen",
            typeof(bool),
            typeof(WebPluginUiModalState),
            new PropertyMetadata(false));

    internal static bool IsOpen(DependencyObject? element)
        => element is not null && (bool)element.GetValue(IsOpenProperty);

    internal static void SetOpen(DependencyObject element, bool value)
        => element.SetValue(IsOpenProperty, value);

    internal static bool TryRead(string json, out bool isOpen)
    {
        isOpen = false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(
                    type.GetString(),
                    MessageType,
                    StringComparison.Ordinal)
                || !root.TryGetProperty("open", out var open)
                || open.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            isOpen = open.GetBoolean();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
