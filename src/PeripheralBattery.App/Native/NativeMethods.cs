using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;

namespace PeripheralBattery.Native;

internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

internal static class NativeMethods
{
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmRButtonUp = 0x0205;
    public const int WmUser = 0x0400;
    public const int NimAdd = 0x00000000;
    public const int NimModify = 0x00000001;
    public const int NimDelete = 0x00000002;
    public const int NifMessage = 0x00000001;
    public const int NifIcon = 0x00000002;
    public const int NifTip = 0x00000004;
    public const int NifInfo = 0x00000010;
    public const int NiifInfo = 0x00000001;
    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExAppWindow = 0x00040000;
    public const uint AbmNew = 0;
    public const uint AbmRemove = 1;
    public const uint AbmGetState = 4;
    public const uint AbmGetTaskbarPos = 5;
    public const int AbsAutoHide = 0x1;
    public const int AbnFullscreenApp = 2;
    public const uint MonitorDefaultToNearest = 2;
    public const uint MonitorInfoPrimary = 1;
    public const int SwpNoSize = 0x0001;
    public const int SwpNoMove = 0x0002;
    public const int SwpNoActivate = 0x0010;
    public const int SwpShowWindow = 0x0040;
    public const int GwlpHwndParent = -8;
    public const uint GwHwndPrev = 3;
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint WaitObject0 = 0;
    public const uint WaitTimeout = 0x00000102;
    public const int ErrorIoPending = 997;
    public const int ErrorAccessDenied = 5;
    public const int ErrorSharingViolation = 32;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint FileShareDelete = 0x00000004;
    public const uint OpenExisting = 3;
    public const int DigcfPresent = 0x00000002;
    public const int DigcfDeviceInterface = 0x00000010;
    public const int HidPInput = 0;
    public const int HidPOutput = 1;
    public const int HidPFeature = 2;
    public const int HidPStatusSuccess = 0x00110000;
    public const int CrSuccess = 0;
    public const int CrBufferSmall = 26;
    public const int CmGetIdListFilterPresent = 0x00000100;
    public const int DevPropTypeString = 0x00000012;
    public const int DevPropTypeUint32 = 0x00000007;
    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr InvalidHandle = new(-1);

    public static readonly DEVPROPKEY BatteryLevel = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    public static readonly DEVPROPKEY FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public static readonly DEVPROPKEY DeviceDesc = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
    public static readonly DEVPROPKEY BluetoothAddress = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 1);
    public static readonly DEVPROPKEY BluetoothClassOfDevice = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 10);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr device, ref HidAttributes attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetManufacturerString(IntPtr device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(IntPtr device, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetInputReport(IntPtr device, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(IntPtr device, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FlushQueue(IntPtr device);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    // Byte counts are NULL. Overlapped HID transfers report the size through
    // GetOverlappedResult, and a non-NULL count can make WriteFile fail.
    // The buffer and the OVERLAPPED block have to stay allocated until that call.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr file, IntPtr buffer, int count, IntPtr written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr file, IntPtr buffer, int count, IntPtr read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(IntPtr file, IntPtr overlapped, out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(IntPtr file, IntPtr overlapped);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsed, ref HidCaps caps);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(int reportType, [Out] HidValueCaps[] caps, ref ushort count, IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern int HidP_GetButtonCaps(int reportType, [Out] HidButtonCaps[] caps, ref ushort count, IntPtr preparsed);

    [DllImport("hid.dll", CharSet = CharSet.Ansi)]
    public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint usageValue, IntPtr preparsed, byte[] report, uint reportLength);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_ID_List_Size(out int length, string? filter, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_ID_List(string? filter, char[] buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNode(out int devInst, string deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_Property(int devInst, ref DEVPROPKEY key, out int propertyType, byte[]? buffer, ref int bufferSize, int flags);

    /// <summary>
    /// Make <paramref name="hwnd"/> the foreground window. A tray click is
    /// delivered by Explorer, which is still foreground, and
    /// <see cref="SetForegroundWindow"/> then fails unless this thread is
    /// briefly attached to Explorer's input. The attachment is undone before
    /// returning so the two queues do not stay joined.
    /// Call this before a context menu exists. Attaching after the menu has
    /// captured the mouse releases that capture, and the menu closes.
    /// </summary>
    public static void MakeForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var foreground = GetForegroundWindow();
        var attached = false;
        var foreThread = 0u;
        var current = GetCurrentThreadId();
        if (foreground != IntPtr.Zero && foreground != hwnd)
        {
            foreThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            if (foreThread != 0 && foreThread != current)
                attached = AttachThreadInput(foreThread, current, true);
        }

        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(foreThread, current, false);
        }
    }

    /// <summary>
    /// Leave <paramref name="window"/>'s thread input queue. Clearing
    /// <c>GWLP_HWNDPARENT</c> removes the owner and does not detach the queue
    /// that owner joined. A menu opened on the still-joined queue is dismissed
    /// as a click outside.
    /// </summary>
    public static void DetachInput(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return;
        var thread = GetWindowThreadProcessId(window, IntPtr.Zero);
        var current = GetCurrentThreadId();
        if (thread == 0 || thread == current)
            return;
        // The owner join and MakeForeground do not use the same argument order.
        // Detach both. A queue that is not attached makes the call fail.
        AttachThreadInput(thread, current, false);
        AttachThreadInput(current, thread, false);
    }

    public static APPBARDATA NewAppBar(IntPtr hwnd)
    {
        return new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
        };
    }

    public static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static bool IsShellWindow(string className) => className is
        "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow";
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint uTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public int cbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DEVPROPKEY
{
    public Guid Fmtid;
    public uint Pid;

    public DEVPROPKEY(Guid fmtid, uint pid)
    {
        Fmtid = fmtid;
        Pid = pid;
    }
}
