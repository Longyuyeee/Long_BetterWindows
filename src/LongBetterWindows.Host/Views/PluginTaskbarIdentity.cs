using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LongBetterWindows.Host.Views;

internal static class PluginTaskbarIdentity
{
    private const string AppIdPrefix = "LongAssistant.Plugin.";
    private static readonly Guid PropertyStoreGuid =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    internal static void Apply(Window window, string pluginId)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        IPropertyStore? store = null;
        PropVariant value = default;
        try
        {
            var interfaceId = PropertyStoreGuid;
            var result = SHGetPropertyStoreForWindow(
                handle,
                ref interfaceId,
                out store);
            if (result != 0 || store is null)
                return;

            value = PropVariant.FromString(CreateAppUserModelId(pluginId));
            var appUserModelIdKey = AppUserModelIdKey;
            store.SetValue(ref appUserModelIdKey, ref value);
            store.Commit();
        }
        finally
        {
            value.Dispose();
            if (store is not null)
                Marshal.ReleaseComObject(store);
        }
    }

    internal static ImageSource CreateIcon(string pluginId, string title)
    {
        const int size = 64;
        var hash = StableHash(pluginId);
        var hue = (uint)hash % 360;
        var background = FromHsl(hue, 0.58, 0.46);
        var glyph = string.IsNullOrWhiteSpace(title)
            ? "L"
            : StringInfo.GetNextTextElement(title.Trim());

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(
                new SolidColorBrush(background),
                null,
                new Rect(4, 4, size - 8, size - 8),
                14,
                14);
            var text = new FormattedText(
                glyph,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable Display Semibold"),
                30,
                Brushes.White,
                1);
            drawing.DrawText(
                text,
                new Point(
                    (size - text.WidthIncludingTrailingWhitespace) / 2,
                    (size - text.Height) / 2 - 1));
        }

        var bitmap = new RenderTargetBitmap(
            size,
            size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static string CreateAppUserModelId(string pluginId)
    {
        var safe = new string(pluginId
            .Select(character => char.IsLetterOrDigit(character) || character == '.'
                ? character
                : '.')
            .ToArray())
            .Trim('.');
        return AppIdPrefix + (safe.Length == 0 ? "Unknown" : safe);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value.ToUpperInvariant())
        {
            hash ^= character;
            hash = unchecked(hash * prime);
        }
        return hash;
    }

    private static Color FromHsl(uint hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var segment = hue / 60d;
        var secondary = chroma * (1 - Math.Abs(segment % 2 - 1));
        var (red, green, blue) = segment switch
        {
            < 1 => (chroma, secondary, 0d),
            < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary),
            < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };
        var match = lightness - chroma / 2;
        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr window,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        uint GetCount();
        PropertyKey GetAt(uint propertyIndex);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey
    {
        internal PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        private readonly Guid FormatId;
        private readonly uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)]
        private ushort valueType;
        [FieldOffset(8)]
        private IntPtr pointerValue;

        internal static PropVariant FromString(string value)
            => new()
            {
                valueType = 31, // VT_LPWSTR
                pointerValue = Marshal.StringToCoTaskMemUni(value),
            };

        public void Dispose()
        {
            if (valueType == 0)
                return;
            PropVariantClear(ref this);
            valueType = 0;
            pointerValue = IntPtr.Zero;
        }
    }
}
