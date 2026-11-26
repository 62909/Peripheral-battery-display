using Xunit;

namespace PeripheralBattery.Core.Tests;

public class AkkoHuaxiMouseTests
{
    [Fact]
    public void Only_38ee_0022_uses_the_ffa8_collection()
    {
        Assert.True(AkkoHuaxiMouse.IsCandidate(0x38EE, 0x0022));
        Assert.False(AkkoHuaxiMouse.IsCandidate(0x38EE, 0x0023));
        Assert.False(AkkoHuaxiMouse.IsCandidate(0x38EE, 0x0024));
        Assert.False(AkkoHuaxiMouse.IsCandidate(0x320F, 0x5088));
        Assert.True(AkkoHuaxiMouse.IsInfoCollection(0xFFA8, 0x0002));
        Assert.False(AkkoHuaxiMouse.IsInfoCollection(0x0001, 0x0002));
        Assert.False(AkkoHuaxiMouse.IsInfoCollection(0xFF1C, 0x0092));
    }

    [Fact]
    public void Info_request_is_devgetinfo_129_and_a_zero()
    {
        Assert.Equal(new byte[] { 129, 0 }, AkkoHuaxiMouse.InfoRequest());
        Assert.Equal(11, AkkoHuaxiMouse.ReportId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void Level_at_offset_14_after_the_three_byte_header_is_the_percentage(int level)
    {
        var payload = Packet(level, charging: 80, full: 2);
        Assert.True(AkkoHuaxiMouse.TryParseInfoPayload(payload, out var info));
        Assert.Equal(level, info.BatteryPercent);
        Assert.False(info.Charging);
        Assert.False(info.Full);

        var windows = new byte[1 + payload.Length];
        windows[0] = AkkoHuaxiMouse.ReportId;
        payload.CopyTo(windows, 1);
        Assert.True(AkkoHuaxiMouse.TryParseInfoReport(windows, out var fromReport));
        Assert.Equal(level, fromReport.BatteryPercent);
    }

    [Fact]
    public void Charging_and_full_are_not_the_percentage()
    {
        Assert.True(AkkoHuaxiMouse.TryParseInfoPayload(Packet(40, charging: 1, full: 1), out var info));
        Assert.Equal(40, info.BatteryPercent);
        Assert.True(info.Charging);
        Assert.True(info.Full);
    }

    [Fact]
    public void A_level_above_100_is_not_clipped_into_a_percentage()
    {
        Assert.True(AkkoHuaxiMouse.TryParseInfoPayload(Packet(101, charging: 0, full: 0), out var info));
        Assert.Null(info.BatteryPercent);
        Assert.False(info.Charging);
    }

    [Fact]
    public void A_mouse_collection_or_a_failed_status_is_not_an_info_report()
    {
        var mouse = new byte[32];
        mouse[0] = 0x01;
        mouse[1] = 0x02;
        Assert.False(AkkoHuaxiMouse.TryParseInfoReport(mouse, out _));

        var badStatus = Packet(50, charging: 0, full: 0);
        badStatus[2] = 1;
        Assert.False(AkkoHuaxiMouse.TryParseInfoPayload(badStatus, out _));

        var otherCommand = Packet(50, charging: 0, full: 0);
        otherCommand[0] = 0x01;
        Assert.False(AkkoHuaxiMouse.TryParseInfoPayload(otherCommand, out _));

        var shortPacket = new byte[] { 129, 0, 0, 40 };
        Assert.False(AkkoHuaxiMouse.TryParseInfoPayload(shortPacket, out _));
    }

    [Fact]
    public void Mi_00_is_the_mouse_collection_and_is_not_opened_by_path()
    {
        Assert.True(AkkoHuaxiMouse.IsMouseInterfacePath(@"\\?\hid#vid_38ee&pid_0022&mi_00&col01#{guid}"));
        Assert.True(AkkoHuaxiMouse.IsMouseInterfacePath(@"\\?\HID#VID_38EE&PID_0022&MI_00#{guid}"));
        Assert.False(AkkoHuaxiMouse.IsMouseInterfacePath(@"\\?\hid#vid_38ee&pid_0022&mi_02&col03#{guid}"));
        Assert.False(AkkoHuaxiMouse.IsMouseInterfacePath(null));
    }

    [Fact]
    public void Device_type_and_mouse_connection_follow_the_official_bytes()
    {
        Assert.True(AkkoHuaxiMouse.TryParseDeviceType(new byte[] { 196, 0, 0, 1 }, out var dongle));
        Assert.Equal(HuaxiDeviceType.Dongle, dongle);
        Assert.True(AkkoHuaxiMouse.TryParseDeviceType(new byte[] { 7, 196, 0, 0, 2 }, out var mouse));
        Assert.Equal(HuaxiDeviceType.Mouse, mouse);

        Assert.True(AkkoHuaxiMouse.TryParseMouseConnected(new byte[] { 208, 0, 0, 0 }, out var off));
        Assert.False(off);
        Assert.True(AkkoHuaxiMouse.TryParseMouseConnected(new byte[] { 4, 208, 0, 0, 1 }, out var on));
        Assert.True(on);
    }

    [Fact]
    public void Pid_0022_is_not_named_tan8_ultra_by_the_aggregator()
    {
        var devices = DeviceAggregator.Combine(
        [
            new RawDeviceReading
            {
                SourceId = "akko-huaxi",
                Priority = SourcePriority.Vendor,
                InstanceId = "huaxi:38EE:0022",
                VendorId = 0x38EE,
                ProductId = 0x0022,
                KindHint = DeviceKind.Mouse,
                BatteryPercent = 40,
            },
        ], EmptyOverrideStore.Instance);

        var device = Assert.Single(devices);
        Assert.Null(device.DisplayName);
        Assert.Equal(40, device.BatteryPercent);
        Assert.DoesNotContain("空影", device.DisplayName ?? "");
    }

    [Fact]
    public void A_sighting_that_includes_ffa8_does_not_say_the_battery_was_skipped()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0022, "AKKO", 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0022, "AKKO", 0xFFA8, 0x0002),
        ]);

        Assert.Contains("38EE:0022", text);
        Assert.Contains("FFA8:0002", text);
        Assert.DoesNotContain("%", text);
        Assert.DoesNotContain("空影", text);
    }

    private static byte[] Packet(int level, int charging, int full)
    {
        var payload = new byte[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset + 1];
        payload[0] = AkkoHuaxiMouse.InfoCommand;
        payload[2] = 0;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.ChargingOffset] = (byte)charging;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset] = (byte)level;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset] = (byte)full;
        return payload;
    }
}
