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
            // The message window was brought forward while the click was still
            // current, and Explorer's input queue was then detached. Do not
            // attach again here. MenuBase captures the mouse as this popup
            // opens, and a later foreground change releases that capture, so
            // the menu paints and then closes. The popup also answers
            // WM_MOUSEACTIVATE with MA_NOACTIVATE, and the anchor is
            // WS_EX_NOACTIVATE, so neither can hold foreground.
            if (foreground != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(foreground);
            menu.IsOpen = true;
            var notify = foreground != IntPtr.Zero ? foreground : hwnd;
            if (notify != IntPtr.Zero)
                NativeMethods.PostMessage(notify, 0, IntPtr.Zero, IntPtr.Zero);
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
