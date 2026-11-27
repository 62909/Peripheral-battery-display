namespace PeripheralBattery.Core;

/// <summary>
/// HECHI battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>useDevice</c> class <c>vp</c> / <c>fs</c>).
/// The filter is vendor <c>0x373B</c>, usage page <c>0xFFA0</c>, usage <c>0x0002</c>.
/// <c>getMouseInfo</c> is command 19 on report id 11. The payload is 63 bytes.
/// Byte 0 is the command, byte 1 is the length, byte 2 is the result code.
/// The charge byte is base offset 3 plus 13, and the level is the next byte.
/// The hub getter turns a level above 100 into 100. This reader does not:
/// that value is not a percentage. Charge 1 is charging and 0 is not.
/// This collection is the A9 Pro Max family in that hub, not BITMOUSE or COMPX.
/// </summary>
public static class AtkHechi
{
    public const int VendorId = 0x373B;
    public const ushort UsagePage = 0xFFA0;
    public const ushort Usage = 0x0002;
    public const byte ReportId = 11;
    public const byte InfoCommand = 19;
    public const int PayloadLength = 63;
    public const int BaseOffset = 3;
    public const int ChargeIndex = BaseOffset + 13;
    public const int LevelIndex = BaseOffset + 14;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId && productId >= 0 && usagePage == UsagePage && usage == Usage;

    public static byte[] InfoRequest()
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = InfoCommand;
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
            InfoRequest(),
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
        if (!AtkOfficialExchange.TryView(report, ReportId, InfoCommand, out var view) || view.Length <= LevelIndex)
            return false;
        var level = view[LevelIndex];
        percent = level <= 100 ? level : null;
        charging = view[ChargeIndex] switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
        return true;
    }
}
