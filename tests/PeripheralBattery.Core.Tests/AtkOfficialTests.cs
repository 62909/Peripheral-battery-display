using Xunit;

namespace PeripheralBattery.Core.Tests;

public class AtkOfficialTests
{
    [Fact]
    public void Bitmouse_compx_and_hechi_use_different_collections()
    {
        Assert.True(AtkBitmouse.IsCollection(0x373B, 0x1216, 0xFF05, 0x0001));
        Assert.False(AtkBitmouse.IsCollection(0x373B, 0x1216, 0xFF04, 0x0002));
        Assert.False(AtkBitmouse.IsCollection(0x373B, 0x1216, 0xFF02, 0x0002));
        Assert.False(AtkBitmouse.IsCollection(0x38EE, 0x0022, 0xFF05, 0x0001));

        Assert.True(AtkCompx.IsCollection(0x373B, 0x1084, 0xFF04, 0x0002));
        Assert.False(AtkCompx.IsCollection(0x373B, 0x1084, 0xFF02, 0x0002));
        Assert.False(AtkCompx.IsCollection(0x373B, 0x1084, 0xFF05, 0x0001));

        Assert.True(AtkHechi.IsCollection(0x373B, 0x2134, 0xFFA0, 0x0002));
        Assert.False(AtkHechi.IsCollection(0x373B, 0x2134, 0xFF10, 0x0001));
    }

    [Fact]
    public void Hfd_keyboards_are_only_the_product_ids_on_that_controller()
    {
        Assert.True(AtkHfdKeyboard.IsCollection(0x373B, 4382, 0xFF60, 0x0061));
        Assert.True(AtkHfdKeyboard.IsCollection(0x373B, 0x1152, 0xFF60, 0x0061));
        Assert.False(AtkHfdKeyboard.IsCollection(0x373B, 4272, 0xFF60, 0x0061));
        Assert.False(AtkHfdKeyboard.IsCollection(0x373B, 4382, 0xFF60, 0x0001));
        Assert.False(AtkHfdKeyboard.IsCollection(0x373B, 4382, 0x000C, 0x0001));
    }

    [Fact]
    public void Bitmouse_checksum_covers_every_byte_after_it()
    {
        var battery = AtkBitmouse.BatteryRequest(AtkBitmouse.TargetLocal);
        Assert.Equal(63, battery.Length);
        Assert.Equal(new byte[] { 124, 0x72, 2, 0, 0, 7, 1 }, battery[..7]);
        Assert.All(battery.Skip(7), b => Assert.Equal(0, b));

        var behind = AtkBitmouse.BatteryRequest(AtkBitmouse.TargetMouseBehindReceiver);
        Assert.Equal(125, behind[0]);
        Assert.Equal(1, behind[4]);

        var type = AtkBitmouse.DeviceTypeRequest();
        Assert.Equal(new byte[] { 143, 0x72, 2, 0, 0, 26, 1 }, type[..7]);
    }

