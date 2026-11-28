using Xunit;

namespace PeripheralBattery.Core.Tests;

/// <summary>
/// The lightning bolt is the charging mark on the device list and the taskbar pill.
/// A shown 0–100 level on a known wired link is charging even when the official
/// field is not. 2.4G keeps that field. An unknown link does not gain a bolt
/// from this rule. Xiberia has no charge byte and still follows its connection table.
/// </summary>
public class ChargingMarkTests
{
    [Fact]
    public void The_list_and_the_pill_use_the_same_bolt_after_the_percentage()
    {
        var charging = new PeripheralDevice("m", "鼠标", DeviceKind.Mouse, 40, Charging: true);
        var idle = new PeripheralDevice("k", "键盘", DeviceKind.Keyboard, 10, Charging: false);
        var omitted = new PeripheralDevice("h", "耳机", DeviceKind.Headset, 80);
        var unknownWithFlag = new PeripheralDevice("u", null, DeviceKind.Unknown, null, Charging: true);

        AssertBolt(charging, "40%");
        AssertNoBolt(idle, "10%");
        AssertNoBolt(omitted, "80%");
        AssertBolt(unknownWithFlag, "未知");
    }

    [Fact]
    public void Xiberia_wired_with_a_shown_level_gets_the_bolt_and_wireless_does_not()
    {
        var level = ReadXiberia(40);
        Assert.Equal(AtkExchangeStatus.Ok, level.Status);
        Assert.Equal(40, level.Battery.Percent);
        Assert.Null(level.Battery.Charging);

        var wired = PublishXiberia(level, 0x36F9, XiberiaHeadset.T7WiredProductId, null);
        Assert.Null(wired.DisplayName);
        Assert.Equal(DeviceKind.Headset, wired.Kind);
        AssertBolt(wired, "40%");

        var namedWired = PublishXiberia(level, 0x10D6, XiberiaHeadset.SharedProductId, "XIBERIA K03S");
        Assert.Equal("XIBERIA K03S", namedWired.DisplayName);
        AssertBolt(namedWired, "40%");

        var wireless = PublishXiberia(ReadXiberia(72), 0x36F9, XiberiaHeadset.T7WirelessProductId, "XIBERIA T7");
        Assert.Equal("XIBERIA T7", wireless.DisplayName);
        Assert.False(wireless.Charging);
        AssertNoBolt(wireless, "72%");

        var unknown = PublishXiberia(level, 0x10D6, XiberiaHeadset.SharedProductId, "XIBERIA WG500S");
        Assert.Null(unknown.Charging);
        AssertNoBolt(unknown, "40%");

        var unmarked = PublishXiberia(level, 0x36F9, XiberiaHeadset.K06SProductId, null);
        Assert.Null(unmarked.Charging);
        AssertNoBolt(unmarked, "40%");

        var over = ReadXiberia(101);
        Assert.Equal(AtkExchangeStatus.LevelOutOfRange, over.Status);
        var hidden = PublishXiberia(over, 0x36F9, XiberiaHeadset.T7WiredProductId, null);
        Assert.Null(hidden.BatteryPercent);
        Assert.Null(hidden.Charging);
        AssertNoBolt(hidden, "未知");
    }

