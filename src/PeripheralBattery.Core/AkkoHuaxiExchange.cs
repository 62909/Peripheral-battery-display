namespace PeripheralBattery.Core;

public enum AkkoHuaxiExchangeStatus
{
    NoReply = 0,
    SendFailed = 1,
    LevelOutOfRange = 2,
    Ok = 3,
    MouseNotConnected = 4,
}

public readonly record struct AkkoHuaxiExchangeResult(AkkoHuaxiExchangeStatus Status, HuaxiInfo Info);

public readonly record struct AkkoHuaxiSessionResult(
    AkkoHuaxiExchangeStatus Status,
    HuaxiInfo Info,
    bool AmbientCfgFailed);

/// <summary>
/// One interrupt exchange with the Huaxi collection.
/// <see cref="ArmRead"/> has to succeed before <see cref="SendOutput"/>:
/// the official page registers its <c>inputreport</c> listener before
/// <c>sendReport</c>. A reply that arrives with no read waiting is not
/// asked for again.
/// </summary>
public interface IAkkoHuaxiLink
{
    void FlushInput();

    /// <summary>True when a read is pending, or a report is already held.</summary>
    bool ArmRead();

    bool SendOutput(ReadOnlySpan<byte> report, int timeoutMs);

    /// <summary>
    /// False on timeout or a failed read. The caller keeps waiting until the deadline.
    /// </summary>
    bool WaitRead(int timeoutMs, out byte[]? report);
}

/// <summary>
/// DevGetInfo over the interrupt pipe. The output and input buffers are the
/// report lengths from HidP_GetCaps, including the report id. Control
/// transfers are not part of this exchange.
/// </summary>
public static class AkkoHuaxiExchange
{
    public const int TimeoutMs = 3000;

    public static int InputBufferLength(int inputReportByteLength) =>
        inputReportByteLength > 0 ? inputReportByteLength : 0;

    /// <summary>
    /// Report id 11 followed by <see cref="AkkoHuaxiMouse.InfoRequest"/>.
    /// The array length is <paramref name="outputReportByteLength"/>, which
    /// is what WriteFile has to be given. A shorter buffer is not padded up,
    /// and a command that does not fit is not sent.
    /// </summary>
    public static byte[] CreateOutputReport(int outputReportByteLength) =>
        CreateOutputReport(outputReportByteLength, AkkoHuaxiMouse.InfoRequest());

    /// <summary>
    /// Report id 11 followed by <paramref name="command"/>.
    /// Used for <c>[196]</c>, <c>[208]</c>, <c>[152, 0]</c>, and <c>[129, 0]</c>.
    /// </summary>
    public static byte[] CreateOutputReport(int outputReportByteLength, ReadOnlySpan<byte> command)
    {
        if (command.Length == 0 || outputReportByteLength < 1 + command.Length)
            return [];
        var buffer = new byte[outputReportByteLength];
        buffer[0] = AkkoHuaxiMouse.ReportId;
        command.CopyTo(buffer.AsSpan(1));
        return buffer;
    }

