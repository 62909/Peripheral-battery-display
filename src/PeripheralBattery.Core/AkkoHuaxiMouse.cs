namespace PeripheralBattery.Core;

/// <summary>
/// 38EE:0022 battery path from the public Huaxi page on hub.akkogear.com
/// (<c>huaxi/infortech-sdk.esm.js</c>). The WebHID filter is usage page
/// <c>0xFFA8</c> usage <c>0x0002</c>. The output report id is 11.
/// <c>initDevice(false, false)</c> sends <c>GetCurDevType</c> 196, then
/// <c>DevGetDongleLEDEfficacy</c> <c>[152, 0]</c> when that reply says dongle,
/// then <c>IsConnectMouse</c> 208, and only then <c>DevGetInfo</c> <c>[129, 0]</c>.
/// <c>parseRecvType</c> is byte 0. <c>parseRecvState</c> requires byte 2 to be 0.
/// <c>getDataSlice</c> drops the first 3 bytes when the payload is at least 4 bytes.
/// On that slice, charging is offset 13, the level is offset 14, and the full flag
/// is offset 15. A level outside 0–100 is not a percentage.
/// The public page labels <c>0x0022</c> as the receiver connection. Device type
/// is dongle or mouse, not a wired link, so charging stays that byte.
/// <c>parseDevType</c> is payload byte 3: 1 dongle, 2 mouse, 3 dongle boot, 4 mouse boot.
/// The mouse is connected only when <c>getDataSlice</c> byte 0 is 1.
/// Windows <c>ReadFile</c> may prefix a report id. WebHID data starts at the command.
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
    public const byte CurDevTypeCommand = 196;
    public const byte DongleLedCommand = 152;
    public const byte ConnectMouseCommand = 208;
    public const byte DeviceTypeDongle = 1;
    public const byte DeviceTypeMouse = 2;
    public const byte DeviceTypeDongleBoot = 3;
    public const byte DeviceTypeMouseBoot = 4;
    public const int HeaderLength = 3;
    public const int LevelOffset = 14;
    public const int ChargingOffset = 13;
    public const int FullOffset = 15;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoInfoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";
    public const string NoteMouseNotConnected = "接收器没有上报鼠标已连接。";

    public static bool IsCandidate(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public static bool IsInfoCollection(int usagePage, int usage) =>
        usagePage == UsagePage && usage == Usage;

    /// <summary>
    /// <c>queryCurDevTypeCmd</c> returns <c>[GetCurDevType]</c>.
    /// </summary>
    public static byte[] CurDevTypeRequest() => [CurDevTypeCommand];

    /// <summary>
    /// <c>queryDGAmbientCfgCmd</c> returns <c>[DevGetDongleLEDEfficacy, 0]</c>.
    /// </summary>
    public static byte[] DongleLedRequest() => [DongleLedCommand, 0];

    /// <summary>
    /// <c>queryIsConnectMouseCmd</c> returns <c>[IsConnectMouse]</c>.
    /// </summary>
    public static byte[] ConnectMouseRequest() => [ConnectMouseCommand];

    /// <summary>
    /// <c>queryDeviceInfoCmd</c> returns <c>[DevGetInfo, 0]</c>.
    /// </summary>
    public static byte[] InfoRequest() => [InfoCommand, 0];

    /// <summary>
    /// The mouse collection is <c>MI_00</c>. It is never opened.
    /// </summary>
    public static bool IsMouseInterfacePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var lower = path.ToLowerInvariant();
        var index = 0;
        while ((index = lower.IndexOf("mi_00", index, StringComparison.Ordinal)) >= 0)
        {
            var after = index + 5;
            if (after >= lower.Length || !IsHex(lower[after]))
                return true;
            index = after;
        }

        return false;
    }

    public static bool TryParseDeviceType(ReadOnlySpan<byte> report, out HuaxiDeviceType type)
    {
        type = HuaxiDeviceType.Unknown;
        if (!TryExtractPayload(report, CurDevTypeCommand, out var payload) || payload.Length <= HeaderLength)
            return false;
        type = payload[HeaderLength] switch
        {
            DeviceTypeDongle => HuaxiDeviceType.Dongle,
            DeviceTypeMouse => HuaxiDeviceType.Mouse,
            DeviceTypeDongleBoot => HuaxiDeviceType.DongleBoot,
            DeviceTypeMouseBoot => HuaxiDeviceType.MouseBoot,
            _ => HuaxiDeviceType.Unknown,
        };
        return true;
    }

    /// <summary>
    /// <c>parseSleepResult</c> is <c>getDataSlice(payload)[0] === 1</c>.
    /// </summary>
    public static bool TryParseMouseConnected(ReadOnlySpan<byte> report, out bool connected)
    {
        connected = false;
        if (!TryExtractPayload(report, ConnectMouseCommand, out var payload) || payload.Length <= HeaderLength)
            return false;
        connected = payload[HeaderLength] == 1;
        return true;
    }

    public static bool TryParseInfoReport(ReadOnlySpan<byte> report, out HuaxiInfo info)
    {
        if (TryParseInfoPayload(report, out info))
            return true;
        if (report.Length > 1 && TryParseInfoPayload(report[1..], out info))
            return true;
        info = default;
        return false;
    }

    /// <summary>
    /// Official payload, or the same payload after a Windows report-id byte.
    /// <c>parseRecvType</c> is the command and <c>parseRecvState</c> requires byte 2 to be 0.
    /// </summary>
    public static bool TryExtractPayload(ReadOnlySpan<byte> report, byte command, out ReadOnlySpan<byte> payload)
    {
        if (IsCommandPayload(report, command))
        {
            payload = report;
            return true;
        }

        if (report.Length > 1 && IsCommandPayload(report[1..], command))
        {
            payload = report[1..];
            return true;
        }

        payload = default;
        return false;
    }

    private static bool IsCommandPayload(ReadOnlySpan<byte> payload, byte command) =>
        payload.Length >= HeaderLength && payload[0] == command && payload[2] == 0;

    private static bool IsHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

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

public enum HuaxiDeviceType
{
    Unknown = 0,
    Dongle = 1,
    Mouse = 2,
    DongleBoot = 3,
    MouseBoot = 4,
}
