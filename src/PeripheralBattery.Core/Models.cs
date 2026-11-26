namespace PeripheralBattery.Core;

public enum DeviceKind
{
    Unknown = 0,
    Keyboard = 1,
    Mouse = 2,
    Headset = 3,
    Other = 4,
}

public enum BatteryTone
{
    Unknown = 0,
    Ok = 1,
    Low = 2,
    Critical = 3,
}

/// <summary>
/// One observation from a single reader. Several observations of the same
/// physical device are merged later. A missing battery stays null.
/// </summary>
public sealed record RawDeviceReading
{
    public required string SourceId { get; init; }
    public required int Priority { get; init; }
    public string? BluetoothAddress { get; init; }
    public string? InstanceId { get; init; }
    public string? WindowsName { get; init; }
    public int? VendorId { get; init; }
    public int? ProductId { get; init; }
    public DeviceKind KindHint { get; init; } = DeviceKind.Unknown;
    public int? BatteryPercent { get; init; }

    /// <summary>
    /// Optional list note. Never a battery percentage.
    /// </summary>
    public string? Note { get; init; }
}

public sealed record PeripheralDevice(
    string Key,
    string? DisplayName,
    DeviceKind Kind,
    int? BatteryPercent,
    string? Note = null);

/// <summary>
/// Manual name or type correction. Battery is intentionally absent:
/// an override must not invent a percentage.
/// </summary>
public sealed record IdentityOverride(string? DisplayName, DeviceKind? Kind);

public static class SourcePriority
{
    public const int Vendor = 100;
    public const int Gatt = 80;
    public const int Hid = 60;
    public const int WindowsProperty = 40;
    public const int Demo = 10;
}

public interface IPeripheralSource
{
    string SourceId { get; }
    int Priority { get; }
    Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Marker for a vendor protocol reader such as the Huaxi 38EE:0022 source.
/// Register an implementation from the app.
/// A reader must return null battery when its protocol does not provide one.
/// </summary>
public interface IVendorBatterySource : IPeripheralSource;

public interface IIdentityOverrideStore
{
    IdentityOverride? Find(string stableKey);
}

public static class BatteryMath
{
    public static int? NormalizePercent(int? value) =>
        value is >= 0 and <= 100 ? value : null;

    public static int? Lowest(IEnumerable<PeripheralDevice> devices)
    {
        int? best = null;
        foreach (var device in devices)
        {
            if (NormalizePercent(device.BatteryPercent) is not int value)
                continue;
            best = best is null ? value : Math.Min(best.Value, value);
        }

        return best;
    }

    public static BatteryTone Tone(int? percent) => percent switch
    {
        null => BatteryTone.Unknown,
        < 20 => BatteryTone.Critical,
        < 40 => BatteryTone.Low,
        _ => BatteryTone.Ok,
    };

    /// <summary>
    /// Tray icons are 16–32 px. A two-character Chinese word does not fit,
    /// so an unknown level uses "?" there. Lists and the taskbar pill still say 未知.
    /// </summary>
    public static string TrayGlyph(int? percent) =>
        NormalizePercent(percent) is int value ? value.ToString() : "?";
}
