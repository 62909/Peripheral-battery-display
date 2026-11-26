namespace PeripheralBattery.Core;

public static class DeviceAggregator
{
    public static IReadOnlyList<PeripheralDevice> Combine(
        IEnumerable<RawDeviceReading> readings,
        IIdentityOverrideStore overrides)
    {
        var groups = new Dictionary<string, List<RawDeviceReading>>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in readings)
        {
            var key = GroupKey(reading);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups.Add(key, list);
            }

            list.Add(reading);
        }

        var devices = new List<PeripheralDevice>(groups.Count);
        foreach (var (key, group) in groups)
        {
            var over = overrides.Find(key);
            var windowsName = BestWindowsName(group);
            var (vid, pid) = BestIds(group);
            var power = PickBattery(group);
            devices.Add(new PeripheralDevice(
                key,
                DeviceIdentity.ResolveDisplayName(windowsName, vid, pid, over?.DisplayName),
                DeviceIdentity.ResolveKind(windowsName, BestKind(group), over?.Kind),
                power.Percent,
                BestNote(group),
                power.Charging));
        }

        return devices
            .OrderBy(d => d.BatteryPercent is null)
            .ThenBy(d => d.BatteryPercent ?? int.MaxValue)
            .ThenBy(d => d.DisplayName ?? "\uffff", StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string GroupKey(RawDeviceReading reading)
    {
        var address = DeviceIdentity.NormalizeBluetoothAddress(reading.BluetoothAddress)
            ?? DeviceIdentity.TryExtractBluetoothAddress(reading.InstanceId);
        if (address is not null)
            return "bt:" + address;
        if (!string.IsNullOrWhiteSpace(reading.InstanceId))
            return "dev:" + reading.InstanceId.Trim();
        var name = DeviceIdentity.StripProfileSuffix(reading.WindowsName) ?? "";
        return "loose:" + reading.SourceId + "|" + name;
    }

    private static string? BestWindowsName(List<RawDeviceReading> group)
    {
        return group
            .OrderByDescending(r => r.Priority)
            .Select(r => DeviceIdentity.StripProfileSuffix(r.WindowsName))
            .FirstOrDefault(DeviceIdentity.IsMeaningfulName)
            ?? group.Select(r => DeviceIdentity.StripProfileSuffix(r.WindowsName)).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
    }

    private static (int? Vid, int? Pid) BestIds(List<RawDeviceReading> group)
    {
        var withBoth = group.FirstOrDefault(r => r.VendorId is not null && r.ProductId is not null);
        if (withBoth is not null)
            return (withBoth.VendorId, withBoth.ProductId);
        var withVid = group.FirstOrDefault(r => r.VendorId is not null);
        return (withVid?.VendorId, withVid?.ProductId);
    }

    private static DeviceKind BestKind(List<RawDeviceReading> group)
    {
        var hinted = group.Where(r => r.KindHint != DeviceKind.Unknown).ToList();
        if (hinted.Count == 0)
            return DeviceKind.Unknown;
        var bestPriority = hinted.Max(r => r.Priority);
        var top = hinted.Where(r => r.Priority == bestPriority).Select(r => r.KindHint).Distinct().ToList();
        if (top.Count == 1)
            return top[0];
        return DeviceKind.Other;
    }

    private static string? BestNote(List<RawDeviceReading> group)
    {
        return group
            .OrderByDescending(r => r.Priority)
            .Select(r => r.Note)
            .FirstOrDefault(note => !string.IsNullOrWhiteSpace(note));
    }

    private readonly record struct PickedBattery(int? Percent, bool? Charging);

    /// <summary>
    /// Percentage rules are unchanged: the highest priority that reported a
    /// 0–100 value wins, and two different values at that priority stay unknown.
    /// Charging is taken only from those winning readings. An omitted flag is
    /// not a vote. One explicit value is kept; true and false together stay null.
    /// </summary>
    private static PickedBattery PickBattery(List<RawDeviceReading> group)
    {
        var known = group
            .Select(r => (r.Priority, Percent: BatteryMath.NormalizePercent(r.BatteryPercent), r.Charging))
            .Where(r => r.Percent is not null)
            .ToList();
        if (known.Count == 0)
            return new(null, null);
        var bestPriority = known.Max(r => r.Priority);
        var winners = known.Where(r => r.Priority == bestPriority).ToList();
        var values = winners.Select(r => r.Percent!.Value).Distinct().ToList();
        if (values.Count != 1)
            return new(null, null);
        var flags = winners.Where(r => r.Charging is not null).Select(r => r.Charging!.Value).Distinct().ToList();
        bool? charging = flags.Count == 1 ? flags[0] : null;
        return new(values[0], charging);
    }
}

public sealed class EmptyOverrideStore : IIdentityOverrideStore
{
    public static EmptyOverrideStore Instance { get; } = new();

    public IdentityOverride? Find(string stableKey) => null;
}
