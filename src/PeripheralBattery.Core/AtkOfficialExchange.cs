namespace PeripheralBattery.Core;

public enum AtkExchangeStatus
{
    NoReply = 0,
    SendFailed = 1,
    LevelOutOfRange = 2,
    Ok = 3,
    DeviceTypeUnknown = 4,
    ChargingWithoutLevel = 5,
    NoDeviceType = 6,
}

public readonly record struct AtkBatteryReading(int? Percent, bool? Charging);

public readonly record struct AtkExchangeResult(AtkExchangeStatus Status, AtkBatteryReading Battery);

/// <summary>
/// Listen-before-send interrupt exchange used by the ATK hub protocols.
/// The official page registers <c>inputreport</c> before <c>sendReport</c>.
/// </summary>
public interface IAtkReportLink
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
/// Output framing shared by the hub protocols. A non-zero report id is the
/// Windows <c>WriteFile</c> prefix. Report id 0 is the HFD keyboard collection:
/// the official page calls <c>sendReport(0, payload)</c> and the buffer is the
/// payload itself.
/// </summary>
public static class AtkOfficialExchange
{
    public const int TimeoutMs = 2000;

    public static byte[] FrameOutput(int outputReportByteLength, byte reportId, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || outputReportByteLength <= 0)
            return [];
        if (reportId == 0)
        {
            if (outputReportByteLength < payload.Length)
                return [];
            var bare = new byte[outputReportByteLength];
            payload.CopyTo(bare);
            return bare;
        }

        if (outputReportByteLength < 1 + payload.Length)
            return [];
        var framed = new byte[outputReportByteLength];
        framed[0] = reportId;
        payload.CopyTo(framed.AsSpan(1));
        return framed;
    }

    /// <summary>
    /// WebHID <c>data</c> starts at <paramref name="marker"/>. Windows
    /// <c>ReadFile</c> may prefix <paramref name="reportId"/> when that id is not 0.
    /// </summary>
    public static bool TryView(ReadOnlySpan<byte> report, byte reportId, byte marker, out ReadOnlySpan<byte> view)
    {
        if (!report.IsEmpty && report[0] == marker)
        {
            view = report;
            return true;
        }

        if (reportId != 0 && report.Length > 1 && report[0] == reportId && report[1] == marker)
        {
            view = report[1..];
            return true;
        }

        view = default;
        return false;
    }

    public static AtkExchangePhase Send(
        IAtkReportLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        byte reportId,
        ReadOnlySpan<byte> payload,
        Func<byte[], bool> accept,
        CancellationToken cancellationToken,
        Func<long>? nowMs,
        int timeoutMs,
        out byte[]? report)
    {
        report = null;
        var output = FrameOutput(outputReportByteLength, reportId, payload);
        if (output.Length == 0)
            return AtkExchangePhase.SendFailed;
        if (inputReportByteLength <= 0)
            return AtkExchangePhase.NoReply;
        if (cancellationToken.IsCancellationRequested)
            return AtkExchangePhase.NoReply;

        var now = nowMs ?? (() => Environment.TickCount64);
        var deadline = now() + timeoutMs;
        link.FlushInput();
        if (!ArmUntil(link, now, deadline, cancellationToken))
            return AtkExchangePhase.NoReply;

        var sendBudget = Budget(now, deadline);
        if (sendBudget <= 0 || cancellationToken.IsCancellationRequested)
            return AtkExchangePhase.NoReply;
        if (!link.SendOutput(output, sendBudget))
            return AtkExchangePhase.SendFailed;

        while (true)
        {
            var remaining = Budget(now, deadline);
            if (remaining <= 0 || cancellationToken.IsCancellationRequested)
                break;
            if (link.WaitRead(remaining, out var incoming) && incoming is { Length: > 0 } && accept(incoming))
            {
                report = incoming;
                return AtkExchangePhase.Accepted;
            }

            if (Budget(now, deadline) <= 0 || cancellationToken.IsCancellationRequested)
                break;
            link.ArmRead();
        }

        return AtkExchangePhase.NoReply;
    }

    private static bool ArmUntil(
        IAtkReportLink link,
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
}

public enum AtkExchangePhase
{
    NoReply,
    SendFailed,
    Accepted,
}
