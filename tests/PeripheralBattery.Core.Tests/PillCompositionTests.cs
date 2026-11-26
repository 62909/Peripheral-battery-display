using Xunit;

namespace PeripheralBattery.Core.Tests;

public class PillCompositionTests
{
    [Theory]
    [InlineData(DeviceKind.Mouse, DeviceSilhouette.Mouse)]
    [InlineData(DeviceKind.Keyboard, DeviceSilhouette.Keyboard)]
    [InlineData(DeviceKind.Headset, DeviceSilhouette.Headset)]
    [InlineData(DeviceKind.Unknown, DeviceSilhouette.Generic)]
    [InlineData(DeviceKind.Other, DeviceSilhouette.Generic)]
    public void Silhouette_follows_the_device_type_and_does_not_guess_a_mouse(DeviceKind kind, DeviceSilhouette expected)
    {
        Assert.Equal(expected, PillComposition.SilhouetteFor(kind));
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(18, "18%")]
    [InlineData(100, "100%")]
    [InlineData(null, "未知")]
    [InlineData(101, "未知")]
    [InlineData(-1, "未知")]
    public void Percent_text_is_the_number_or_unknown(int? percent, string expected)
    {
        Assert.Equal(expected, PillComposition.PercentText(percent));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(18, 0.18)]
    [InlineData(100, 1)]
    [InlineData(null, 0)]
    [InlineData(140, 0)]
    public void Fill_is_empty_when_the_level_is_not_a_percentage(int? percent, double expected)
    {
        Assert.Equal(expected, PillComposition.FillFraction(percent), 3);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void Charging_mark_requires_an_explicit_flag(bool? charging, bool shown)
    {
        Assert.Equal(shown, PillComposition.ShowCharging(charging));
    }

    [Fact]
    public void Pill_uses_the_device_with_the_lowest_known_battery()
    {
        var face = PillComposition.Select(
        [
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 72),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 18),
            new PeripheralDevice("u", null, DeviceKind.Unknown, 55),
            new PeripheralDevice("h", "耳机", DeviceKind.Headset, null),
        ]);

