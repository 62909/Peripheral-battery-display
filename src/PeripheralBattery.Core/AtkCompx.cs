namespace PeripheralBattery.Core;

/// <summary>
/// COMPX battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>static/filter-DZcjCGve.js</c>).
/// The filter is vendor <c>0x373B</c>, usage page <c>0xFF04</c>, usage <c>0x0002</c>.
/// <c>GetBatteryLevel</c> is command 4 on report id 8. The payload is 16 bytes.
/// The checksum byte is <c>0x55</c> minus the low byte of (report id 8 plus the
/// first 15 payload bytes). <c>commandStatus</c> must be <c>Success</c> (0).
/// The level is byte 5 and the charge byte is byte 6. 1 is charging and 0 is not.
/// A level outside 0–100 is not a percentage. This is not the BITMOUSE path.
/// </summary>
public static class AtkCompx
{
    public const int VendorId = 0x373B;
    public const ushort UsagePage = 0xFF04;
    public const ushort Usage = 0x0002;
    public const byte ReportId = 8;
    public const byte BatteryCommand = 4;
    public const byte Success = 0;
    public const int PayloadLength = 16;
    public const int LevelOffset = 5;
    public const int ChargeOffset = 6;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId && productId >= 0 && usagePage == UsagePage && usage == Usage;

    public static byte[] BatteryRequest()
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = BatteryCommand;
        var sum = (int)ReportId;
        for (var i = 0; i < PayloadLength - 1; i++)
            sum += buffer[i];
        buffer[PayloadLength - 1] = (byte)(0x55 - (sum & 0xFF));
        return buffer;
    }

    public static AtkExchangeResult ReadBattery(
        IAtkReportLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        CancellationToken cancellationToken,
        Func<long>? nowMs = null,
        int timeoutMs = AtkOfficialExchange.TimeoutMs)
    {
        var phase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            BatteryRequest(),
            report => TryParseBattery(report, out _, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var report);
        if (phase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (phase != AtkExchangePhase.Accepted || report is null || !TryParseBattery(report, out var percent, out var charging))
            return new(AtkExchangeStatus.NoReply, default);
        if (percent is null)
            return new(AtkExchangeStatus.LevelOutOfRange, new AtkBatteryReading(null, charging));
        return new(AtkExchangeStatus.Ok, new AtkBatteryReading(percent, charging));
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out int? percent, out bool? charging)
    {
        percent = null;
        charging = null;
        if (!AtkOfficialExchange.TryView(report, ReportId, BatteryCommand, out var view))
            return false;
        if (view.Length <= LevelOffset || view[1] != Success)
            return false;
        var level = view[LevelOffset];
        percent = level <= 100 ? level : null;
        if (view.Length > ChargeOffset)
        {
            charging = view[ChargeOffset] switch
            {
                0 => false,
                1 => true,
                _ => null,
            };
        }

        return true;
    }
}
