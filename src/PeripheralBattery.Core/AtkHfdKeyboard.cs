namespace PeripheralBattery.Core;

/// <summary>
/// HFD keyboard battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>useDevice</c> controller <c>ts</c>).
/// The running collection is vendor <c>0x373B</c>, usage page <c>0xFF60</c>,
/// usage <c>0x0061</c>, and only the product ids registered on that controller.
/// Report id is 0 and the payload is 32 bytes. The command is
/// <c>id_get_keyboard_value</c> 2, then <c>customId</c> 150, then <c>battery</c> 36.
/// The level is the next byte and the charge byte follows it.
/// The hub keeps the level only when the charge byte is 0. Any other charge
/// byte is charging and the level is not a percentage. BITYUAN keyboards use
/// the same usage page and are not this controller; they are not matched.
/// </summary>
public static class AtkHfdKeyboard
{
    public const int VendorId = 0x373B;
    public const ushort UsagePage = 0xFF60;
    public const ushort Usage = 0x0061;
    public const byte ReportId = 0;
    public const byte ValueCommand = 2;
    public const byte CustomId = 150;
    public const byte BatterySubcommand = 36;
    public const int PayloadLength = 32;
    public const int LevelOffset = 3;
    public const int ChargeOffset = 4;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";
    public const string NoteChargingWithoutLevel = "正在充电，官方回报里没有电量百分比。";

    /// <summary>
    /// Decimal ids on controller <c>ts</c>: 4382, 4383, 4657, 4429, 4430, 4433, 4434.
    /// </summary>
    private static readonly HashSet<int> ProductIds =
    [
        0x111E,
        0x111F,
        0x1231,
        0x114D,
        0x114E,
        0x1151,
        0x1152,
    ];

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId && ProductIds.Contains(productId) && usagePage == UsagePage && usage == Usage;

    public static byte[] BatteryRequest()
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = ValueCommand;
        buffer[1] = CustomId;
        buffer[2] = BatterySubcommand;
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
        if (charging == true)
            return new(AtkExchangeStatus.ChargingWithoutLevel, new AtkBatteryReading(null, true));
        if (percent is null)
            return new(AtkExchangeStatus.LevelOutOfRange, new AtkBatteryReading(null, charging));
        return new(AtkExchangeStatus.Ok, new AtkBatteryReading(percent, charging));
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out int? percent, out bool? charging)
    {
        percent = null;
        charging = null;
        if (!AtkOfficialExchange.TryView(report, ReportId, ValueCommand, out var view) || view.Length <= ChargeOffset)
            return false;
        if (view[1] != CustomId || view[2] != BatterySubcommand)
            return false;
        if (view[ChargeOffset] != 0)
        {
            charging = true;
            return true;
        }

        var level = view[LevelOffset];
        percent = level <= 100 ? level : null;
        charging = false;
        return true;
    }
}
