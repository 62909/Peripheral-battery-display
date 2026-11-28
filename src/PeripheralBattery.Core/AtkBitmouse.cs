namespace PeripheralBattery.Core;

/// <summary>
/// BITMOUSE battery path from the public ATK HUB page
/// (<c>hub.atk.pro</c> 3.2.34, <c>static/filter-CWFKFZdv.js</c>).
/// The filter is vendor <c>0x373B</c>, usage page <c>0xFF05</c>, usage <c>0x0001</c>.
/// <c>sendReport</c> id is 8. The payload is 63 bytes: checksum, <c>0x72</c>,
/// param length, serial, target, command, data length, then the body.
/// Checksum is the low byte of the sum of every byte after it.
/// <c>GetDeviceType</c> is 26 and is sent at target 0. Its body byte is the
/// connect type: 0, 1, 4 and 5 are receivers; 2, 3, 6 and 7 are wired.
/// A receiver then uses target 1. <c>GetBatteryLevel</c> is 7 and
/// <c>GetBatteryChargingStatus</c> is 15. Both set param length 2 and data
/// length 1. A reply has no checksum byte; the hub inserts a leading 0 before
/// reusing the request offsets, so the level is the first data byte.
/// Status 0 is success. Status 2 is ignored and the wait continues.
/// A level outside 0–100 is not a percentage. A shown 0–100 level on a wired
/// type (2, 3, 6, 7) is charging even when <c>GetBatteryChargingStatus</c> is
/// not 1. A receiver stays on that status. An unknown type is not queried.
/// This is not every ATK mouse.
/// </summary>
public static class AtkBitmouse
{
    public const int VendorId = 0x373B;
    public const ushort UsagePage = 0xFF05;
    public const ushort Usage = 0x0001;
    public const byte ReportId = 8;
    public const byte CommandPrefix = 0x72;
    public const byte BatteryCommand = 7;
    public const byte ChargingCommand = 15;
    public const byte DeviceTypeCommand = 26;
    public const byte TargetLocal = 0;
    public const byte TargetMouseBehindReceiver = 1;
    public const int PayloadLength = 63;
    public const byte ParamLength = 2;
    public const byte DataLength = 1;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";
    public const string NoteNoDeviceType = "没有收到设备类型。";
    public const string NoteDeviceTypeUnknown = "设备类型不在官方取值里。";

    public static bool IsCollection(int vendorId, int productId, int usagePage, int usage) =>
        vendorId == VendorId && productId >= 0 && usagePage == UsagePage && usage == Usage;

    public static byte[] DeviceTypeRequest() => CreatePayload(DeviceTypeCommand, TargetLocal);

    public static byte[] BatteryRequest(byte target) => CreatePayload(BatteryCommand, target);

    public static byte[] ChargingRequest(byte target) => CreatePayload(ChargingCommand, target);

    public static byte[] CreatePayload(byte command, byte target)
    {
        var buffer = new byte[PayloadLength];
        buffer[1] = CommandPrefix;
        buffer[2] = ParamLength;
        buffer[4] = target;
        buffer[5] = command;
        buffer[6] = DataLength;
        var sum = 0;
        for (var i = 1; i < buffer.Length; i++)
            sum += buffer[i];
        buffer[0] = (byte)(sum & 0xFF);
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
        var typePhase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            DeviceTypeRequest(),
            report => TryParseDeviceType(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var typeReport);
        if (typePhase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (typePhase != AtkExchangePhase.Accepted
            || typeReport is null
            || !TryParseDeviceType(typeReport, out var kind))
            return new(AtkExchangeStatus.NoDeviceType, default);
        if (kind == AtkBitmouseLinkKind.Unknown)
            return new(AtkExchangeStatus.DeviceTypeUnknown, default);

        var target = kind == AtkBitmouseLinkKind.Dongle ? TargetMouseBehindReceiver : TargetLocal;
        var batteryPhase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            BatteryRequest(target),
            report => TryParseBattery(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var batteryReport);
        if (batteryPhase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (batteryPhase != AtkExchangePhase.Accepted
            || batteryReport is null
            || !TryParseBattery(batteryReport, out var percent))
            return new(AtkExchangeStatus.NoReply, default);
        if (percent is null)
            return new(AtkExchangeStatus.LevelOutOfRange, default);

        bool? charging = null;
        var chargingPhase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            ChargingRequest(target),
            report => TryParseCharging(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var chargingReport);
        if (chargingPhase == AtkExchangePhase.Accepted
            && chargingReport is not null
            && TryParseCharging(chargingReport, out var parsed))
            charging = parsed;

        var wired = kind == AtkBitmouseLinkKind.Wired
            ? true
            : kind == AtkBitmouseLinkKind.Dongle ? false : (bool?)null;
        return new(AtkExchangeStatus.Ok, new AtkBatteryReading(percent, WiredCharging.Apply(charging, wired, percent)));
    }

    public static bool TryParseDeviceType(ReadOnlySpan<byte> report, out AtkBitmouseLinkKind kind)
    {
        kind = AtkBitmouseLinkKind.Unknown;
        if (!TryData(report, DeviceTypeCommand, out var data))
            return false;
        kind = data[0] switch
        {
            0 or 1 or 4 or 5 => AtkBitmouseLinkKind.Dongle,
            2 or 3 or 6 or 7 => AtkBitmouseLinkKind.Wired,
            _ => AtkBitmouseLinkKind.Unknown,
        };
        return true;
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out int? percent)
    {
        percent = null;
        if (!TryData(report, BatteryCommand, out var data))
            return false;
        var level = data[0];
        percent = level <= 100 ? level : null;
        return true;
    }

    /// <summary>
    /// Official <c>chargingStatus === 1</c> is charging. 0 is discharging.
    /// Any other byte is not a flag.
    /// </summary>
    public static bool TryParseCharging(ReadOnlySpan<byte> report, out bool? charging)
    {
        charging = null;
        if (!TryData(report, ChargingCommand, out var data))
            return false;
        charging = data[0] switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
        return true;
    }

    private static bool TryData(ReadOnlySpan<byte> report, byte command, out ReadOnlySpan<byte> data)
    {
        data = default;
        if (!AtkOfficialExchange.TryView(report, ReportId, CommandPrefix, out var view) || view.Length < 7)
            return false;
        if (view[1] != 0 || view[4] != command || view[5] < 1 || view.Length < 6 + view[5])
            return false;
        data = view.Slice(6, view[5]);
        return data.Length > 0;
    }
}

public enum AtkBitmouseLinkKind
{
    Unknown = 0,
    Dongle = 1,
    Wired = 2,
}
