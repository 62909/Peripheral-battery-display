using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
        Width = TaskbarPlacement.PillAlongDip;
        Height = TaskbarPlacement.PillCrossDip;
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

    public void SetReading(PillFace face, int count, bool placing, bool lightTaskbar, bool demo)
    {
        _placing = placing;
        var palette = new Palette(lightTaskbar);
        var toneBrush = palette.Tone(BatteryMath.Tone(face.Percent));
        var background = palette.Brush(palette.Background);
        Shell.Background = background;
        Shell.BorderBrush = placing ? palette.Brush(palette.Accent) : palette.Brush(palette.Line);
        Shell.BorderThickness = new Thickness(placing ? 2 : 1);
        DeviceGlyph.Draw(DeviceIcon, face.Silhouette, toneBrush);
        ValueText.Foreground = toneBrush;
        ValueText.Text = face.PercentText;
        Body.Stroke = toneBrush;
        Cap.Fill = toneBrush;
        Fill.Fill = face.Percent is null ? Brushes.Transparent : toneBrush;
        Fill.Width = BatteryInteriorWidth * face.FillFraction;
        Bolt.Visibility = face.Charging ? Visibility.Visible : Visibility.Collapsed;
        Bolt.Fill = background;
        Bolt.Stroke = toneBrush;
        Cursor = placing ? Cursors.SizeAll : Cursors.Hand;
        ToolTip = placing
            ? UiText.PlaceHint
            : UiText.TrayTip(face.Percent, count, demo);
    }

    /// <summary>
    /// Inner width of the battery body. 100% fills it; an unknown level stays empty.
    /// </summary>
    private const double BatteryInteriorWidth = 11.6;

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
