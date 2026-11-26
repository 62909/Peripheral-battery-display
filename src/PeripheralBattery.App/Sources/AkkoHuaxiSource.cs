using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// 38EE:0022 on the Huaxi collection FFA8:0002 (USB MI_02).
/// The mouse interface MI_00 / 0001:0002 is never opened.
/// The info collection is opened exclusively first, then shared if that fails.
/// initDevice runs before DevGetInfo, and each command arms its read first.
/// </summary>
internal sealed class AkkoHuaxiSource : IVendorBatterySource
{
    public string SourceId => "akko-huaxi";
    public int Priority => SourcePriority.Vendor;

    public Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => (IReadOnlyList<RawDeviceReading>)Read(cancellationToken), cancellationToken);
    }

    private static List<RawDeviceReading> Read(CancellationToken cancellationToken)
    {
        var readings = new List<RawDeviceReading>();
        var openFailed = false;
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
                if (AkkoHuaxiMouse.IsMouseInterfacePath(path))
                    continue;
                try
                {
                    if (Probe(path, cancellationToken, out var failed) is RawDeviceReading reading)
                        readings.Add(reading);
                    else if (failed)
                        openFailed = true;
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

        if (readings.Count == 0 && openFailed)
            readings.Add(Reading(null, null, AkkoHuaxiMouse.NoteInterfaceBusy));
        return readings;
    }

    private static RawDeviceReading? Probe(string path, CancellationToken cancellationToken, out bool openFailed)
    {
        openFailed = false;
        var handle = Open(path, exclusive: true);
        if (!Usable(handle))
            handle = Open(path, exclusive: false);
        if (!Usable(handle))
        {
            openFailed = true;
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
                if (caps.UsagePage == 0x0001 && caps.Usage == 0x0002)
                    return null;
                if (!AkkoHuaxiMouse.IsInfoCollection(caps.UsagePage, caps.Usage))
                    return null;

                var name = ReadProduct(handle);
                using var link = new AkkoHuaxiLink(handle, caps.InputReportByteLength, cancellationToken);
                var result = AkkoHuaxiExchange.ReadBattery(
                    link,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    cancellationToken);
                return result.Status switch
                {
                    AkkoHuaxiExchangeStatus.Ok => Reading(name, result.Info.BatteryPercent, null, result.Info.Charging),
                    AkkoHuaxiExchangeStatus.LevelOutOfRange => Reading(name, null, AkkoHuaxiMouse.NoteLevelOutOfRange),
                    AkkoHuaxiExchangeStatus.SendFailed => Reading(name, null, AkkoHuaxiMouse.NoteSendFailed),
                    AkkoHuaxiExchangeStatus.MouseNotConnected => Reading(name, null, AkkoHuaxiMouse.NoteMouseNotConnected),
                    _ => Reading(name, null, AkkoHuaxiMouse.NoteNoInfoReply),
                };
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static RawDeviceReading Reading(string? name, int? percent, string? note, bool? charging = null) => new()
    {
        SourceId = "akko-huaxi",
        Priority = SourcePriority.Vendor,
        InstanceId = "huaxi:" + AkkoHuaxiMouse.VendorId.ToString("X4") + ":" + AkkoHuaxiMouse.ProductId.ToString("X4"),
        WindowsName = name,
        VendorId = AkkoHuaxiMouse.VendorId,
        ProductId = AkkoHuaxiMouse.ProductId,
        KindHint = DeviceKind.Mouse,
        BatteryPercent = percent,
        Charging = charging,
        Note = note,
    };

    private static IntPtr Open(string path, bool exclusive)
    {
        // The info collection is opened with no sharing first, like a page that
        // owns the device. If that fails, fall back to the shared open.
        var share = exclusive
            ? 0u
            : NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete;
        var access = NativeMethods.GenericRead | NativeMethods.GenericWrite;
        return NativeMethods.CreateFile(path, access, share, IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.FileFlagOverlapped, IntPtr.Zero);
    }

    private static bool Usable(IntPtr handle) =>
        handle != IntPtr.Zero && handle != NativeMethods.InvalidHandle;

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
