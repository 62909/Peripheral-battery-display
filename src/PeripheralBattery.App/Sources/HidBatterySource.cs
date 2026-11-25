using System.Runtime.InteropServices;
using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// Standard HID Battery System page (0x85). Vendor pages are not interpreted.
/// Wired HID devices are listed only when that page is actually present,
/// so a built-in keyboard without a battery report stays out of the list.
/// </summary>
internal sealed class HidBatterySource : IPeripheralSource
{
    private static readonly ushort[] InterestingUsages =
    [
        BatteryParsing.UsageCapacityMode,
        BatteryParsing.UsageRelativeStateOfCharge,
        BatteryParsing.UsageAbsoluteStateOfCharge,
        BatteryParsing.UsageRemainingCapacity,
    ];

    public string SourceId => "hid";
    public int Priority => SourcePriority.Hid;

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
                try
                {
                    if (TryReadDevice(path, out var reading))
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

    private static bool TryReadDevice(string path, out RawDeviceReading reading)
    {
        reading = null!;
        var handle = Open(path);
        if (handle == IntPtr.Zero || handle == NativeMethods.InvalidHandle)
            return false;

        IntPtr preparsed = IntPtr.Zero;
        try
        {
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            var gotAttributes = NativeMethods.HidD_GetAttributes(handle, ref attributes);
            var product = ReadString(handle, NativeMethods.HidD_GetProductString);
            var manufacturer = ReadString(handle, NativeMethods.HidD_GetManufacturerString);
            var name = DeviceIdentity.PreferReportedName(product, manufacturer);
            if (!NativeMethods.HidD_GetPreparsedData(handle, out preparsed) || preparsed == IntPtr.Zero)
                return false;

            var caps = new HidCaps();
            if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HidPStatusSuccess)
                return false;

            var samples = new List<HidUsageValue>();
            var hasBatteryPage = Collect(handle, preparsed, caps, samples);
            var kind = DeviceIdentity.FromHidCollection(caps.UsagePage, caps.Usage);
            if (!DeviceIdentity.ShouldIncludeHid(path, kind, hasBatteryPage, caps.UsagePage))
                return false;
            if (DeviceIdentity.IsReceiverName(name) && !hasBatteryPage)
                return false;

            var parsedIds = DeviceIdentity.ParseVidPid(path);
            reading = new RawDeviceReading
            {
                SourceId = "hid",
                Priority = SourcePriority.Hid,
                BluetoothAddress = DeviceIdentity.TryExtractBluetoothAddress(path),
                InstanceId = path,
                WindowsName = name,
                VendorId = gotAttributes ? attributes.VendorId : parsedIds.VendorId,
                ProductId = gotAttributes ? attributes.ProductId : parsedIds.ProductId,
                KindHint = kind,
                BatteryPercent = BatteryParsing.ParseHidBatteryPercent(samples),
            };
            return true;
        }
        finally
        {
            if (preparsed != IntPtr.Zero)
                NativeMethods.HidD_FreePreparsedData(preparsed);
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool Collect(IntPtr handle, IntPtr preparsed, HidCaps caps, List<HidUsageValue> samples)
    {
        var found = false;
        ReadReport(handle, preparsed, NativeMethods.HidPFeature, caps.NumberFeatureValueCaps, caps.FeatureReportByteLength, samples, ref found);
        ReadReport(handle, preparsed, NativeMethods.HidPInput, caps.NumberInputValueCaps, caps.InputReportByteLength, samples, ref found);
        return found;
    }

    private static void ReadReport(
        IntPtr handle,
        IntPtr preparsed,
        int reportType,
        ushort capCount,
        ushort reportLength,
        List<HidUsageValue> samples,
        ref bool found)
    {
        var caps = ReadValueCaps(preparsed, reportType, capCount);
        var wanted = new List<(HidValueCaps Cap, ushort Usage)>();
        foreach (var cap in caps)
        {
            if (cap.UsagePage != BatteryParsing.BatterySystemPage)
                continue;
            foreach (var usage in InterestingUsages)
            {
                if (!cap.ContainsUsage(usage))
                    continue;
                found = true;
                wanted.Add((cap, usage));
            }
        }

        if (wanted.Count == 0 || reportLength == 0 || reportLength > 4096)
            return;

        foreach (var reportId in wanted.Select(item => item.Cap.ReportId).Distinct())
        {
            var buffer = new byte[reportLength];
            if (reportId != 0)
                buffer[0] = reportId;
            var ok = reportType == NativeMethods.HidPFeature
                ? NativeMethods.HidD_GetFeature(handle, buffer, buffer.Length)
                : NativeMethods.HidD_GetInputReport(handle, buffer, buffer.Length);
            if (!ok)
                continue;
            foreach (var (cap, usage) in wanted.Where(item => item.Cap.ReportId == reportId))
            {
                var status = NativeMethods.HidP_GetUsageValue(
                    reportType,
                    cap.UsagePage,
                    cap.LinkCollection,
                    usage,
                    out var raw,
                    preparsed,
                    buffer,
                    (uint)buffer.Length);
                if (status == NativeMethods.HidPStatusSuccess)
                    samples.Add(new HidUsageValue(cap.UsagePage, usage, unchecked((int)raw), cap.LogicalMin, cap.LogicalMax));
            }
        }
    }

    private static HidValueCaps[] ReadValueCaps(IntPtr preparsed, int reportType, ushort count)
    {
        if (count == 0 || count > 256)
            return [];
        var caps = new HidValueCaps[count];
        var copied = count;
        if (NativeMethods.HidP_GetValueCaps(reportType, caps, ref copied, preparsed) != NativeMethods.HidPStatusSuccess)
            return [];
        if (copied < caps.Length)
            Array.Resize(ref caps, copied);
        return caps;
    }

    private static IntPtr Open(string path)
    {
        var share = NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete;
        var handle = NativeMethods.CreateFile(path, NativeMethods.GenericRead, share, IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);
        if (handle != IntPtr.Zero && handle != NativeMethods.InvalidHandle)
            return handle;
        return NativeMethods.CreateFile(path, 0, share, IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);
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

    private static string? ReadString(IntPtr handle, Func<IntPtr, byte[], int, bool> read)
    {
        var bytes = new byte[512];
        if (!read(handle, bytes, bytes.Length))
            return null;
        var text = Encoding.Unicode.GetString(bytes);
        var end = text.IndexOf('\0');
        if (end >= 0)
            text = text[..end];
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
