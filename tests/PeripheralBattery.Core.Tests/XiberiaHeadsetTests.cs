using Xunit;

namespace PeripheralBattery.Core.Tests;

public class XiberiaHeadsetTests
{
    private static readonly XiberiaReport Report = new(true, XiberiaHeadset.ReportBits);
    private static readonly XiberiaReport Absent = new(false, null);

    [Fact]
    public void The_family_matches_official_ids_and_skips_t10_wired()
    {
        Assert.True(Matches(0x36F9, 0xF002));
        Assert.True(Matches(0x36F9, 0xD001));
        Assert.True(Matches(0x37D3, 0xD2C9));
        Assert.True(Matches(0x36F9, XiberiaHeadset.T10WirelessProductId, usagePage: XiberiaHeadset.UsagePage, usage: XiberiaHeadset.Usage));
        Assert.True(Matches(0x36F9, 0xF008));
        Assert.True(Matches(0x36F9, 0xF014));
        Assert.True(Matches(0x36F9, 0xF009));
        Assert.True(Matches(0x36F9, 0xF016, usagePage: XiberiaHeadset.UsagePage, usage: XiberiaHeadset.Usage));
        Assert.True(Matches(0x36F9, 0xC006));
        Assert.True(Matches(0x36F9, 0xC011));

        Assert.False(XiberiaHeadset.IsCandidate(0x36F9, XiberiaHeadset.T10WiredProductId));
        Assert.False(Matches(0x36F9, XiberiaHeadset.T10WiredProductId));
        Assert.False(Matches(0x36F9, 0x0001));
        Assert.False(Matches(0x373B, 0xF008));
        Assert.False(Matches(0x36F9, 0xF008, input: new(true, 503)));
        Assert.False(Matches(0x36F9, 0xF008, input: Absent));
    }

    [Fact]
    public void Usage_and_input_only_follow_the_official_filters()
    {
        Assert.False(Matches(0x36F9, 0xF001, usagePage: 0x0001, usage: 0x0000));
        Assert.False(Matches(0x36F9, 0xF001, output: Absent, hasOutputReports: false));

        Assert.True(Matches(0x36F9, 0xF008, output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x36F9, 0xF008, usagePage: 0x0001, usage: 0x0000, output: Absent, hasOutputReports: false));
        Assert.True(Matches(0x36F9, 0xF009, output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x36F9, 0xF014, output: Absent, hasOutputReports: false));

        Assert.True(Matches(0x36F9, 0xF016, output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x36F9, 0xF016, usagePage: 0x000C, usage: 0x0001));
        Assert.False(Matches(0x36F9, 0xF016, output: Absent, hasOutputReports: true));

        Assert.True(Matches(0x36F9, 0xF002, output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x36F9, 0xF002, usagePage: 0x0001, usage: 0x0000, output: Absent, hasOutputReports: false));
        Assert.True(Matches(0x36F9, 0xF008, input: new(true, null)));
    }

    [Fact]
    public void Shared_4801_is_split_by_the_official_product_name()
    {
        Assert.True(XiberiaHeadset.IsCandidate(0x10D6, 0x4801));
        Assert.False(Matches(0x10D6, 0x4801, name: null));
        Assert.False(Matches(0x10D6, 0x4801, name: ""));
        Assert.False(Matches(0x10D6, 0x4801, name: "USB Audio Device"));
        Assert.False(Matches(0x10D6, 0x4801, name: "WG500"));
        Assert.False(Matches(0x10D6, 0x4801, name: "K03SWIRED"));

        Assert.True(Matches(0x10D6, 0x4801, name: "K03S", output: Absent, hasOutputReports: false));
        Assert.True(Matches(0x10D6, 0x4801, name: "k03s wired", output: Absent, hasOutputReports: false));
        Assert.True(Matches(0x10D6, 0x4801, name: "XIBERIA K03S", output: Absent, hasOutputReports: false));
        Assert.True(Matches(0x10D6, 0x4801, name: "XIBERIA  K03S WIRED", output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x10D6, 0x4801, name: "XIBERIA K03S", usagePage: 0x0001, usage: 0x0000, output: Absent, hasOutputReports: false));
        Assert.False(Matches(0x10D6, 0x4801, name: "K03S WIRELESS"));

        Assert.True(Matches(0x10D6, 0x4801, name: "XIBERIA WG500S", usagePage: 0x0001, usage: 0x0000));
        Assert.True(Matches(0x10D6, 0x4801, name: "WG 500 S"));
        Assert.False(Matches(0x10D6, 0x4801, name: "XIBERIA WG500S", output: Absent, hasOutputReports: false));
    }

