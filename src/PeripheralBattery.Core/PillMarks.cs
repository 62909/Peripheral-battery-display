using System.Globalization;

namespace PeripheralBattery.Core;

/// <summary>
/// Filled outlines for the taskbar pill. Holes are extra subpaths; drawers use
/// even-odd fill so the pill background shows through. The same strings feed
/// the WPF pill and any preview that draws those marks.
/// </summary>
public static class PillMarks
{
    /// <summary>
    /// Lightning bolt in a 9×13 box, drawn after a charging device's percentage.
    /// </summary>
    public const string Bolt =
        "M 5.05,0.35 L 0.85,7.15 L 3.85,7.15 L 2.15,12.65 L 8.15,4.85 L 5.35,4.85 Z";

    public static IReadOnlyList<string> Outline(DeviceSilhouette silhouette) => silhouette switch
    {
        DeviceSilhouette.Mouse => Mouse,
        DeviceSilhouette.Keyboard => Keyboard,
        DeviceSilhouette.Headset => Headset,
        _ => Generic,
    };

    private static readonly string[] Mouse =
    [
        "M 8,0.85 C 11.7,0.85 14.15,3.05 14.25,5.95 L 14.4,10.15 C 14.5,13.25 11.55,15.2 8,15.2 C 4.45,15.2 1.5,13.25 1.6,10.15 L 1.75,5.95 C 1.85,3.05 4.3,0.85 8,0.85 Z "
        + Hole(7.42, 1.45, 1.16, 2.7)
        + RoundedRect(6.85, 4.15, 2.3, 3.55, 1.05),
    ];

    private static readonly string[] Keyboard =
    [
        RoundedRect(0.55, 3.05, 14.9, 9.9, 1.45) + Keys(),
    ];

    private static readonly string[] Headset =
    [
        "M 2.85,10.35 C 2.85,2.85 13.15,2.85 13.15,10.35 L 11.25,10.35 C 11.25,4.85 4.75,4.85 4.75,10.35 Z ",
        RoundedRect(1.05, 8.15, 3.85, 6.05, 1.45),
        RoundedRect(11.1, 8.15, 3.85, 6.05, 1.45),
    ];

    private static readonly string[] Generic =
    [
        RoundedRect(2.25, 2.15, 11.5, 11.7, 2.45)
        + RoundedRect(4.25, 4.15, 7.5, 4.7, 0.8)
        + "M 8,10.05 A 0.95,0.95 0 1 1 7.99,10.05 Z ",
    ];

    private static string Keys()
    {
        var path = "";
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 4; column++)
                path += Hole(2.15 + column * 3.05, 4.55 + row * 3.15, 2.15, 2.05);
        }

        return path;
    }

    private static string Hole(double x, double y, double width, double height) =>
        RoundedRect(x, y, width, height, Math.Min(0.45, Math.Min(width, height) / 2));

    private static string RoundedRect(double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        var right = x + width;
        var bottom = y + height;
        var culture = CultureInfo.InvariantCulture;
        return string.Format(
            culture,
            "M {0},{1} H {2} A {3},{3} 0 0 1 {4},{5} V {6} A {3},{3} 0 0 1 {7},{8} H {9} A {3},{3} 0 0 1 {10},{11} V {12} A {3},{3} 0 0 1 {0},{1} Z ",
            x + radius,
            y,
            right - radius,
            radius,
            right,
            y + radius,
            bottom - radius,
            right - radius,
            bottom,
            x + radius,
            x,
            bottom - radius,
            y + radius);
    }
}
