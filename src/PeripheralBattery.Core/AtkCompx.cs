namespace PeripheralBattery.Core;

/// <summary>
/// COMPX battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>static/filter-DZcjCGve.js</c>).
/// The filter is vendor <c>0x373B</c>, usage page <c>0xFF04</c>, usage <c>0x0002</c>.
/// <c>GetBatteryLevel</c> is command 4 on report id 8. The payload is 16 bytes.
/// The checksum byte is <c>0x55</c> minus the low byte of (report id 8 plus the
/// first 15 payload bytes). <c>commandStatus</c> must be <c>Success</c> (0).
/// The level is byte 5 and the charge byte is byte 6. 1 is charging and 0 is not.
/// <c>getMouseInfo</c> reads the link first, from <c>DownLoadData</c> (command 1).
/// Device type uses the same values as BITMOUSE: 2, 3, 6 and 7 are wired;
/// 0, 1, 4 and 5 are receivers; 8 is nearlink. The type byte is
/// <c>baseOffset + 6</c> (byte 11). A shown level on a wired type is charging
/// even when the charge byte is not 1. A receiver, nearlink, or a type this
/// reply does not name stays on the charge byte.
/// A level outside 0–100 is not a percentage. This is not the BITMOUSE path.
/// </summary>
public static class AtkCompx
{
    public const int VendorId = 0x373B;
    public const ushort UsagePage = 0xFF04;
    public const ushort Usage = 0x0002;
    public const byte ReportId = 8;
    public const byte BatteryCommand = 4;
    public const byte DeviceTypeCommand = 1;
    public const byte Success = 0;
    public const int PayloadLength = 16;
    public const int LevelOffset = 5;
    public const int ChargeOffset = 6;
    public const int DeviceTypeOffset = 11;
    public const byte DeviceTypeDataLength = 8;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId && productId >= 0 && usagePage == UsagePage && usage == Usage;

    public static byte[] BatteryRequest() => Command(BatteryCommand, dataLength: 0);

    /// <summary>
    /// <c>DownLoadData</c>. The hub writes four bytes at <c>baseOffset</c> and
    /// sets <c>dataValidLen</c> to 8. Those bytes are not the link. The link
    /// is the reply's device type.
    /// </summary>
    public static byte[] DeviceTypeRequest() => Command(DeviceTypeCommand, DeviceTypeDataLength);

    /// <summary>
    /// Wired types are 2, 3, 6 and 7. Receiver types and nearlink (8) are not.
    /// Any other byte is unmarked.
    /// </summary>
    public static bool TryParseLink(ReadOnlySpan<byte> report, out bool? wired)
    {
        wired = null;
        if (!AtkOfficialExchange.TryView(report, ReportId, DeviceTypeCommand, out var view))
            return false;
        if (view.Length <= DeviceTypeOffset || view[1] != Success)
            return false;
        wired = view[DeviceTypeOffset] switch
        {
            2 or 3 or 6 or 7 => true,
            0 or 1 or 4 or 5 or 8 => false,
            _ => null,
        };
        return true;
    }

    private static byte[] Command(byte command, byte dataLength)
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = command;
        buffer[4] = dataLength;
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
        bool? wired = null;
        var typePhase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            DeviceTypeRequest(),
            report => TryParseLink(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var typeReport);
        if (typePhase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (typePhase == AtkExchangePhase.Accepted && typeReport is not null)
            TryParseLink(typeReport, out wired);

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
        charging = WiredCharging.Apply(charging, wired, percent);
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
