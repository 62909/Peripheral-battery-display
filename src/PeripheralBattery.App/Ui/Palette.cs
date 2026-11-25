using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

internal readonly record struct Palette(bool Light)
{
    public Color Background => Light ? Color.FromRgb(247, 247, 247) : Color.FromRgb(32, 32, 32);
    public Color Text => Light ? Color.FromRgb(28, 28, 28) : Color.FromRgb(240, 240, 240);
    public Color Muted => Light ? Color.FromRgb(92, 92, 92) : Color.FromRgb(176, 176, 176);
    public Color Line => Light ? Color.FromRgb(214, 214, 214) : Color.FromRgb(68, 68, 68);
    public Color Hover => Light ? Color.FromRgb(232, 232, 232) : Color.FromRgb(48, 48, 48);
    public Color Accent => Color.FromRgb(0, 120, 212);

    public SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public SolidColorBrush Tone(BatteryTone tone)
    {
        var color = tone switch
        {
            BatteryTone.Ok => Light ? Color.FromRgb(14, 124, 74) : Color.FromRgb(126, 214, 160),
            BatteryTone.Low => Light ? Color.FromRgb(156, 90, 0) : Color.FromRgb(242, 184, 75),
            BatteryTone.Critical => Light ? Color.FromRgb(186, 42, 36) : Color.FromRgb(255, 128, 112),
            _ => Muted,
        };
        return Brush(color);
    }
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