    [Fact]
    public void Battery_and_device_info_frames_follow_the_official_layout()
    {
        var battery = XiberiaHeadset.BatteryRequest();
        Assert.Equal(63, battery.Length);
        Assert.Equal(new byte[] { 0x10, XiberiaHeadset.BatteryCommand, 1, 0 }, battery[..4]);
        Assert.All(battery.Skip(4), b => Assert.Equal(0, b));
        Assert.NotEqual(XiberiaHeadset.VersionCommand, battery[0]);

        var info = XiberiaHeadset.DeviceInfoRequest();
        Assert.Equal(63, info.Length);
        Assert.Equal(new byte[] { 0x10, XiberiaHeadset.DeviceInfoCommand, 0 }, info[..3]);
        Assert.All(info.Skip(3), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void Command_9_level_is_the_first_payload_byte(int level)
    {
        Assert.True(XiberiaHeadset.TryParseBattery(BatteryFrame(level, prefix: false), out var plain));
        Assert.Equal(level, plain);
        Assert.True(XiberiaHeadset.TryParseBattery(BatteryFrame(level, prefix: true), out var framed));
        Assert.Equal(level, framed);
    }

    [Fact]
    public void Command_9_above_100_is_unknown_and_does_not_set_charging()
    {
        Assert.True(XiberiaHeadset.TryParseBattery(BatteryFrame(101, prefix: true, extra: 1), out var over));
        Assert.Null(over);
        Assert.True(XiberiaHeadset.TryParseBattery(BatteryFrame(255, prefix: false), out var high));
        Assert.Null(high);

        var version = new byte[63];
        version[0] = XiberiaHeadset.VersionCommand;
        version[1] = 80;
        Assert.False(XiberiaHeadset.TryParseBattery(version, out _));
        Assert.False(XiberiaHeadset.TryParseDeviceInfo(version, out _, out _));
        Assert.False(XiberiaHeadset.TryParseDeviceInfo(BatteryFrame(40, prefix: false), out _, out _));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 64)]
    [InlineData(1, 100)]
    public void Command_1_accepts_a_connected_level_at_byte_3(int status, int level)
    {
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(status, level, prefix: false), out var plain, out var rejected));
        Assert.Equal(level, plain);
        Assert.False(rejected);
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(status, level, prefix: true, marker: 1), out var framed, out rejected));
        Assert.Equal(level, framed);
        Assert.False(rejected);

        var stepped = InfoFrame(1, 44, prefix: false);
        stepped[4] = 5;
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(stepped, out var kept, out rejected));
        Assert.Equal(44, kept);
        Assert.False(rejected);
    }

    [Fact]
    public void Command_1_accepts_a_short_form_and_a_header_without_padding()
    {
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(ShortInfo(2, 33, prefix: true), out var level, out var rejected));
        Assert.Equal(33, level);
        Assert.False(rejected);

        var compact = new byte[3 + 11];
        compact[0] = 0x10;
        compact[1] = XiberiaHeadset.DeviceInfoCommand;
        compact[2] = 11;
        compact[3] = 1;
        compact[6] = 18;
        compact[9] = 1;
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(compact, out level, out rejected));
        Assert.Equal(18, level);
        Assert.False(rejected);

        var withReport = new byte[compact.Length + 1];
        withReport[0] = XiberiaHeadset.ReportId;
        compact.CopyTo(withReport, 1);
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(withReport, out level, out _));
        Assert.Equal(18, level);
    }

    [Fact]
    public void Disconnected_or_out_of_range_status_does_not_invent_a_level_or_a_charge_flag()
    {
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(0, 80, prefix: true, marker: 1), out var hidden, out var rejected));
        Assert.Null(hidden);
        Assert.False(rejected);

        var disconnected = new byte[63];
        disconnected[0] = 0x10;
        disconnected[1] = XiberiaHeadset.DeviceInfoCommand;
        disconnected[2] = XiberiaHeadset.DisconnectedLength;
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(disconnected, out hidden, out rejected));
        Assert.Null(hidden);
        Assert.False(rejected);

        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(2, 101, prefix: false, marker: 1), out var over, out rejected));
        Assert.Null(over);
        Assert.True(rejected);
        Assert.False(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(3, 40, prefix: false), out _, out _));
        Assert.False(XiberiaHeadset.TryParseDeviceInfo(new byte[] { 1, 0, 0, 40, 0, 0, 0, 0, 0, 0, 0 }, out _, out _));
        Assert.False(XiberiaHeadset.TryParseBattery(InfoFrame(1, 40, prefix: false), out _));
    }

    [Fact]
    public void A_battery_reply_is_enough_and_a_miss_falls_back_to_device_info()
    {
        var direct = new ScriptedLink();
        direct.Plan.Add(BatteryFrame(72, prefix: true));
        var directResult = XiberiaHeadset.ReadBattery(direct, 64, 64, CancellationToken.None, () => direct.Now);
        Assert.Equal(AtkExchangeStatus.Ok, directResult.Status);
        Assert.Equal(72, directResult.Battery.Percent);
        Assert.Null(directResult.Battery.Charging);
        Assert.Single(direct.Sent);
        Assert.Equal(64, direct.Sent[0].Length);
        Assert.Equal(XiberiaHeadset.ReportId, direct.Sent[0][0]);
        Assert.Equal(new byte[] { 0x10, 0x09, 1, 0 }, direct.Sent[0][1..5]);
        Assert.All(direct.Sent[0].Skip(5), b => Assert.Equal(0, b));

        var fallback = new ScriptedLink();
        fallback.Plan.Add(null);
        fallback.Plan.Add(InfoFrame(1, 41, prefix: false, marker: 1));
        var fallbackResult = XiberiaHeadset.ReadBattery(fallback, 64, 64, CancellationToken.None, () => fallback.Now);
        Assert.Equal(AtkExchangeStatus.Ok, fallbackResult.Status);
        Assert.Equal(41, fallbackResult.Battery.Percent);
        Assert.Null(fallbackResult.Battery.Charging);
        Assert.Equal(2, fallback.Sent.Count);
        Assert.Equal(new byte[] { XiberiaHeadset.ReportId, 0x10, 0x01, 0 }, fallback.Sent[1][..4]);

        var over = new ScriptedLink();
        over.Plan.Add(BatteryFrame(101, prefix: true, extra: 1));
        over.Plan.Add(InfoFrame(0, 90, prefix: true, marker: 1));
        var overResult = XiberiaHeadset.ReadBattery(over, 64, 64, CancellationToken.None, () => over.Now);
        Assert.Equal(AtkExchangeStatus.LevelOutOfRange, overResult.Status);
        Assert.Null(overResult.Battery.Percent);
        Assert.Null(overResult.Battery.Charging);

        var rescued = new ScriptedLink();
        rescued.Plan.Add(BatteryFrame(255, prefix: false));
        rescued.Plan.Add(ShortInfo(2, 15, prefix: true));
        var rescuedResult = XiberiaHeadset.ReadBattery(rescued, 64, 64, CancellationToken.None, () => rescued.Now);
        Assert.Equal(15, rescuedResult.Battery.Percent);
        Assert.Null(rescuedResult.Battery.Charging);
    }

    [Fact]
    public void A_short_output_buffer_is_not_sent()
    {
        Assert.Empty(AtkOfficialExchange.FrameOutput(63, XiberiaHeadset.ReportId, XiberiaHeadset.BatteryRequest()));
        var link = new ScriptedLink();
        var result = XiberiaHeadset.ReadBattery(link, 64, 63, CancellationToken.None, () => link.Now);
        Assert.Equal(AtkExchangeStatus.SendFailed, result.Status);
        Assert.Null(result.Battery.Percent);
        Assert.Null(result.Battery.Charging);
        Assert.Empty(link.Sent);
    }

    [Fact]
    public void Charging_follows_the_hub_wired_mark_and_not_the_status_bytes()
    {
        Assert.True(XiberiaHeadset.ChargingFor(0x36F9, 0xF014, null));
        Assert.True(XiberiaHeadset.ChargingFor(0x10D6, 0x4801, "XIBERIA K03S"));
        Assert.True(XiberiaHeadset.ChargingFor(0x10D6, 0x4801, "K03S WIRED"));

        Assert.False(XiberiaHeadset.ChargingFor(0x36F9, 0xF008, "CROCIRIS T7"));
        Assert.False(XiberiaHeadset.ChargingFor(0x36F9, 0xF009, null));
        Assert.False(XiberiaHeadset.ChargingFor(0x36F9, 0xF001, null));
        Assert.False(XiberiaHeadset.ChargingFor(0x36F9, 0xF016, null));
        Assert.False(XiberiaHeadset.ChargingFor(0x36F9, 0xF002, null));

        Assert.Null(XiberiaHeadset.ChargingFor(0x36F9, 0xD001, null));
        Assert.Null(XiberiaHeadset.ChargingFor(0x37D3, 0xD2C9, null));
        Assert.Null(XiberiaHeadset.ChargingFor(0x36F9, 0xC006, null));
        Assert.Null(XiberiaHeadset.ChargingFor(0x36F9, 0xC011, null));
        Assert.Null(XiberiaHeadset.ChargingFor(0x10D6, 0x4801, "XIBERIA WG500S"));
        Assert.Null(XiberiaHeadset.ChargingFor(0x10D6, 0x4801, null));
        Assert.Null(XiberiaHeadset.ChargingFor(0x36F9, XiberiaHeadset.T10WiredProductId, null));

        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(1, 40, prefix: false), out var connected, out _));
        Assert.True(XiberiaHeadset.TryParseDeviceInfo(InfoFrame(2, 40, prefix: true), out var alsoConnected, out _));
        Assert.Equal(40, connected);
        Assert.Equal(connected, alsoConnected);

        Assert.True(PillComposition.ShowCharging(XiberiaHeadset.ChargingFor(0x36F9, 0xF014, null)));
        Assert.False(PillComposition.ShowCharging(XiberiaHeadset.ChargingFor(0x36F9, 0xF008, null)));
        Assert.False(PillComposition.ShowCharging(XiberiaHeadset.ChargingFor(0x36F9, 0xD001, null)));
    }

    [Fact]
    public void The_hid_product_string_is_kept_and_no_model_name_is_invented()
    {
        var devices = DeviceAggregator.Combine(
        [
            new RawDeviceReading
            {
                SourceId = "xiberia-headset",
                Priority = SourcePriority.Vendor,
                InstanceId = "xiberia-headset:36F9:F008",
                WindowsName = "XIBERIA T7",
                VendorId = 0x36F9,
                ProductId = 0xF008,
                KindHint = DeviceKind.Headset,
                BatteryPercent = 72,
                Charging = XiberiaHeadset.ChargingFor(0x36F9, 0xF008, "XIBERIA T7"),
            },
            new RawDeviceReading
            {
                SourceId = "xiberia-headset",
                Priority = SourcePriority.Vendor,
                InstanceId = "xiberia-headset:36F9:F014",
                VendorId = 0x36F9,
                ProductId = 0xF014,
                KindHint = DeviceKind.Headset,
                BatteryPercent = 40,
                Charging = XiberiaHeadset.ChargingFor(0x36F9, 0xF014, null),
            },
            new RawDeviceReading
            {
                SourceId = "xiberia-headset",
                Priority = SourcePriority.Vendor,
                InstanceId = "xiberia-headset:10D6:4801",
                WindowsName = "XIBERIA WG500S",
                VendorId = 0x10D6,
                ProductId = 0x4801,
                KindHint = DeviceKind.Headset,
                BatteryPercent = 55,
                Charging = XiberiaHeadset.ChargingFor(0x10D6, 0x4801, "XIBERIA WG500S"),
            },
        ], EmptyOverrideStore.Instance);

        Assert.Equal(3, devices.Count);
        Assert.Contains(devices, device => device.DisplayName == "XIBERIA T7" && device.Kind == DeviceKind.Headset && device.BatteryPercent == 72 && device.Charging == false && !PillComposition.ShowCharging(device.Charging));
        Assert.Contains(devices, device => device.DisplayName is null && device.Kind == DeviceKind.Headset && device.BatteryPercent == 40 && device.Charging == true && PillComposition.ShowCharging(device.Charging));
        Assert.Contains(devices, device => device.DisplayName == "XIBERIA WG500S" && device.BatteryPercent == 55 && device.Charging is null && !PillComposition.ShowCharging(device.Charging));
        Assert.DoesNotContain(devices, device => device.DisplayName is "T7" or "CROCIRIS T7" or "K03S" or "WG500S" or "西伯利亚");
    }

    private static bool Matches(
        int vendorId,
        int productId,
        string? name = null,
        int usagePage = XiberiaHeadset.UsagePage,
        int usage = XiberiaHeadset.Usage,
        XiberiaReport? input = null,
        XiberiaReport? output = null,
        bool hasOutputReports = true) =>
        XiberiaHeadset.IsCollection(
            vendorId,
            productId,
            name,
            usagePage,
            usage,
            input ?? Report,
            output ?? Report,
            hasOutputReports);

    private static byte[] BatteryFrame(int level, bool prefix, int extra = 0)
    {
        var frame = XiberiaHeadset.BatteryRequest();
        frame[3] = (byte)level;
        frame[4] = (byte)extra;
        return prefix ? Prefix(frame) : frame;
    }

    private static byte[] InfoFrame(int status, int level, bool prefix, int marker = 0)
    {
        var payload = Status(status, level, marker);
        var frame = new byte[XiberiaHeadset.FrameLength];
        frame[0] = XiberiaHeadset.FramePrefix;
        frame[1] = XiberiaHeadset.DeviceInfoCommand;
        frame[2] = (byte)payload.Length;
        payload.CopyTo(frame, 3);
        return prefix ? Prefix(frame) : frame;
    }

    private static byte[] ShortInfo(int status, int level, bool prefix)
    {
        var payload = Status(status, level, marker: 1);
        var frame = new byte[XiberiaHeadset.FrameLength];
        frame[0] = XiberiaHeadset.DeviceInfoCommand;
        frame[1] = (byte)payload.Length;
        payload.CopyTo(frame, 2);
        return prefix ? Prefix(frame) : frame;
    }

    private static byte[] Status(int status, int level, int marker)
    {
        var payload = new byte[XiberiaHeadset.StatusLength];
        payload[0] = (byte)status;
        payload[3] = (byte)level;
        payload[6] = (byte)marker;
        return payload;
    }

    private static byte[] Prefix(byte[] frame)
    {
        var prefixed = new byte[frame.Length + 1];
        prefixed[0] = XiberiaHeadset.ReportId;
        frame.CopyTo(prefixed, 1);
        return prefixed;
    }

    private sealed class ScriptedLink : IAtkReportLink
    {
        public List<byte[]> Sent { get; } = [];
        public List<byte[]?> Plan { get; } = [];
        public long Now { get; private set; }
        private int _cursor;
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
            Sent.Add(report.ToArray());
            _armed = false;
            return true;
        }

        public bool WaitRead(int timeoutMs, out byte[]? report)
        {
            if (_cursor < Plan.Count && Plan[_cursor] is byte[] next)
            {
                _cursor++;
                report = next;
                return report.Length > 0;
            }

            if (_cursor < Plan.Count)
                _cursor++;
            Now += timeoutMs;
            report = null;
            return false;
        }
    }
}
