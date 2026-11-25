using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PeripheralBattery.Native;

internal sealed class TrayIconHost : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _callback;
    private readonly uint _appBarMessage;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;

    public TrayIconHost()
    {
        var parameters = new HwndSourceParameters("PeripheralBattery.Message")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = NativeMethods.WsExToolWindow,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _callback = NativeMethods.WmUser + 20;
        _appBarMessage = NativeMethods.WmUser + 21;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    public IntPtr Hwnd => _source.Handle;
    public uint AppBarMessage => _appBarMessage;
    public event Action? LeftClick;
    public event Action? RightClick;
    public event Action? ExplorerRestarted;

    public void Add()
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifTip;
        data.szTip = UiText.AppName;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data);
    }

    public void Update(IntPtr icon, string tip)
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip;
        data.hIcon = icon;
        data.szTip = tip.Length > 127 ? tip[..127] : tip;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref data);
        if (_icon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_icon);
        _icon = icon;
    }

    public void ShowBalloon(string title, string text)
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NifInfo;
        data.szInfoTitle = title;
        data.szInfo = text.Length > 250 ? text[..250] : text;
        data.dwInfoFlags = NativeMethods.NiifInfo;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref data);
    }

    public void Remove()
    {
        var data = CreateData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Remove();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private NOTIFYICONDATA CreateData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = Hwnd,
            uID = 1,
            uCallbackMessage = _callback,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_callback)
        {
            var mouse = lParam.ToInt32();
            if (mouse is NativeMethods.WmLButtonUp or NativeMethods.WmLButtonDblClk)
                LeftClick?.Invoke();
            else if (mouse == NativeMethods.WmRButtonUp)
                RightClick?.Invoke();
            handled = true;
        }
        else if (msg == (int)_appBarMessage)
        {
            AppBarNotified?.Invoke(wParam.ToInt32(), lParam);
            handled = true;
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated)
        {
            ExplorerRestarted?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public event Action<int, IntPtr>? AppBarNotified;
}
