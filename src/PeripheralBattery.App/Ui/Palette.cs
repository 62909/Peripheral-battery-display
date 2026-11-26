using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

internal readonly record struct Palette(bool Light)
{
    public Color Background => ColorOf(BatteryColors.Background(Light));
    public Color Text => ColorOf(BatteryColors.Text(Light));
    public Color Muted => ColorOf(BatteryColors.Muted(Light));
    public Color Line => ColorOf(BatteryColors.Line(Light));
    public Color Hover => ColorOf(BatteryColors.Hover(Light));
    public Color Accent => ColorOf(BatteryColors.Accent);

    public SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public SolidColorBrush Tone(BatteryTone tone) => Brush(ColorOf(BatteryColors.Tone(tone, Light)));

    private static Color ColorOf(RgbColor color) => Color.FromRgb(color.R, color.G, color.B);
}

internal static class SystemTheme
{
    public static bool TaskbarIsLight() => Read("SystemUsesLightTheme");
    public static bool AppsAreLight() => Read("AppsUseLightTheme");

    private static bool Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
