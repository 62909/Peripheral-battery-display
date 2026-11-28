using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// ATK HUB 3.2.34 protocols that publish a battery query.
/// BITMOUSE, COMPX, and HECHI are separate mouse collections.
/// HFD is a keyboard collection limited to the product ids on that controller.
/// EVISION is the <c>0x320F</c> keyboard controller (wired <c>0x5055</c>, 2.4G <c>0x5088</c>).
/// A usage page alone is not enough: BITYUAN keyboards share FF60 and are not queried.
/// </summary>
internal sealed class AtkOfficialSource : IVendorBatterySource
{
    private static readonly Protocol[] Protocols =
    [
        new(
            "atk-bitmouse",
            DeviceKind.Mouse,
            AtkBitmouse.IsCollection,
            (link, input, output, _, token) => AtkBitmouse.ReadBattery(link, input, output, token),
            static status => status switch
            {
                AtkExchangeStatus.LevelOutOfRange => AtkBitmouse.NoteLevelOutOfRange,
                AtkExchangeStatus.SendFailed => AtkBitmouse.NoteSendFailed,
                AtkExchangeStatus.DeviceTypeUnknown => AtkBitmouse.NoteDeviceTypeUnknown,
                AtkExchangeStatus.NoDeviceType => AtkBitmouse.NoteNoDeviceType,
                _ => AtkBitmouse.NoteNoReply,
            }),
        new(
            "atk-compx",
            DeviceKind.Mouse,
            AtkCompx.IsCollection,
            (link, input, output, _, token) => AtkCompx.ReadBattery(link, input, output, token),
            static status => status switch
            {
                AtkExchangeStatus.LevelOutOfRange => AtkCompx.NoteLevelOutOfRange,
                AtkExchangeStatus.SendFailed => AtkCompx.NoteSendFailed,
                _ => AtkCompx.NoteNoReply,
            }),
        new(
            "atk-hechi",
            DeviceKind.Mouse,
            AtkHechi.IsCollection,
            (link, input, output, _, token) => AtkHechi.ReadBattery(link, input, output, token),
            static status => status switch
            {
                AtkExchangeStatus.LevelOutOfRange => AtkHechi.NoteLevelOutOfRange,
                AtkExchangeStatus.SendFailed => AtkHechi.NoteSendFailed,
                _ => AtkHechi.NoteNoReply,
            }),
        new(
            "atk-hfd",
            DeviceKind.Keyboard,
            AtkHfdKeyboard.IsCollection,
            (link, input, output, _, token) => AtkHfdKeyboard.ReadBattery(link, input, output, token),
            static status => status switch
            {
                AtkExchangeStatus.ChargingWithoutLevel => AtkHfdKeyboard.NoteChargingWithoutLevel,
                AtkExchangeStatus.LevelOutOfRange => AtkHfdKeyboard.NoteLevelOutOfRange,
                AtkExchangeStatus.SendFailed => AtkHfdKeyboard.NoteSendFailed,
                _ => AtkHfdKeyboard.NoteNoReply,
            }),
        new(
            "atk-evision",
            DeviceKind.Keyboard,
            AtkEvisionKeyboard.IsCollection,
            (link, input, output, productId, token) => AtkEvisionKeyboard.ReadBattery(link, input, output, productId, token),
            static status => status switch
            {
                AtkExchangeStatus.LevelOutOfRange => AtkEvisionKeyboard.NoteLevelOutOfRange,
                AtkExchangeStatus.SendFailed => AtkEvisionKeyboard.NoteSendFailed,
                _ => AtkEvisionKeyboard.NoteNoReply,
            }),
    ];

    public string SourceId => "atk-official";
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
                if (ids.VendorId is not int vid || ids.ProductId is not int pid || !AcceptsVendor(vid))
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
        if (!Usable(looked))
            return null;

        Protocol? protocol;
        try
        {
            if (!TryMatch(looked, vendorId, productId, out protocol) || protocol is null)
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
            if (!TryMatch(handle, vendorId, productId, out var again) || again != protocol || protocol is null)
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
                var result = protocol.Read(link, caps.InputReportByteLength, caps.OutputReportByteLength, productId, cancellationToken);
                var percent = result.Status == AtkExchangeStatus.Ok ? result.Battery.Percent : null;
                var charging = result.Status is AtkExchangeStatus.Ok or AtkExchangeStatus.ChargingWithoutLevel
                    ? result.Battery.Charging
                    : null;
                var note = result.Status == AtkExchangeStatus.Ok ? null : protocol.Note(result.Status);
                return Reading(protocol, name, vendorId, productId, percent, charging, note);
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

    private static bool TryMatch(IntPtr handle, int vendorId, int productId, out Protocol? protocol)
    {
        protocol = null;
        var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
            return false;
        if (attributes.VendorId != vendorId || attributes.ProductId != productId)
            return false;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
            return false;
        try
        {
            var caps = new HidCaps();
            if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                return false;
            foreach (var candidate in Protocols)
            {
                if (!candidate.Matches(attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage))
                    continue;
                protocol = candidate;
                return true;
            }

            return false;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }
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

    private static RawDeviceReading BusyReading() => new()
    {
        SourceId = "atk-official",
        Priority = SourcePriority.Vendor,
        InstanceId = "atk-official:busy",
        Note = AtkBitmouse.NoteInterfaceBusy,
    };

    private static RawDeviceReading Reading(
        Protocol protocol,
        string? name,
        int vendorId,
        int productId,
        int? percent,
        bool? charging,
        string? note) => new()
    {
        SourceId = protocol.Id,
        Priority = SourcePriority.Vendor,
        InstanceId = protocol.Id + ":" + vendorId.ToString("X4") + ":" + productId.ToString("X4"),
        WindowsName = name,
        VendorId = vendorId,
        ProductId = productId,
        KindHint = protocol.Kind,
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

    private static bool AcceptsVendor(int vendorId) =>
        vendorId == AtkBitmouse.VendorId || vendorId == AtkEvisionKeyboard.VendorId;

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

    private sealed class Protocol
    {
        public Protocol(
            string id,
            DeviceKind kind,
            Func<int, int, int, int, bool> matches,
            Func<IAtkReportLink, int, int, int, CancellationToken, AtkExchangeResult> read,
            Func<AtkExchangeStatus, string> note)
        {
            Id = id;
            Kind = kind;
            Matches = matches;
            Read = read;
            Note = note;
        }

        public string Id { get; }
        public DeviceKind Kind { get; }
        public Func<int, int, int, int, bool> Matches { get; }
        public Func<IAtkReportLink, int, int, int, CancellationToken, AtkExchangeResult> Read { get; }
        public Func<AtkExchangeStatus, string> Note { get; }
    }
}
