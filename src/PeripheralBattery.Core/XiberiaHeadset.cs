namespace PeripheralBattery.Core;

/// <summary>
/// One input or output report on a Xiberia headset collection.
/// <paramref name="BitCount"/> is null when the report id is present and the
/// official filter has no item list to sum. A known count has to be 504.
/// </summary>
public readonly record struct XiberiaReport(bool Present, int? BitCount);

/// <summary>
/// Headset battery path from the public XIBERIA HUB
/// (<c>hub.xiberia.net</c>, 2026-11-28).
/// Desktop <c>selectedHeadsetSession-DRlw0-zg.js</c> calls this family
/// <c>legacy-55</c>. <c>device-core-ISUoM2Rb.js</c> calls the same reports
/// <c>usb-audio-k06s</c>, and <c>deviceConnectionStore-llQZnRTG.js</c> sends
/// the battery command. The name on screen is the HID product string.
/// Vendors are <c>0x36F9</c>, <c>0x37D3</c>, and <c>0x10D6</c>.
/// The collection carries report id 85 (<c>0x55</c>), 504 bits, 63 bytes.
/// Some models also require usage page <c>0xFF90</c> usage 1.
/// K03S, T7 wireless, and T7 GT may have input 85 and no output reports;
/// K06S has the same input-only case in its own filter.
/// A normal frame is <c>[0x10, cmd, len, payload…]</c> padded to 63 bytes.
/// Version command <c>0x5E</c> has no <c>0x10</c> prefix and is not read here.
/// Command 9 (battery) sends <c>[0]</c>, so the frame is <c>10 09 01 00…</c>.
/// After the header, payload byte 0 is the percentage.
/// Command 1 (device info) sends an empty payload, frame <c>10 01 00…</c>.
/// Replies are accepted with or without a leading <c>0x55</c>, and as a short
/// form that starts at command <c>0x01</c>. Unpacked byte 0 is connection
/// status: 0 is disconnected, and any other accepted value only means
/// connected. Bytes 1 and 2 are not relabeled as USB, 2.4G, or Bluetooth.
/// Unpacked byte 3 is the level, and only when status is not 0 and the value
/// is at most 100. Status above 2 is not a status block.
/// This protocol has no charging field. A level outside 0–100 stays unknown.
/// T10 wired <c>0x36F9:0xD003</c> is <c>t10-lxsp</c> and is not queried.
/// <c>0x10D6:0x4801</c> is K03S wired or WG500S, chosen by the product name.
/// </summary>
public static class XiberiaHeadset
{
    public const int VendorPrimary = 0x36F9;
    public const int VendorAlternate = 0x37D3;
    public const int VendorShared = 0x10D6;

    public const int K06SWirelessProductId = 0xF002;
    public const int K06SProductId = 0xD001;
    public const int K06SAlternateProductId = 0xD2C9;
    public const int T10WirelessProductId = 0xF001;
    public const int T10WiredProductId = 0xD003;
    public const int T7WirelessProductId = 0xF008;
    public const int T7WiredProductId = 0xF014;
    public const int T7GtProductId = 0xF009;
    public const int K03SWirelessProductId = 0xF016;
    public const int SharedProductId = 0x4801;
    public const int Wg600SProductId = 0xC006;
    public const int S19GlProductId = 0xC011;

    public const ushort UsagePage = 0xFF90;
    public const ushort Usage = 1;
    public const byte ReportId = 85;
    public const int ReportBits = 504;
    public const int FrameLength = 63;
    public const byte FramePrefix = 0x10;
    public const byte BatteryCommand = 9;
    public const byte DeviceInfoCommand = 1;
    public const byte VersionCommand = 0x5E;
    public const int StatusLength = 11;
    public const int DisconnectedLength = 8;
    public const byte Disconnected = 0;
    public const byte ConnectedMax = 2;

    public const string NoteInterfaceBusy = "接口被占用。请先退出官方驱动，再点刷新。";
    public const string NoteSendFailed = "查询没有发出去。";
    public const string NoteNoReply = "没有收到电量报告。";
    public const string NoteLevelOutOfRange = "电量不在 0–100。";

    public static bool IsCandidate(int vendorId, int productId) =>
        vendorId == VendorShared && productId == SharedProductId
        || ModelFor(vendorId, productId, null) is not null;

