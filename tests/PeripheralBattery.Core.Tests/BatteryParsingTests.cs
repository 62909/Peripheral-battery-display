using Xunit;

namespace PeripheralBattery.Core.Tests;

public class BatteryParsingTests
{
    [Theory]
    [InlineData(new byte[] { 0 }, 0)]
    [InlineData(new byte[] { 100 }, 100)]
    [InlineData(new byte[] { 42, 9 }, 42)]
    public void Gatt_accepts_a_single_byte_percentage(byte[] payload, int expected)
    {
        Assert.Equal(expected, BatteryParsing.ParseGattBatteryLevel(payload));
    }

    [Theory]
    [InlineData(new byte[] { 101 })]
    [InlineData(new byte[0])]
    public void Gatt_rejects_missing_or_out_of_range(byte[] payload)
    {
        Assert.Null(BatteryParsing.ParseGattBatteryLevel(payload));
    }

    [Fact]
    public void Windows_nodes_keep_a_real_zero_and_drop_a_placeholder_zero()
    {
        Assert.Equal(70, BatteryParsing.CollapseWindowsPercents(new int?[] { 0, 70 }));
        Assert.Equal(0, BatteryParsing.CollapseWindowsPercents(new int?[] { 0, null }));
        Assert.Null(BatteryParsing.CollapseWindowsPercents(new int?[] { 10, 20 }));
        Assert.Null(BatteryParsing.CollapseWindowsPercents(new int?[] { 0, 10, 20 }));
        Assert.Null(BatteryParsing.CollapseWindowsPercents(Array.Empty<int?>()));
    }

    [Fact]
    public void Windows_property_accepts_only_a_byte_in_range()
    {
        Assert.Equal(80, BatteryParsing.ParseWindowsBatteryProperty(0x3, new byte[] { 80 }));
        Assert.Equal(0, BatteryParsing.ParseWindowsBatteryProperty(0x3, new byte[] { 0 }));
        Assert.Null(BatteryParsing.ParseWindowsBatteryProperty(0x3, new byte[] { 101 }));
        Assert.Null(BatteryParsing.ParseWindowsBatteryProperty(0x7, new byte[] { 80 }));
        Assert.Null(BatteryParsing.ParseWindowsBatteryProperty(0x3, new byte[] { 80, 0 }));
        Assert.Null(BatteryParsing.ParseWindowsBatteryProperty(0x3, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Hid_relative_state_of_charge_is_a_percent()
    {
        var values = new[]
        {
            Sample(BatteryParsing.UsageRelativeStateOfCharge, 40, 0, 255),
        };
        Assert.Equal(40, BatteryParsing.ParseHidBatteryPercent(values));
    }

    [Fact]
    public void Hid_rejects_vendor_pages_and_non_percent_capacity()
    {
        var vendor = new[] { new HidUsageValue(0xFF00, 0x64, 40, 0, 100) };
        Assert.Null(BatteryParsing.ParseHidBatteryPercent(vendor));

        var milliamp = new[]
        {
            Sample(BatteryParsing.UsageRemainingCapacity, 40, 0, 5000),
            Sample(BatteryParsing.UsageCapacityMode, 0, 0, 2),
        };
        Assert.Null(BatteryParsing.ParseHidBatteryPercent(milliamp));
    }

    [Fact]
    public void Hid_remaining_capacity_accepts_percent_mode_or_a_0_to_100_range()
    {
        var byMode = new[]
        {
            Sample(BatteryParsing.UsageRemainingCapacity, 63, 0, 65535),
            Sample(BatteryParsing.UsageCapacityMode, BatteryParsing.CapacityModePercent, 0, 2),
        };
        Assert.Equal(63, BatteryParsing.ParseHidBatteryPercent(byMode));

        var byRange = new[]
        {
            Sample(BatteryParsing.UsageRemainingCapacity, 15, 0, 100),
        };
        Assert.Equal(15, BatteryParsing.ParseHidBatteryPercent(byRange));
    }

    [Fact]
    public void Hid_does_not_clip_absolute_charge_above_100()
    {
        var high = new[] { Sample(BatteryParsing.UsageAbsoluteStateOfCharge, 130, 0, 255) };
        Assert.Null(BatteryParsing.ParseHidBatteryPercent(high));

        var ok = new[] { Sample(BatteryParsing.UsageAbsoluteStateOfCharge, 88, 0, 255) };
        Assert.Equal(88, BatteryParsing.ParseHidBatteryPercent(ok));
    }

    [Fact]
    public void Hid_conflict_on_the_same_usage_is_unknown()
    {
        var values = new[]
        {
            Sample(BatteryParsing.UsageRelativeStateOfCharge, 10, 0, 100),
            Sample(BatteryParsing.UsageRelativeStateOfCharge, 70, 0, 100),
            Sample(BatteryParsing.UsageAbsoluteStateOfCharge, 70, 0, 100),
        };
        Assert.Null(BatteryParsing.ParseHidBatteryPercent(values));
    }

    [Fact]
    public void Hid_prefers_relative_charge_over_other_usages()
    {
        var values = new[]
        {
            Sample(BatteryParsing.UsageRemainingCapacity, 10, 0, 100),
            Sample(BatteryParsing.UsageAbsoluteStateOfCharge, 20, 0, 100),
            Sample(BatteryParsing.UsageRelativeStateOfCharge, 33, 0, 100),
        };
        Assert.Equal(33, BatteryParsing.ParseHidBatteryPercent(values));
    }

    [Fact]
    public void Full_charge_capacity_is_not_treated_as_a_percentage()
    {
        var values = new[] { Sample(0x67, 80, 0, 100) };
        Assert.Null(BatteryParsing.ParseHidBatteryPercent(values));
    }

    private static HidUsageValue Sample(ushort usage, int value, int min, int max) =>
        new(BatteryParsing.BatterySystemPage, usage, value, min, max);
}
