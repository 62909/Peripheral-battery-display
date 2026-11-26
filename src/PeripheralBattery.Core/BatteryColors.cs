namespace PeripheralBattery.Core;

/// <summary>
/// sRGB colors shared by the device list, the taskbar pill, and previews.
/// Tray-icon pixels stay in the app painter and are not these values.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string Hex => FormattableString.Invariant($"#{R:X2}{G:X2}{B:X2}");
}

public static class BatteryColors
{
    public static RgbColor Background(bool light) => light ? new(247, 247, 247) : new(32, 32, 32);
    public static RgbColor Text(bool light) => light ? new(28, 28, 28) : new(240, 240, 240);
    public static RgbColor Muted(bool light) => light ? new(92, 92, 92) : new(176, 176, 176);
    public static RgbColor Line(bool light) => light ? new(214, 214, 214) : new(68, 68, 68);
    public static RgbColor Hover(bool light) => light ? new(232, 232, 232) : new(48, 48, 48);
    public static RgbColor Accent => new(0, 120, 212);

    public static RgbColor Tone(BatteryTone tone, bool light) => tone switch
    {
        BatteryTone.Ok => light ? new(14, 124, 74) : new(126, 214, 160),
        BatteryTone.Low => light ? new(156, 90, 0) : new(242, 184, 75),
        BatteryTone.Critical => light ? new(186, 42, 36) : new(255, 128, 112),
        _ => Muted(light),
    };
}
