namespace PeripheralBattery.Core;

/// <summary>
/// AKKO 空影 TAN8 Ultra (Echo TAN8 Ultra), from the official mouse driver at
/// driver.akkogear.com (device module M0019-8762+3950-TAN8, productName15).
/// Wired <c>38EE:0023</c> and 2.4G <c>38EE:0024</c> share those USB IDs with
/// 疾风 TAN7 Ultra (cid 1) and 灵动 V9 Ultra / Dash V9 Ultra (cid 5).
/// The vendor collection is <c>FF1C:0092</c>. Identity is command 0x30
/// (wrapped by the driver's 0x01 / 0x02 pair); byte 0x0A is cid. Battery is
/// sent only after cid 2. Payloads below do not include the report id.
/// </summary>
public static class AkkoTan8Ultra
{
    public const int VendorId = 0x38EE;
    public const int WiredProductId = 0x0023;
    public const int DongleProductId = 0x0024;
    public const ushort UsagePage = 0xFF1C;
    public const ushort Usage = 0x0092;
    public const byte ReportId = 0x04;
    public const int PayloadLength = 63;
    public const int ModelCid = 2;
    public const string ModelName = "空影 TAN8 Ultra";

    public const byte StartCommand = 0x01;
    public const byte EndCommand = 0x02;
    public const byte IdentityCommand = 0x30;
    public const byte BatteryCommand = 0x1A;

    /// <summary>
    /// Mouse driver deviceType 1 puts 0x38 in the length byte of the 0x01/0x02 wrapper.
    /// </summary>
    public const byte WrapperLengthByte = 0x38;
    public const byte IdentityLengthByte = 0x10;
    public const byte BatteryLengthByte = 0x06;

    public const int MinimumWrappedResponse = 7;
    public const int MinimumIdentityResponse = 0x0B;
    public const int MinimumBatteryResponse = 9;

    public static bool IsCandidate(int vendorId, int productId) =>
        vendorId == VendorId && productId is WiredProductId or DongleProductId;

    public static bool IsVendorCollection(int usagePage, int usage) =>
        usagePage == UsagePage && usage == Usage;

    public static byte[] StartPayload() => Command(StartCommand, WrapperLengthByte);

    public static byte[] EndPayload() => Command(EndCommand, WrapperLengthByte);

    public static byte[] IdentityPayload() => Command(IdentityCommand, IdentityLengthByte);

    public static byte[] BatteryPayload() => Command(BatteryCommand, BatteryLengthByte);

    /// <summary>
    /// Windows HID reads include the report id in byte 0. The parser indexes the payload after it.
    /// </summary>
    public static bool TryGetPayload(ReadOnlySpan<byte> windowsReport, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (windowsReport.Length < 2 || windowsReport[0] != ReportId)
            return false;
        payload = windowsReport[1..];
        return true;
    }

    public static bool Matches(ReadOnlySpan<byte> payload, byte command, int minimumLength) =>
        payload.Length >= minimumLength
        && payload.Length > 2
        && payload[2] == command;

    public static bool TryReadCid(ReadOnlySpan<byte> payload, out int cid)
    {
        cid = 0;
        if (!Matches(payload, IdentityCommand, MinimumIdentityResponse))
            return false;
        cid = payload[0x0A];
        return true;
    }

    public static bool IsThisModel(int vendorId, int productId, ReadOnlySpan<byte> identityPayload) =>
        IsCandidate(vendorId, productId)
        && TryReadCid(identityPayload, out var cid)
        && cid == ModelCid;

    /// <summary>
    /// Null unless this USB id answered cid 2. Callers must not send <see cref="BatteryPayload"/> otherwise.
    /// </summary>
    public static string? ModelNameFor(int vendorId, int productId, ReadOnlySpan<byte> identityPayload) =>
        IsThisModel(vendorId, productId, identityPayload) ? ModelName : null;

    public static bool AllowsBatteryQuery(int vendorId, int productId, ReadOnlySpan<byte> identityPayload) =>
        ModelNameFor(vendorId, productId, identityPayload) is not null;

    /// <summary>
    /// Byte 7 is the level. Values outside 0–100 are not a percentage and are not clipped.
    /// Byte 8 is the charge byte stored by the official driver with no closed set of values;
    /// it is not turned into a percentage.
    /// </summary>
    public static BatteryReport ParseBattery(ReadOnlySpan<byte> payload)
    {
        if (!Matches(payload, BatteryCommand, MinimumBatteryResponse))
            return default;
        var level = payload[7];
        return new BatteryReport(level <= 100 ? level : null, payload[8]);
    }

    private static byte[] Command(byte command, byte lengthByte)
    {
        var payload = new byte[PayloadLength];
        payload[2] = command;
        payload[3] = lengthByte;
        return payload;
    }
}

public readonly record struct BatteryReport(int? Percent, byte? ChargeState);
