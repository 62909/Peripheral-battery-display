using Xunit;

namespace PeripheralBattery.Core.Tests;

public class AkkoHuaxiExchangeTests
{
    [Fact]
    public void A_reply_that_arrives_during_send_is_parsed_only_if_the_read_was_already_armed()
    {
        var link = new ScriptedLink { DropUnlessArmed = true, Reply = Report(40, charging: 1, full: 1) };
        var result = Run(link, inputLength: 64, outputLength: 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.Ok, result.Status);
        Assert.Equal(40, result.Info.BatteryPercent);
        Assert.True(result.Info.Charging);
        Assert.True(result.Info.Full);
        Assert.Equal(["flush", "arm", "send", "wait"], link.Ops);
        Assert.Equal(64, link.Sent!.Length);
        Assert.Equal(AkkoHuaxiMouse.ReportId, link.Sent[0]);
        Assert.Equal(AkkoHuaxiMouse.InfoRequest(), link.Sent[1..3]);
        Assert.All(link.Sent.Skip(3), b => Assert.Equal(0, b));
    }

    [Fact]
    public void The_command_is_not_sent_until_a_read_is_armed()
    {
        var link = new ScriptedLink { ArmFailures = 2, DropUnlessArmed = true, Reply = Report(15) };
        var result = Run(link, inputLength: 32, outputLength: 32);

        Assert.Equal(AkkoHuaxiExchangeStatus.Ok, result.Status);
        Assert.Equal(15, result.Info.BatteryPercent);
        Assert.Equal(["flush", "arm", "arm", "arm", "send", "wait"], link.Ops);
    }

    [Fact]
    public void A_failed_read_does_not_end_the_wait()
    {
        var link = new ScriptedLink();
        link.Reads.Enqueue(ReadStep.Failed(20));
        link.Reads.Enqueue(ReadStep.Report(Report(55)));
        var result = Run(link, inputLength: 64, outputLength: 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.Ok, result.Status);
        Assert.Equal(55, result.Info.BatteryPercent);
        var firstWait = link.Ops.IndexOf("wait");
        var armAgain = link.Ops.IndexOf("arm", firstWait + 1);
        var secondWait = link.Ops.IndexOf("wait", firstWait + 1);
        Assert.True(armAgain > firstWait && armAgain < secondWait);
        Assert.Equal(1, link.Ops.Count(op => op == "flush"));
    }

    [Fact]
    public void An_unrelated_report_is_skipped_until_devgetinfo()
    {
        var mouse = new byte[64];
        mouse[0] = 0x01;
        mouse[1] = 99;
        var link = new ScriptedLink();
        link.Reads.Enqueue(ReadStep.Report(mouse));
        link.Reads.Enqueue(ReadStep.Report(Report(62, charging: 0, full: 0, length: 64)));
        var result = Run(link, inputLength: 64, outputLength: 64);

        Assert.Equal(62, result.Info.BatteryPercent);
        Assert.False(result.Info.Charging);
        Assert.False(result.Info.Full);
    }