    [Fact]
    public void A_wired_level_gets_a_bolt_and_a_dongle_stays_on_the_official_field()
    {
        var evisionWiredIdle = ReadEvision(72, charge: 0, AtkEvisionKeyboard.WiredProductId, payload: 63);
        Assert.Equal(true, evisionWiredIdle.Battery.Charging);
        AssertBolt(PublishAtk(evisionWiredIdle, DeviceKind.Keyboard), "72%");

        var evisionWired = ReadEvision(40, charge: 2, AtkEvisionKeyboard.WiredProductId, payload: 63);
        Assert.Equal(true, evisionWired.Battery.Charging);
        AssertBolt(PublishAtk(evisionWired, DeviceKind.Keyboard), "40%");

        var evisionDongleIdle = ReadEvision(72, charge: 0, AtkEvisionKeyboard.DongleProductId, payload: 31);
        Assert.Equal(false, evisionDongleIdle.Battery.Charging);
        AssertNoBolt(PublishAtk(evisionDongleIdle, DeviceKind.Keyboard), "72%");

        var evisionDongle = ReadEvision(41, charge: 2, AtkEvisionKeyboard.DongleProductId, payload: 31);
        Assert.Equal(true, evisionDongle.Battery.Charging);
        AssertBolt(PublishAtk(evisionDongle, DeviceKind.Keyboard), "41%");

        var bitmouseWiredIdle = ReadBitmouse(type: 2, level: 18, charge: 0);
        Assert.Equal(true, bitmouseWiredIdle.Battery.Charging);
        AssertBolt(PublishAtk(bitmouseWiredIdle, DeviceKind.Mouse), "18%");

        var bitmouseWired = ReadBitmouse(type: 2, level: 18, charge: 1);
        Assert.Equal(true, bitmouseWired.Battery.Charging);
        AssertBolt(PublishAtk(bitmouseWired, DeviceKind.Mouse), "18%");

        var bitmouseDongle = ReadBitmouse(type: 0, level: 55, charge: 1);
        Assert.Equal(true, bitmouseDongle.Battery.Charging);
        AssertBolt(PublishAtk(bitmouseDongle, DeviceKind.Mouse), "55%");

        var bitmouseDongleIdle = ReadBitmouse(type: 5, level: 33, charge: 0);
        Assert.Equal(false, bitmouseDongleIdle.Battery.Charging);
        AssertNoBolt(PublishAtk(bitmouseDongleIdle, DeviceKind.Mouse), "33%");

        var missingOnWired = ReadBitmouse(type: 2, level: 18, charge: null);
        Assert.Equal(18, missingOnWired.Battery.Percent);
        Assert.Equal(true, missingOnWired.Battery.Charging);
        AssertBolt(PublishAtk(missingOnWired, DeviceKind.Mouse), "18%");

        var missingOnDongle = ReadBitmouse(type: 0, level: 55, charge: null);
        Assert.Null(missingOnDongle.Battery.Charging);
        AssertNoBolt(PublishAtk(missingOnDongle, DeviceKind.Mouse), "55%");

        Assert.True(AkkoHuaxiMouse.TryParseDeviceType(HuaxiType(AkkoHuaxiMouse.DeviceTypeMouse), out var mouse));
        Assert.Equal(HuaxiDeviceType.Mouse, mouse);
        Assert.True(AkkoHuaxiMouse.TryParseInfoPayload(HuaxiInfo(40, charging: 0), out var akkoIdle));
        Assert.False(akkoIdle.Charging);
        AssertNoBolt(Publish(akkoIdle.BatteryPercent, akkoIdle.Charging, DeviceKind.Mouse), "40%");

        Assert.True(AkkoHuaxiMouse.TryParseInfoPayload(HuaxiInfo(62, charging: 1), out var akkoOn));
        Assert.True(akkoOn.Charging);
        AssertBolt(Publish(akkoOn.BatteryPercent, akkoOn.Charging, DeviceKind.Mouse), "62%");

        var compxWired = ReadCompx(61, charge: 0, type: 2);
        Assert.Equal(true, compxWired.Battery.Charging);
        AssertBolt(PublishAtk(compxWired, DeviceKind.Mouse), "61%");
        var compxDongleIdle = ReadCompx(61, charge: 0, type: 1);
        Assert.Equal(false, compxDongleIdle.Battery.Charging);
        AssertNoBolt(PublishAtk(compxDongleIdle, DeviceKind.Mouse), "61%");
        var compxDongleOn = ReadCompx(12, charge: 1, type: 0);
        AssertBolt(PublishAtk(compxDongleOn, DeviceKind.Mouse), "12%");
        var compxUnmarked = ReadCompx(33, charge: 0, type: 9);
        Assert.Equal(false, compxUnmarked.Battery.Charging);
        AssertNoBolt(PublishAtk(compxUnmarked, DeviceKind.Mouse), "33%");
        var compxUnmarkedOn = ReadCompx(33, charge: 1, type: 9);
        AssertBolt(PublishAtk(compxUnmarkedOn, DeviceKind.Mouse), "33%");

        var hechiWired = ReadHechi(77, charge: 0, AtkHechi.WiredProductId);
        Assert.Equal(true, hechiWired.Battery.Charging);
        AssertBolt(PublishAtk(hechiWired, DeviceKind.Mouse), "77%");
        var hechiDongle = ReadHechi(77, charge: 0, AtkHechi.DongleProductId);
        Assert.Equal(false, hechiDongle.Battery.Charging);
        AssertNoBolt(PublishAtk(hechiDongle, DeviceKind.Mouse), "77%");
        var hechiDongleOn = ReadHechi(20, charge: 1, AtkHechi.Dongle8KProductId);
        AssertBolt(PublishAtk(hechiDongleOn, DeviceKind.Mouse), "20%");
        var hechiUnmarked = ReadHechi(40, charge: 0, 0x2134);
        Assert.Equal(false, hechiUnmarked.Battery.Charging);
        AssertNoBolt(PublishAtk(hechiUnmarked, DeviceKind.Mouse), "40%");

        var hfdWired = ReadHfd(64, charge: 0, 4382);
        Assert.Equal(true, hfdWired.Battery.Charging);
        AssertBolt(PublishAtk(hfdWired, DeviceKind.Keyboard), "64%");
        var hfdDongle = ReadHfd(64, charge: 0, 4383);
        Assert.Equal(false, hfdDongle.Battery.Charging);
        AssertNoBolt(PublishAtk(hfdDongle, DeviceKind.Keyboard), "64%");
        var hfdUnmarked = ReadHfd(64, charge: 0, 4272);
        Assert.Equal(false, hfdUnmarked.Battery.Charging);
        AssertNoBolt(PublishAtk(hfdUnmarked, DeviceKind.Keyboard), "64%");
    }