    public static bool IsCollection(
        int vendorId,
        int productId,
        string? productName,
        int usagePage,
        int usage,
        XiberiaReport input,
        XiberiaReport output,
        bool hasOutputReports)
    {
        var model = ModelFor(vendorId, productId, productName);
        if (model is null)
            return false;
        var vendorUsage = usagePage == UsagePage && usage == Usage;
        return model switch
        {
            Model.K06S or Model.T7Wireless or Model.T7Gt =>
                ReportOk(input) && (ReportOk(output) || vendorUsage && !hasOutputReports),
            Model.K03SWireless or Model.K03SWired =>
                vendorUsage && ReportOk(input) && (ReportOk(output) || !hasOutputReports),
            Model.T10Wireless =>
                vendorUsage && ReportOk(input) && ReportOk(output),
            Model.T7Wired or Model.Wg600S or Model.Wg500S or Model.S19Gl =>
                ReportOk(input) && ReportOk(output),
            _ => false,
        };
    }

    public static byte[] BatteryRequest() => CommandFrame(BatteryCommand, [0]);

    public static byte[] DeviceInfoRequest() => CommandFrame(DeviceInfoCommand, []);

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out int? percent)
    {
        percent = null;
        if (!TryUnwrapFrame(report, out var command, out var payload) || command != BatteryCommand || payload.IsEmpty)
            return false;
        if (payload[0] <= 100)
            percent = payload[0];
        return true;
    }

    /// <summary>
    /// True when the reply is a command 1 status block.
    /// <paramref name="levelRejected"/> is set when a connected headset
    /// reported a level above 100. Disconnected status does not.
    /// </summary>
    public static bool TryParseDeviceInfo(ReadOnlySpan<byte> report, out int? percent, out bool levelRejected)
    {
        percent = null;
        levelRejected = false;
        ReadOnlySpan<byte> status;
        if (TryUnwrapFrame(report, out var command, out var payload))
        {
            if (command != DeviceInfoCommand)
                return false;
            status = payload;
        }
        else if (!TryStripHeader(report, out status))
        {
            return false;
        }

        return TryReadStatus(status, out percent, out levelRejected);
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
            static report => TryParseBattery(report, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out var report);
        if (phase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);

        var batteryOutOfRange = false;
        if (phase == AtkExchangePhase.Accepted && report is not null && TryParseBattery(report, out var percent))
        {
            if (percent is not null)
                return new(AtkExchangeStatus.Ok, new AtkBatteryReading(percent, null));
            batteryOutOfRange = true;
        }

        phase = AtkOfficialExchange.Send(
            link,
            inputReportByteLength,
            outputReportByteLength,
            ReportId,
            DeviceInfoRequest(),
            static report => TryParseDeviceInfo(report, out _, out _),
            cancellationToken,
            nowMs,
            timeoutMs,
            out report);
        if (phase == AtkExchangePhase.SendFailed)
            return new(AtkExchangeStatus.SendFailed, default);
        if (phase == AtkExchangePhase.Accepted
            && report is not null
            && TryParseDeviceInfo(report, out var infoPercent, out var rejected))
        {
            if (infoPercent is not null)
                return new(AtkExchangeStatus.Ok, new AtkBatteryReading(infoPercent, null));
            if (rejected || batteryOutOfRange)
                return new(AtkExchangeStatus.LevelOutOfRange, new AtkBatteryReading(null, null));
            return new(AtkExchangeStatus.NoReply, default);
        }

        if (batteryOutOfRange)
            return new(AtkExchangeStatus.LevelOutOfRange, new AtkBatteryReading(null, null));
        return new(AtkExchangeStatus.NoReply, default);
    }

    private static bool ReportOk(XiberiaReport report) =>
        report.Present && report.BitCount is null or ReportBits;

    private static Model? ModelFor(int vendorId, int productId, string? productName)
    {
        if (vendorId == VendorPrimary)
        {
            return productId switch
            {
                K06SWirelessProductId or K06SProductId => Model.K06S,
                T10WirelessProductId => Model.T10Wireless,
                T7WirelessProductId => Model.T7Wireless,
                T7WiredProductId => Model.T7Wired,
                T7GtProductId => Model.T7Gt,
                K03SWirelessProductId => Model.K03SWireless,
                Wg600SProductId => Model.Wg600S,
                S19GlProductId => Model.S19Gl,
                _ => null,
            };
        }

        if (vendorId == VendorAlternate && productId == K06SAlternateProductId)
            return Model.K06S;
        if (vendorId != VendorShared || productId != SharedProductId)
            return null;
        if (IsK03SWiredName(productName))
            return Model.K03SWired;
        if (IsWg500SName(productName))
            return Model.Wg500S;
        return null;
    }

    /// <summary>
    /// Official desktop name gate for wired K03S:
    /// <c>/^(?:XIBERIA\s+)?K03S(?:\s+WIRED)?$/i</c> after trim.
    /// </summary>
    private static bool IsK03SWiredName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return false;
        var text = productName.Trim();
        if (text.StartsWith("XIBERIA", StringComparison.OrdinalIgnoreCase))
        {
            var rest = text["XIBERIA".Length..];
            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
                return false;
            text = rest.TrimStart();
        }

        if (text.Equals("K03S", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!text.StartsWith("K03S", StringComparison.OrdinalIgnoreCase))
            return false;
        var suffix = text["K03S".Length..];
        return suffix.Length > 0
            && char.IsWhiteSpace(suffix[0])
            && suffix.Trim().Equals("WIRED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Official web classifier: trim, drop whitespace, ignore case, then
    /// <c>includes("WG500S")</c>. VID and PID alone do not select this model.
    /// </summary>
    private static bool IsWg500SName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return false;
        Span<char> buffer = stackalloc char[productName.Length];
        var written = 0;
        foreach (var ch in productName.Trim())
        {
            if (char.IsWhiteSpace(ch))
                continue;
            buffer[written++] = char.ToUpperInvariant(ch);
        }

        return ((ReadOnlySpan<char>)buffer[..written]).Contains("WG500S", StringComparison.Ordinal);
    }

    private static byte[] CommandFrame(byte command, ReadOnlySpan<byte> payload)
    {
        if (command == VersionCommand || payload.Length > FrameLength - 3)
            return [];
        var frame = new byte[FrameLength];
        frame[0] = FramePrefix;
        frame[1] = command;
        frame[2] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(3));
        return frame;
    }

    private static bool TryUnwrapFrame(ReadOnlySpan<byte> report, out byte command, out ReadOnlySpan<byte> payload)
    {
        command = 0;
        payload = default;
        var body = report;
        if (body.Length >= FrameLength + 1 && body[0] == ReportId)
            body = body[1..];
        if (body.Length < FrameLength)
            return false;
        var frame = body[..FrameLength];
        if (frame[0] == VersionCommand)
        {
            command = VersionCommand;
            payload = frame[1..];
            return true;
        }

        if (frame[0] == FramePrefix)
        {
            var length = frame[2];
            if (length > FrameLength - 3)
                return false;
            command = frame[1];
            payload = frame.Slice(3, length);
            return true;
        }

        if (frame[0] != DeviceInfoCommand)
            return false;
        var shortLength = frame[1];
        if (shortLength > FrameLength - 2)
            return false;
        command = DeviceInfoCommand;
        payload = frame.Slice(2, shortLength);
        return true;
    }

    private static bool TryStripHeader(ReadOnlySpan<byte> report, out ReadOnlySpan<byte> status)
    {
        status = default;
        if (report.Length >= 15
            && report[0] == ReportId
            && report[1] == FramePrefix
            && report[2] == DeviceInfoCommand
            && TryTake(report, 4, report[3], out status))
            return true;
        if (report.Length >= 14
            && report[0] == FramePrefix
            && report[1] == DeviceInfoCommand
            && TryTake(report, 3, report[2], out status))
            return true;
        return IsShortCommand(report) && TryTake(report, 2, report[1], out status);
    }

    private static bool IsShortCommand(ReadOnlySpan<byte> report)
    {
        if (report.Length < 13 || report[0] != DeviceInfoCommand)
            return false;
        var length = report[1];
        return report[2] <= ConnectedMax && report[6] <= 3 && length <= report.Length - 2;
    }

    private static bool TryTake(ReadOnlySpan<byte> buffer, int offset, int length, out ReadOnlySpan<byte> slice)
    {
        if (length < 0 || offset > buffer.Length || length > buffer.Length - offset)
        {
            slice = default;
            return false;
        }

        slice = buffer.Slice(offset, length);
        return true;
    }

    private static bool TryReadStatus(ReadOnlySpan<byte> status, out int? percent, out bool levelRejected)
    {
        // Byte 0 is the connection status, including 1. It is not a second
        // command header, and 1 is not renamed to a transport.
        percent = null;
        levelRejected = false;
        if (status.Length == DisconnectedLength && status[0] == Disconnected)
            return true;
        if (status.Length < StatusLength || status[0] > ConnectedMax)
            return false;
        if (status[0] == Disconnected)
            return true;
        if (status[3] <= 100)
        {
            percent = status[3];
            return true;
        }

        levelRejected = true;
        return true;
    }

    private enum Model
    {
        K06S,
        T10Wireless,
        T7Wireless,
        T7Wired,
        T7Gt,
        K03SWireless,
        K03SWired,
        Wg600S,
        Wg500S,
        S19Gl,
    }
}
