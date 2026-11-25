namespace PeripheralBattery.Core;

/// <summary>
/// Fixed sample used by --demo and the tray menu. Numbers here are fixtures,
/// not live readings, and they still go through the same merge rules.
/// </summary>
public static class DemoCatalog
{
    public static IReadOnlyList<RawDeviceReading> Readings() =>
    [
        new RawDeviceReading
        {
            SourceId = "demo",
            Priority = SourcePriority.Demo,
            BluetoothAddress = "AA:BB:CC:DD:EE:01",
            WindowsName = "罗技 MX Keys",
            VendorId = 0x046D,
            KindHint = DeviceKind.Keyboard,
            BatteryPercent = 72,
        },
        new RawDeviceReading
        {
            SourceId = "demo",
            Priority = SourcePriority.Demo,
            BluetoothAddress = "AA:BB:CC:DD:EE:02",
            WindowsName = "罗技 MX Master 3",
            VendorId = 0x046D,
            ProductId = 0xB023,
            KindHint = DeviceKind.Mouse,
            BatteryPercent = 18,
        },
        new RawDeviceReading
        {
            SourceId = "demo",
            Priority = SourcePriority.Demo,
            BluetoothAddress = "AA:BB:CC:DD:EE:03",
            WindowsName = "WH-1000XM5",
            VendorId = 0x054C,
            KindHint = DeviceKind.Headset,
            BatteryPercent = null,
        },
        new RawDeviceReading
        {
            SourceId = "demo",
            Priority = SourcePriority.Demo,
            BluetoothAddress = "AA:BB:CC:DD:EE:04",
            WindowsName = null,
            KindHint = DeviceKind.Unknown,
            BatteryPercent = 55,
        },
        new RawDeviceReading
        {
            SourceId = "demo",
            Priority = SourcePriority.Demo,
            BluetoothAddress = "AA:BB:CC:DD:EE:05",
            WindowsName = "HID-compliant mouse",
            KindHint = DeviceKind.Mouse,
            BatteryPercent = null,
        },
    ];
}
