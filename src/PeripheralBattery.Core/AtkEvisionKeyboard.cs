namespace PeripheralBattery.Core;

/// <summary>
/// EVISION keyboard battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>keyboard-info.util</c> class <c>Fr</c>,
/// <c>solutionProvider: EVISION</c>).
/// The running collection is vendor <c>0x320F</c>, usage page <c>0xFF1C</c>,
/// usage <c>0x0092</c>, and product id <c>0x5055</c> (wired) or <c>0x5088</c>
/// (2.4G). Those two ids are shared by the keyboards on this controller
/// (V75X, V75 PRO, V87, Kafka, and the rest of that <c>0x320F</c> filter).
/// The name is the HID product string, not a model from the hub table.
/// <c>power_info</c> is command 26 on report id 4. Bytes 0–1 are the request
/// code, little-endian; byte 2 is 26; the rest is zero. Wired <c>BUFFER_LEN</c>
/// is 63 and 2.4G is 31. A reply whose byte 2 is 255 failed. On success the
/// data starts at offset 7: the first byte is the level and the second byte
/// is charging only when it is 2. The hub turns a level above 100 into 100.
/// This reader does not. A shown 0–100 level on the wired id is charging even
/// when that byte is not 2. The 2.4G id stays on the byte. Bluetooth is not
/// in this filter.
/// V75X Ultra is vendor <c>0x373B</c> and usage page <c>FF60</c>; it is not
/// this controller.
/// </summary>
public static class AtkEvisionKeyboard
{
    public const int VendorId = 0x320F;
    public const int WiredProductId = 0x5055;
    public const int DongleProductId = 0x5088;
    public const ushort UsagePage = 0xFF1C;
    public const ushort Usage = 0x0092;
    public const byte ReportId = 4;
    public const byte PowerCommand = 26;
    public const byte Failure = 255;
    public const byte ChargingState = 2;
    public const ushort RequestCode = 1;
    public const int WiredPayloadLength = 63;
    public const int DonglePayloadLength = 31;
    public const int DataOffset = 7;
    public const int LevelOffset = DataOffset;
    public const int ChargeOffset = DataOffset + 1;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId
        && (productId == WiredProductId || productId == DongleProductId)
        && usagePage == UsagePage
        && usage == Usage;

    /// <summary>
    /// <c>0x5055</c> is wired. <c>0x5088</c> is 2.4G. Any other id is unmarked.
    /// </summary>
    public static bool? WiredLink(int productId) => productId switch
    {
        WiredProductId => true,
        DongleProductId => false,
        _ => null,
    };

    public static int PayloadLength(int productId) => productId switch
    {
        DongleProductId => DonglePayloadLength,
        WiredProductId => WiredPayloadLength,
        _ => 0,
    };

    public static byte[] BatteryRequest(int productId, ushort requestCode = RequestCode)
    {
        var length = PayloadLength(productId);
        if (length == 0)
            return [];
        var buffer = new byte[length];
        buffer[0] = (byte)requestCode;
        buffer[1] = (byte)(requestCode >> 8);
        buffer[2] = PowerCommand;
        return buffer;
    }

    public static AtkExchangeResult ReadBattery(
        IAtkReportLink link,
        int inputReportByteLength,
        int outputReportByteLength,
        int productId,
        CancellationToken cancellationToken,
        Func<long>? nowMs = null,
        int timeoutMs = AtkOfficialExchange.TimeoutMs)
    {
        var phase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            BatteryRequest(productId),
            report => ReplyMatches(report, RequestCode),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var report);
        if (phase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (phase != AtkExchangePhase.Accepted || report is null || !TryParseBattery(report, out var percent, out var charging))
            return new(AtkExchangeStatus.NoReply, default);
        charging = WiredCharging.Apply(charging, WiredLink(productId), percent);
        if (percent is null)
            return new(AtkExchangeStatus.LevelOutOfRange, new AtkBatteryReading(null, charging));
        return new(AtkExchangeStatus.Ok, new AtkBatteryReading(percent, charging));
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out int? percent, out bool? charging) =>
        TryParseBattery(report, RequestCode, out percent, out charging);

    public static bool TryParseBattery(ReadOnlySpan<byte> report, ushort requestCode, out int? percent, out bool? charging)
    {
        percent = null;
        charging = null;
        if (!TryView(report, requestCode, out var view) || view.Length <= ChargeOffset || view[2] == Failure)
            return false;
        var level = view[LevelOffset];
        percent = level <= 100 ? level : null;
        charging = view[ChargeOffset] == ChargingState;
        return true;
    }

    /// <summary>
    /// The hub matches the reply by the request code in bytes 0–1, then
    /// treats byte 2 == 255 as failure. A matching failure still ends the wait.
    /// </summary>
    private static bool ReplyMatches(ReadOnlySpan<byte> report, ushort requestCode) =>
        TryView(report, requestCode, out var view) && view.Length > 2;

    /// <summary>
    /// WebHID <c>data</c> starts at the request code. Windows <c>ReadFile</c>
    /// may prefix report id 4. The low byte of the request code can itself be 4,
    /// so the prefixed form is recognized only when byte 2 is the high byte.
    /// </summary>
    private static bool TryView(ReadOnlySpan<byte> report, ushort requestCode, out ReadOnlySpan<byte> view)
    {
        var lo = (byte)requestCode;
        var hi = (byte)(requestCode >> 8);
        if (report.Length >= 3 && report[0] == ReportId && report[1] == lo && report[2] == hi)
        {
            view = report[1..];
            return true;
        }

        if (report.Length >= 2 && report[0] == lo && report[1] == hi)
        {
            view = report;
            return true;
        }

        view = default;
        return false;
    }
}
