using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PeripheralBattery.Core;
using PeripheralBattery.Native;
using PeripheralBattery.Sources;
using PeripheralBattery.Ui;

namespace PeripheralBattery;

public sealed class AppController : IDisposable
{
    private bool? _sessionDemo;
    private readonly Dispatcher _dispatcher;
    private readonly JsonIdentityOverrideStore _overrides;
    private readonly TrayIconHost _tray;
    private readonly TaskbarWatcher _taskbar;
    private readonly DeviceListWindow _list;
    private TaskbarWidgetWindow _widget;
    private bool _widgetDestroyed;
    private readonly Window _menuAnchor;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _followTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private UserSettings _settings;
    private IReadOnlyList<PeripheralDevice> _devices = [];
    private string? _error;
    private bool _hasRefreshed;
    private DateTime? _refreshedAt;
    private bool _placing;
    private int _widgetWidth = (int)PillComposition.Arrange([]).WidthDip;
    private int _widgetHeight = TaskbarPlacement.PillCrossDip;
    private int _placedX = int.MinValue;
    private int _placedY;
    private int _placedW;
    private int _placedH;
    private bool _placedMode;
    private bool _menuOpen;
    private bool _disposed;

    public AppController(bool sessionDemo)
    {
        _sessionDemo = sessionDemo ? true : null;
        _dispatcher = Application.Current.Dispatcher;
        _settings = UserSettingsStore.Load(SettingsPath);
        _overrides = new JsonIdentityOverrideStore(OverridePath);
        _tray = new TrayIconHost();
        _taskbar = new TaskbarWatcher();
        _list = new DeviceListWindow();
        _widget = new TaskbarWidgetWindow();
        _menuAnchor = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
        };
        WindowChrome.Apply(_menuAnchor, noActivate: true);
        _list.Attach(this);
        _widget.Attach(this);
        WatchWidget(_widget);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RequestRefresh();
        _followTimer.Tick += (_, _) =>
        {
            _taskbar.Poll();
            FollowTaskbar();
        };
    }

    public void Start()
    {
        StartupRegistration.Apply(_settings.LaunchAtStartup);
        _tray.LeftClick += OpenListAtCursor;
        _tray.RightClick += ShowMenu;
        _tray.ExplorerRestarted += OnExplorerRestarted;
        _tray.AppBarNotified += (code, param) =>
        {
            _taskbar.NoteAppBar(code, param);
            FollowTaskbar();
        };
        _tray.Add();
        UpdatePresentation();
        _taskbar.Attach(_tray.Hwnd, _tray.AppBarMessage);
        _followTimer.Start();
        _refreshTimer.Start();
        FollowTaskbar();
        RequestRefresh();
    }

    public void RequestRefresh() => _ = RefreshAsync();

    public void ShowMenu()
    {
        if (_menuOpen || _disposed)
            return;
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        // The placed pill is owned by Shell_TrayWnd so a click on empty taskbar
        // space cannot cover it. That cross-process owner joins this thread's
        // input queue to Explorer, and the context menu is then dismissed
        // before it paints. Drop the owner for the menu and put it back when
        // the menu closes. Opening waits until this click returns, so the
        // button-up that asked for the menu does not dismiss it.
        _menuOpen = true;
        try
        {
            ReleaseTrayOwner();
            NativeMethods.MakeForeground(_tray.Hwnd);
            var dpi = DpiNear(point.X, point.Y);
            var entries = BuildMenu();
            _dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => OpenMenu(point.X, point.Y, dpi, entries)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            EndMenu();
        }
    }

    public void OpenListFromWidget()
    {
        if (_placing || !_widget.IsVisible)
            return;
        var hwnd = new WindowInteropHelper(_widget).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            OpenListAtCursor();
            return;
        }

        var bar = ResolveAnchor();
        PresentList(rect.Left, rect.Top, bar?.Dpi ?? 96, bar, rect.Width, rect.Height);
    }

    public void DragWidget()
    {
        if (!_placing || !NativeMethods.GetCursorPos(out var point))
            return;
        var hit = _taskbar.Current.FirstOrDefault(bar => !bar.Collapsed && bar.Bounds.Contains(point.X, point.Y))
            ?? ResolveAnchor();
        if (hit is null)
            return;
        var size = WidgetSizeFor(hit);
        _settings.AnchorFraction = TaskbarPlacement.FractionFromPointer(
            hit.Bounds, size, point.X, point.Y, _settings.AnchorFraction);
        _settings.AnchorMonitor = hit.IsPrimary ? TaskbarPlacement.PrimaryMonitor : hit.MonitorDeviceName;
        _settings.TaskbarWidgetEnabled = true;
        MoveWidget(hit, size);
    }

    public void EndWidgetDrag() => UserSettingsStore.Save(SettingsPath, _settings);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTimer.Stop();
        _followTimer.Stop();
        _shutdown.Cancel();
        _taskbar.Detach();
        _tray.Dispose();
        UserSettingsStore.Save(SettingsPath, _settings);
        if (!_widgetDestroyed)
            _widget.Close();
        _list.Close();
        _menuAnchor.Close();
        _shutdown.Dispose();
    }

    private bool Demo => _sessionDemo ?? _settings.DemoMode;

    private async Task RefreshAsync()
    {
        if (_disposed || !await _refreshGate.WaitAsync(0))
            return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var collected = await CollectAsync(timeout.Token);
            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;
                _overrides.Reload();
                _devices = DeviceAggregator.Combine(collected.Readings, _overrides);
                _error = collected.Errors.Count == 0 ? null : string.Join("；", collected.Errors);
                _hasRefreshed = true;
                _refreshedAt = DateTime.Now;
                UpdatePresentation();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            await _dispatcher.InvokeAsync(() =>
            {
                _error = "读取中断";
                _hasRefreshed = true;
                UpdatePresentation();
            });
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<(List<RawDeviceReading> Readings, List<string> Errors)> CollectAsync(CancellationToken token)
    {
        if (Demo)
            return (DemoCatalog.Readings().ToList(), []);

        var readings = new List<RawDeviceReading>();
        var errors = new List<string>();
        var hid = new HidBatterySource();
        var jobs = new List<(IPeripheralSource Source, string Error)>
        {
            (new GattBatterySource(), UiText.GattFailed),
            (new WindowsPropertyBatterySource(), UiText.PropertyFailed),
            (hid, UiText.HidFailed),
        };
        foreach (var vendor in VendorSourceRegistry.Create())
            jobs.Add((vendor, UiText.VendorFailed));

        foreach (var (source, error) in jobs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                readings.AddRange(await source.ReadAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                errors.Add(error);
            }
        }

        return (readings, errors);
    }

    private void UpdatePresentation()
    {
        var lowest = BatteryMath.Lowest(_devices);
        var icon = IconPainter.Create(BatteryMath.TrayGlyph(lowest), BatteryMath.Tone(lowest));
        _tray.Update(icon, UiText.TrayTip(lowest, _devices.Count, Demo));
        EnsureWidget();
        if (_settings.TaskbarWidgetEnabled)
            FollowTaskbar();
        else
            PaintWidget();
        if (_list.IsVisible)
            _list.SetState(_devices, _hasRefreshed, _error, Demo, _refreshedAt, SystemTheme.AppsAreLight());
    }

    private void OpenListAtCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;
        PresentList(point.X, point.Y, DpiNear(point.X, point.Y), anchor: null, anchorWidth: 0, anchorHeight: 0);
    }

    private void PresentList(int physicalX, int physicalY, int dpi, TaskbarSnapshot? anchor, int anchorWidth, int anchorHeight)
    {
        _list.SetState(_devices, _hasRefreshed, _error, Demo, _refreshedAt, SystemTheme.AppsAreLight());
        _list.Measure(new Size(336, 900));
        var widthDip = _list.DesiredSize.Width > 1 ? _list.DesiredSize.Width : 336;
        var heightDip = _list.DesiredSize.Height > 1 ? _list.DesiredSize.Height : 180;
        var width = Math.Max(40, TaskbarPlacement.DipToPx(widthDip, dpi));
        var height = Math.Max(40, TaskbarPlacement.DipToPx(heightDip, dpi));
        int x;
        int y;
        if (anchor is not null)
        {
            (x, y) = TaskbarPlacement.PlaceFlyout(
                anchor.Bounds.Edge,
                physicalX,
                physicalY,
                Math.Max(anchorWidth, 1),
                Math.Max(anchorHeight, 1),
                width,
                height,
                anchor.WorkX,
                anchor.WorkY,
                anchor.WorkWidth,
                anchor.WorkHeight,
                8);
        }
        else if (TryWorkArea(physicalX, physicalY, out var areaX, out var areaY, out var areaW, out var areaH, out var areaDpi))
        {
            dpi = areaDpi;
            (x, y) = TaskbarPlacement.PlaceNearPoint(physicalX, physicalY, width, height, areaX, areaY, areaW, areaH, 8);
        }
        else
        {
            (x, y) = (physicalX, physicalY);
        }

        _list.Left = TaskbarPlacement.PxToDip(x, dpi);
        _list.Top = TaskbarPlacement.PxToDip(y, dpi);
        _list.Show();
    }

    private void OpenMenu(int physicalX, int physicalY, int dpi, List<MenuEntry> entries)
    {
        if (_disposed)
        {
            EndMenu();
            return;
        }

        try
        {
            TrayMenu.Show(_menuAnchor, _tray.Hwnd, physicalX, physicalY, dpi, entries, EndMenu);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            EndMenu();
        }
    }

    private void EndMenu()
    {
        if (!_menuOpen)
            return;
        _menuOpen = false;
        if (!_disposed)
            FollowTaskbar();
    }

    private void FollowTaskbar()
    {
        if (_disposed || _menuOpen)
            return;
        EnsureWidget();
        var bar = ResolveAnchor();
        var show = bar is not null && TaskbarPlacement.ShouldShow(_settings.TaskbarWidgetEnabled, bar.Collapsed, _taskbar.FullscreenApp);
        if (!show || bar is null)
        {
            _placedX = int.MinValue;
            HideWidget();
            return;
        }

        MoveWidget(bar, WidgetSizeFor(bar));
    }

    private PillStrip CurrentStrip() =>
        PillComposition.Arrange(_devices, _placing ? 2 : 1);

    private WidgetSize WidgetSizeFor(TaskbarSnapshot bar) =>
        TaskbarPlacement.Fit(bar.Bounds, bar.Dpi, CurrentStrip().WidthDip);

    private void PaintWidget(TaskbarSnapshot? bar = null)
    {
        bar ??= ResolveAnchor();
        var strip = CurrentStrip();
        var scale = bar is null
            ? 1
            : TaskbarPlacement.ContentScale(bar.Bounds, bar.Dpi, strip.WidthDip);
        var edge = bar?.Bounds.Edge ?? TaskbarEdge.Bottom;
        _widget.SetReading(strip, _devices.Count, _placing, SystemTheme.TaskbarIsLight(), Demo, edge, scale);
    }

    private void MoveWidget(TaskbarSnapshot bar, WidgetSize size)
    {
        var (x, y) = TaskbarPlacement.Place(bar.Bounds, size, _settings.AnchorFraction);
        _widgetWidth = size.Width;
        _widgetHeight = size.Height;
        var wasShown = _widget.IsVisible;
        var hwnd = WidgetHandle();
        var tray = bar.TrayWindow;
        var owner = OwnerOf(hwnd);
        var applied = new PlacedWidget(wasShown, _placedX, _placedY, _placedW, _placedH, owner);
        var desired = new PlacedWidget(true, x, y, size.Width, size.Height, tray.ToInt64());
        var ownedByTray = tray != IntPtr.Zero && owner == tray.ToInt64();
        var covered = wasShown && !ownedByTray && hwnd != IntPtr.Zero && tray != IntPtr.Zero && !IsAbove(hwnd, tray);
        var raise = TaskbarPlacement.NeedsRaise(applied, desired, covered) || _placing != _placedMode;
        PaintWidget(bar);
        if (!raise)
            return;

        _widget.Width = Math.Max(1, TaskbarPlacement.PxToDip(size.Width, bar.Dpi));
        _widget.Height = Math.Max(1, TaskbarPlacement.PxToDip(size.Height, bar.Dpi));
        _widget.Left = TaskbarPlacement.PxToDip(x, bar.Dpi);
        _widget.Top = TaskbarPlacement.PxToDip(y, bar.Dpi);
        if (!wasShown)
            _widget.Show();
        hwnd = WidgetHandle();
        if (hwnd == IntPtr.Zero)
            return;

        _placedX = x;
        _placedY = y;
        _placedW = size.Width;
        _placedH = size.Height;
        _placedMode = _placing;
        // Shell_TrayWnd is topmost. A click on empty taskbar space raises it
        // over other topmost windows and covers this overlay. Owning the popup
        // to that tray keeps the pill above the bar. SetWindowPos applies the
        // owner change; without it the click stays covered. Skip that while a
        // menu is open: ShowMenu cleared the owner so the menu can stay up.
        if (!_menuOpen)
            BindTrayOwner(hwnd, tray);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            x,
            y,
            size.Width,
            size.Height,
            (uint)(NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow));
    }

    private void HideWidget()
    {
        if (_widgetDestroyed)
            return;
        if (_widget.IsVisible)
            _widget.Hide();
        ReleaseTrayOwner();
    }

    private void EnsureWidget()
    {
        if (!_widgetDestroyed)
        {
            var hwnd = new WindowInteropHelper(_widget).Handle;
            if (hwnd == IntPtr.Zero || NativeMethods.IsWindow(hwnd))
                return;
            _widgetDestroyed = true;
        }

        _widgetDestroyed = false;
        _placedX = int.MinValue;
        var replacement = new TaskbarWidgetWindow();
        replacement.Attach(this);
        WatchWidget(replacement);
        _widget = replacement;
    }

    private void WatchWidget(TaskbarWidgetWindow widget)
    {
        widget.Closed += (_, _) =>
        {
            if (!_disposed && ReferenceEquals(widget, _widget))
                _widgetDestroyed = true;
        };
    }

    private IntPtr WidgetHandle() => _widgetDestroyed ? IntPtr.Zero : new WindowInteropHelper(_widget).Handle;

    private static long OwnerOf(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? 0 : NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlpHwndParent).ToInt64();

    private static void BindTrayOwner(IntPtr hwnd, IntPtr tray)
    {
        if (hwnd == IntPtr.Zero || tray == IntPtr.Zero || !NativeMethods.IsWindow(tray))
            return;
        if (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlpHwndParent) == tray)
            return;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlpHwndParent, tray);
    }

    private void ReleaseTrayOwner()
    {
        var hwnd = WidgetHandle();
        if (hwnd == IntPtr.Zero)
            return;
        if (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlpHwndParent) == IntPtr.Zero)
            return;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlpHwndParent, IntPtr.Zero);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            (uint)(NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate));
    }

    private static bool IsAbove(IntPtr window, IntPtr other)
    {
        if (window == IntPtr.Zero || other == IntPtr.Zero || window == other)
            return false;
        var guard = 0;
        for (var cursor = NativeMethods.GetWindow(window, NativeMethods.GwHwndPrev);
             cursor != IntPtr.Zero && guard < 48;
             cursor = NativeMethods.GetWindow(cursor, NativeMethods.GwHwndPrev))
        {
            guard++;
            if (cursor == other)
                return false;
        }

        return true;
    }

    private TaskbarSnapshot? ResolveAnchor() =>
        _taskbar.Current.FirstOrDefault(bar => bar.Matches(_settings.AnchorMonitor));

    private void BeginPlacement()
    {
        var first = !_settings.TaskbarWidgetEnabled;
        _placing = true;
        if (first)
            _settings.AnchorFraction = TaskbarPlacement.DefaultFraction;
        _settings.TaskbarWidgetEnabled = true;
        _settings.AnchorMonitor = string.IsNullOrWhiteSpace(_settings.AnchorMonitor)
            ? TaskbarPlacement.PrimaryMonitor
            : _settings.AnchorMonitor;
        UserSettingsStore.Save(SettingsPath, _settings);
        _taskbar.Poll();
        FollowTaskbar();
        if (first)
            _tray.ShowBalloon(UiText.AppName, UiText.PlaceHint);
    }

    private void FinishPlacement()
    {
        _placing = false;
        UserSettingsStore.Save(SettingsPath, _settings);
        FollowTaskbar();
        UpdatePresentation();
    }

    private void RemoveFromTaskbar()
    {
        _placing = false;
        _settings.TaskbarWidgetEnabled = false;
        UserSettingsStore.Save(SettingsPath, _settings);
        _placedX = int.MinValue;
        HideWidget();
    }

    private void ToggleStartup()
    {
        _settings.LaunchAtStartup = !_settings.LaunchAtStartup;
        StartupRegistration.Apply(_settings.LaunchAtStartup);
        UserSettingsStore.Save(SettingsPath, _settings);
    }

    private void ToggleDemo()
    {
        var next = !Demo;
        _sessionDemo = null;
        _settings.DemoMode = next;
        UserSettingsStore.Save(SettingsPath, _settings);
        RequestRefresh();
    }

    private List<MenuEntry> BuildMenu()
    {
        var entries = new List<MenuEntry>
        {
            new(UiText.Refresh, false, false, RequestRefresh),
            new(UiText.OpenList, false, false, OpenListAtCursor),
            new(null, false, false, null, Separator: true),
        };
        if (!_settings.TaskbarWidgetEnabled)
            entries.Add(new MenuEntry(UiText.PlaceOnTaskbar, false, false, BeginPlacement));
        else if (_placing)
            entries.Add(new MenuEntry(UiText.FinishPlacement, false, false, FinishPlacement));
        else
            entries.Add(new MenuEntry(UiText.AdjustPosition, false, false, BeginPlacement));
        if (_settings.TaskbarWidgetEnabled)
            entries.Add(new MenuEntry(UiText.RemoveFromTaskbar, false, false, RemoveFromTaskbar));
        entries.Add(new MenuEntry(null, false, false, null, Separator: true));
        entries.Add(new MenuEntry(UiText.LaunchAtStartup, true, _settings.LaunchAtStartup, ToggleStartup));
        entries.Add(new MenuEntry(UiText.DemoMode, true, Demo, ToggleDemo));
        entries.Add(new MenuEntry(null, false, false, null, Separator: true));
        entries.Add(new MenuEntry(UiText.Exit, false, false, () => Application.Current.Shutdown()));
        return entries;
    }

    private void OnExplorerRestarted()
    {
        _taskbar.Detach();
        _tray.Add();
        UpdatePresentation();
        _taskbar.Attach(_tray.Hwnd, _tray.AppBarMessage);
        FollowTaskbar();
    }

    private int DpiNear(int x, int y)
    {
        if (TryWorkArea(x, y, out _, out _, out _, out _, out var dpi))
            return dpi;
        return ResolveAnchor()?.Dpi ?? 96;
    }

    private static bool TryWorkArea(int x, int y, out int areaX, out int areaY, out int areaW, out int areaH, out int dpi)
    {
        areaX = areaY = 0;
        areaW = areaH = 0;
        dpi = 96;
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 && dx > 0)
            dpi = (int)dx;
        areaX = info.rcWork.Left;
        areaY = info.rcWork.Top;
        areaW = info.rcWork.Width;
        areaH = info.rcWork.Height;
        return areaW > 0 && areaH > 0;
    }

    private static string SettingsPath => Path.Combine(AppDirectory, "settings.json");
    private static string OverridePath => Path.Combine(AppDirectory, "identity-overrides.json");

    private static string AppDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralBattery");
}
