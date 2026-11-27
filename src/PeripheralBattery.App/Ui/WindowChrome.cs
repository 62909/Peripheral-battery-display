using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PeripheralBattery.Native;

namespace PeripheralBattery.Ui;

internal static class WindowChrome
{
    private static ImageSource? _icon;

    public static void Apply(Window window, bool noActivate)
    {
        window.Icon = Icon;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
            style |= NativeMethods.WsExToolWindow;
            style &= ~NativeMethods.WsExAppWindow;
            if (noActivate)
                style |= NativeMethods.WsExNoActivate;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, style);
        };
    }

    private static ImageSource Icon => _icon ??= LoadIcon();

    private static ImageSource LoadIcon()
    {
        var uri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
        var resource = Application.GetResourceStream(uri);
        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).First();
        frame.Freeze();
        return frame;
    }
}
