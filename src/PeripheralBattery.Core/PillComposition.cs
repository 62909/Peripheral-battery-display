namespace PeripheralBattery.Core;

/// <summary>
/// Which silhouette the taskbar pill draws for a device type.
/// Unknown and other types share a generic mark so the pill does not guess a mouse.
/// </summary>
public enum DeviceSilhouette
{
    Generic = 0,
    Keyboard = 1,
    Mouse = 2,
    Headset = 3,
}

/// <summary>
/// What the taskbar pill shows for the current devices.
/// Left to right the view draws <see cref="Silhouette"/>, a battery filled to
/// <see cref="FillFraction"/>, then <see cref="PercentText"/>.
/// </summary>
public readonly record struct PillFace(
    DeviceSilhouette Silhouette,
    int? Percent,
    string PercentText,
    double FillFraction,
    bool Charging);

/// <summary>
/// Chooses the taskbar pill from the devices already on screen.
/// The tray icon does not use this. It still prints only the lowest percentage.
/// </summary>
public static class PillComposition
{
    public const string UnknownText = "未知";

    public static DeviceSilhouette SilhouetteFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => DeviceSilhouette.Keyboard,
        DeviceKind.Mouse => DeviceSilhouette.Mouse,
        DeviceKind.Headset => DeviceSilhouette.Headset,
        _ => DeviceSilhouette.Generic,
    };

    public static string PercentText(int? percent) =>
        BatteryMath.NormalizePercent(percent) is int value ? $"{value}%" : UnknownText;

    /// <summary>
    /// 0 when the level is missing or outside 0–100. Never a guessed fill.
    /// </summary>
    public static double FillFraction(int? percent) =>
        BatteryMath.NormalizePercent(percent) is int value ? value / 100.0 : 0;

    public static bool ShowCharging(bool? charging) => charging == true;

    /// <summary>
    /// The device with the lowest known percentage, first one when several tie.
    /// If none have a known percentage, a single device still lends its type and
    /// charging flag. Several devices with no known level use the generic mark.
    /// </summary>
    public static PillFace Select(IEnumerable<PeripheralDevice> devices)
    {
        PeripheralDevice? lowest = null;
        PeripheralDevice? single = null;
        var count = 0;
        foreach (var device in devices)
        {
            count++;
            if (count == 1)
                single = device;
            if (BatteryMath.NormalizePercent(device.BatteryPercent) is int value
                && (lowest is null || value < lowest.BatteryPercent!.Value))
                lowest = device;
        }

        if (lowest is not null)
            return Describe(lowest.Kind, lowest.BatteryPercent, lowest.Charging);
        if (count == 1 && single is not null)
            return Describe(single.Kind, null, single.Charging);
        return Describe(DeviceKind.Unknown, null, null);
    }

    private static PillFace Describe(DeviceKind kind, int? percent, bool? charging)
    {
        var level = BatteryMath.NormalizePercent(percent);
        return new PillFace(
            SilhouetteFor(kind),
            level,
            PercentText(level),
            FillFraction(level),
            ShowCharging(charging));
    }
}
