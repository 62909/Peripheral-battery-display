namespace PeripheralBattery.Core;

public enum AkkoHuaxiExchangeStatus
{
    NoReply = 0,
    SendFailed = 1,
    LevelOutOfRange = 2,
    Ok = 3,
}

public readonly record struct AkkoHuaxiExchangeResult(AkkoHuaxiExchangeStatus Status, HuaxiInfo Info);

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
    public static byte[] CreateOutputReport(int outputReportByteLength)
    {
        var payload = AkkoHuaxiMouse.InfoRequest();
        if (outputReportByteLength < 1 + payload.Length)
            return [];
        var buffer = new byte[outputReportByteLength];
        buffer[0] = AkkoHuaxiMouse.ReportId;
        payload.CopyTo(buffer, 1);
        return buffer;
    }

    public static AkkoHuaxiExchangeResult Run(
        IAkkoHuaxiLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        CancellationToken cancellationToken,
        Func<long>? nowMs = null,
        int timeoutMs = TimeoutMs)
    {
        var output = CreateOutputReport(outputReportByteLength);
        if (output.Length == 0)
            return new(AkkoHuaxiExchangeStatus.SendFailed, default);
        if (InputBufferLength(inputReportByteLength) == 0)
            return new(AkkoHuaxiExchangeStatus.NoReply, default);
        if (cancellationToken.IsCancellationRequested)
            return new(AkkoHuaxiExchangeStatus.NoReply, default);

        var now = nowMs ?? (() => Environment.TickCount64);
        var deadline = now() + timeoutMs;
        link.FlushInput();
        if (!ArmUntil(link, now, deadline, cancellationToken))
            return new(AkkoHuaxiExchangeStatus.NoReply, default);

        var sendBudget = Budget(now, deadline);
        if (sendBudget <= 0 || cancellationToken.IsCancellationRequested)
            return new(AkkoHuaxiExchangeStatus.NoReply, default);
        if (!link.SendOutput(output, sendBudget))
            return new(AkkoHuaxiExchangeStatus.SendFailed, default);

        while (true)
        {
            var remaining = Budget(now, deadline);
            if (remaining <= 0 || cancellationToken.IsCancellationRequested)
                break;
            if (link.WaitRead(remaining, out var report) && TryAccept(report, out var accepted))
                return accepted;
            if (Budget(now, deadline) <= 0 || cancellationToken.IsCancellationRequested)
                break;
            link.ArmRead();
        }

        return new(AkkoHuaxiExchangeStatus.NoReply, default);
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

    private static bool TryAccept(byte[]? report, out AkkoHuaxiExchangeResult result)
    {
        result = default;
        if (report is null || !AkkoHuaxiMouse.TryParseInfoReport(report, out var info))
            return false;
        var status = info.BatteryPercent is null
            ? AkkoHuaxiExchangeStatus.LevelOutOfRange
            : AkkoHuaxiExchangeStatus.Ok;
        result = new AkkoHuaxiExchangeResult(status, info);
        return true;
    }

    private static int Budget(Func<long> now, long deadline)
    {
        var remaining = deadline - now();
        if (remaining <= 0)
            return 0;
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }
}
