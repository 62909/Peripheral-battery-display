using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Ui;

internal readonly record struct MenuEntry(string? Label, bool Checkable, bool Checked, Action? Invoke, bool Separator = false);

internal static class TrayMenu
{
    public static void Show(
        Window anchor,
        IntPtr foreground,
        int physicalX,
        int physicalY,
        int dpi,
        IReadOnlyList<MenuEntry> entries,
        Action closed)
    {
        var finished = false;

        void Finish()
        {
            if (finished)
                return;
            finished = true;
            try
            {
                if (anchor.IsLoaded)
                    anchor.Hide();
            }
            finally
            {
                closed();
            }
        }

        try
        {
            anchor.Left = TaskbarPlacement.PxToDip(physicalX, dpi);
            anchor.Top = TaskbarPlacement.PxToDip(physicalY, dpi);
            anchor.Show();
            var menu = new ContextMenu
            {
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 13,
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            foreach (var entry in entries)
            {
                if (entry.Separator)
                {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var item = new MenuItem
                {
                    Header = entry.Label,
                    IsCheckable = entry.Checkable,
                    IsChecked = entry.Checked,
                };
                var action = entry.Invoke;
                item.Click += (_, _) => action?.Invoke();
                menu.Items.Add(item);
            }

            menu.Closed += (_, _) => Finish();
            var hwnd = new WindowInteropHelper(anchor).Handle;
            // Foreground has to be the tray message window or the popup.
            // The anchor is WS_EX_NOACTIVATE, so SetForegroundWindow on it
            // alone does not keep the menu up once Explorer owns the pill.
            if (foreground != IntPtr.Zero)
                NativeMethods.MakeForeground(foreground);
            if (hwnd != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(hwnd);
            menu.IsOpen = true;
            if (PresentationSource.FromVisual(menu) is HwndSource source && source.Handle != IntPtr.Zero)
                NativeMethods.MakeForeground(source.Handle);
            if (hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            if (!menu.IsOpen)
                Finish();
        }
        catch
        {
            Finish();
            throw;
        }
    }
}
