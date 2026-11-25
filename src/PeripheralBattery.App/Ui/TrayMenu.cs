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
    public static void Show(Window anchor, int physicalX, int physicalY, int dpi, IReadOnlyList<MenuEntry> entries)
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

        menu.Closed += (_, _) => anchor.Hide();
        var hwnd = new WindowInteropHelper(anchor).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(hwnd);
        menu.IsOpen = true;
        if (hwnd != IntPtr.Zero)
            NativeMethods.PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
