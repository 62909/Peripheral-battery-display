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
        Assert.NotEqual(PillMarks.Outline(DeviceSilhouette.Mouse), PillMarks.Outline(DeviceSilhouette.Generic));
    }

    [Fact]
    public void Silhouette_outlines_keep_each_device_mark()
    {
        Assert.Single(PillMarks.Outline(DeviceSilhouette.Mouse));
        Assert.Single(PillMarks.Outline(DeviceSilhouette.Keyboard));
        Assert.Equal(3, PillMarks.Outline(DeviceSilhouette.Headset).Count);
        Assert.Single(PillMarks.Outline(DeviceSilhouette.Generic));
        Assert.Contains("M ", PillMarks.Bolt, StringComparison.Ordinal);
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

    [Fact]
    public void Text_width_reserves_digits_percent_and_unknown()
    {
        Assert.Equal(
            PillComposition.TextSlackDip + 3 * PillComposition.DigitDip + PillComposition.PercentMarkDip,
            PillComposition.TextWidthDip("100%"));
        Assert.Equal(
            PillComposition.TextSlackDip + 2 * PillComposition.DigitDip + PillComposition.PercentMarkDip,
            PillComposition.TextWidthDip("18%"));
        Assert.Equal(
            PillComposition.TextSlackDip + 2 * PillComposition.CjkDip,
            PillComposition.TextWidthDip("未知"));
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
    public void One_device_is_icon_and_percent_with_no_trailing_gap()
    {
        var strip = PillComposition.Arrange(
        [
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 18),
        ]);

        var box = Assert.Single(strip.Boxes);
        Assert.Equal(DeviceSilhouette.Mouse, box.Slot.Silhouette);
        Assert.Equal(18, box.Slot.Percent);
        Assert.Equal("18%", box.Slot.PercentText);
        Assert.False(box.Slot.Charging);
        Assert.Null(box.BoltX);
        Assert.Equal(BatteryTone.Critical, box.Slot.Tone);
        Assert.Equal(
            PillComposition.IconDip + PillComposition.IconTextGapDip + box.TextWidth,
            box.Width);
        Assert.Equal(strip.BorderDip + PillComposition.PadDip, box.X);
        Assert.Equal(box.X + box.Width + PillComposition.PadDip + strip.BorderDip, strip.WidthDip);
        Assert.Equal(PillComposition.HeightDip, strip.HeightDip);
        Assert.True(strip.GapDip > PillComposition.IconTextGapDip * 2);
    }

    [Fact]
    public void Several_devices_keep_list_order_and_a_visible_gap()
    {
        PeripheralDevice[] devices =
        [
            new("k", "键盘", DeviceKind.Keyboard, 90),
            new("m", "鼠标", DeviceKind.Mouse, 10, Charging: true),
            new("h", "耳机", DeviceKind.Headset, null),
            new("o", "其他", DeviceKind.Other, 25),
        ];

        var strip = PillComposition.Arrange(devices);

        Assert.Equal(devices.Length, strip.Slots.Count);
        Assert.Equal(
            devices.Select(d => PillComposition.SilhouetteFor(d.Kind)),
            strip.Slots.Select(s => s.Silhouette));
        Assert.Equal(["90%", "10%", "未知", "25%"], strip.Slots.Select(s => s.PercentText));
        Assert.Equal([90, 10, null, 25], strip.Slots.Select(s => s.Percent));
        Assert.Equal(DeviceSilhouette.Generic, strip.Slots[3].Silhouette);
        Assert.Equal(
            [BatteryTone.Ok, BatteryTone.Critical, BatteryTone.Unknown, BatteryTone.Low],
            strip.Slots.Select(s => s.Tone));

        Assert.True(strip.GapDip >= 12);
        Assert.Equal(strip.Boxes[0].X + strip.Boxes[0].Width + strip.GapDip, strip.Boxes[1].X);
        Assert.Equal(strip.Boxes[1].X + strip.Boxes[1].Width + strip.GapDip, strip.Boxes[2].X);
        Assert.Equal(strip.Boxes[2].X + strip.Boxes[2].Width + strip.GapDip, strip.Boxes[3].X);
        var last = strip.Boxes[^1];
        Assert.Equal(last.X + last.Width + PillComposition.PadDip + strip.BorderDip, strip.WidthDip);

        var one = PillComposition.Arrange([devices[0]]);
        var two = PillComposition.Arrange([devices[0], devices[1]]);
        Assert.Equal(one.WidthDip + strip.GapDip + strip.Boxes[1].Width, two.WidthDip);
        Assert.True(two.WidthDip > one.WidthDip);
        Assert.True(strip.WidthDip > two.WidthDip);
    }

    [Fact]
    public void Charging_bolt_is_only_on_the_device_that_is_charging()
    {
        var idle = PillComposition.Arrange(
        [
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40),
        ]);
        var charging = PillComposition.Arrange(
        [
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40, Charging: true),
        ]);
        var mixed = PillComposition.Arrange(
        [
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 10, Charging: false),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40, Charging: true),
            new PeripheralDevice("h", "耳机", DeviceKind.Headset, 80),
        ]);

        Assert.Null(idle.Boxes[0].BoltX);
        Assert.False(idle.Slots[0].Charging);
        Assert.Equal(idle.Boxes[0].TextX + idle.Boxes[0].TextWidth + PillComposition.TextBoltGapDip, charging.Boxes[0].BoltX);
        Assert.Equal(
            idle.WidthDip + PillComposition.TextBoltGapDip + PillComposition.BoltWidthDip,
            charging.WidthDip);
        Assert.Equal("40%", charging.Slots[0].PercentText);

        Assert.Null(mixed.Boxes[0].BoltX);
        Assert.NotNull(mixed.Boxes[1].BoltX);
        Assert.Null(mixed.Boxes[2].BoltX);
        Assert.False(mixed.Slots[0].Charging);
        Assert.True(mixed.Slots[1].Charging);
        Assert.False(mixed.Slots[2].Charging);
        Assert.Equal(mixed.Boxes[0].X + mixed.Boxes[0].Width + mixed.GapDip, mixed.Boxes[1].X);
    }

    [Fact]
    public void Unknown_level_keeps_the_device_type_and_does_not_invent_a_percentage()
    {
        var face = PillComposition.Arrange(
        [
            new PeripheralDevice("h", "耳机", DeviceKind.Headset, null, Charging: true),
            new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 140),
            new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, -3),
        ]);

        Assert.Equal(DeviceSilhouette.Headset, face.Slots[0].Silhouette);
        Assert.Null(face.Slots[0].Percent);
        Assert.Equal("未知", face.Slots[0].PercentText);
        Assert.True(face.Slots[0].Charging);
        Assert.NotNull(face.Boxes[0].BoltX);
        Assert.Equal(DeviceSilhouette.Mouse, face.Slots[1].Silhouette);
        Assert.Null(face.Slots[1].Percent);
        Assert.Equal("未知", face.Slots[1].PercentText);
        Assert.False(face.Slots[1].Charging);
        Assert.Null(face.Slots[2].Percent);
        Assert.Equal("未知", face.Slots[2].PercentText);
        Assert.All(face.Slots, slot => Assert.NotEqual("100%", slot.PercentText));
    }

    [Fact]
    public void No_devices_is_one_generic_unknown_without_a_percentage()
    {
        var empty = PillComposition.Arrange([]);

        var slot = Assert.Single(empty.Slots);
        Assert.Equal(DeviceSilhouette.Generic, slot.Silhouette);
        Assert.Null(slot.Percent);
        Assert.Equal("未知", slot.PercentText);
        Assert.False(slot.Charging);
        Assert.Null(empty.Boxes[0].BoltX);
    }

    [Fact]
    public void Demo_pill_lists_every_device_in_list_order_and_the_tray_stays_lowest()
    {
        var devices = DeviceAggregator.Combine(DemoCatalog.Readings(), EmptyOverrideStore.Instance);
        var strip = PillComposition.Arrange(devices);

        Assert.Equal(18, BatteryMath.Lowest(devices));
        Assert.Equal("18", BatteryMath.TrayGlyph(18));
        Assert.Equal(devices.Count, strip.Slots.Count);
        Assert.Equal(
            devices.Select(d => PillComposition.SilhouetteFor(d.Kind)),
            strip.Slots.Select(s => s.Silhouette));
        Assert.Equal(
            devices.Select(d => PillComposition.PercentText(d.BatteryPercent)),
            strip.Slots.Select(s => s.PercentText));
        Assert.Equal(
            devices.Select(d => BatteryMath.NormalizePercent(d.BatteryPercent)),
            strip.Slots.Select(s => s.Percent));
        Assert.Equal(DeviceSilhouette.Mouse, strip.Slots[0].Silhouette);
        Assert.Equal(18, strip.Slots[0].Percent);
        Assert.Contains(strip.Slots, slot => slot.PercentText == "未知" && slot.Percent is null);
        Assert.Contains(strip.Slots, slot => slot.Silhouette == DeviceSilhouette.Generic && slot.Percent == 55);
        Assert.All(strip.Slots, slot => Assert.False(slot.Charging));
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
        var strip = PillComposition.Arrange(devices);
        var slot = Assert.Single(strip.Slots);
        Assert.True(slot.Charging);
        Assert.Equal(DeviceSilhouette.Generic, slot.Silhouette);
        Assert.Equal("40%", slot.PercentText);
        Assert.Equal(40, slot.Percent);
        Assert.NotNull(strip.Boxes[0].BoltX);
    }

    [Fact]
    public void Missing_or_disagreeing_charging_flags_are_not_shown()
    {
        var missing = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:11", 60, null),
        ], EmptyOverrideStore.Instance);
        Assert.Null(Assert.Single(missing).Charging);
        Assert.False(Assert.Single(PillComposition.Arrange(missing).Slots).Charging);

        var omittedBesideTrue = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:13", 50, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:13", 50, null),
        ], EmptyOverrideStore.Instance);
        Assert.True(Assert.Single(omittedBesideTrue).Charging);
        Assert.True(Assert.Single(PillComposition.Arrange(omittedBesideTrue).Slots).Charging);

        var mixed = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:12", 50, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:12", 50, false),
        ], EmptyOverrideStore.Instance);
        var device = Assert.Single(mixed);
        Assert.Equal(50, device.BatteryPercent);
        Assert.Null(device.Charging);
        Assert.False(Assert.Single(PillComposition.Arrange(mixed).Slots).Charging);
        Assert.Null(Assert.Single(PillComposition.Arrange(mixed).Boxes).BoltX);
    }

    [Fact]
    public void A_percentage_conflict_drops_the_charging_flag_and_does_not_invent_a_level()
    {
        var devices = DeviceAggregator.Combine(
        [
            Reading("gatt", SourcePriority.Gatt, "aa:bb:cc:dd:ee:14", 10, true),
            Reading("gatt", SourcePriority.Gatt, "AA:BB:CC:DD:EE:14", 90, true),
        ], EmptyOverrideStore.Instance);

        var device = Assert.Single(devices);
        Assert.Null(device.BatteryPercent);
        Assert.Null(device.Charging);
        var slot = Assert.Single(PillComposition.Arrange(devices).Slots);
        Assert.Equal("未知", slot.PercentText);
        Assert.Null(slot.Percent);
        Assert.False(slot.Charging);
        Assert.Equal(DeviceSilhouette.Generic, slot.Silhouette);
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
