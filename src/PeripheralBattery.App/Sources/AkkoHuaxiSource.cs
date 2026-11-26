using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// 38EE:0022 on the Huaxi collection FFA8:0002 (USB MI_02).
/// The mouse interface MI_00 is not queried.
/// </summary>
internal sealed class AkkoHuaxiSource : IVendorBatterySource
{
    private const int TimeoutMs = 3000;

    public string SourceId => "akko-huaxi";
    public int Priority => SourcePriority.Vendor;

    public Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => (IReadOnlyList<RawDeviceReading>)Read(cancellationToken), cancellationToken);
    }

    private static List<RawDeviceReading> Read(CancellationToken cancellationToken)
    {
        var readings = new List<RawDeviceReading>();
        NativeMethods.HidD_GetHidGuid(out var guid);
        var set = NativeMethods.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == NativeMethods.InvalidHandle)
            return readings;

        try
        {
            for (var index = 0; ; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    break;
                var path = ReadPath(set, ref data);
                if (path is null)
                    continue;
                var ids = DeviceIdentity.ParseVidPid(path);
                if (ids.VendorId is not int vid || ids.ProductId is not int pid || !AkkoHuaxiMouse.IsCandidate(vid, pid))
                    continue;
                try
                {
                    if (Probe(path, cancellationToken) is RawDeviceReading reading)
                        readings.Add(reading);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return readings;
    }

    private static RawDeviceReading? Probe(string path, CancellationToken cancellationToken)
    {
        var handle = Open(path, overlapped: true);
        if (handle == IntPtr.Zero || handle == NativeMethods.InvalidHandle)
            return null;

        var plain = IntPtr.Zero;
        var ioEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
        if (ioEvent == IntPtr.Zero || ioEvent == NativeMethods.InvalidHandle)
        {
            NativeMethods.CloseHandle(handle);
            return null;
        }

        try
        {
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)
                || !AkkoHuaxiMouse.IsCandidate(attributes.VendorId, attributes.ProductId))
                return null;

            if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
                return null;

            try
            {
                var caps = new HidCaps();
                if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                    return null;
                if (!AkkoHuaxiMouse.IsInfoCollection(caps.UsagePage, caps.Usage))
                    return null;

                var name = ReadProduct(handle);
                NativeMethods.HidD_FlushQueue(handle);
                var request = AkkoHuaxiMouse.InfoRequest();
                if (!Send(handle, ref plain, path, ioEvent, caps, request, cancellationToken))
                    return Reading(name, null, AkkoHuaxiMouse.NoteSendFailed);

                var info = ReadInfo(handle, ref plain, path, ioEvent, caps, cancellationToken);
                if (info is null)
                    return Reading(name, null, AkkoHuaxiMouse.NoteNoInfoReply);
                if (info.Value.BatteryPercent is null)
                    return Reading(name, null, AkkoHuaxiMouse.NoteLevelOutOfRange);
                return Reading(name, info.Value.BatteryPercent, null);
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            if (plain != IntPtr.Zero && plain != NativeMethods.InvalidHandle)
                NativeMethods.CloseHandle(plain);
            NativeMethods.CloseHandle(ioEvent);
            NativeMethods.CloseHandle(handle);
        }
    }

    private static RawDeviceReading Reading(string? name, int? percent, string? note) => new()
    {
        SourceId = "akko-huaxi",
        Priority = SourcePriority.Vendor,
        InstanceId = "huaxi:" + AkkoHuaxiMouse.VendorId.ToString("X4") + ":" + AkkoHuaxiMouse.ProductId.ToString("X4"),
        WindowsName = name,
        VendorId = AkkoHuaxiMouse.VendorId,
        ProductId = AkkoHuaxiMouse.ProductId,
        KindHint = DeviceKind.Mouse,
        BatteryPercent = percent,
        Note = note,
    };

    private static bool Send(
        IntPtr handle,
        ref IntPtr plain,
        string path,
        IntPtr ioEvent,
        HidCaps caps,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var length = Math.Max((int)caps.OutputReportByteLength, 1 + payload.Length);
        var output = new byte[length];
        output[0] = AkkoHuaxiMouse.ReportId;
        Buffer.BlockCopy(payload, 0, output, 1, payload.Length);
        if (caps.OutputReportByteLength >= output.Length && WriteFile(handle, ioEvent, output, cancellationToken))
            return true;

        plain = EnsurePlain(plain, path);
        return plain != IntPtr.Zero
            && plain != NativeMethods.InvalidHandle
            && NativeMethods.HidD_SetOutputReport(plain, output, output.Length);
    }

    private static HuaxiInfo? ReadInfo(
        IntPtr handle,
        ref IntPtr plain,
        string path,
        IntPtr ioEvent,
        HidCaps caps,
        CancellationToken cancellationToken)
    {
        var inputLength = Math.Max((int)caps.InputReportByteLength, 1 + AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset + 1);
        var input = new byte[inputLength];
        var deadline = Environment.TickCount64 + TimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;
            var remaining = (int)Math.Max(1, Math.Min(int.MaxValue, deadline - Environment.TickCount64));
            if (!ReadFile(handle, ioEvent, input, remaining, out var read, cancellationToken))
                break;
            if (AkkoHuaxiMouse.TryParseInfoReport(input.AsSpan(0, read), out var info))
                return info;
        }

        plain = EnsurePlain(plain, path);
        if (plain == IntPtr.Zero || plain == NativeMethods.InvalidHandle)
            return null;
        var control = new byte[inputLength];
        control[0] = AkkoHuaxiMouse.ReportId;
        if (!NativeMethods.HidD_GetInputReport(plain, control, control.Length))
            return null;
        return AkkoHuaxiMouse.TryParseInfoReport(control, out var fromControl) ? fromControl : null;
    }

    private static bool WriteFile(IntPtr handle, IntPtr ioEvent, byte[] buffer, CancellationToken cancellationToken)
    {
        var bufferPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var overlapped = new System.Threading.NativeOverlapped[1];
        var overlappedPin = GCHandle.Alloc(overlapped, GCHandleType.Pinned);
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return false;
            NativeMethods.ResetEvent(ioEvent);
            overlapped[0].EventHandle = ioEvent;
            if (NativeMethods.WriteFile(handle, buffer, buffer.Length, out var written, ref overlapped[0]))
                return written > 0;
            if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
                return false;
            return Wait(handle, ioEvent, ref overlapped[0], TimeoutMs, out written, cancellationToken) && written > 0;
        }
        finally
        {
            overlappedPin.Free();
            bufferPin.Free();
        }
    }

    private static bool ReadFile(IntPtr handle, IntPtr ioEvent, byte[] buffer, int timeoutMs, out int read, CancellationToken cancellationToken)
    {
        read = 0;
        var bufferPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var overlapped = new System.Threading.NativeOverlapped[1];
        var overlappedPin = GCHandle.Alloc(overlapped, GCHandleType.Pinned);
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return false;
            NativeMethods.ResetEvent(ioEvent);
            overlapped[0].EventHandle = ioEvent;
            if (NativeMethods.ReadFile(handle, buffer, buffer.Length, out read, ref overlapped[0]))
                return read > 0;
            if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
                return false;
            return Wait(handle, ioEvent, ref overlapped[0], timeoutMs, out read, cancellationToken) && read > 0;
        }
        finally
        {
            overlappedPin.Free();
            bufferPin.Free();
        }
    }

    private static bool Wait(
        IntPtr handle,
        IntPtr ioEvent,
        ref System.Threading.NativeOverlapped overlapped,
        int timeoutMs,
        out int transferred,
        CancellationToken cancellationToken)
    {
        transferred = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                NativeMethods.CancelIoEx(handle, ref overlapped);
                NativeMethods.GetOverlappedResult(handle, ref overlapped, out _, true);
                return false;
            }

            var slice = (int)Math.Min(100, Math.Max(0, deadline - Environment.TickCount64));
            var status = NativeMethods.WaitForSingleObject(ioEvent, (uint)slice);
            if (status == NativeMethods.WaitObject0)
                return NativeMethods.GetOverlappedResult(handle, ref overlapped, out transferred, false);
            if (status != NativeMethods.WaitTimeout || Environment.TickCount64 >= deadline)
            {
                NativeMethods.CancelIoEx(handle, ref overlapped);
                NativeMethods.GetOverlappedResult(handle, ref overlapped, out _, true);
                return false;
            }
        }
    }

    private static IntPtr EnsurePlain(IntPtr plain, string path)
    {
        if (plain != IntPtr.Zero && plain != NativeMethods.InvalidHandle)
            return plain;
        return Open(path, overlapped: false);
    }

    private static IntPtr Open(string path, bool overlapped)
    {
        var share = NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete;
        var access = NativeMethods.GenericRead | NativeMethods.GenericWrite;
        var flags = overlapped ? NativeMethods.FileFlagOverlapped : 0;
        return NativeMethods.CreateFile(path, access, share, IntPtr.Zero, NativeMethods.OpenExisting, flags, IntPtr.Zero);
    }

    private static string? ReadPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required < 8)
            return null;
        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, IntPtr.Zero))
                return null;
            var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            if (string.IsNullOrWhiteSpace(path))
                path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 8));
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ReadProduct(IntPtr handle)
    {
        var bytes = new byte[512];
        if (!NativeMethods.HidD_GetProductString(handle, bytes, bytes.Length))
            return null;
        var text = Encoding.Unicode.GetString(bytes);
        var end = text.IndexOf('\0');
        if (end >= 0)
            text = text[..end];
        text = text.Trim();
        return DeviceIdentity.IsMeaningfulName(text) ? text : null;
    }
}