    [Fact]
    public void A_charge_state_without_a_percentage_does_not_invent_a_level_or_a_bolt()
    {
        var charging = ReadHfd(80, charge: 1, 4382);
        Assert.Equal(AtkExchangeStatus.ChargingWithoutLevel, charging.Status);
        Assert.Equal(true, charging.Battery.Charging);
        Assert.Null(charging.Battery.Percent);
        var hidden = PublishAtk(charging, DeviceKind.Keyboard, AtkHfdKeyboard.NoteChargingWithoutLevel);
        Assert.Equal(AtkHfdKeyboard.NoteChargingWithoutLevel, hidden.Note);
        Assert.Null(hidden.BatteryPercent);
        Assert.Null(hidden.Charging);
        AssertNoBolt(hidden, "未知");

        var over = ReadEvision(101, charge: 2, AtkEvisionKeyboard.WiredProductId, payload: 63);
        Assert.Equal(AtkExchangeStatus.LevelOutOfRange, over.Status);
        Assert.Equal(true, over.Battery.Charging);
        var unpublished = PublishAtk(over, DeviceKind.Keyboard, AtkEvisionKeyboard.NoteLevelOutOfRange);
        Assert.Equal(AtkEvisionKeyboard.NoteLevelOutOfRange, unpublished.Note);
        AssertNoBolt(unpublished, "未知");
    }

    private static void AssertBolt(PeripheralDevice device, string text)
    {
        var cell = DeviceListComposition.Describe(device);
        var box = Assert.Single(PillComposition.Arrange([device]).Boxes);
        Assert.Equal(text, cell.Text);
        Assert.True(cell.Bolt);
        Assert.Equal(text, box.Slot.PercentText);
        Assert.True(box.Slot.Charging);
        Assert.NotNull(box.BoltX);
        Assert.Equal(box.TextX + box.TextWidth + PillComposition.TextBoltGapDip, box.BoltX);
    }

    private static void AssertNoBolt(PeripheralDevice device, string text)
    {
        var cell = DeviceListComposition.Describe(device);
        var box = Assert.Single(PillComposition.Arrange([device]).Boxes);
        Assert.Equal(text, cell.Text);
        Assert.False(cell.Bolt);
        Assert.Equal(text, box.Slot.PercentText);
        Assert.False(box.Slot.Charging);
        Assert.Null(box.BoltX);
    }

    private static PeripheralDevice Publish(int? percent, bool? charging, DeviceKind kind, string? note = null, string? name = null, int? vid = null, int? pid = null)
    {
        return Assert.Single(DeviceAggregator.Combine(
        [
            new RawDeviceReading
            {
                SourceId = "vendor",
                Priority = SourcePriority.Vendor,
                InstanceId = "dev:charging-mark",
                WindowsName = name,
                VendorId = vid,
                ProductId = pid,
                KindHint = kind,
                BatteryPercent = percent,
                Charging = charging,
                Note = note,
            },
        ], EmptyOverrideStore.Instance));
    }

    private static PeripheralDevice PublishXiberia(AtkExchangeResult result, int vendorId, int productId, string? name)
    {
        var percent = result.Status == AtkExchangeStatus.Ok ? result.Battery.Percent : null;
        return Publish(
            percent,
            XiberiaHeadset.ChargingFor(vendorId, productId, name),
            DeviceKind.Headset,
            name: name,
            vid: vendorId,
            pid: productId);
    }

    private static PeripheralDevice PublishAtk(AtkExchangeResult result, DeviceKind kind, string? note = null)
    {
        var percent = result.Status == AtkExchangeStatus.Ok ? result.Battery.Percent : null;
        var charging = result.Status is AtkExchangeStatus.Ok or AtkExchangeStatus.ChargingWithoutLevel
            ? result.Battery.Charging
            : null;
        return Publish(percent, charging, kind, result.Status == AtkExchangeStatus.Ok ? null : note);
    }

