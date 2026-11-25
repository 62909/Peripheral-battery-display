using System.Windows;
using System.Windows.Interop;
using PeripheralBattery.Native;

namespace PeripheralBattery.Ui;

internal static class WindowChrome
{
    public static void Apply(Window window, bool noActivate)
    {
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
}