    /// <summary>
    /// Official <c>initDevice(false, false)</c> prelude, then <c>DevGetInfo</c>.
    /// Each command uses its own listen-before-send exchange.
    /// A dongle LED query that fails is recorded and does not stop the later queries.
    /// <c>IsConnectMouse</c> must come back as 1 before a percentage is read.
    /// </summary>
    public static AkkoHuaxiSessionResult ReadBattery(
        IAkkoHuaxiLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        CancellationToken cancellationToken,
        Func<long>? nowMs = null,
        int timeoutMs = TimeoutMs)
    {
        var type = HuaxiDeviceType.Unknown;
        if (SendAndRead(
                link,
                inputReportByteLength,
                outputReportByteLength,
                AkkoHuaxiMouse.CurDevTypeRequest(),
                report => AkkoHuaxiMouse.TryParseDeviceType(report, out _),
                cancellationToken,
                nowMs,
                timeoutMs,
                out var typeReport) == SendPhase.Accepted
            && typeReport is not null)
        {
            AkkoHuaxiMouse.TryParseDeviceType(typeReport, out type);
        }

        var ambientFailed = false;
        if (type == HuaxiDeviceType.Dongle
            && SendAndRead(
                link,
                inputReportByteLength,
                outputReportByteLength,
                AkkoHuaxiMouse.DongleLedRequest(),
                report => AkkoHuaxiMouse.TryExtractPayload(report, AkkoHuaxiMouse.DongleLedCommand, out _),
                cancellationToken,
                nowMs,
                timeoutMs,
                out _) != SendPhase.Accepted)
        {
            ambientFailed = true;
        }

        var connected = SendAndRead(
            link,
            inputReportByteLength,
            outputReportByteLength,
            AkkoHuaxiMouse.ConnectMouseRequest(),
            report => AkkoHuaxiMouse.TryParseMouseConnected(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var connectReport);
        if (connected != SendPhase.Accepted
            || connectReport is null
            || !AkkoHuaxiMouse.TryParseMouseConnected(connectReport, out var mouseConnected)
            || !mouseConnected)
        {
            return new(AkkoHuaxiExchangeStatus.MouseNotConnected, default, ambientFailed);
        }

        var info = Run(link, inputReportByteLength, outputReportByteLength, cancellationToken, nowMs, timeoutMs);
        return new(info.Status, info.Info, ambientFailed);
    }

    public static AkkoHuaxiExchangeResult Run(
        IAkkoHuaxiLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        CancellationToken cancellationToken,
        Func<long>? nowMs = null,
        int timeoutMs = TimeoutMs)
    {
        var phase = SendAndRead(
            link,
            inputReportByteLength,
            outputReportByteLength,
            AkkoHuaxiMouse.InfoRequest(),
            report => AkkoHuaxiMouse.TryParseInfoReport(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var report);
        if (phase == SendPhase.SendFailed)
            return new(AkkoHuaxiExchangeStatus.SendFailed, default);
        if (phase == SendPhase.Accepted && report is not null && AkkoHuaxiMouse.TryParseInfoReport(report, out var info))
            return new(info.BatteryPercent is null ? AkkoHuaxiExchangeStatus.LevelOutOfRange : AkkoHuaxiExchangeStatus.Ok, info);
        return new(AkkoHuaxiExchangeStatus.NoReply, default);
    }

    private static SendPhase SendAndRead(
        IAkkoHuaxiLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        ReadOnlySpan<byte> command,
        Func<byte[], bool> accept,
        CancellationToken cancellationToken,
        Func<long>? nowMs,
        int timeoutMs,
        out byte[]? report)
    {
        report = null;
        var output = CreateOutputReport(outputReportByteLength, command);
        if (output.Length == 0)
            return SendPhase.SendFailed;
        if (InputBufferLength(inputReportByteLength) == 0)
            return SendPhase.NoReply;
        if (cancellationToken.IsCancellationRequested)
            return SendPhase.NoReply;

        var now = nowMs ?? (() => Environment.TickCount64);
        var deadline = now() + timeoutMs;
        link.FlushInput();
        if (!ArmUntil(link, now, deadline, cancellationToken))
            return SendPhase.NoReply;

        var sendBudget = Budget(now, deadline);
        if (sendBudget <= 0 || cancellationToken.IsCancellationRequested)
            return SendPhase.NoReply;
        if (!link.SendOutput(output, sendBudget))
            return SendPhase.SendFailed;

        while (true)
        {
            var remaining = Budget(now, deadline);
            if (remaining <= 0 || cancellationToken.IsCancellationRequested)
                break;
            if (link.WaitRead(remaining, out var incoming) && incoming is { Length: > 0 } && accept(incoming))
            {
                report = incoming;
                return SendPhase.Accepted;
            }

            if (Budget(now, deadline) <= 0 || cancellationToken.IsCancellationRequested)
                break;
            link.ArmRead();
        }

        return SendPhase.NoReply;
    }

    private static bool ArmUntil(
        IAkkoHuaxiLink link,
        Func<long> now,
        long deadline,
        CancellationToken cancellationToken)
    {
        while (Budget(now, deadline) > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;
            if (link.ArmRead())
                return true;
        }

        return false;
    }

    private static int Budget(Func<long> now, long deadline)
    {
        var remaining = deadline - now();
        if (remaining <= 0)
            return 0;
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }

    private enum SendPhase
    {
        NoReply,
        SendFailed,
        Accepted,
    }
}