    [Fact]
    public void Compx_checksum_includes_report_id_8()
    {
        var request = AtkCompx.BatteryRequest();
        Assert.Equal(16, request.Length);
        Assert.Equal(4, request[0]);
        Assert.Equal(73, request[15]);
        Assert.All(request.Skip(1).Take(14), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void Bitmouse_level_is_the_first_data_byte(int level)
    {
        var web = BitmouseReply(AtkBitmouse.BatteryCommand, level, status: 0, prefix: false);
        Assert.True(AtkBitmouse.TryParseBattery(web, out var plain));
        Assert.Equal(level, plain);

        var windows = BitmouseReply(AtkBitmouse.BatteryCommand, level, status: 0, prefix: true);
        Assert.True(AtkBitmouse.TryParseBattery(windows, out var framed));
        Assert.Equal(level, framed);
    }

    [Fact]
    public void Bitmouse_status_2_and_a_level_above_100_are_not_a_percentage()
    {
        Assert.False(AtkBitmouse.TryParseBattery(BitmouseReply(AtkBitmouse.BatteryCommand, 40, status: 2, prefix: true), out _));
        Assert.True(AtkBitmouse.TryParseBattery(BitmouseReply(AtkBitmouse.BatteryCommand, 101, status: 0, prefix: false), out var over));
        Assert.Null(over);

        Assert.True(AtkBitmouse.TryParseCharging(BitmouseReply(AtkBitmouse.ChargingCommand, 1, status: 0, prefix: true), out var on));
        Assert.Equal(true, on);
        Assert.True(AtkBitmouse.TryParseCharging(BitmouseReply(AtkBitmouse.ChargingCommand, 0, status: 0, prefix: false), out var off));
        Assert.Equal(false, off);
        Assert.True(AtkBitmouse.TryParseCharging(BitmouseReply(AtkBitmouse.ChargingCommand, 4, status: 0, prefix: false), out var other));
        Assert.Null(other);
    }

    [Fact]
    public void Bitmouse_device_type_selects_the_receiver_target_before_the_level()
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, 5, status: 0, prefix: true));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, 40, status: 2, prefix: true));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, 40, status: 0, prefix: false));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.ChargingCommand, 1, status: 0, prefix: true));

        var result = AtkBitmouse.ReadBattery(link, 64, 64, CancellationToken.None, () => link.Now);

        Assert.Equal(AtkExchangeStatus.Ok, result.Status);
        Assert.Equal(40, result.Battery.Percent);
        Assert.Equal(true, result.Battery.Charging);
        Assert.Equal(3, link.Sent.Count);
        Assert.Equal(AtkBitmouse.DeviceTypeCommand, link.Sent[0][6]);
        Assert.Equal(0, link.Sent[0][5]);
        Assert.Equal(AtkBitmouse.BatteryCommand, link.Sent[1][6]);
        Assert.Equal(AtkBitmouse.TargetMouseBehindReceiver, link.Sent[1][5]);
        Assert.Equal(AtkBitmouse.ChargingCommand, link.Sent[2][6]);
        Assert.Equal(AtkBitmouse.ReportId, link.Sent[1][0]);
        Assert.Equal(64, link.Sent[1].Length);
    }

    [Fact]
    public void A_wired_bitmouse_stays_on_target_0_and_an_unknown_type_is_not_queried()
    {
        var wired = new ScriptedLink();
        wired.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, 2, status: 0, prefix: false));
        wired.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, 18, status: 0, prefix: true));
        var wiredResult = AtkBitmouse.ReadBattery(wired, 64, 64, CancellationToken.None, () => wired.Now);
        Assert.Equal(18, wiredResult.Battery.Percent);
        Assert.Null(wiredResult.Battery.Charging);
        Assert.Equal(0, wired.Sent[1][5]);

        var unknown = new ScriptedLink();
        unknown.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, 9, status: 0, prefix: true));
        var unknownResult = AtkBitmouse.ReadBattery(unknown, 64, 64, CancellationToken.None, () => unknown.Now);
        Assert.Equal(AtkExchangeStatus.DeviceTypeUnknown, unknownResult.Status);
        Assert.Null(unknownResult.Battery.Percent);
        Assert.Single(unknown.Sent);
    }

    [Fact]
    public void A_missing_charging_reply_keeps_the_bitmouse_percentage()
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, 0, status: 0, prefix: true));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, 55, status: 0, prefix: true));
        var result = AtkBitmouse.ReadBattery(link, 64, 64, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkExchangeStatus.Ok, result.Status);
        Assert.Equal(55, result.Battery.Percent);
        Assert.Null(result.Battery.Charging);
    }

    [Fact]
    public void Compx_level_and_charge_follow_the_16_byte_layout()
    {
        var web = CompxReply(61, charge: 0, status: 0, prefix: false);
        Assert.True(AtkCompx.TryParseBattery(web, out var level, out var charging));
        Assert.Equal(61, level);
        Assert.Equal(false, charging);

        var windows = CompxReply(12, charge: 1, status: 0, prefix: true);
        Assert.True(AtkCompx.TryParseBattery(windows, out level, out charging));
        Assert.Equal(12, level);
        Assert.Equal(true, charging);

        Assert.False(AtkCompx.TryParseBattery(CompxReply(12, charge: 1, status: 1, prefix: true), out _, out _));
        Assert.True(AtkCompx.TryParseBattery(CompxReply(101, charge: 0, status: 0, prefix: false), out var over, out _));
        Assert.Null(over);
    }

    [Fact]
    public void Hechi_does_not_clip_a_level_above_100()
    {
        Assert.True(AtkHechi.TryParseBattery(HechiReply(77, charge: 1, prefix: true), out var level, out var charging));
        Assert.Equal(77, level);
        Assert.Equal(true, charging);

        Assert.True(AtkHechi.TryParseBattery(HechiReply(101, charge: 0, prefix: false), out var over, out var idle));
        Assert.Null(over);
        Assert.Equal(false, idle);
        Assert.Equal(19, AtkHechi.InfoRequest()[0]);
        Assert.Equal(63, AtkHechi.InfoRequest().Length);
    }

    [Fact]
    public void Hfd_hides_the_level_unless_the_charge_byte_is_0()
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(HfdReply(64, charge: 0));
        var result = AtkHfdKeyboard.ReadBattery(link, 32, 32, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkExchangeStatus.Ok, result.Status);
        Assert.Equal(64, result.Battery.Percent);
        Assert.Equal(false, result.Battery.Charging);
        Assert.Equal(new byte[] { 2, 150, 36 }, link.Sent[0][..3]);
        Assert.Equal(32, link.Sent[0].Length);
        Assert.NotEqual(AtkHfdKeyboard.ReportId, link.Sent[0][0]);

        var charging = new ScriptedLink();
        charging.Replies.Enqueue(HfdReply(80, charge: 1));
        var hidden = AtkHfdKeyboard.ReadBattery(charging, 32, 32, CancellationToken.None, () => charging.Now);
        Assert.Equal(AtkExchangeStatus.ChargingWithoutLevel, hidden.Status);
        Assert.Null(hidden.Battery.Percent);
        Assert.Equal(true, hidden.Battery.Charging);

        Assert.False(AtkHfdKeyboard.TryParseBattery(new byte[] { 2, 150, 19, 80, 0 }, out _, out _));
    }

    [Fact]
    public void A_short_output_buffer_is_not_sent()
    {
        Assert.Empty(AtkOfficialExchange.FrameOutput(63, AtkBitmouse.ReportId, AtkBitmouse.BatteryRequest(0)));
        Assert.Empty(AtkOfficialExchange.FrameOutput(16, AtkCompx.ReportId, AtkCompx.BatteryRequest()));
        Assert.Empty(AtkOfficialExchange.FrameOutput(31, AtkHfdKeyboard.ReportId, AtkHfdKeyboard.BatteryRequest()));

        var link = new ScriptedLink { SendSucceeds = false };
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, 2, status: 0, prefix: true));
        var result = AtkBitmouse.ReadBattery(link, 64, 8, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkExchangeStatus.SendFailed, result.Status);
        Assert.Empty(link.Sent);
    }

    [Fact]
    public void An_atk_id_is_not_labeled_with_a_model_name()
    {
        var devices = DeviceAggregator.Combine(
        [
            new RawDeviceReading
            {
                SourceId = "atk-hfd",
                Priority = SourcePriority.Vendor,
                InstanceId = "atk-hfd:373B:111E",
                VendorId = 0x373B,
                ProductId = 0x111E,
                KindHint = DeviceKind.Keyboard,
                BatteryPercent = 64,
            },
            new RawDeviceReading
            {
                SourceId = "atk-bitmouse",
                Priority = SourcePriority.Vendor,
                InstanceId = "atk-bitmouse:373B:1216",
                VendorId = 0x373B,
                ProductId = 0x1216,
                KindHint = DeviceKind.Mouse,
                BatteryPercent = 40,
                Charging = true,
            },
        ], EmptyOverrideStore.Instance);

        Assert.Equal(2, devices.Count);
        Assert.All(devices, device => Assert.Null(device.DisplayName));
        Assert.Contains(devices, device => device.Kind == DeviceKind.Keyboard && device.BatteryPercent == 64);
        Assert.Contains(devices, device => device.Kind == DeviceKind.Mouse && device.BatteryPercent == 40 && device.Charging == true);
    }

    private static byte[] BitmouseReply(byte command, int data, int status, bool prefix)
    {
        var payload = new byte[63];
        payload[0] = AtkBitmouse.CommandPrefix;
        payload[1] = (byte)status;
        payload[4] = command;
        payload[5] = 1;
        payload[6] = (byte)data;
        return prefix ? Prefix(AtkBitmouse.ReportId, payload) : payload;
    }

    private static byte[] CompxReply(int level, int charge, int status, bool prefix)
    {
        var payload = new byte[16];
        payload[0] = AtkCompx.BatteryCommand;
        payload[1] = (byte)status;
        payload[AtkCompx.LevelOffset] = (byte)level;
        payload[AtkCompx.ChargeOffset] = (byte)charge;
        return prefix ? Prefix(AtkCompx.ReportId, payload) : payload;
    }

    private static byte[] HechiReply(int level, int charge, bool prefix)
    {
        var payload = new byte[63];
        payload[0] = AtkHechi.InfoCommand;
        payload[AtkHechi.ChargeIndex] = (byte)charge;
        payload[AtkHechi.LevelIndex] = (byte)level;
        return prefix ? Prefix(AtkHechi.ReportId, payload) : payload;
    }

    private static byte[] HfdReply(int level, int charge)
    {
        var payload = AtkHfdKeyboard.BatteryRequest();
        payload[AtkHfdKeyboard.LevelOffset] = (byte)level;
        payload[AtkHfdKeyboard.ChargeOffset] = (byte)charge;
        return payload;
    }

    private static byte[] Prefix(byte reportId, byte[] payload)
    {
        var framed = new byte[1 + payload.Length];
        framed[0] = reportId;
        payload.CopyTo(framed, 1);
        return framed;
    }

    private sealed class ScriptedLink : IAtkReportLink
    {
        public List<byte[]> Sent { get; } = [];
        public Queue<byte[]> Replies { get; } = new();
        public long Now { get; private set; }
        public bool SendSucceeds { get; init; } = true;
        private bool _armed;

        public void FlushInput()
        {
        }

        public bool ArmRead()
        {
            _armed = true;
            return true;
        }

        public bool SendOutput(ReadOnlySpan<byte> report, int timeoutMs)
        {
            if (!_armed || !SendSucceeds)
                return SendSucceeds && _armed;
            Sent.Add(report.ToArray());
            _armed = false;
            return true;
        }

        public bool WaitRead(int timeoutMs, out byte[]? report)
        {
            if (Replies.Count > 0)
            {
                report = Replies.Dequeue();
                return report.Length > 0;
            }

            Now += timeoutMs;
            report = null;
            return false;
        }
    }
}
