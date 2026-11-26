using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// 空影 TAN8 Ultra on the shared 38EE:0023 / 38EE:0024 interfaces.
/// 38EE:0022 is not a candidate: the official filters do not list it, and a
/// standard mouse collection is not opened as FF1C:0092.
/// Opens only that vendor collection. Identity is the driver's read-only
/// 0x01 / 0x30 / 0x02 wrap. 0x1A is sent only after cid 2.
/// A candidate that cannot be confirmed is still listed, with a note and no percentage.
/// </summary>
internal sealed class AkkoTan8UltraSource : IVendorBatterySource
{
    private const int Attempts = 3;
    private const int TimeoutMs = 2000;

    public string SourceId => "akko-tan8-ultra";
    public int Priority => SourcePriority.Vendor;

    public Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => (IReadOnlyList<RawDeviceReading>)Read(cancellationToken), cancellationToken);
    }

    private static List<RawDeviceReading> Read(CancellationToken cancellationToken)
    {
        var readings = new List<RawDeviceReading>();
        var best = new Dictionary<(int Vid, int Pid), Tan8Decision>();
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
                if (ids.VendorId is not int pathVid || ids.ProductId is not int pathPid || !AkkoTan8Ultra.IsCandidate(pathVid, pathPid))
                    continue;
                Tan8Decision decision;
                try
                {
                    decision = Probe(path, pathVid, pathPid, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    decision = AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.SendFailed, default, false, default, false);
                }

                if (decision.Outcome == Tan8Outcome.None)
                    continue;
                var key = (pathVid, pathPid);
                if (!best.TryGetValue(key, out var existing) || decision.Outcome > existing.Outcome)
                    best[key] = decision;
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        foreach (var ((vid, pid), decision) in best)
        {
            readings.Add(new RawDeviceReading
            {
                SourceId = "akko-tan8-ultra",
                Priority = SourcePriority.Vendor,
                InstanceId = "akko-tan8:" + vid.ToString("X4") + ":" + pid.ToString("X4"),
                WindowsName = decision.DisplayName,
                VendorId = vid,
                ProductId = pid,
                KindHint = DeviceKind.Mouse,
                BatteryPercent = decision.BatteryPercent,
                Note = decision.Note,
            });
        }

        return readings;
    }

    private static Tan8Decision Probe(string path, int pathVid, int pathPid, CancellationToken cancellationToken)
    {
        var handle = Open(path, overlapped: true);
        if (handle == IntPtr.Zero || handle == NativeMethods.InvalidHandle)
        {
            var error = Marshal.GetLastWin32Error();
            var failure = error is NativeMethods.ErrorAccessDenied or NativeMethods.ErrorSharingViolation
                ? Tan8ProbeFailure.InterfaceBusy
                : Tan8ProbeFailure.SendFailed;
            return AkkoTan8Ultra.Decide(pathVid, pathPid, failure, default, false, default, false);
        }

        var plain = IntPtr.Zero;
        var ioEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
        if (ioEvent == IntPtr.Zero || ioEvent == NativeMethods.InvalidHandle)
        {
            NativeMethods.CloseHandle(handle);
            return AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.SendFailed, default, false, default, false);
        }

        try
        {
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)
                || attributes.VendorId != pathVid
                || attributes.ProductId != pathPid
                || !AkkoTan8Ultra.IsCandidate(attributes.VendorId, attributes.ProductId))
                return default;

            if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
                return AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.SendFailed, default, false, default, false);

            try
            {
                var caps = new HidCaps();
                if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                    return AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.SendFailed, default, false, default, false);
                if (!AkkoTan8Ultra.IsVendorCollection(caps.UsagePage, caps.Usage))
                    return AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.NotVendorCollection, default, false, default, false);

                NativeMethods.HidD_FlushQueue(handle);
                if (cancellationToken.IsCancellationRequested)
                    return AkkoTan8Ultra.Decide(pathVid, pathPid, Tan8ProbeFailure.NoReply, default, false, default, false);

                // The wrapper's start ack is optional. A sleeping 2.4G radio may miss it and still answer 0x30.
                Transact(handle, ref plain, path, ioEvent, caps, AkkoTan8Ultra.StartPayload(), AkkoTan8Ultra.StartCommand, AkkoTan8Ultra.MinimumWrappedResponse, 1, 1000, cancellationToken);
                var identity = Transact(handle, ref plain, path, ioEvent, caps, AkkoTan8Ultra.IdentityPayload(), AkkoTan8Ultra.IdentityCommand, AkkoTan8Ultra.MinimumIdentityResponse, Attempts, TimeoutMs, cancellationToken);
                if (identity.Payload is null || !AkkoTan8Ultra.TryReadCid(identity.Payload, out var cid))
                {
                    var failure = identity.Sent ? Tan8ProbeFailure.NoReply : Tan8ProbeFailure.SendFailed;
                    return AkkoTan8Ultra.Decide(attributes.VendorId, attributes.ProductId, failure, default, false, default, false);
                }

                Transact(handle, ref plain, path, ioEvent, caps, AkkoTan8Ultra.EndPayload(), AkkoTan8Ultra.EndCommand, AkkoTan8Ultra.MinimumWrappedResponse, 1, 500, cancellationToken);
                if (cid != AkkoTan8Ultra.ModelCid)
                    return AkkoTan8Ultra.Decide(attributes.VendorId, attributes.ProductId, Tan8ProbeFailure.None, identity.Payload, true, default, false);

                var battery = Transact(handle, ref plain, path, ioEvent, caps, AkkoTan8Ultra.BatteryPayload(), AkkoTan8Ultra.BatteryCommand, AkkoTan8Ultra.MinimumBatteryResponse, Attempts, TimeoutMs, cancellationToken);
                return AkkoTan8Ultra.Decide(
                    attributes.VendorId,
                    attributes.ProductId,
                    Tan8ProbeFailure.None,
                    identity.Payload,
                    true,
                    battery.Payload ?? [],
                    battery.Payload is not null);
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

    private readonly record struct Exchange(bool Sent, byte[]? Payload);

    private static Exchange Transact(
        IntPtr handle,
        ref IntPtr plain,
        string path,
        IntPtr ioEvent,
        HidCaps caps,
        byte[] payload,
        byte command,
        int minimumPayload,
        int attempts,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var sent = false;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            if (!Send(handle, ref plain, path, ioEvent, caps, payload, cancellationToken))
                continue;
            sent = true;
            var reply = ReadReply(handle, ref plain, path, ioEvent, caps, command, minimumPayload, timeoutMs, cancellationToken);
            if (reply is not null)
                return new Exchange(true, reply);
        }

        return new Exchange(sent, null);
    }

    private static bool Send(
        IntPtr handle,
        ref IntPtr plain,
        string path,
        IntPtr ioEvent,
        HidCaps caps,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (caps.OutputReportByteLength >= 1 + AkkoTan8Ultra.PayloadLength)
        {
            var output = new byte[caps.OutputReportByteLength];
            output[0] = AkkoTan8Ultra.ReportId;
            Buffer.BlockCopy(payload, 0, output, 1, payload.Length);
            if (WriteFile(handle, ioEvent, output, cancellationToken))
                return true;
        }

        var control = new byte[1 + AkkoTan8Ultra.PayloadLength];
        control[0] = AkkoTan8Ultra.ReportId;
        Buffer.BlockCopy(payload, 0, control, 1, payload.Length);
        plain = EnsurePlain(plain, path);
        return plain != IntPtr.Zero
            && plain != NativeMethods.InvalidHandle
            && NativeMethods.HidD_SetOutputReport(plain, control, control.Length);
    }

    private static byte[]? ReadReply(
        IntPtr handle,
        ref IntPtr plain,
        string path,
        IntPtr ioEvent,
        HidCaps caps,
        byte command,
        int minimumPayload,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var inputLength = caps.InputReportByteLength > 0 ? caps.InputReportByteLength : 1 + AkkoTan8Ultra.PayloadLength;
        var input = new byte[inputLength];
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;
            var remaining = (int)Math.Max(1, Math.Min(int.MaxValue, deadline - Environment.TickCount64));
            if (!ReadFile(handle, ioEvent, input, remaining, out var read, cancellationToken))
                break;
            if (Match(input.AsSpan(0, read), command, minimumPayload, out var payload))
                return payload;
        }

        plain = EnsurePlain(plain, path);
        if (plain == IntPtr.Zero || plain == NativeMethods.InvalidHandle)
            return null;
        var controlLength = Math.Max(inputLength, 1 + AkkoTan8Ultra.PayloadLength);
        var control = new byte[controlLength];
        control[0] = AkkoTan8Ultra.ReportId;
        if (!NativeMethods.HidD_GetInputReport(plain, control, control.Length))
            return null;
        return Match(control, command, minimumPayload, out var fromControl) ? fromControl : null;
    }

    private static bool Match(ReadOnlySpan<byte> report, byte command, int minimumPayload, out byte[]? payload)
    {
        payload = null;
        if (!AkkoTan8Ultra.TryGetPayload(report, out var body))
            return false;
        if (!AkkoTan8Ultra.Matches(body, command, minimumPayload))
            return false;
        payload = body.ToArray();
        return true;
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
}
