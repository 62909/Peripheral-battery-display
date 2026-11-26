namespace PeripheralBattery.Core;

/// <summary>
/// Which silhouette the taskbar pill draws for a device type.
/// Unknown and other types share a generic mark so the pill does not guess a mouse.
/// </summary>
public enum DeviceSilhouette
{
    Generic = 0,
    Keyboard = 1,
    Mouse = 2,
    Headset = 3,
}

/// <summary>
/// One device on the taskbar pill: silhouette, the text to draw, and whether
/// that device's own charging flag adds a bolt after the text.
/// </summary>
public readonly record struct PillSlot(
    DeviceSilhouette Silhouette,
    int? Percent,
    string PercentText,
    bool Charging,
    BatteryTone Tone);

/// <summary>
/// Horizontal place of one slot inside the pill, in DIPs from the left edge.
/// <see cref="BoltX"/> is null when that device has no charging bolt.
/// </summary>
public readonly record struct PillSlotBox(
    double X,
    double Width,
    double IconX,
    double TextX,
    double TextWidth,
    double? BoltX,
    PillSlot Slot);

/// <summary>
/// The whole pill. Width grows with <see cref="Slots"/> and includes padding,
/// the border, and a gap between devices. A single slot has no extra trailing gap.
/// </summary>
public sealed record PillStrip(
    IReadOnlyList<PillSlot> Slots,
    IReadOnlyList<PillSlotBox> Boxes,
    double WidthDip,
    double HeightDip,
    double GapDip,
    double BorderDip);

/// <summary>
/// Lays out every device already on the device list, in that order.
/// The tray icon does not use this. It still prints only the lowest percentage.
/// </summary>
public static class PillComposition
{
    public const string UnknownText = "未知";

    public const double HeightDip = 32;
    public const double PadDip = 8;
    public const double IconDip = 16;
    public const double IconTextGapDip = 5;
    public const double TextBoltGapDip = 3;
    public const double BoltWidthDip = 9;
    public const double BoltHeightDip = 13;

    /// <summary>
    /// Space between the end of one device and the start of the next.
    /// Wider than the gap inside a device so the groups stay separate.
    /// </summary>
    public const double DeviceGapDip = 16;

    public const double DigitDip = 8;
    public const double PercentMarkDip = 12;
    public const double CjkDip = 14;

    /// <summary>
    /// Extra room so Semibold YaHei / Segoe digits are not clipped.
    /// </summary>
    public const double TextSlackDip = 2;

    public static DeviceSilhouette SilhouetteFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => DeviceSilhouette.Keyboard,
        DeviceKind.Mouse => DeviceSilhouette.Mouse,
        DeviceKind.Headset => DeviceSilhouette.Headset,
        _ => DeviceSilhouette.Generic,
    };

    public static string PercentText(int? percent) =>
        BatteryMath.NormalizePercent(percent) is int value ? $"{value}%" : UnknownText;

    public static bool ShowCharging(bool? charging) => charging == true;

    public static double TextWidthDip(string text)
    {
        var width = TextSlackDip;
        foreach (var ch in text)
        {
            width += ch == '%'
                ? PercentMarkDip
                : ch is >= '0' and <= '9'
                    ? DigitDip
                    : CjkDip;
        }

        return width;
    }

    public static double SlotWidthDip(PillSlot slot)
    {
        var width = IconDip + IconTextGapDip + TextWidthDip(slot.PercentText);
        if (slot.Charging)
            width += TextBoltGapDip + BoltWidthDip;
        return width;
    }

    /// <summary>
    /// One box per device, in list order. An empty list still yields one generic
    /// 「未知」slot so the pill can sit on the bar before a reading arrives.
    /// That placeholder has no percentage. No battery outline is measured.
    /// </summary>
    public static PillStrip Arrange(IEnumerable<PeripheralDevice> devices, double borderDip = 1)
    {
        if (borderDip < 0 || !double.IsFinite(borderDip))
            borderDip = 1;

        var slots = new List<PillSlot>();
        foreach (var device in devices)
            slots.Add(Describe(device));
        if (slots.Count == 0)
            slots.Add(new PillSlot(DeviceSilhouette.Generic, null, UnknownText, false, BatteryTone.Unknown));

        var boxes = new List<PillSlotBox>(slots.Count);
        var x = borderDip + PadDip;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var textWidth = TextWidthDip(slot.PercentText);
            var width = SlotWidthDip(slot);
            double? boltX = slot.Charging
                ? x + IconDip + IconTextGapDip + textWidth + TextBoltGapDip
                : null;
            boxes.Add(new PillSlotBox(
                x,
                width,
                x,
                x + IconDip + IconTextGapDip,
                textWidth,
                boltX,
                slot));
            x += width;
            if (i < slots.Count - 1)
                x += DeviceGapDip;
        }

        x += PadDip + borderDip;
        return new PillStrip(slots, boxes, x, HeightDip, DeviceGapDip, borderDip);
    }

    private static PillSlot Describe(PeripheralDevice device)
    {
        var level = BatteryMath.NormalizePercent(device.BatteryPercent);
        return new PillSlot(
            SilhouetteFor(device.Kind),
            level,
            PercentText(level),
            ShowCharging(device.Charging),
            BatteryMath.Tone(level));
    }
}
