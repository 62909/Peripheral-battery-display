using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

/// <summary>
/// Draws <see cref="PillMarks"/> into the taskbar pill and the device list.
/// Unknown and other types share <see cref="DeviceSilhouette.Generic"/>.
/// </summary>
internal static class DeviceGlyph
{
    /// <summary>
    /// The charging bolt, shared by the taskbar pill and the device list.
    /// </summary>
    public static Geometry Bolt { get; } = Shape(PillMarks.Bolt);

    private static readonly Dictionary<DeviceSilhouette, Geometry[]> Cache = new();

    public static void Draw(Canvas canvas, DeviceSilhouette silhouette, Brush ink)
    {
        canvas.Children.Clear();
        foreach (var geometry in Geometries(silhouette))
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

    private static Geometry[] Geometries(DeviceSilhouette silhouette)
    {
        if (Cache.TryGetValue(silhouette, out var cached))
            return cached;

        var built = PillMarks.Outline(silhouette).Select(Shape).ToArray();
        Cache[silhouette] = built;
        return built;
    }

    private static Geometry Shape(string path)
    {
        var geometry = PathGeometry.CreateFromGeometry(Geometry.Parse(path));
        geometry.FillRule = FillRule.EvenOdd;
        geometry.Freeze();
        return geometry;
    }
}
