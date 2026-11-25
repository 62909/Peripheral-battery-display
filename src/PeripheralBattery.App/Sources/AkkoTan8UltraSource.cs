using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// 空影 TAN8 Ultra on the shared 38EE:0023 / 38EE:0024 interfaces.
/// Opens only that vendor collection, sends the driver's read-only 0x01 / 0x30 / 0x02
/// identity wrap, and sends 0x1A only when byte 0x0A is cid 2.
/// </summary>
internal sealed class AkkoTan8UltraSource : IVendorBatterySource
{
    private const int Attempts = 3;
    private const int TimeoutMs = 1000;

    public string SourceId => "akko-tan8-ultra";
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
                cancellationToken.ThrowIfCancellationRequested();
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    break;
                var path = ReadPath(set, ref data);
                if (path is null)
                    continue;
                var ids = DeviceIdentity.ParseVidPid(path);
                if (ids.VendorId is not int pathVid || ids.ProductId is not int pathPid || !AkkoTan8Ultra.IsCandidate(pathVid, pathPid))
                    continue;
                try
                {
                    if (TryReadDevice(path, pathVid, pathPid, cancellationToken, out var reading))
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

    private static bool TryReadDevice(string path, int pathVid, int pathPid, CancellationToken cancellationToken, out RawDeviceReading reading)
    {
        reading = null!;
        var handle = Open(path);
        if (handle == IntPtr.Zero || handle == NativeMethods.InvalidHandle)
            return false;

        var ioEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
        if (ioEvent == IntPtr.Zero || ioEvent == NativeMethods.InvalidHandle)
        {
            NativeMethods.CloseHandle(handle);
            return false;
        }

        try
        {
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
                return false;
            if (attributes.VendorId != pathVid || attributes.ProductId != pathPid || !AkkoTan8Ultra.IsCandidate(attributes.VendorId, attributes.ProductId))
                return false;
            if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
                return false;

            try
            {
                var caps = new HidCaps();
                if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                    return false;
                if (!AkkoTan8Ultra.IsVendorCollection(caps.UsagePage, caps.Usage))
                    return false;
                if (caps.OutputReportByteLength < 1 + AkkoTan8Ultra.PayloadLength)
                    return false;
                if (caps.InputReportByteLength < 1 + AkkoTan8Ultra.MinimumIdentityResponse)
                    return false;

                NativeMethods.HidD_FlushQueue(handle);
                if (Transact(handle, ioEvent, caps, AkkoTan8Ultra.StartPayload(), AkkoTan8Ultra.StartCommand, AkkoTan8Ultra.MinimumWrappedResponse, cancellationToken) is null)
                    return false;
                var identity = Transact(handle, ioEvent, caps, AkkoTan8Ultra.IdentityPayload(), AkkoTan8Ultra.IdentityCommand, AkkoTan8Ultra.MinimumIdentityResponse, cancellationToken);
                if (identity is null || !AkkoTan8Ultra.TryReadCid(identity, out var cid))
                    return false;
                if (Transact(handle, ioEvent, caps, AkkoTan8Ultra.EndPayload(), AkkoTan8Ultra.EndCommand, AkkoTan8Ultra.MinimumWrappedResponse, cancellationToken) is null)
                    return false;
                if (cid != AkkoTan8Ultra.ModelCid || !AkkoTan8Ultra.AllowsBatteryQuery(attributes.VendorId, attributes.ProductId, identity))
                    return false;

                var battery = Transact(handle, ioEvent, caps, AkkoTan8Ultra.BatteryPayload(), AkkoTan8Ultra.BatteryCommand, AkkoTan8Ultra.MinimumBatteryResponse, cancellationToken);
                var parsed = battery is null ? default : AkkoTan8Ultra.ParseBattery(battery);
                reading = new RawDeviceReading
                {
                    SourceId = "akko-tan8-ultra",
                    Priority = SourcePriority.Vendor,
                    InstanceId = path,
                    WindowsName = AkkoTan8Ultra.ModelName,
                    VendorId = attributes.VendorId,
                    ProductId = attributes.ProductId,
                    KindHint = DeviceKind.Mouse,
                    BatteryPercent = parsed.Percent,
                };
                return true;
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(ioEvent);
            NativeMethods.CloseHandle(handle);
        }
    }

    private static byte[]? Transact(
        IntPtr handle,
        IntPtr ioEvent,
        HidCaps caps,
        byte[] payload,
        byte command,
        int minimumPayload,
        CancellationToken cancellationToken)
    {
        var output = new byte[caps.OutputReportByteLength];
        output[0] = AkkoTan8Ultra.ReportId;
        Buffer.BlockCopy(payload, 0, output, 1, Math.Min(payload.Length, output.Length - 1));
        var input = new byte[caps.InputReportByteLength];

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Write(handle, ioEvent, output, cancellationToken))
                continue;
            var deadline = Environment.TickCount64 + TimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = (int)Math.Min(int.MaxValue, deadline - Environment.TickCount64);
                if (remaining <= 0)
                    break;
                if (!Read(handle, ioEvent, input, remaining, out var read, cancellationToken))
                    break;
                if (!AkkoTan8Ultra.TryGetPayload(input.AsSpan(0, read), out var report))
                    continue;
                if (!AkkoTan8Ultra.Matches(report, command, minimumPayload))
                    continue;
                return report.ToArray();
            }
        }

        return null;
    }

    private static bool Write(IntPtr handle, IntPtr ioEvent, byte[] buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var overlapped = NewOverlapped(ioEvent);
        if (NativeMethods.WriteFile(handle, buffer, buffer.Length, out var written, ref overlapped))
            return written > 0;
        if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
            return false;
        return Wait(handle, ioEvent, ref overlapped, TimeoutMs, out written, cancellationToken) && written > 0;
    }

    private static bool Read(IntPtr handle, IntPtr ioEvent, byte[] buffer, int timeoutMs, out int read, CancellationToken cancellationToken)
    {
        read = 0;
        cancellationToken.ThrowIfCancellationRequested();
        var overlapped = NewOverlapped(ioEvent);
        if (NativeMethods.ReadFile(handle, buffer, buffer.Length, out read, ref overlapped))
            return read > 0;
        if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
            return false;
        return Wait(handle, ioEvent, ref overlapped, timeoutMs, out read, cancellationToken) && read > 0;
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
                cancellationToken.ThrowIfCancellationRequested();
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

    private static System.Threading.NativeOverlapped NewOverlapped(IntPtr ioEvent)
    {
        NativeMethods.ResetEvent(ioEvent);
        return new System.Threading.NativeOverlapped { EventHandle = ioEvent };
    }

    private static IntPtr Open(string path)
    {
        var share = NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete;
        var access = NativeMethods.GenericRead | NativeMethods.GenericWrite;
        return NativeMethods.CreateFile(path, access, share, IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.FileFlagOverlapped, IntPtr.Zero);
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
}
