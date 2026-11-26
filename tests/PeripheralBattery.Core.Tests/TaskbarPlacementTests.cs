using Xunit;

namespace PeripheralBattery.Core.Tests;

public class TaskbarPlacementTests
{
    [Fact]
    public void Default_fraction_sits_toward_the_end_of_a_bottom_bar()
    {
        var bar = new TaskbarBounds(0, 1040, 1920, 40, TaskbarEdge.Bottom);
        var widget = TaskbarPlacement.Fit(bar, 96);
        Assert.Equal(112, widget.Width);
        Assert.Equal(32, widget.Height);
        var (x, y) = TaskbarPlacement.Place(bar, widget, TaskbarPlacement.DefaultFraction);
        Assert.Equal(1040 + 4, y);
        Assert.InRange(x, 1400, 1600);
        Assert.True(x + widget.Width <= bar.X + bar.Width);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void Fraction_zero_and_one_stay_inside(TaskbarEdge edge)
    {
        var bar = edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? new TaskbarBounds(0, 0, 48, 1080, edge)
            : new TaskbarBounds(0, 0, 1920, 48, edge);
        if (edge == TaskbarEdge.Right)
            bar = new TaskbarBounds(1872, 0, 48, 1080, edge);
        if (edge == TaskbarEdge.Bottom)
            bar = new TaskbarBounds(0, 1032, 1920, 48, edge);

        var widget = TaskbarPlacement.Fit(bar, 96);
        var start = TaskbarPlacement.Place(bar, widget, 0);
        var end = TaskbarPlacement.Place(bar, widget, 1);
        Assert.InRange(start.X, bar.X, bar.X + bar.Width - widget.Width);
        Assert.InRange(start.Y, bar.Y, bar.Y + bar.Height - widget.Height);
        Assert.InRange(end.X, bar.X, bar.X + bar.Width - widget.Width);
        Assert.InRange(end.Y, bar.Y, bar.Y + bar.Height - widget.Height);
        if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
        {
            Assert.Equal(bar.X, start.X);
            Assert.Equal(bar.X + bar.Width - widget.Width, end.X);
        }
        else
        {
            Assert.Equal(bar.Y, start.Y);
            Assert.Equal(bar.Y + bar.Height - widget.Height, end.Y);
        }
    }

    [Fact]
    public void Moving_the_bar_keeps_the_fraction()
    {
        var bottom = new TaskbarBounds(0, 1040, 1000, 40, TaskbarEdge.Bottom);
        var widget = new WidgetSize(100, 28);
        var fraction = TaskbarPlacement.FractionFromPointer(bottom, widget, 550, 1060, 0.2);
        var placed = TaskbarPlacement.Place(bottom, widget, fraction);

        var left = new TaskbarBounds(0, 0, 40, 800, TaskbarEdge.Left);
        var followed = TaskbarPlacement.Place(left, new WidgetSize(28, 100), fraction);
        var expectedY = (int)Math.Round((800 - 100) * fraction);
        Assert.Equal(expectedY, followed.Y);
        Assert.Equal(6, followed.X);
        Assert.NotEqual(placed.X, followed.X);
    }

    [Fact]
    public void Negative_monitor_origin_is_preserved()
    {
        var bar = new TaskbarBounds(-1920, 1040, 1920, 40, TaskbarEdge.Bottom);
        var widget = new WidgetSize(80, 30);
        var (x, y) = TaskbarPlacement.Place(bar, widget, 0);
        Assert.Equal(-1920, x);
        Assert.Equal(1045, y);
    }

    [Fact]
    public void Pointer_fraction_clamps_at_both_ends()
    {
        var bar = new TaskbarBounds(0, 1000, 500, 40, TaskbarEdge.Top);
        var widget = new WidgetSize(100, 20);
        Assert.Equal(0, TaskbarPlacement.FractionFromPointer(bar, widget, 0, 1010, 0.4));
        Assert.Equal(1, TaskbarPlacement.FractionFromPointer(bar, widget, 2000, 1010, 0.4));
    }

    [Fact]
    public void Empty_taskbar_click_keeps_an_owned_overlay()
    {
        Assert.True(TaskbarPlacement.ShouldShow(true, collapsed: false, fullscreenApp: false));
        Assert.False(TaskbarPlacement.ShouldShow(true, collapsed: true, fullscreenApp: false));
        Assert.False(TaskbarPlacement.ShouldShow(true, collapsed: false, fullscreenApp: true));

        var owned = new PlacedWidget(true, 1500, 1044, 84, 32, 0x1200);
        Assert.False(TaskbarPlacement.NeedsRaise(owned, owned, trayCoversOverlay: false));

        var unowned = owned with { TrayWindow = 0 };
        Assert.True(TaskbarPlacement.NeedsRaise(unowned, owned, trayCoversOverlay: false));
        Assert.True(TaskbarPlacement.NeedsRaise(owned, owned, trayCoversOverlay: true));
        Assert.True(TaskbarPlacement.NeedsRaise(owned, owned with { TrayWindow = 0x3400 }, trayCoversOverlay: false));
        Assert.True(TaskbarPlacement.NeedsRaise(owned, owned with { X = 20 }, trayCoversOverlay: false));
        Assert.True(TaskbarPlacement.NeedsRaise(owned with { Shown = false }, owned, trayCoversOverlay: false));

        var floating = owned with { TrayWindow = 0 };
        Assert.False(TaskbarPlacement.NeedsRaise(floating, floating, trayCoversOverlay: false));
        Assert.True(TaskbarPlacement.NeedsRaise(floating, floating, trayCoversOverlay: true));
    }

    [Fact]
    public void Auto_hide_and_fullscreen_hide_the_widget()
    {
        Assert.True(TaskbarPlacement.IsCollapsed(autoHide: true, visibleThickness: 2));
        Assert.False(TaskbarPlacement.IsCollapsed(autoHide: true, visibleThickness: 40));
        Assert.False(TaskbarPlacement.IsCollapsed(autoHide: false, visibleThickness: 2));
        Assert.False(TaskbarPlacement.ShouldShow(true, collapsed: true, fullscreenApp: false));
        Assert.False(TaskbarPlacement.ShouldShow(true, collapsed: false, fullscreenApp: true));
        Assert.True(TaskbarPlacement.ShouldShow(true, collapsed: false, fullscreenApp: false));
        Assert.False(TaskbarPlacement.ShouldShow(false, collapsed: false, fullscreenApp: false));
    }

    [Fact]
    public void Visible_thickness_uses_the_real_window_not_the_reported_slot()
    {
        var collapsed = TaskbarPlacement.VisibleThickness(
            TaskbarEdge.Bottom, 0, 1078, 1920, 2, 0, 0, 1920, 1080);
        var shown = TaskbarPlacement.VisibleThickness(
            TaskbarEdge.Bottom, 0, 1040, 1920, 40, 0, 0, 1920, 1080);
        Assert.Equal(2, collapsed);
        Assert.Equal(40, shown);
        Assert.True(TaskbarPlacement.CoversMonitor(0, 0, 1920, 1080, 0, 0, 1920, 1080));
        Assert.False(TaskbarPlacement.CoversMonitor(10, 10, 800, 600, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Flyout_opens_off_the_bar_and_clamps_into_the_work_area()
    {
        var (x, y) = TaskbarPlacement.PlaceFlyout(
            TaskbarEdge.Bottom, 1800, 1040, 84, 32, 320, 400, 0, 0, 1920, 1040, 8);
        Assert.Equal(1600, x);
        Assert.Equal(632, y);

        var top = TaskbarPlacement.PlaceFlyout(
            TaskbarEdge.Top, 10, 0, 84, 40, 200, 100, 0, 40, 800, 600, 4);
        Assert.Equal(10, top.X);
        Assert.Equal(44, top.Y);
    }

    [Fact]
    public void Dip_conversion_rounds_physical_pixels()
    {
        Assert.Equal(144, TaskbarPlacement.DipToPx(96, 144));
        Assert.Equal(96, TaskbarPlacement.PxToDip(144, 144), 3);
    }
}
