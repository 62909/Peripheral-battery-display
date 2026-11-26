using System.Globalization;
using System.Windows.Media;
using System.Windows.Shapes;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

/// <summary>
/// Filled device marks for the taskbar pill. Holes use even-odd fill so the
/// pill background shows through. Unknown and other types share <see cref="DeviceSilhouette.Generic"/>.
/// </summary>
internal static class DeviceGlyph
{
    private static readonly Geometry Mouse = Shape(
        "M 8,0.85 C 11.7,0.85 14.15,3.05 14.25,5.95 L 14.4,10.15 C 14.5,13.25 11.55,15.2 8,15.2 C 4.45,15.2 1.5,13.25 1.6,10.15 L 1.75,5.95 C 1.85,3.05 4.3,0.85 8,0.85 Z "
        + Hole(7.42, 1.45, 1.16, 2.7)
        + RoundedRect(6.85, 4.15, 2.3, 3.55, 1.05));

    private static readonly Geometry Keyboard = Shape(
        RoundedRect(0.55, 3.05, 14.9, 9.9, 1.45)
        + Keys());

    private static readonly Geometry HeadsetBand = Shape(
        "M 2.85,10.35 C 2.85,2.85 13.15,2.85 13.15,10.35 L 11.25,10.35 C 11.25,4.85 4.75,4.85 4.75,10.35 Z ");

    private static readonly Geometry HeadsetLeft = Shape(RoundedRect(1.05, 8.15, 3.85, 6.05, 1.45));
    private static readonly Geometry HeadsetRight = Shape(RoundedRect(11.1, 8.15, 3.85, 6.05, 1.45));

    private static readonly Geometry Generic = Shape(
        RoundedRect(2.25, 2.15, 11.5, 11.7, 2.45)
        + RoundedRect(4.25, 4.15, 7.5, 4.7, 0.8)
        + "M 8,10.05 A 0.95,0.95 0 1 1 7.99,10.05 Z ");

    public static void Draw(System.Windows.Controls.Canvas canvas, DeviceSilhouette silhouette, Brush ink)
    {
        canvas.Children.Clear();
        foreach (var geometry in Parts(silhouette))
        {
            canvas.Children.Add(new Path
            {
                Data = geometry,
                Fill = ink,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
            });
        }
    }

    private static IEnumerable<Geometry> Parts(DeviceSilhouette silhouette)
    {
        switch (silhouette)
        {
            case DeviceSilhouette.Mouse:
                yield return Mouse;
                yield break;
            case DeviceSilhouette.Keyboard:
                yield return Keyboard;
                yield break;
            case DeviceSilhouette.Headset:
                yield return HeadsetBand;
                yield return HeadsetLeft;
                yield return HeadsetRight;
                yield break;
            default:
                yield return Generic;
                yield break;
        }
    }

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

    private static Geometry Shape(string path)
    {
        var geometry = PathGeometry.CreateFromGeometry(Geometry.Parse(path));
        geometry.FillRule = FillRule.EvenOdd;
        geometry.Freeze();
        return geometry;
    }
}
