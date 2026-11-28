using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// XIBERIA HUB headset family <c>legacy-55</c> / <c>usb-audio-k06s</c>.
/// Opens the collection that carries report 85 and reads command 9, then
/// command 1 when that reply has no 0–100 level. Charging follows the hub
/// table's wired or wireless mark for that id. The reply has no charge byte.
/// Report 85 is the top-level collection's value caps and button caps.
/// A partial cap list still counts. The first shared open of a candidate
/// that fails is the busy row.
/// </summary>
internal sealed class XiberiaHeadsetSource : IVendorBatterySource
{
    public string SourceId => "xiberia-headset";
    public int Priority => SourcePriority.Vendor;

    public Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => (IReadOnlyList<RawDeviceReading>)Read(cancellationToken), cancellationToken);
    }

    private static List<RawDeviceReading> Read(CancellationToken cancellationToken)
    {
        var readings = new List<RawDeviceReading>();
        var busy = false;
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
                if (ids.VendorId is not int vid || ids.ProductId is not int pid || !XiberiaHeadset.IsCandidate(vid, pid))
                    continue;
                try
                {
                    if (Probe(path, vid, pid, cancellationToken, out var failed) is RawDeviceReading reading)
                        readings.Add(reading);
                    else if (failed)
                        busy = true;
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

        if (readings.Count == 0 && busy)
            readings.Add(BusyReading());
        return PreferLevels(readings);
    }

    private static RawDeviceReading? Probe(
        string path,
        int vendorId,
        int productId,
        CancellationToken cancellationToken,
        out bool openFailed)
    {
        openFailed = false;
        var looked = Open(path, exclusive: false);
        var first = XiberiaHeadset.SharedOpen(Usable(looked));
        if (!first.Continue)
        {
            openFailed = first.Busy;
            return null;
        }

        try
        {
            if (!TryMatch(looked, vendorId, productId))
                return null;
        }
        finally
        {
            NativeMethods.CloseHandle(looked);
        }

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
            if (!TryMatch(handle, vendorId, productId))
                return null;
            if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
                return null;
            try
            {
                var caps = new HidCaps();
                if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                    return null;
                var name = ReadProduct(handle);
                using var link = new AkkoHuaxiLink(handle, caps.InputReportByteLength, cancellationToken);
                var result = XiberiaHeadset.ReadBattery(link, caps.InputReportByteLength, caps.OutputReportByteLength, cancellationToken);
                var percent = result.Status == AtkExchangeStatus.Ok ? result.Battery.Percent : null;
                var note = result.Status == AtkExchangeStatus.Ok ? null : Note(result.Status);
                return Reading(name, vendorId, productId, percent, XiberiaHeadset.ChargingFor(vendorId, productId, name), note);
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

    private static bool TryMatch(IntPtr handle, int vendorId, int productId)
    {
        var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
            return false;
        if (attributes.VendorId != vendorId || attributes.ProductId != productId)
            return false;
        if (!XiberiaHeadset.IsCandidate(attributes.VendorId, attributes.ProductId))
            return false;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
            return false;
        try
        {
            var caps = new HidCaps();
            if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                return false;
            // P() checks usage and output reports on the same top-level
            // collection. Child collections stay out of both.
            return XiberiaHeadset.MatchesOfficialCollection(
                attributes.VendorId,
                attributes.ProductId,
                ReadProduct(handle),
                caps.UsagePage,
                caps.Usage,
                ReadCaps(preparsed, NativeMethods.HidPInput, caps.NumberInputValueCaps, buttons: false),
                ReadCaps(preparsed, NativeMethods.HidPInput, caps.NumberInputButtonCaps, buttons: true),
                ReadCaps(preparsed, NativeMethods.HidPOutput, caps.NumberOutputValueCaps, buttons: false),
                ReadCaps(preparsed, NativeMethods.HidPOutput, caps.NumberOutputButtonCaps, buttons: true));
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }
    }

    private static XiberiaCapList ReadCaps(IntPtr preparsed, int reportType, ushort count, bool buttons)
    {
        if (count == 0)
            return new(true, []);
        // An unread list must not look empty: N() would accept report 85
        // for an empty item list, and P() would treat output reports as absent.
        if (count > 256)
            return new(false, null);
        if (buttons)
        {
            var caps = new HidButtonCaps[count];
            var copied = count;
            if (NativeMethods.HidP_GetButtonCaps(reportType, caps, ref copied, preparsed) != NativeMethods.HidPStatusSuccess)
                return new(false, null);
            var limit = Math.Min(copied, caps.Length);
            var list = new XiberiaCap[limit];
            for (var i = 0; i < limit; i++)
            {
                list[i] = new XiberiaCap(
                    caps[i].ReportId,
                    0,
                    0,
                    caps[i].LinkCollection,
                    IsButton: true,
                    IsAlias: caps[i].IsAlias != 0);
            }

            return new(true, list);
        }

        var values = new HidValueCaps[count];
        var valueCopied = count;
        if (NativeMethods.HidP_GetValueCaps(reportType, values, ref valueCopied, preparsed) != NativeMethods.HidPStatusSuccess)
            return new(false, null);
        var valueLimit = Math.Min(valueCopied, values.Length);
        var valueList = new XiberiaCap[valueLimit];
        for (var i = 0; i < valueLimit; i++)
        {
            valueList[i] = new XiberiaCap(
                values[i].ReportId,
                values[i].BitSize,
                values[i].ReportCount,
                values[i].LinkCollection,
                IsButton: false,
                IsAlias: values[i].IsAlias != 0);
        }

        return new(true, valueList);
    }

    private static List<RawDeviceReading> PreferLevels(List<RawDeviceReading> readings)
    {
        var leveled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in readings)
        {
            if (reading.BatteryPercent is not null && reading.InstanceId is not null)
                leveled.Add(reading.InstanceId);
        }

        if (leveled.Count == 0)
            return readings;
        return readings
            .Where(reading => reading.BatteryPercent is not null || reading.InstanceId is null || !leveled.Contains(reading.InstanceId))
            .ToList();
    }

    private static string Note(AtkExchangeStatus status) => status switch
    {
        AtkExchangeStatus.LevelOutOfRange => XiberiaHeadset.NoteLevelOutOfRange,
        AtkExchangeStatus.SendFailed => XiberiaHeadset.NoteSendFailed,
        _ => XiberiaHeadset.NoteNoReply,
    };

    private static RawDeviceReading BusyReading() => new()
    {
        SourceId = "xiberia-headset",
        Priority = SourcePriority.Vendor,
        InstanceId = "xiberia-headset:busy",
        Note = XiberiaHeadset.NoteInterfaceBusy,
    };

    private static RawDeviceReading Reading(
        string? name,
        int vendorId,
        int productId,
        int? percent,
        bool? charging,
        string? note) => new()
    {
        SourceId = "xiberia-headset",
        Priority = SourcePriority.Vendor,
        InstanceId = "xiberia-headset:" + vendorId.ToString("X4") + ":" + productId.ToString("X4"),
        WindowsName = name,
        VendorId = vendorId,
        ProductId = productId,
        KindHint = DeviceKind.Headset,
        BatteryPercent = percent,
        Charging = charging,
        Note = note,
    };

    private static IntPtr Open(string path, bool exclusive)
    {
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
