namespace PeripheralBattery.Core;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public readonly record struct TaskbarBounds(int X, int Y, int Width, int Height, TaskbarEdge Edge)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int px, int py) =>
        px >= X && py >= Y && px < X + Width && py < Y + Height;
}

public readonly record struct WidgetSize(int Width, int Height);

/// <summary>
/// The overlay slot last applied on screen. TrayWindow is the tray HWND that
/// owns the popup, or zero when the popup has no tray owner.
/// </summary>
public readonly record struct PlacedWidget(bool Shown, int X, int Y, int Width, int Height, long TrayWindow);

/// <summary>
/// Pure geometry for a window that sits inside the taskbar rectangle.
/// The stored value is a fraction along the bar, so a later edge or monitor
/// change keeps the same relative spot. Pixel positions are physical pixels.
/// </summary>
public static class TaskbarPlacement
{
    public const double DefaultFraction = 0.82;
    public const string PrimaryMonitor = "primary";

    /// <summary>
    /// Cross-axis room for the pill, in DIPs. One taskbar row.
    /// The along-axis length comes from <see cref="PillComposition.Arrange"/>.
    /// </summary>
    public const int PillCrossDip = 32;

    /// <summary>
    /// Fits the pill on the bar. <paramref name="alongDip"/> is the strip width
    /// from <see cref="PillComposition.Arrange"/>. When that strip is longer than
    /// the bar, the widget stays inside the bar and <see cref="ContentScale"/>
    /// drops below 1 so every device remains visible.
    /// </summary>
    public static WidgetSize Fit(TaskbarBounds bar, int dpi, double alongDip) =>
        Measure(bar, dpi, alongDip).Size;

    /// <summary>
    /// The empty pill: one generic 「未知」slot. Callers that know the device
    /// list pass that strip's width instead.
    /// </summary>
    public static WidgetSize Fit(TaskbarBounds bar, int dpi) =>
        Fit(bar, dpi, PillComposition.Arrange([]).WidthDip);

    /// <summary>
    /// 1 when the strip fits on the bar. Below 1 the view scales the whole strip
    /// down so it stays on the bar without hiding a device or scrolling.
    /// </summary>
    public static double ContentScale(TaskbarBounds bar, int dpi, double alongDip) =>
        Measure(bar, dpi, alongDip).Scale;

    private readonly record struct MeasuredFit(WidgetSize Size, double Scale);

    private static MeasuredFit Measure(TaskbarBounds bar, int dpi, double alongDip)
    {
        if (dpi <= 0)
            dpi = 96;
        if (alongDip <= 0 || !double.IsFinite(alongDip))
            alongDip = PillComposition.Arrange([]).WidthDip;
        var scale = dpi / 96.0;
        var margin = Math.Max(2, (int)Math.Round(4 * scale));
        var horizontal = bar.Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
        var cross = Math.Max(0, horizontal ? bar.Height : bar.Width);
        var along = Math.Max(0, horizontal ? bar.Width : bar.Height);
        // Ceiling keeps the window at least as wide as the DIP strip after DPI rounding.
        var preferredAlong = Math.Max(1, (int)Math.Ceiling(alongDip * scale - 1e-9));
        var preferredCross = Math.Max(1, (int)Math.Round(PillCrossDip * scale));
        var crossSize = cross <= margin * 2
            ? Math.Max(1, cross - (cross > 2 ? 2 : 0))
            : Math.Min(preferredCross, Math.Max(1, cross - margin * 2));
        var alongRoom = along > margin * 2 ? along - margin * 2 : Math.Max(1, along);
        var alongSize = Math.Max(1, Math.Min(preferredAlong, alongRoom));
        var contentScale = alongSize >= preferredAlong ? 1d : alongSize / (double)preferredAlong;
        var size = horizontal
            ? new WidgetSize(alongSize, crossSize)
            : new WidgetSize(crossSize, alongSize);
        return new MeasuredFit(size, contentScale);
    }

    public static (int X, int Y) Place(TaskbarBounds bar, WidgetSize widget, double fraction)
    {
        fraction = ClampFraction(fraction);
        var horizontal = bar.Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
        if (horizontal)
        {
            var travel = Math.Max(0, bar.Width - widget.Width);
            var x = bar.X + (int)Math.Round(travel * fraction);
            var y = widget.Height >= bar.Height
                ? bar.Y
                : bar.Y + (bar.Height - widget.Height) / 2;
            return (x, y);
        }

        var travelY = Math.Max(0, bar.Height - widget.Height);
        var yPos = bar.Y + (int)Math.Round(travelY * fraction);
        var xPos = widget.Width >= bar.Width
            ? bar.X
            : bar.X + (bar.Width - widget.Width) / 2;
        return (xPos, yPos);
    }

    public static double FractionFromPointer(
        TaskbarBounds bar,
        WidgetSize widget,
        int pointerX,
        int pointerY,
        double currentFraction)
    {
        var horizontal = bar.Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
        if (horizontal)
        {
            var travel = bar.Width - widget.Width;
            if (travel <= 0)
                return ClampFraction(currentFraction);
            var left = pointerX - widget.Width / 2;
            return ClampFraction((left - bar.X) / (double)travel);
        }

        var travelY = bar.Height - widget.Height;
        if (travelY <= 0)
            return ClampFraction(currentFraction);
        var top = pointerY - widget.Height / 2;
        return ClampFraction((top - bar.Y) / (double)travelY);
    }

