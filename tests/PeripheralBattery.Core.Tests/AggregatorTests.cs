using Xunit;

namespace PeripheralBattery.Core.Tests;

public class AggregatorTests
{
    [Fact]
    public void Lowest_ignores_unknown_and_empty()
    {
        Assert.Null(BatteryMath.Lowest([]));
        var devices = new[]
        {
            new PeripheralDevice("a", "键盘", DeviceKind.Keyboard, null),
            new PeripheralDevice("b", "鼠标", DeviceKind.Mouse, 18),
            new PeripheralDevice("c", null, DeviceKind.Unknown, 55),
            new PeripheralDevice("d", "坏值", DeviceKind.Other, 140),
        };
        Assert.Equal(18, BatteryMath.Lowest(devices));
        Assert.Equal("18", BatteryMath.TrayGlyph(18));
        Assert.Equal("?", BatteryMath.TrayGlyph(null));
        Assert.Equal(BatteryTone.Critical, BatteryMath.Tone(18));
        Assert.Equal(BatteryTone.Unknown, BatteryMath.Tone(null));
    }

    [Fact]
    public void Gatt_wins_over_a_different_windows_property_reading()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:01", 80, "键盘 A"),
            Reading("win", SourcePriority.WindowsProperty, "AA-BB-CC-DD-EE-01", 10, "键盘 A Hands-Free AG"),
        ], EmptyOverrideStore.Instance);

        var device = Assert.Single(devices);
        Assert.Equal(80, device.BatteryPercent);
        Assert.Equal("键盘 A", device.DisplayName);
        Assert.Equal("bt:AA:BB:CC:DD:EE:01", device.Key);
    }

    [Fact]
    public void Missing_higher_priority_battery_falls_back()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "11:22:33:44:55:66", null, "耳机"),
            Reading("win", SourcePriority.WindowsProperty, "112233445566", 41, "耳机 Hands-Free"),
        ], EmptyOverrideStore.Instance);

        Assert.Equal(41, Assert.Single(devices).BatteryPercent);
    }

    [Fact]
    public void Same_priority_conflict_is_unknown()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "11:22:33:44:55:01", 10, "A"),
            Reading("gatt", SourcePriority.Gatt, "11:22:33:44:55:01", 90, "A"),
        ], EmptyOverrideStore.Instance);

        Assert.Null(Assert.Single(devices).BatteryPercent);
    }

    [Fact]
    public void Same_model_name_on_two_addresses_stays_two_devices()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "10:00:00:00:00:01", 10, "罗技 MX Master 3"),
            Reading("gatt", SourcePriority.Gatt, "10:00:00:00:00:02", 90, "罗技 MX Master 3"),
        ], EmptyOverrideStore.Instance);

        Assert.Equal(2, devices.Count);
        Assert.Equal(10, BatteryMath.Lowest(devices));
    }

    [Fact]
    public void Unknown_name_uses_vendor_id_and_override_replaces_it()
    {
        var raw = new RawDeviceReading
        {
            SourceId = "hid",
            Priority = SourcePriority.Hid,
            InstanceId = @"HID\VID_046D&PID_9999\1",
            WindowsName = "HID-compliant mouse",
            VendorId = 0x046D,
            ProductId = 0x9999,
            KindHint = DeviceKind.Mouse,
            BatteryPercent = null,
        };
        var plain = DeviceAggregator.Combine([raw], EmptyOverrideStore.Instance);
        var device = Assert.Single(plain);
        Assert.Equal("罗技", device.DisplayName);
        Assert.Equal(DeviceKind.Mouse, device.Kind);
        Assert.Null(device.BatteryPercent);

        var store = new DictionaryOverrideStore(device.Key, new IdentityOverride("左手鼠标", DeviceKind.Mouse));
        var replaced = Assert.Single(DeviceAggregator.Combine([raw], store));
        Assert.Equal("左手鼠标", replaced.DisplayName);
    }

    [Fact]
    public void Demo_pipeline_keeps_unknowns_and_lowest_is_18()
    {
        var devices = DeviceAggregator.Combine(DemoCatalog.Readings(), EmptyOverrideStore.Instance);
        Assert.Equal(18, BatteryMath.Lowest(devices));
        Assert.Contains(devices, d => d.DisplayName is null && d.BatteryPercent == 55 && d.Kind == DeviceKind.Unknown);
        Assert.Contains(devices, d => d.Kind == DeviceKind.Headset && d.BatteryPercent is null && d.DisplayName == "WH-1000XM5");
        Assert.Contains(devices, d => d.Kind == DeviceKind.Mouse && d.DisplayName is null && d.BatteryPercent is null);
        Assert.Equal(5, devices.Count);
    }

    private static RawDeviceReading Reading(string source, int priority, string address, int? battery, string name) =>
        new()
        {
            SourceId = source,
            Priority = priority,
            BluetoothAddress = address,
            WindowsName = name,
            BatteryPercent = battery,
        };

    private sealed class DictionaryOverrideStore(string key, IdentityOverride value) : IIdentityOverrideStore
    {
        public IdentityOverride? Find(string stableKey) =>
            string.Equals(stableKey, key, StringComparison.OrdinalIgnoreCase) ? value : null;
    }
}
