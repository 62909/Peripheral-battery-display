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
        Assert.Equal(true, wiredResult.Battery.Charging);
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
        var result = AtkHfdKeyboard.ReadBattery(link, 32, 32, 4383, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkExchangeStatus.Ok, result.Status);
        Assert.Equal(64, result.Battery.Percent);
        Assert.Equal(false, result.Battery.Charging);
        Assert.Equal(new byte[] { 2, 150, 36 }, link.Sent[0][..3]);
        Assert.Equal(32, link.Sent[0].Length);
        Assert.NotEqual(AtkHfdKeyboard.ReportId, link.Sent[0][0]);

        var charging = new ScriptedLink();
        charging.Replies.Enqueue(HfdReply(80, charge: 1));
        var hidden = AtkHfdKeyboard.ReadBattery(charging, 32, 32, 4382, CancellationToken.None, () => charging.Now);
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

    [Fact]
    public void Evision_matches_the_shared_320f_controller_and_ignores_other_ids()
    {
        Assert.True(AtkEvisionKeyboard.IsCollection(0x320F, 0x5055, 0xFF1C, 0x0092));
        Assert.True(AtkEvisionKeyboard.IsCollection(0x320F, 0x5088, 0xFF1C, 0x0092));
        Assert.False(AtkEvisionKeyboard.IsCollection(0x373B, 0x5055, 0xFF1C, 0x0092));
        Assert.False(AtkEvisionKeyboard.IsCollection(0x320F, 0x5055, 0xFF60, 0x0061));
        Assert.False(AtkEvisionKeyboard.IsCollection(0x320F, 0x5055, 0xFF1C, 0x0002));
        Assert.False(AtkEvisionKeyboard.IsCollection(0x320F, 0x111E, 0xFF1C, 0x0092));
        Assert.False(AtkEvisionKeyboard.IsCollection(0x373B, 4679, 0xFF60, 0x0061));
        Assert.False(AtkHfdKeyboard.IsCollection(0x320F, 0x5088, 0xFF1C, 0x0092));
        Assert.False(AtkHfdKeyboard.IsCollection(0x373B, 4679, 0xFF60, 0x0061));
    }

    [Fact]
    public void Evision_request_is_command_26_with_a_little_endian_request_code()
    {
        var wired = AtkEvisionKeyboard.BatteryRequest(AtkEvisionKeyboard.WiredProductId, 0x0201);
        Assert.Equal(63, wired.Length);
        Assert.Equal(new byte[] { 0x01, 0x02, AtkEvisionKeyboard.PowerCommand }, wired[..3]);
        Assert.All(wired.Skip(3), b => Assert.Equal(0, b));

        var dongle = AtkEvisionKeyboard.BatteryRequest(AtkEvisionKeyboard.DongleProductId);
        Assert.Equal(31, dongle.Length);
        Assert.Equal(new byte[] { 0x01, 0x00, 26 }, dongle[..3]);
        Assert.All(dongle.Skip(3), b => Assert.Equal(0, b));
        Assert.Empty(AtkEvisionKeyboard.BatteryRequest(0x111E));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(40, 2, true)]
    [InlineData(100, 1, false)]
    public void Evision_level_is_the_first_data_byte_and_charge_2_means_charging(int level, int charge, bool charging)
    {
        var web = EvisionReply(level, charge, prefix: false, payloadLength: 63);
        Assert.True(AtkEvisionKeyboard.TryParseBattery(web, out var plain, out var plainCharge));
        Assert.Equal(level, plain);
        Assert.Equal(charging, plainCharge);

        var windows = EvisionReply(level, charge, prefix: true, payloadLength: 31);
        Assert.True(AtkEvisionKeyboard.TryParseBattery(windows, out var framed, out var framedCharge));
        Assert.Equal(level, framed);
        Assert.Equal(charging, framedCharge);
    }

    [Fact]
    public void Evision_keeps_an_out_of_range_level_unknown_and_ignores_a_failure_reply()
    {
        Assert.True(AtkEvisionKeyboard.TryParseBattery(EvisionReply(101, 2, prefix: false, payloadLength: 63), out var over, out var charging));
        Assert.Null(over);
        Assert.Equal(true, charging);
        Assert.True(AtkEvisionKeyboard.TryParseBattery(EvisionReply(255, 0, prefix: true, payloadLength: 31), out var high, out var idle));
        Assert.Null(high);
        Assert.Equal(false, idle);

        Assert.False(AtkEvisionKeyboard.TryParseBattery(EvisionReply(40, 2, prefix: true, payloadLength: 63, status: AtkEvisionKeyboard.Failure), out _, out _));
        Assert.False(AtkEvisionKeyboard.TryParseBattery(EvisionReply(40, 2, prefix: false, payloadLength: 31, requestCode: 2), out _, out _));

        var lowByteIsReportId = EvisionReply(18, 2, prefix: true, payloadLength: 63, requestCode: 4);
        Assert.Equal(AtkEvisionKeyboard.ReportId, lowByteIsReportId[0]);
        Assert.Equal(AtkEvisionKeyboard.ReportId, lowByteIsReportId[1]);
        Assert.True(AtkEvisionKeyboard.TryParseBattery(lowByteIsReportId, 4, out var level, out var on));
        Assert.Equal(18, level);
        Assert.Equal(true, on);
        Assert.True(AtkEvisionKeyboard.TryParseBattery(EvisionReply(18, 2, prefix: false, payloadLength: 63, requestCode: 4), 4, out level, out on));
        Assert.Equal(18, level);
        Assert.Equal(true, on);
    }

    [Fact]
    public void Evision_wired_and_dongle_use_different_buffer_lengths()
    {
        var wired = new ScriptedLink();
        wired.Replies.Enqueue(EvisionReply(72, 0, prefix: true, payloadLength: 63));
        var wiredResult = AtkEvisionKeyboard.ReadBattery(
            wired, 64, 64, AtkEvisionKeyboard.WiredProductId, CancellationToken.None, () => wired.Now);
        Assert.Equal(AtkExchangeStatus.Ok, wiredResult.Status);
        Assert.Equal(72, wiredResult.Battery.Percent);
        Assert.Equal(true, wiredResult.Battery.Charging);
        Assert.Equal(64, wired.Sent[0].Length);
        Assert.Equal(AtkEvisionKeyboard.ReportId, wired.Sent[0][0]);
        Assert.Equal(new byte[] { 1, 0, 26 }, wired.Sent[0][1..4]);
        Assert.All(wired.Sent[0].Skip(4), b => Assert.Equal(0, b));

        var dongle = new ScriptedLink();
        dongle.Replies.Enqueue(EvisionReply(41, 2, prefix: false, payloadLength: 31));
        var dongleResult = AtkEvisionKeyboard.ReadBattery(
            dongle, 32, 32, AtkEvisionKeyboard.DongleProductId, CancellationToken.None, () => dongle.Now);
        Assert.Equal(AtkExchangeStatus.Ok, dongleResult.Status);
        Assert.Equal(41, dongleResult.Battery.Percent);
        Assert.Equal(true, dongleResult.Battery.Charging);
        Assert.Equal(32, dongle.Sent[0].Length);
        Assert.Equal(new byte[] { AtkEvisionKeyboard.ReportId, 1, 0, 26 }, dongle.Sent[0][..4]);
        Assert.All(dongle.Sent[0].Skip(4), b => Assert.Equal(0, b));

        var over = new ScriptedLink();
        over.Replies.Enqueue(EvisionReply(101, 2, prefix: true, payloadLength: 63));
        var overResult = AtkEvisionKeyboard.ReadBattery(
            over, 64, 64, AtkEvisionKeyboard.WiredProductId, CancellationToken.None, () => over.Now);
        Assert.Equal(AtkExchangeStatus.LevelOutOfRange, overResult.Status);
        Assert.Null(overResult.Battery.Percent);
        Assert.Equal(true, overResult.Battery.Charging);

        var failed = new ScriptedLink();
        failed.Replies.Enqueue(EvisionReply(80, 2, prefix: true, payloadLength: 31, status: AtkEvisionKeyboard.Failure));
        var failedResult = AtkEvisionKeyboard.ReadBattery(
            failed, 32, 32, AtkEvisionKeyboard.DongleProductId, CancellationToken.None, () => failed.Now);
        Assert.Equal(AtkExchangeStatus.NoReply, failedResult.Status);
        Assert.Null(failedResult.Battery.Percent);
        Assert.Equal(0, failed.Now);

        Assert.Empty(AtkOfficialExchange.FrameOutput(63, AtkEvisionKeyboard.ReportId, AtkEvisionKeyboard.BatteryRequest(AtkEvisionKeyboard.WiredProductId)));
        Assert.Empty(AtkOfficialExchange.FrameOutput(31, AtkEvisionKeyboard.ReportId, AtkEvisionKeyboard.BatteryRequest(AtkEvisionKeyboard.DongleProductId)));
        var shortLink = new ScriptedLink();
        var shortResult = AtkEvisionKeyboard.ReadBattery(
            shortLink, 32, 31, AtkEvisionKeyboard.DongleProductId, CancellationToken.None, () => shortLink.Now);
        Assert.Equal(AtkExchangeStatus.SendFailed, shortResult.Status);
        Assert.Empty(shortLink.Sent);
    }

    [Fact]
    public void An_evision_keyboard_uses_the_hid_product_string_and_does_not_invent_a_model()
    {
        var devices = DeviceAggregator.Combine(
        [
            new RawDeviceReading
            {
                SourceId = "atk-evision",
                Priority = SourcePriority.Vendor,
                InstanceId = "atk-evision:320F:5088",
                WindowsName = "ATK V75X 2.4G Dongle",
                VendorId = 0x320F,
                ProductId = 0x5088,
                KindHint = DeviceKind.Keyboard,
                BatteryPercent = 72,
                Charging = false,
            },
            new RawDeviceReading
            {
                SourceId = "atk-evision",
                Priority = SourcePriority.Vendor,
                InstanceId = "atk-evision:320F:5055",
                VendorId = 0x320F,
                ProductId = 0x5055,
                KindHint = DeviceKind.Keyboard,
                BatteryPercent = 40,
            },
        ], EmptyOverrideStore.Instance);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, device => device.DisplayName == "ATK V75X 2.4G Dongle" && device.Kind == DeviceKind.Keyboard && device.BatteryPercent == 72 && device.Charging == false);
        Assert.Contains(devices, device => device.DisplayName is null && device.Kind == DeviceKind.Keyboard && device.BatteryPercent == 40);
        Assert.DoesNotContain(devices, device => device.DisplayName is "V75X" or "ATK V75X");
    }

    [Fact]
    public void A_shown_level_on_a_wired_link_draws_a_bolt_and_a_dongle_does_not()
    {
        var wiredKeyboard = ReadEvision(AtkEvisionKeyboard.WiredProductId, level: 72, charge: 0, payloadLength: 63);
        Assert.Equal(72, wiredKeyboard.Percent);
        Assert.Equal(true, wiredKeyboard.Charging);
        AssertBolt(72, wiredKeyboard.Charging, bolt: true);

        var idleDongle = ReadEvision(AtkEvisionKeyboard.DongleProductId, level: 72, charge: 0, payloadLength: 31);
        Assert.Equal(false, idleDongle.Charging);
        AssertBolt(72, idleDongle.Charging, bolt: false);

        var chargingDongle = ReadEvision(AtkEvisionKeyboard.DongleProductId, level: 41, charge: 2, payloadLength: 31);
        Assert.Equal(true, chargingDongle.Charging);
        AssertBolt(41, chargingDongle.Charging, bolt: true);

        var hiddenLevel = ReadEvision(AtkEvisionKeyboard.WiredProductId, level: 101, charge: 0, payloadLength: 63);
        Assert.Null(hiddenLevel.Percent);
        Assert.Equal(false, hiddenLevel.Charging);
        AssertBolt(null, hiddenLevel.Charging, bolt: false);

        var unmarkedKeyboard = new ScriptedLink();
        var unmarkedKeyboardResult = AtkEvisionKeyboard.ReadBattery(
            unmarkedKeyboard, 64, 64, 0x111E, CancellationToken.None, () => unmarkedKeyboard.Now);
        Assert.Equal(AtkExchangeStatus.SendFailed, unmarkedKeyboardResult.Status);
        Assert.Null(unmarkedKeyboardResult.Battery.Charging);
        AssertBolt(null, unmarkedKeyboardResult.Battery.Charging, bolt: false);

        var wiredMouse = ReadBitmouse(type: 2, level: 18, charge: 0);
        Assert.Equal(true, wiredMouse.Charging);
        AssertBolt(18, wiredMouse.Charging, bolt: true);

        var idleReceiver = ReadBitmouse(type: 5, level: 55, charge: 0);
        Assert.Equal(false, idleReceiver.Charging);
        AssertBolt(55, idleReceiver.Charging, bolt: false);

        var chargingReceiver = ReadBitmouse(type: 0, level: 40, charge: 1);
        Assert.Equal(true, chargingReceiver.Charging);
        AssertBolt(40, chargingReceiver.Charging, bolt: true);

        var wiredCompx = ReadCompx(type: 2, level: 61, charge: 0);
        Assert.Equal(true, wiredCompx.Charging);
        AssertBolt(61, wiredCompx.Charging, bolt: true);

        var idleCompx = ReadCompx(type: 1, level: 61, charge: 0);
        Assert.Equal(false, idleCompx.Charging);
        AssertBolt(61, idleCompx.Charging, bolt: false);

        var chargingCompx = ReadCompx(type: 4, level: 12, charge: 1);
        Assert.Equal(true, chargingCompx.Charging);
        AssertBolt(12, chargingCompx.Charging, bolt: true);

        var nearlink = ReadCompx(type: 8, level: 33, charge: 0);
        Assert.Equal(false, nearlink.Charging);
        AssertBolt(33, nearlink.Charging, bolt: false);

        var unmarkedCompx = ReadCompx(type: 9, level: 33, charge: 0);
        Assert.Equal(false, unmarkedCompx.Charging);
        AssertBolt(33, unmarkedCompx.Charging, bolt: false);

        var unmarkedCompxCharging = ReadCompx(type: 9, level: 33, charge: 1);
        Assert.Equal(true, unmarkedCompxCharging.Charging);
        AssertBolt(33, unmarkedCompxCharging.Charging, bolt: true);

        var wiredHechi = ReadHechi(AtkHechi.WiredProductId, level: 40, charge: 0);
        Assert.Equal(true, wiredHechi.Charging);
        AssertBolt(40, wiredHechi.Charging, bolt: true);

        var idleHechi = ReadHechi(AtkHechi.DongleProductId, level: 40, charge: 0);
        Assert.Equal(false, idleHechi.Charging);
        AssertBolt(40, idleHechi.Charging, bolt: false);

        var chargingHechi = ReadHechi(AtkHechi.Dongle8KProductId, level: 77, charge: 1);
        Assert.Equal(true, chargingHechi.Charging);
        AssertBolt(77, chargingHechi.Charging, bolt: true);

        var unmarkedHechi = ReadHechi(0x2134, level: 40, charge: 0);
        Assert.Equal(false, unmarkedHechi.Charging);
        AssertBolt(40, unmarkedHechi.Charging, bolt: false);

        var unmarkedHechiCharging = ReadHechi(0x2134, level: 40, charge: 1);
        Assert.Equal(true, unmarkedHechiCharging.Charging);
        AssertBolt(40, unmarkedHechiCharging.Charging, bolt: true);

        var wiredHfd = ReadHfd(4382, level: 64, charge: 0);
        Assert.Equal(AtkExchangeStatus.Ok, wiredHfd.Status);
        Assert.Equal(64, wiredHfd.Percent);
        Assert.Equal(true, wiredHfd.Charging);
        AssertBolt(64, wiredHfd.Charging, bolt: true);

        var idleHfd = ReadHfd(4383, level: 64, charge: 0);
        Assert.Equal(false, idleHfd.Charging);
        AssertBolt(64, idleHfd.Charging, bolt: false);

        var chargingHfd = ReadHfd(4430, level: 80, charge: 1);
        Assert.Equal(AtkExchangeStatus.ChargingWithoutLevel, chargingHfd.Status);
        Assert.Null(chargingHfd.Percent);
        Assert.Equal(true, chargingHfd.Charging);
        AssertBolt(null, chargingHfd.Charging, bolt: true);

        var unmarkedHfd = ReadHfd(4272, level: 64, charge: 0);
        Assert.Equal(false, unmarkedHfd.Charging);
        AssertBolt(64, unmarkedHfd.Charging, bolt: false);
    }

    private readonly record struct Seen(int? Percent, bool? Charging, AtkExchangeStatus Status);

    private static Seen ReadEvision(int productId, int level, int charge, int payloadLength)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(EvisionReply(level, charge, prefix: true, payloadLength: payloadLength));
        var result = AtkEvisionKeyboard.ReadBattery(
            link, payloadLength + 1, payloadLength + 1, productId, CancellationToken.None, () => link.Now);
        return new(result.Battery.Percent, result.Battery.Charging, result.Status);
    }

    private static Seen ReadBitmouse(int type, int level, int charge)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, type, status: 0, prefix: true));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, level, status: 0, prefix: false));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.ChargingCommand, charge, status: 0, prefix: true));
        var result = AtkBitmouse.ReadBattery(link, 64, 64, CancellationToken.None, () => link.Now);
        return new(result.Battery.Percent, result.Battery.Charging, result.Status);
    }

    private static Seen ReadCompx(int type, int level, int charge)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(CompxTypeReply(type, prefix: true));
        link.Replies.Enqueue(CompxReply(level, charge, status: 0, prefix: false));
        var result = AtkCompx.ReadBattery(link, 32, 32, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkCompx.DeviceTypeCommand, link.Sent[0][1]);
        Assert.Equal(AtkCompx.DeviceTypeDataLength, link.Sent[0][5]);
        return new(result.Battery.Percent, result.Battery.Charging, result.Status);
    }

    private static Seen ReadHechi(int productId, int level, int charge)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(HechiReply(level, charge, prefix: true));
        var result = AtkHechi.ReadBattery(link, 64, 64, productId, CancellationToken.None, () => link.Now);
        return new(result.Battery.Percent, result.Battery.Charging, result.Status);
    }

    private static Seen ReadHfd(int productId, int level, int charge)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(HfdReply(level, charge));
        var result = AtkHfdKeyboard.ReadBattery(link, 32, 32, productId, CancellationToken.None, () => link.Now);
        return new(result.Battery.Percent, result.Battery.Charging, result.Status);
    }

    private static void AssertBolt(int? percent, bool? charging, bool bolt)
    {
        var box = Assert.Single(PillComposition.Arrange(
        [
            new PeripheralDevice("d", null, DeviceKind.Keyboard, percent, Charging: charging),
        ]).Boxes);
        Assert.Equal(bolt, box.Slot.Charging);
        Assert.Equal(bolt, box.BoltX is not null);
        if (bolt && percent is not null)
            Assert.True(box.BoltX > box.TextX);
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

    private static byte[] CompxTypeReply(int type, bool prefix)
    {
        var payload = new byte[16];
        payload[0] = AtkCompx.DeviceTypeCommand;
        payload[1] = AtkCompx.Success;
        payload[AtkCompx.DeviceTypeOffset] = (byte)type;
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

    private static byte[] EvisionReply(int level, int charge, bool prefix, int payloadLength, ushort requestCode = AtkEvisionKeyboard.RequestCode, byte status = AtkEvisionKeyboard.PowerCommand)
    {
        var payload = new byte[payloadLength];
        payload[0] = (byte)requestCode;
        payload[1] = (byte)(requestCode >> 8);
        payload[2] = status;
        payload[AtkEvisionKeyboard.LevelOffset] = (byte)level;
        payload[AtkEvisionKeyboard.ChargeOffset] = (byte)charge;
        return prefix ? Prefix(AtkEvisionKeyboard.ReportId, payload) : payload;
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
