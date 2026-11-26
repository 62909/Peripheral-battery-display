namespace PeripheralBattery.Core;

/// <summary>
/// 38EE:0022 battery path from the public Huaxi page on hub.akkogear.com
/// (<c>huaxi/infortech-sdk.esm.js</c>). The WebHID filter is usage page
/// <c>0xFFA8</c> usage <c>0x0002</c>. Report id is 11. <c>DevGetInfo</c> is 129.
/// <c>parseRecvType</c> is byte 0. <c>parseRecvState</c> requires byte 2 to be 0.
/// <c>getDataSlice</c> drops the first 3 bytes when the payload is at least 4 bytes.
/// On that slice, charging is offset 13, the level is offset 14, and the full flag
/// is offset 15. A level outside 0–100 is not a percentage.
/// This id is not labeled 空影 TAN8 Ultra.
/// </summary>
public static class AkkoHuaxiMouse
{
    public const int VendorId = 0x38EE;
    public const int ProductId = 0x0022;
    public const ushort UsagePage = 0xFFA8;
    public const ushort Usage = 0x0002;
    public const byte ReportId = 11;
    public const byte InfoCommand = 129;
    public const int HeaderLength = 3;
    public const int LevelOffset = 14;
    public const int ChargingOffset = 13;
    public const int FullOffset = 15;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoInfoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCandidate(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public static bool IsInfoCollection(int usagePage, int usage) =>
        usagePage == UsagePage && usage == Usage;

    /// <summary>
    /// <c>queryDeviceInfoCmd</c> returns <c>[DevGetInfo, 0]</c>.
    /// </summary>
    public static byte[] InfoRequest() => [InfoCommand, 0];

    public static bool TryParseInfoReport(ReadOnlySpan<byte> windowsReport, out HuaxiInfo info)
    {
        info = default;
        if (windowsReport.Length < 2 || windowsReport[0] != ReportId)
            return false;
        return TryParseInfoPayload(windowsReport[1..], out info);
    }

    public static bool TryParseInfoPayload(ReadOnlySpan<byte> payload, out HuaxiInfo info)
    {
        info = default;
        if (payload.Length < 4 || payload[0] != InfoCommand || payload[2] != 0)
            return false;
        var data = payload[HeaderLength..];
        if (data.Length <= LevelOffset)
            return false;
        var level = data[LevelOffset];
        info = new HuaxiInfo(
            level <= 100 ? level : null,
            data.Length > ChargingOffset ? data[ChargingOffset] == 1 : null,
            data.Length > FullOffset ? data[FullOffset] == 1 : null);
        return true;
    }
}

public readonly record struct HuaxiInfo(int? BatteryPercent, bool? Charging, bool? Full);
