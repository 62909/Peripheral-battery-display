using PeripheralBattery.Core;

namespace PeripheralBattery.Native;

/// <summary>
/// Reads the taskbar with SHAppBarMessage(ABM_GETTASKBARPOS), which is the
/// supported shell call for the primary bar. Secondary bars are read-only
/// window rectangles of Shell_SecondaryTrayWnd because that API is primary-only.
/// Nothing is injected into Explorer.
/// </summary>
internal sealed class TaskbarWatcher
{
    private readonly Dictionary<string, TaskbarBounds> _lastFull = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _hwnd;
    private bool _shellFullscreen;
    private bool _foregroundFullscreen;

    public bool FullscreenApp => _shellFullscreen || _foregroundFullscreen;
    public IReadOnlyList<TaskbarSnapshot> Current { get; private set; } = [];

    public void Attach(IntPtr hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        var data = NativeMethods.NewAppBar(hwnd);
        data.uCallbackMessage = callbackMessage;
        NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        Poll();
    }

    public void Detach()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var data = NativeMethods.NewAppBar(_hwnd);
        NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
        _hwnd = IntPtr.Zero;
    }

    public void NoteAppBar(int code, IntPtr lParam)
    {
        if (code == NativeMethods.AbnFullscreenApp)
            _shellFullscreen = lParam != IntPtr.Zero;
        Poll();
    }

    public void Poll()
    {
        var found = new List<TaskbarSnapshot>();
        var primaryWindow = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var autoHide = ReadAutoHide();
        if (TryReadPrimary(primaryWindow, autoHide, out var primary))
            found.Add(primary);

        var secondary = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.ClassNameOf(hwnd) == "Shell_SecondaryTrayWnd")
                secondary.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hwnd in secondary)
        {
            if (TryReadSecondary(hwnd, out var snapshot))
                found.Add(snapshot);
        }

        _foregroundFullscreen = ForegroundCovers(found);
        Current = found;
    }

    private bool TryReadPrimary(IntPtr trayWindow, bool autoHide, out TaskbarSnapshot snapshot)
    {
        snapshot = default!;
        var data = NativeMethods.NewAppBar(_hwnd);
        var ok = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) != IntPtr.Zero;
        if (!ok || data.rc.Width <= 0 || data.rc.Height <= 0)
            return false;

        var edge = Enum.IsDefined(typeof(TaskbarEdge), (int)data.uEdge)
            ? (TaskbarEdge)data.uEdge
            : TaskbarEdge.Bottom;
        var bounds = new TaskbarBounds(data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height, edge);
        var monitor = trayWindow != IntPtr.Zero
            ? NativeMethods.MonitorFromWindow(trayWindow, NativeMethods.MonitorDefaultToNearest)
            : NativeMethods.MonitorFromPoint(new POINT { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 }, NativeMethods.MonitorDefaultToNearest);
        if (!TryReadMonitor(monitor, out var info, out var dpi))
            return false;

        var visible = trayWindow != IntPtr.Zero && NativeMethods.GetWindowRect(trayWindow, out var windowRect)
            ? TaskbarPlacement.VisibleThickness(edge, windowRect.Left, windowRect.Top, windowRect.Width, windowRect.Height, info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height)
            : (edge is TaskbarEdge.Top or TaskbarEdge.Bottom ? bounds.Height : bounds.Width);
        var collapsed = TaskbarPlacement.IsCollapsed(autoHide, visible);
        snapshot = Build("primary-window", info, bounds, visible, autoHide, dpi, collapsed, isPrimary: true);
        return true;
    }

    private bool TryReadSecondary(IntPtr hwnd, out TaskbarSnapshot snapshot)
    {
        snapshot = default!;
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect) || windowRect.Width <= 0 || windowRect.Height <= 0)
            return false;
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (!TryReadMonitor(monitor, out var info, out var dpi))
            return false;
        var edge = InferEdge(windowRect, info.rcMonitor);
        var visible = TaskbarPlacement.VisibleThickness(edge, windowRect.Left, windowRect.Top, windowRect.Width, windowRect.Height, info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height);
        var bounds = new TaskbarBounds(windowRect.Left, windowRect.Top, windowRect.Width, windowRect.Height, edge);
        var collapsed = visible <= 6;
        if (collapsed && _lastFull.TryGetValue(info.szDevice, out var previous))
            bounds = previous;
        snapshot = Build(info.szDevice, info, bounds, visible, collapsed, dpi, collapsed, isPrimary: false);
        return true;
    }

    private TaskbarSnapshot Build(
        string key,
        MONITORINFOEX info,
        TaskbarBounds bounds,
        int visible,
        bool autoHide,
        int dpi,
        bool collapsed,
        bool isPrimary)
    {
        if (!collapsed)
            _lastFull[key] = bounds;
        return new TaskbarSnapshot(
            info.szDevice ?? "",
            isPrimary,
            bounds,
            visible,
            autoHide,
            dpi,
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Width,
            info.rcWork.Height,
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Width,
            info.rcMonitor.Height,
            collapsed);
    }

    private static bool ReadAutoHide()
    {
        var data = NativeMethods.NewAppBar(IntPtr.Zero);
        var state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref data).ToInt64();
        return (state & NativeMethods.AbsAutoHide) != 0;
    }

    private static bool TryReadMonitor(IntPtr monitor, out MONITORINFOEX info, out int dpi)
    {
        info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        dpi = 96;
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0)
            dpi = (int)x;
        return true;
    }

    private static TaskbarEdge InferEdge(RECT window, RECT monitor)
    {
        if (window.Width >= window.Height)
            return Math.Abs(window.Top - monitor.Top) <= Math.Abs(monitor.Bottom - window.Bottom)
                ? TaskbarEdge.Top
                : TaskbarEdge.Bottom;
        return Math.Abs(window.Left - monitor.Left) <= Math.Abs(monitor.Right - window.Right)
            ? TaskbarEdge.Left
            : TaskbarEdge.Right;
    }

    private static bool ForegroundCovers(IReadOnlyList<TaskbarSnapshot> bars)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;
        if (NativeMethods.IsShellWindow(NativeMethods.ClassNameOf(foreground)))
            return false;
        if (!NativeMethods.GetWindowRect(foreground, out var rect))
            return false;
        foreach (var bar in bars)
        {
            if (TaskbarPlacement.CoversMonitor(rect.Left, rect.Top, rect.Width, rect.Height, bar.MonitorX, bar.MonitorY, bar.MonitorWidth, bar.MonitorHeight))
                return true;
        }

        return false;
    }
}

internal sealed record TaskbarSnapshot(
    string MonitorDeviceName,
    bool IsPrimary,
    TaskbarBounds Bounds,
    int VisibleThickness,
    bool AutoHide,
    int Dpi,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    int MonitorX,
    int MonitorY,
    int MonitorWidth,
    int MonitorHeight,
    bool Collapsed)
{
    public bool Matches(string anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor) || anchor == TaskbarPlacement.PrimaryMonitor)
            return IsPrimary;
        return string.Equals(MonitorDeviceName, anchor, StringComparison.OrdinalIgnoreCase);
    }
}