    private static AtkExchangeResult ReadXiberia(int level)
    {
        var link = new ScriptedLink();
        var frame = XiberiaHeadset.BatteryRequest();
        frame[3] = (byte)level;
        link.Replies.Enqueue(frame);
        return XiberiaHeadset.ReadBattery(link, 64, 64, CancellationToken.None, () => link.Now);
    }

    private static AtkExchangeResult ReadEvision(int level, int charge, int productId, int payload)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(EvisionReply(level, charge, payload));
        return AtkEvisionKeyboard.ReadBattery(link, payload + 1, payload + 1, productId, CancellationToken.None, () => link.Now);
    }

    private static AtkExchangeResult ReadBitmouse(int type, int level, int? charge)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.DeviceTypeCommand, type));
        link.Replies.Enqueue(BitmouseReply(AtkBitmouse.BatteryCommand, level));
        if (charge is int flag)
            link.Replies.Enqueue(BitmouseReply(AtkBitmouse.ChargingCommand, flag));
        return AtkBitmouse.ReadBattery(link, 64, 64, CancellationToken.None, () => link.Now);
    }

    private static AtkExchangeResult ReadCompx(int level, int charge, int type)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(CompxTypeReply(type));
        link.Replies.Enqueue(CompxReply(level, charge));
        return AtkCompx.ReadBattery(link, 17, 17, CancellationToken.None, () => link.Now);
    }

    private static AtkExchangeResult ReadHechi(int level, int charge, int productId)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(HechiReply(level, charge));
        return AtkHechi.ReadBattery(link, 64, 64, productId, CancellationToken.None, () => link.Now);
    }

    private static AtkExchangeResult ReadHfd(int level, int charge, int productId)
    {
        var link = new ScriptedLink();
        link.Replies.Enqueue(HfdReply(level, charge));
        return AtkHfdKeyboard.ReadBattery(link, 32, 32, productId, CancellationToken.None, () => link.Now);
    }

    private static byte[] BitmouseReply(byte command, int data)
    {
        var payload = new byte[63];
        payload[0] = AtkBitmouse.CommandPrefix;
        payload[4] = command;
        payload[5] = 1;
        payload[6] = (byte)data;
        return payload;
    }

    private static byte[] EvisionReply(int level, int charge, int payloadLength)
    {
        var payload = new byte[payloadLength];
        payload[0] = (byte)AtkEvisionKeyboard.RequestCode;
        payload[1] = (byte)(AtkEvisionKeyboard.RequestCode >> 8);
        payload[2] = AtkEvisionKeyboard.PowerCommand;
        payload[AtkEvisionKeyboard.LevelOffset] = (byte)level;
        payload[AtkEvisionKeyboard.ChargeOffset] = (byte)charge;
        return payload;
    }

    private static byte[] CompxReply(int level, int charge)
    {
        var payload = new byte[16];
        payload[0] = AtkCompx.BatteryCommand;
        payload[AtkCompx.LevelOffset] = (byte)level;
        payload[AtkCompx.ChargeOffset] = (byte)charge;
        return payload;
    }

    private static byte[] CompxTypeReply(int type)
    {
        var payload = new byte[16];
        payload[0] = AtkCompx.DeviceTypeCommand;
        payload[1] = AtkCompx.Success;
        payload[AtkCompx.DeviceTypeOffset] = (byte)type;
        return payload;
    }

    private static byte[] HechiReply(int level, int charge)
    {
        var payload = new byte[63];
        payload[0] = AtkHechi.InfoCommand;
        payload[AtkHechi.ChargeIndex] = (byte)charge;
        payload[AtkHechi.LevelIndex] = (byte)level;
        return payload;
    }

    private static byte[] HfdReply(int level, int charge)
    {
        var payload = AtkHfdKeyboard.BatteryRequest();
        payload[AtkHfdKeyboard.LevelOffset] = (byte)level;
        payload[AtkHfdKeyboard.ChargeOffset] = (byte)charge;
        return payload;
    }

    private static byte[] HuaxiType(byte type)
    {
        var payload = new byte[AkkoHuaxiMouse.HeaderLength + 1];
        payload[0] = AkkoHuaxiMouse.CurDevTypeCommand;
        payload[AkkoHuaxiMouse.HeaderLength] = type;
        return payload;
    }

    private static byte[] HuaxiInfo(int level, int charging)
    {
        var payload = new byte[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset + 1];
        payload[0] = AkkoHuaxiMouse.InfoCommand;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset] = (byte)level;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.ChargingOffset] = (byte)charging;
        return payload;
    }

    private sealed class ScriptedLink : IAtkReportLink
    {
        public Queue<byte[]> Replies { get; } = new();
        public long Now { get; private set; }
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
            if (!_armed)
                return false;
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
