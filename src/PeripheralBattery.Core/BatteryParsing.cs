namespace PeripheralBattery.Core;

/// <summary>
/// One decoded HID value. Logical range is the descriptor's range, not a scaled guess.
/// </summary>
public readonly record struct HidUsageValue(
    ushort UsagePage,
    ushort Usage,
    int Value,
    int LogicalMin,
    int LogicalMax);

public static class BatteryParsing
{
    public const ushort BatteryServiceUuid = 0x180F;
    public const ushort BatteryLevelUuid = 0x2A19;

    public const ushort BatterySystemPage = 0x85;
    public const ushort UsageCapacityMode = 0x2C;
    public const ushort UsageRelativeStateOfCharge = 0x64;
    public const ushort UsageAbsoluteStateOfCharge = 0x65;
    public const ushort UsageRemainingCapacity = 0x66;

    public const int CapacityModePercent = 2;
    public const int WindowsBatteryPropertyTypeByte = 0x00000003;

    /// <summary>
    /// Bluetooth Battery Level characteristic: one uint8, 0–100.
    /// Values outside that range are not a percentage.
    /// </summary>
    public static int? ParseGattBatteryLevel(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1)
            return null;
        return payload[0] <= 100 ? payload[0] : null;
    }

    /// <summary>
    /// Windows stores some Bluetooth batteries as a BYTE property
    /// {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2. Other property types are ignored.
    /// </summary>
    /// <summary>
    /// Several Bluetooth profile nodes can carry the same property.
    /// A lone 0 is kept. A 0 beside one positive reading is dropped,
    /// because this undocumented property often stores 0 when a node has no report.
    /// Two different positive readings stay unknown.
    /// </summary>
    public static int? CollapseWindowsPercents(IEnumerable<int?> values)
    {
        var distinct = values
            .Select(BatteryMath.NormalizePercent)
            .OfType<int>()
            .Distinct()
            .ToList();
        if (distinct.Count == 0)
            return null;
        if (distinct.Count == 1)
            return distinct[0];
        var positive = distinct.Where(v => v > 0).ToList();
        if (positive.Count == 1 && distinct.Contains(0))
            return positive[0];
        return null;
    }

    public static int? ParseWindowsBatteryProperty(int propertyType, ReadOnlySpan<byte> data)
    {
        if (propertyType != WindowsBatteryPropertyTypeByte || data.Length != 1)
            return null;
        return data[0] <= 100 ? data[0] : null;
    }

    /// <summary>
    /// Standard HID Battery System page only.
    /// RelativeStateOfCharge (0x64) is defined as a percent.
    /// AbsoluteStateOfCharge (0x65) is a percent of design capacity; values above 100 are left unknown rather than clipped.
    /// RemainingCapacity (0x66) is accepted only when CapacityMode says percent, or the descriptor range is exactly 0–100.
    /// Vendor pages are never scaled into a percentage.
    /// Disagreeing values at the same usage yield unknown.
    /// </summary>
    public static int? ParseHidBatteryPercent(IReadOnlyList<HidUsageValue> values)
    {
        if (values.Count == 0)
            return null;

        var relative = AgreeInRange(values, BatterySystemPage, UsageRelativeStateOfCharge, 0, 100);
        if (relative.Status == Agreement.Conflict)
            return null;
        if (relative.Status == Agreement.Value)
            return relative.Value;

        var absolute = AgreeInRange(values, BatterySystemPage, UsageAbsoluteStateOfCharge, 0, 100);
        if (absolute.Status == Agreement.Conflict)
            return null;
        if (absolute.Status == Agreement.Value)
            return absolute.Value;

        if (!RemainingCapacityIsPercent(values))
            return null;

        var remaining = AgreeInRange(values, BatterySystemPage, UsageRemainingCapacity, 0, 100);
        return remaining.Status == Agreement.Value ? remaining.Value : null;
    }

    private static bool RemainingCapacityIsPercent(IReadOnlyList<HidUsageValue> values)
    {
        var modes = values
            .Where(v => v.UsagePage == BatterySystemPage && v.Usage == UsageCapacityMode)
            .Select(v => v.Value)
            .Distinct()
            .ToList();
        if (modes.Count == 1 && modes[0] == CapacityModePercent)
            return true;
        if (modes.Count > 1)
            return false;

        var caps = values
            .Where(v => v.UsagePage == BatterySystemPage && v.Usage == UsageRemainingCapacity)
            .ToList();
        return caps.Count > 0 && caps.All(v => v.LogicalMin == 0 && v.LogicalMax == 100);
    }

    private enum Agreement
    {
        Absent,
        Value,
        Conflict,
    }

    private readonly record struct AgreementResult(Agreement Status, int Value);

    private static AgreementResult AgreeInRange(
        IReadOnlyList<HidUsageValue> values,
        ushort page,
        ushort usage,
        int min,
        int max)
    {
        var matches = values
            .Where(v => v.UsagePage == page && v.Usage == usage)
            .Select(v => v.Value)
            .Where(v => v >= min && v <= max)
            .Distinct()
            .ToList();
        var rejected = values.Any(v =>
            v.UsagePage == page && v.Usage == usage && (v.Value < min || v.Value > max));

        if (matches.Count == 1 && !rejected)
            return new AgreementResult(Agreement.Value, matches[0]);
        if (matches.Count > 1 || (rejected && matches.Count > 0))
            return new AgreementResult(Agreement.Conflict, 0);
        if (rejected)
            return new AgreementResult(Agreement.Conflict, 0);
        return new AgreementResult(Agreement.Absent, 0);
    }
}
