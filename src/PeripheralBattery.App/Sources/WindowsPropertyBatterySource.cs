using System.Text;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// Reads the battery byte Windows already stores on Bluetooth devnodes:
/// {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2. This is the same value many
/// headsets report through the hands-free profile. The property is not in the
/// public SDK headers; when it is missing the device stays 未知.
/// </summary>
internal sealed class WindowsPropertyBatterySource : IPeripheralSource
{
    public string SourceId => "winprop";
    public int Priority => SourcePriority.WindowsProperty;

    public Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => (IReadOnlyList<RawDeviceReading>)Read(cancellationToken), cancellationToken);
    }

    private static List<RawDeviceReading> Read(CancellationToken cancellationToken)
    {
        var nodes = new List<Node>();
        foreach (var id in PresentBluetoothIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.CM_Locate_DevNode(out var devInst, id, 0) != NativeMethods.CrSuccess)
                continue;
            var name = ReadString(devInst, NativeMethods.FriendlyName) ?? ReadString(devInst, NativeMethods.DeviceDesc);
            var address = DeviceIdentity.NormalizeBluetoothAddress(ReadString(devInst, NativeMethods.BluetoothAddress))
                ?? DeviceIdentity.TryExtractBluetoothAddress(id);
            int? battery = null;
            if (TryGetProperty(devInst, NativeMethods.BatteryLevel, out var type, out var data))
                battery = BatteryParsing.ParseWindowsBatteryProperty(type, data);

            int? major = null;
            var hint = DeviceKind.Unknown;
            if (TryGetProperty(devInst, NativeMethods.BluetoothClassOfDevice, out var codType, out var codData)
                && codType == NativeMethods.DevPropTypeUint32
                && codData.Length >= 4)
            {
                var cod = BitConverter.ToUInt32(codData, 0);
                var decoded = DeviceIdentity.DecodeClassOfDevice(cod);
                major = decoded.Major;
                hint = DeviceIdentity.FromBluetoothClass(decoded.Major, decoded.Minor);
            }

            var (vid, pid) = DeviceIdentity.ParseVidPid(id);
            if (!DeviceIdentity.ShouldListNode(name, major, battery, hint))
                continue;
            nodes.Add(new Node(address, id, name, vid, pid, hint, major, battery));
        }

        var readings = new List<RawDeviceReading>();
        foreach (var group in nodes.Where(n => n.Address is not null).GroupBy(n => n.Address!, StringComparer.OrdinalIgnoreCase))
            readings.Add(Merge(group));
        foreach (var node in nodes.Where(n => n.Address is null))
            readings.Add(ToReading(node, node.Battery));
        return readings;
    }

    private static RawDeviceReading Merge(IEnumerable<Node> nodes)
    {
        var list = nodes.ToList();
        var battery = BatteryParsing.CollapseWindowsPercents(list.Select(n => n.Battery));
        var name = list.Select(n => n.Name).FirstOrDefault(DeviceIdentity.IsMeaningfulName)
            ?? list.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        var hint = list.Select(n => n.Hint).FirstOrDefault(k => k != DeviceKind.Unknown);
        var withIds = list.FirstOrDefault(n => n.VendorId is not null);
        var address = list[0].Address;
        return new RawDeviceReading
        {
            SourceId = "winprop",
            Priority = SourcePriority.WindowsProperty,
            BluetoothAddress = address,
            InstanceId = list[0].InstanceId,
            WindowsName = name,
            VendorId = withIds?.VendorId,
            ProductId = withIds?.ProductId,
            KindHint = hint,
            BatteryPercent = battery,
        };
    }

    private static RawDeviceReading ToReading(Node node, int? battery) => new()
    {
        SourceId = "winprop",
        Priority = SourcePriority.WindowsProperty,
        BluetoothAddress = node.Address,
        InstanceId = node.InstanceId,
        WindowsName = node.Name,
        VendorId = node.VendorId,
        ProductId = node.ProductId,
        KindHint = node.Hint,
        BatteryPercent = battery,
    };

    private static IEnumerable<string> PresentBluetoothIds()
    {
        if (NativeMethods.CM_Get_Device_ID_List_Size(out var length, null, NativeMethods.CmGetIdListFilterPresent) != NativeMethods.CrSuccess
            || length <= 1)
            yield break;
        var buffer = new char[length];
        if (NativeMethods.CM_Get_Device_ID_List(null, buffer, length, NativeMethods.CmGetIdListFilterPresent) != NativeMethods.CrSuccess)
            yield break;
        foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                || id.Contains("BTHLE", StringComparison.OrdinalIgnoreCase))
                yield return id;
        }
    }

    private static string? ReadString(int devInst, DEVPROPKEY key)
    {
        if (!TryGetProperty(devInst, key, out var type, out var data) || type != NativeMethods.DevPropTypeString || data.Length < 2)
            return null;
        var text = Encoding.Unicode.GetString(data);
        var end = text.IndexOf('\0');
        if (end >= 0)
            text = text[..end];
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool TryGetProperty(int devInst, DEVPROPKEY key, out int type, out byte[] data)
    {
        var buffer = new byte[512];
        var size = buffer.Length;
        var cr = NativeMethods.CM_Get_DevNode_Property(devInst, ref key, out type, buffer, ref size, 0);
        if (cr == NativeMethods.CrBufferSmall && size > 0 && size < 1024 * 1024)
        {
            buffer = new byte[size];
            cr = NativeMethods.CM_Get_DevNode_Property(devInst, ref key, out type, buffer, ref size, 0);
        }

        if (cr != NativeMethods.CrSuccess || size <= 0)
        {
            data = [];
            return false;
        }

        if (size < buffer.Length)
            Array.Resize(ref buffer, size);
        data = buffer;
        return true;
    }

    private sealed record Node(
        string? Address,
        string InstanceId,
        string? Name,
        int? VendorId,
        int? ProductId,
        DeviceKind Hint,
        int? Major,
        int? Battery);
}
