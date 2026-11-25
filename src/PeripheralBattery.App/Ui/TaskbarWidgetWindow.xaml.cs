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

    public void SetReading(int? percent, int count, bool placing, bool lightTaskbar, bool demo)
    {
        _placing = placing;
        var palette = new Palette(lightTaskbar);
        var tone = BatteryMath.Tone(percent);
        var toneBrush = palette.Tone(tone);
        Shell.Background = palette.Brush(palette.Background);
        Shell.BorderBrush = placing ? palette.Brush(palette.Accent) : palette.Brush(palette.Line);
        Shell.BorderThickness = new Thickness(placing ? 2 : 1);
        ValueText.Foreground = toneBrush;
        ValueText.Text = percent is int value ? $"{value}%" : UiText.Unknown;
        Body.Stroke = toneBrush;
        Cap.Fill = toneBrush;
        Fill.Fill = percent is null ? Brushes.Transparent : toneBrush;
        Fill.Width = percent is int level ? Math.Max(0, 9.0 * level / 100.0) : 0;
        Cursor = placing ? Cursors.SizeAll : Cursors.Hand;
        ToolTip = placing
            ? UiText.PlaceHint
            : UiText.TrayTip(percent, count, demo);
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