    [Fact]
    public void A_level_outside_0_to_100_is_not_a_percentage()
    {
        var link = new ScriptedLink();
        link.Reads.Enqueue(ReadStep.Report(Report(101)));
        link.Reads.Enqueue(ReadStep.Report(Report(40)));
        var result = Run(link, inputLength: 64, outputLength: 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.LevelOutOfRange, result.Status);
        Assert.Null(result.Info.BatteryPercent);
        Assert.Equal(1, link.Ops.Count(op => op == "wait"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_ends_of_the_percentage_range_are_kept(int level)
    {
        var link = new ScriptedLink { Reply = Report(level), DropUnlessArmed = true };
        var result = Run(link, inputLength: 64, outputLength: 64);
        Assert.Equal(level, result.Info.BatteryPercent);
    }

    [Fact]
    public void Caps_length_is_not_raised_to_the_parse_minimum()
    {
        Assert.Equal(64, AkkoHuaxiExchange.InputBufferLength(64));
        Assert.Equal(8, AkkoHuaxiExchange.InputBufferLength(8));
        Assert.Equal(0, AkkoHuaxiExchange.InputBufferLength(0));
        Assert.Equal(0, AkkoHuaxiExchange.InputBufferLength(-1));

        var shortOutput = AkkoHuaxiExchange.CreateOutputReport(8);
        Assert.Equal(8, shortOutput.Length);
        Assert.Equal(new byte[] { AkkoHuaxiMouse.ReportId, 129, 0, 0, 0, 0, 0, 0 }, shortOutput);
        Assert.NotEqual(1 + AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset + 1, shortOutput.Length);

        Assert.Empty(AkkoHuaxiExchange.CreateOutputReport(2));
        Assert.Empty(AkkoHuaxiExchange.CreateOutputReport(0));
    }

    [Fact]
    public void A_command_that_does_not_fit_the_output_report_is_not_sent()
    {
        var link = new ScriptedLink { Reply = Report(40) };
        var result = Run(link, inputLength: 64, outputLength: 2);

        Assert.Equal(AkkoHuaxiExchangeStatus.SendFailed, result.Status);
        Assert.Empty(link.Ops);
    }

    [Fact]
    public void No_input_report_means_no_reply_and_does_not_send()
    {
        var link = new ScriptedLink { Reply = Report(40) };
        var result = Run(link, inputLength: 0, outputLength: 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.NoReply, result.Status);
        Assert.Empty(link.Ops);
    }

    [Fact]
    public void Send_failure_does_not_invent_a_battery()
    {
        var link = new ScriptedLink { SendSucceeds = false, Reply = Report(40), DropUnlessArmed = true };
        var result = Run(link, inputLength: 64, outputLength: 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.SendFailed, result.Status);
        Assert.Equal(["flush", "arm", "send"], link.Ops);
    }

    [Fact]
    public void Silence_until_the_deadline_is_no_reply()
    {
        var link = new ScriptedLink();
        var result = Run(link, inputLength: 64, outputLength: 32);

        Assert.Equal(AkkoHuaxiExchangeStatus.NoReply, result.Status);
        Assert.Equal(["flush", "arm", "send", "wait"], link.Ops);
        Assert.Equal(32, link.Sent!.Length);
    }

    [Fact]
    public void Cancellation_does_not_send()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var link = new ScriptedLink { Reply = Report(40) };
        var result = AkkoHuaxiExchange.Run(link, 64, 64, canceled.Token, () => link.Now);

        Assert.Equal(AkkoHuaxiExchangeStatus.NoReply, result.Status);
        Assert.Empty(link.Ops);
    }

    [Fact]
    public void Official_prelude_queries_type_then_dongle_led_then_connection_then_battery()
    {
        var link = new PreludeLink
        {
            Answer = command => command switch
            {
                AkkoHuaxiMouse.CurDevTypeCommand => CommandReply(AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DeviceTypeDongle, prefix: true, reportId: 7),
                AkkoHuaxiMouse.DongleLedCommand => CommandReply(AkkoHuaxiMouse.DongleLedCommand, 1, prefix: true, reportId: 11),
                AkkoHuaxiMouse.ConnectMouseCommand => CommandReply(AkkoHuaxiMouse.ConnectMouseCommand, 1, prefix: false, reportId: 0),
                AkkoHuaxiMouse.InfoCommand => InfoPayload(40, charging: 1, full: 1),
                _ => null,
            },
        };

        var result = Read(link, 64, 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.Ok, result.Status);
        Assert.Equal(40, result.Info.BatteryPercent);
        Assert.True(result.Info.Charging);
        Assert.False(result.AmbientCfgFailed);
        Assert.Equal(
            new[] { AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DongleLedCommand, AkkoHuaxiMouse.ConnectMouseCommand, AkkoHuaxiMouse.InfoCommand },
            link.Sent.Select(CommandByte).ToArray());
        Assert.Equal(new byte[] { AkkoHuaxiMouse.ReportId, AkkoHuaxiMouse.DongleLedCommand, 0 }, link.Sent[1][..3]);
        Assert.All(link.Sent, sent => Assert.Equal(64, sent.Length));
        Assert.Equal(Enumerable.Repeat(new[] { "flush", "arm", "send", "wait" }, 4).SelectMany(step => step), link.Ops);
    }

    [Fact]
    public void A_mouse_type_skips_the_dongle_led_query()
    {
        var link = new PreludeLink
        {
            Answer = command => command switch
            {
                AkkoHuaxiMouse.CurDevTypeCommand => CommandReply(AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DeviceTypeMouse, prefix: false, reportId: 0),
                AkkoHuaxiMouse.ConnectMouseCommand => CommandReply(AkkoHuaxiMouse.ConnectMouseCommand, 1, prefix: true, reportId: 4),
                AkkoHuaxiMouse.InfoCommand => Report(18),
                _ => null,
            },
        };

        var result = Read(link, 32, 32);

        Assert.Equal(18, result.Info.BatteryPercent);
        Assert.Equal(
            new[] { AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.ConnectMouseCommand, AkkoHuaxiMouse.InfoCommand },
            link.Sent.Select(CommandByte).ToArray());
        Assert.DoesNotContain(link.Sent, sent => sent.Length > 1 && sent[1] == AkkoHuaxiMouse.DongleLedCommand);
    }

    [Fact]
    public void A_disconnected_mouse_is_not_given_a_percentage()
    {
        var link = new PreludeLink
        {
            Answer = command => command switch
            {
                AkkoHuaxiMouse.CurDevTypeCommand => CommandReply(AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DeviceTypeDongle, prefix: true, reportId: 11),
                AkkoHuaxiMouse.DongleLedCommand => CommandReply(AkkoHuaxiMouse.DongleLedCommand, 1, prefix: false, reportId: 0),
                AkkoHuaxiMouse.ConnectMouseCommand => CommandReply(AkkoHuaxiMouse.ConnectMouseCommand, 0, prefix: true, reportId: 2),
                AkkoHuaxiMouse.InfoCommand => Report(80),
                _ => null,
            },
        };

        var result = Read(link, 64, 64);

        Assert.Equal(AkkoHuaxiExchangeStatus.MouseNotConnected, result.Status);
        Assert.Null(result.Info.BatteryPercent);
        Assert.Equal("接收器没有上报鼠标已连接。", AkkoHuaxiMouse.NoteMouseNotConnected);
        Assert.Equal(
            new[] { AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DongleLedCommand, AkkoHuaxiMouse.ConnectMouseCommand },
            link.Sent.Select(CommandByte).ToArray());
    }

    [Fact]
    public void A_failed_dongle_led_query_still_reaches_the_battery()
    {
        var link = new PreludeLink
        {
            Answer = command => command switch
            {
                AkkoHuaxiMouse.CurDevTypeCommand => CommandReply(AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DeviceTypeDongle, prefix: false, reportId: 0),
                AkkoHuaxiMouse.ConnectMouseCommand => CommandReply(AkkoHuaxiMouse.ConnectMouseCommand, 1, prefix: false, reportId: 0),
                AkkoHuaxiMouse.InfoCommand => Report(55),
                _ => null,
            },
        };

        var result = Read(link, 64, 16);

        Assert.Equal(55, result.Info.BatteryPercent);
        Assert.True(result.AmbientCfgFailed);
        Assert.Equal(
            new[] { AkkoHuaxiMouse.CurDevTypeCommand, AkkoHuaxiMouse.DongleLedCommand, AkkoHuaxiMouse.ConnectMouseCommand, AkkoHuaxiMouse.InfoCommand },
            link.Sent.Select(CommandByte).ToArray());
    }

    [Fact]
    public void Short_commands_use_the_caps_output_length()
    {
        var type = AkkoHuaxiExchange.CreateOutputReport(8, AkkoHuaxiMouse.CurDevTypeRequest());
        Assert.Equal(new byte[] { AkkoHuaxiMouse.ReportId, 196, 0, 0, 0, 0, 0, 0 }, type);

        var connect = AkkoHuaxiExchange.CreateOutputReport(64, AkkoHuaxiMouse.ConnectMouseRequest());
        Assert.Equal(64, connect.Length);
        Assert.Equal(new byte[] { AkkoHuaxiMouse.ReportId, 208 }, connect[..2]);
        Assert.All(connect.Skip(2), b => Assert.Equal(0, b));

        Assert.Equal(new byte[] { AkkoHuaxiMouse.ReportId, 208 }, AkkoHuaxiExchange.CreateOutputReport(2, AkkoHuaxiMouse.ConnectMouseRequest()));
        Assert.Empty(AkkoHuaxiExchange.CreateOutputReport(1, AkkoHuaxiMouse.CurDevTypeRequest()));
    }

    [Fact]
    public void DevGetInfo_parses_with_and_without_a_leading_report_id()
    {
        var payload = InfoPayload(40, charging: 1, full: 0);
        Assert.True(AkkoHuaxiMouse.TryParseInfoReport(payload, out var plain));
        Assert.Equal(40, plain.BatteryPercent);
        Assert.True(plain.Charging);

        var otherId = new byte[1 + payload.Length];
        otherId[0] = 7;
        payload.CopyTo(otherId, 1);
        Assert.True(AkkoHuaxiMouse.TryParseInfoReport(otherId, out var prefixed));
        Assert.Equal(40, prefixed.BatteryPercent);

        var reportIdEleven = Report(40, charging: 1, full: 0);
        Assert.True(AkkoHuaxiMouse.TryParseInfoReport(reportIdEleven, out var eleven));
        Assert.Equal(40, eleven.BatteryPercent);
    }

    private static AkkoHuaxiSessionResult Read(PreludeLink link, int inputLength, int outputLength) =>
        AkkoHuaxiExchange.ReadBattery(link, inputLength, outputLength, CancellationToken.None, () => link.Now);

    private static byte CommandByte(byte[] sent) => sent[1];

    private static byte[] CommandReply(byte command, byte value, bool prefix, byte reportId)
    {
        var payload = new byte[] { command, 0, 0, value };
        if (!prefix)
            return payload;
        var windows = new byte[1 + payload.Length];
        windows[0] = reportId;
        payload.CopyTo(windows, 1);
        return windows;
    }

    private static byte[] InfoPayload(int level, int charging, int full)
    {
        var payload = new byte[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset + 1];
        payload[0] = AkkoHuaxiMouse.InfoCommand;
        payload[2] = 0;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.ChargingOffset] = (byte)charging;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset] = (byte)level;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset] = (byte)full;
        return payload;
    }

    private static AkkoHuaxiExchangeResult Run(ScriptedLink link, int inputLength, int outputLength) =>
        AkkoHuaxiExchange.Run(link, inputLength, outputLength, CancellationToken.None, () => link.Now);

    private static byte[] Report(int level, int charging = 0, int full = 0, int length = 64)
    {
        var payload = new byte[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset + 1];
        payload[0] = AkkoHuaxiMouse.InfoCommand;
        payload[2] = 0;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.ChargingOffset] = (byte)charging;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.LevelOffset] = (byte)level;
        payload[AkkoHuaxiMouse.HeaderLength + AkkoHuaxiMouse.FullOffset] = (byte)full;
        var windows = new byte[Math.Max(length, 1 + payload.Length)];
        windows[0] = AkkoHuaxiMouse.ReportId;
        payload.CopyTo(windows, 1);
        return windows;
    }

    private readonly record struct ReadStep(bool Ok, byte[]? Data, int AdvanceMs)
    {
        public static ReadStep Failed(int advanceMs) => new(false, null, advanceMs);
        public static ReadStep Report(byte[] data) => new(true, data, 0);
    }

    private sealed class ScriptedLink : IAkkoHuaxiLink
    {
        public List<string> Ops { get; } = [];
        public long Now { get; private set; }
        public byte[]? Sent { get; private set; }
        public byte[]? Reply { get; init; }
        public bool DropUnlessArmed { get; init; }
        public bool SendSucceeds { get; init; } = true;
        public int ArmFailures { get; init; }
        public Queue<ReadStep> Reads { get; } = new();

        private int _armAttempts;
        private bool _armed;

        public void FlushInput() => Ops.Add("flush");

        public bool ArmRead()
        {
            Ops.Add("arm");
            _armAttempts++;
            if (_armAttempts <= ArmFailures)
            {
                Now += 10;
                return false;
            }

            _armed = true;
            return true;
        }

        public bool SendOutput(ReadOnlySpan<byte> report, int timeoutMs)
        {
            Ops.Add("send");
            Sent = report.ToArray();
            if (!SendSucceeds)
                return false;
            if (Reply is null)
                return true;
            if (_armed || !DropUnlessArmed)
                Reads.Enqueue(ReadStep.Report(Reply));
            _armed = false;
            return true;
        }

        public bool WaitRead(int timeoutMs, out byte[]? report)
        {
            Ops.Add("wait");
            if (Reads.Count > 0)
            {
                var step = Reads.Dequeue();
                Now += step.AdvanceMs;
                report = step.Data;
                return step.Ok && step.Data is { Length: > 0 };
            }

            Now += timeoutMs;
            report = null;
            return false;
        }
    }

    private sealed class PreludeLink : IAkkoHuaxiLink
    {
        public List<string> Ops { get; } = [];
        public List<byte[]> Sent { get; } = [];
        public long Now { get; private set; }
        public Func<byte, byte[]?>? Answer { get; init; }
        private bool _armed;
        private byte[]? _held;

        public void FlushInput() => Ops.Add("flush");

        public bool ArmRead()
        {
            Ops.Add("arm");
            _armed = true;
            return true;
        }

        public bool SendOutput(ReadOnlySpan<byte> report, int timeoutMs)
        {
            Ops.Add(_armed ? "send" : "send-deaf");
            var copy = report.ToArray();
            Sent.Add(copy);
            if (_armed && Answer is not null && copy.Length > 1)
                _held = Answer(copy[1]);
            _armed = false;
            return true;
        }

        public bool WaitRead(int timeoutMs, out byte[]? report)
        {
            Ops.Add("wait");
            if (_held is not null)
            {
                report = _held;
                _held = null;
                return report.Length > 0;
            }

            Now += timeoutMs;
            report = null;
            return false;
        }
    }
}
