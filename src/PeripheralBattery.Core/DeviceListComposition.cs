namespace PeripheralBattery.Core;

/// <summary>
/// The battery cell in the device list. The text matches the taskbar pill,
/// and <see cref="Bolt"/> is the same lightning mark, drawn after that text
/// when this device's own charging flag is set.
/// </summary>
public readonly record struct ListBatteryCell(string Text, bool Bolt);

/// <summary>
/// What the device list shows for one battery. The tray icon does not use this.
/// </summary>
public static class DeviceListComposition
{
    public static ListBatteryCell Describe(PeripheralDevice device)
    {
        var level = BatteryMath.NormalizePercent(device.BatteryPercent);
        return new ListBatteryCell(
            PillComposition.PercentText(level),
            PillComposition.ShowCharging(device.Charging));
    }
}