        Assert.Equal(DeviceSilhouette.Mouse, face.Silhouette);
        Assert.Equal(18, face.Percent);
        Assert.Equal("18%", face.PercentText);
        Assert.Equal(0.18, face.FillFraction, 3);
        Assert.False(face.Charging);
    }

    [Fact]
    public void An_unknown_type_with_the_lowest_battery_stays_generic()
    {
        var unknown = PillComposition.Select(
        [
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 80),
            new PeripheralDevice("u", null, DeviceKind.Unknown, 10),
        ]);
        var other = PillComposition.Select(
        [
            new PeripheralDevice("o", "手柄", DeviceKind.Other, 44),
        ]);

        Assert.Equal(DeviceSilhouette.Generic, unknown.Silhouette);
        Assert.Equal("10%", unknown.PercentText);
        Assert.Equal(DeviceSilhouette.Generic, other.Silhouette);
        Assert.Equal("44%", other.PercentText);
    }

    [Fact]
    public void A_tie_keeps_the_first_device_instead_of_preferring_a_mouse()
    {
        var face = PillComposition.Select(
        [
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 20),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 20),
        ]);

        Assert.Equal(DeviceSilhouette.Keyboard, face.Silhouette);
        Assert.Equal(20, face.Percent);
    }

    [Fact]
    public void Charging_follows_the_device_being_shown()
    {
        var lower = PillComposition.Select(
        [
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 10, Charging: false),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40, Charging: true),
        ]);
        var charging = PillComposition.Select(
        [
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40, Charging: true),
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 90),
        ]);

        Assert.Equal(DeviceSilhouette.Keyboard, lower.Silhouette);
        Assert.False(lower.Charging);
        Assert.Equal(DeviceSilhouette.Mouse, charging.Silhouette);
        Assert.True(charging.Charging);
        Assert.Equal("40%", charging.PercentText);
    }

    [Fact]
    public void Unknown_level_keeps_one_devices_type_and_an_empty_battery()
    {
        var face = PillComposition.Select(
        [
            new PeripheralDevice("h", "耳机", DeviceKind.Headset, null, Charging: true),
        ]);

        Assert.Equal(DeviceSilhouette.Headset, face.Silhouette);
        Assert.Null(face.Percent);
        Assert.Equal("未知", face.PercentText);
        Assert.Equal(0, face.FillFraction);
        Assert.True(face.Charging);
    }

    [Fact]
    public void Several_devices_with_no_known_level_use_the_generic_mark()
    {
        var face = PillComposition.Select(
        [
            new PeripheralDevice("h", "耳机", DeviceKind.Headset, null, Charging: true),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 140),
        ]);
        var empty = PillComposition.Select([]);

        Assert.Equal(DeviceSilhouette.Generic, face.Silhouette);
        Assert.Equal("未知", face.PercentText);
        Assert.Equal(0, face.FillFraction);
        Assert.False(face.Charging);
        Assert.Equal(DeviceSilhouette.Generic, empty.Silhouette);
        Assert.Equal("未知", empty.PercentText);
        Assert.False(empty.Charging);
    }

    [Fact]
    public void Demo_pill_is_the_mouse_at_18_percent()
    {
        var devices = DeviceAggregator.Combine(DemoCatalog.Readings(), EmptyOverrideStore.Instance);
        var face = PillComposition.Select(devices);

        Assert.Equal(18, BatteryMath.Lowest(devices));
        Assert.Equal("18", BatteryMath.TrayGlyph(18));
        Assert.Equal(DeviceSilhouette.Mouse, face.Silhouette);
        Assert.Equal(18, face.Percent);
        Assert.Equal("18%", face.PercentText);
        Assert.Equal(0.18, face.FillFraction, 3);
        Assert.False(face.Charging);
    }

    [Fact]
    public void Charging_comes_from_the_reading_that_won_the_percentage()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("vendor", SourcePriority.Vendor, "aa:bb:cc:dd:ee:10", 40, true),
            Reading("win", SourcePriority.WindowsProperty, "AA:BB:CC:DD:EE:10", 12, false),
        ], EmptyOverrideStore.Instance);

        var device = Assert.Single(devices);
        Assert.Equal(40, device.BatteryPercent);
        Assert.Equal(DeviceKind.Unknown, device.Kind);
        Assert.True(device.Charging);
        var face = PillComposition.Select(devices);
        Assert.True(face.Charging);
        Assert.Equal(0.4, face.FillFraction, 3);
        Assert.Equal("40%", face.PercentText);
    }

    [Fact]
    public void Missing_or_disagreeing_charging_flags_are_not_shown()
    {
        var missing = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:11", 60, null),
        ], EmptyOverrideStore.Instance);
        Assert.Null(Assert.Single(missing).Charging);
        Assert.False(PillComposition.Select(missing).Charging);

        var omittedBesideTrue = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:13", 50, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:13", 50, null),
        ], EmptyOverrideStore.Instance);
        Assert.True(Assert.Single(omittedBesideTrue).Charging);

        var mixed = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:12", 50, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:12", 50, false),
        ], EmptyOverrideStore.Instance);
        var device = Assert.Single(mixed);
        Assert.Equal(50, device.BatteryPercent);
        Assert.Null(device.Charging);
        Assert.False(PillComposition.Select(mixed).Charging);
    }

    [Fact]
    public void A_percentage_conflict_drops_the_charging_flag()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:14", 10, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:14", 90, true),
        ], EmptyOverrideStore.Instance);

        var device = Assert.Single(devices);
        Assert.Null(device.BatteryPercent);
        Assert.Null(device.Charging);
        var face = PillComposition.Select(devices);
        Assert.Equal("未知", face.PercentText);
        Assert.Equal(0, face.FillFraction);
        Assert.False(face.Charging);
    }

    private static RawDeviceReading Reading(string source, int priority, string address, int? battery, bool? charging) =>
        new()
        {
            SourceId = source,
            Priority = priority,
            BluetoothAddress = address,
            BatteryPercent = battery,
            Charging = charging,
        };
}
