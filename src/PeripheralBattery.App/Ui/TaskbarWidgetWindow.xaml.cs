using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PeripheralBattery;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

public partial class TaskbarWidgetWindow : Window
{
    private AppController? _controller;
    private bool _placing;
    private bool _dragging;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();
        var empty = PillComposition.Arrange([]);
        Width = empty.WidthDip;
        Height = empty.HeightDip;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        WindowChrome.Apply(this, noActivate: true);
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
        MouseMove += OnMove;
        MouseRightButtonUp += (_, e) =>
        {
            _controller?.ShowMenu();
            e.Handled = true;
        };
    }

    public void Attach(AppController controller) => _controller = controller;

    public void SetReading(
        PillStrip strip,
        int count,
        bool placing,
        bool lightTaskbar,
        bool demo,
        TaskbarEdge edge,
        double scale)
    {
        _placing = placing;
        var palette = new Palette(lightTaskbar);
        var background = palette.Brush(palette.Background);
        Shell.Background = background;
        Shell.BorderBrush = placing ? palette.Brush(palette.Accent) : palette.Brush(palette.Line);
        Shell.BorderThickness = new Thickness(strip.BorderDip);
        Shell.Padding = new Thickness(PillComposition.PadDip, 0, PillComposition.PadDip, 0);
        ApplyChrome(strip, edge, scale);

        Strip.Children.Clear();
        for (var i = 0; i < strip.Boxes.Count; i++)
            Strip.Children.Add(BuildSlot(strip.Boxes[i], palette, i > 0));

        Cursor = placing ? Cursors.SizeAll : Cursors.Hand;
        ToolTip = placing
            ? UiText.PlaceHint
            : UiText.TrayTip(Lowest(strip), count, demo);
    }

    private void ApplyChrome(PillStrip strip, TaskbarEdge edge, double scale)
    {
        var vertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        var scaleDown = scale < 0.999;
        if (!vertical && !scaleDown)
        {
            Shell.LayoutTransform = Transform.Identity;
            Shell.ClearValue(WidthProperty);
            Shell.ClearValue(HeightProperty);
            Shell.HorizontalAlignment = HorizontalAlignment.Stretch;
            Shell.VerticalAlignment = VerticalAlignment.Stretch;
            return;
        }

        Shell.Width = strip.WidthDip;
        Shell.Height = strip.HeightDip;
        Shell.HorizontalAlignment = HorizontalAlignment.Left;
        Shell.VerticalAlignment = VerticalAlignment.Center;
        var group = new TransformGroup();
        if (scaleDown)
            group.Children.Add(new ScaleTransform(scale, scale));
        if (vertical)
            group.Children.Add(new RotateTransform(90));
        Shell.LayoutTransform = group;
    }

    private static FrameworkElement BuildSlot(PillSlotBox box, Palette palette, bool gapBefore)
    {
        var tone = palette.Tone(box.Slot.Tone);
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(gapBefore ? PillComposition.DeviceGapDip : 0, 0, 0, 0),
            IsHitTestVisible = false,
        };

        var icon = new Canvas
        {
            Width = PillComposition.IconDip,
            Height = PillComposition.IconDip,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        DeviceGlyph.Draw(icon, box.Slot.Silhouette, tone);
        group.Children.Add(icon);

        group.Children.Add(new TextBlock
        {
            Text = box.Slot.PercentText,
            Width = box.TextWidth,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = tone,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(PillComposition.IconTextGapDip, 0, 0, 0),
            IsHitTestVisible = false,
        });

        if (box.BoltX is not null)
        {
            group.Children.Add(new Path
            {
                Data = DeviceGlyph.Bolt,
                Fill = tone,
                Width = PillComposition.BoltWidthDip,
                Height = PillComposition.BoltHeightDip,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(PillComposition.TextBoltGapDip, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
            });
        }

        return group;
    }

    private static int? Lowest(PillStrip strip)
    {
        int? best = null;
        foreach (var slot in strip.Slots)
        {
            if (slot.Percent is not int value)
                continue;
            best = best is null ? value : Math.Min(best.Value, value);
        }

        return best;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_placing)
        {
            _dragging = true;
            CaptureMouse();
            _controller?.DragWidget();
        }
        else
        {
            _controller?.OpenListFromWidget();
        }

        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            _controller?.DragWidget();
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        ReleaseMouseCapture();
        _controller?.EndWidgetDrag();
        e.Handled = true;
    }
}