    public static bool IsCollapsed(bool autoHide, int visibleThickness, int collapsedMax = 6) =>
        autoHide && visibleThickness <= collapsedMax;

    public static bool ShouldShow(bool userEnabled, bool collapsed, bool fullscreenApp) =>
        userEnabled && !collapsed && !fullscreenApp;

    /// <summary>
    /// True when the overlay is already shown at this slot and owned by the same
    /// tray window. The taskbar is topmost, and a click on its empty space raises
    /// it over every sibling topmost window. An owned popup stays above its owner,
    /// so that click does not cover the pill. Auto-hide and fullscreen are not
    /// part of this comparison.
    /// </summary>
    public static bool SamePlace(PlacedWidget applied, PlacedWidget desired) =>
        applied.Shown && desired.Shown
        && applied.X == desired.X
        && applied.Y == desired.Y
        && applied.Width == desired.Width
        && applied.Height == desired.Height
        && applied.TrayWindow == desired.TrayWindow;

    /// <summary>
    /// Place again when the slot changed or the tray window is above the overlay.
    /// A covering tray is the empty-space click: the bar was raised and the pill
    /// has to be put back above it. Hiding for auto-hide or fullscreen is separate.
    /// </summary>
    public static bool NeedsRaise(PlacedWidget applied, PlacedWidget desired, bool trayCoversOverlay) =>
        (desired.Shown && trayCoversOverlay) || !SamePlace(applied, desired);

    public static int VisibleThickness(
        TaskbarEdge edge,
        int windowX,
        int windowY,
        int windowWidth,
        int windowHeight,
        int monitorX,
        int monitorY,
        int monitorWidth,
        int monitorHeight)
    {
        var winRight = windowX + windowWidth;
        var winBottom = windowY + windowHeight;
        var monRight = monitorX + monitorWidth;
        var monBottom = monitorY + monitorHeight;
        var overlapW = Math.Max(0, Math.Min(winRight, monRight) - Math.Max(windowX, monitorX));
        var overlapH = Math.Max(0, Math.Min(winBottom, monBottom) - Math.Max(windowY, monitorY));
        return edge is TaskbarEdge.Top or TaskbarEdge.Bottom ? overlapH : overlapW;
    }

    public static bool CoversMonitor(
        int windowX,
        int windowY,
        int windowWidth,
        int windowHeight,
        int monitorX,
        int monitorY,
        int monitorWidth,
        int monitorHeight,
        int tolerance = 2)
    {
        return windowX <= monitorX + tolerance
            && windowY <= monitorY + tolerance
            && windowX + windowWidth >= monitorX + monitorWidth - tolerance
            && windowY + windowHeight >= monitorY + monitorHeight - tolerance;
    }

    public static (int X, int Y) ClampToArea(
        int x,
        int y,
        int width,
        int height,
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight)
    {
        if (areaWidth <= 0 || areaHeight <= 0)
            return (x, y);
        if (width >= areaWidth)
            x = areaX;
        else
            x = Math.Clamp(x, areaX, areaX + areaWidth - width);
        if (height >= areaHeight)
            y = areaY;
        else
            y = Math.Clamp(y, areaY, areaY + areaHeight - height);
        return (x, y);
    }

    public static (int X, int Y) PlaceFlyout(
        TaskbarEdge edge,
        int anchorX,
        int anchorY,
        int anchorWidth,
        int anchorHeight,
        int flyoutWidth,
        int flyoutHeight,
        int workX,
        int workY,
        int workWidth,
        int workHeight,
        int gap)
    {
        var (x, y) = edge switch
        {
            TaskbarEdge.Bottom => (anchorX, anchorY - flyoutHeight - gap),
            TaskbarEdge.Top => (anchorX, anchorY + anchorHeight + gap),
            TaskbarEdge.Left => (anchorX + anchorWidth + gap, anchorY),
            _ => (anchorX - flyoutWidth - gap, anchorY),
        };
        return ClampToArea(x, y, flyoutWidth, flyoutHeight, workX, workY, workWidth, workHeight);
    }

    public static (int X, int Y) PlaceNearPoint(
        int pointX,
        int pointY,
        int width,
        int height,
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight,
        int gap)
    {
        return ClampToArea(pointX - width, pointY - height - gap, width, height, areaX, areaY, areaWidth, areaHeight);
    }

    public static double PxToDip(int px, int dpi) => dpi <= 0 ? px : px * 96.0 / dpi;

    public static int DipToPx(double dip, int dpi) =>
        dpi <= 0 ? (int)Math.Round(dip) : (int)Math.Round(dip * dpi / 96.0);

    public static double ClampFraction(double fraction)
    {
        if (!double.IsFinite(fraction))
            return DefaultFraction;
        return Math.Clamp(fraction, 0d, 1d);
    }
}
