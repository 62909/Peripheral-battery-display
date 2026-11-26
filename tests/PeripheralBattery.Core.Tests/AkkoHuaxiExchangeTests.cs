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
}
