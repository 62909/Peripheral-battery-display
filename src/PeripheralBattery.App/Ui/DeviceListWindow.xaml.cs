using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PeripheralBattery;
using PeripheralBattery.Core;

namespace PeripheralBattery.Ui;

public partial class DeviceListWindow : Window
{
    private AppController? _controller;
    private bool _closeOnDeactivate;

    public DeviceListWindow()
    {
        InitializeComponent();
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        WindowChrome.Apply(this, noActivate: false);
        RefreshButton.Click += (_, _) => _controller?.RequestRefresh();
        CloseButton.Click += (_, _) => Hide();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Hide();
        };
    }

    public void Attach(AppController controller) => _controller = controller;

    public void SetState(
        IReadOnlyList<PeripheralDevice> devices,
        bool hasRefreshed,
        string? error,
        bool demo,
        DateTime? refreshedAt,
        bool light)
    {
        var palette = new Palette(light);
        var background = palette.Brush(palette.Background);
        var text = palette.Brush(palette.Text);
        var muted = palette.Brush(palette.Muted);
        var line = palette.Brush(palette.Line);
        Shell.Background = background;
        Shell.BorderBrush = line;
        TitleText.Foreground = text;
        SubtitleText.Foreground = muted;
        BannerText.Foreground = palette.Tone(BatteryTone.Low);
        EmptyText.Foreground = muted;
        StyleButton(RefreshButton, palette);
        StyleButton(CloseButton, palette);

        if (!hasRefreshed)
            SubtitleText.Text = UiText.Reading;
        else if (demo)
            SubtitleText.Text = devices.Count == 0 ? "演示数据" : $"演示数据 · {devices.Count} 个设备";
        else
            SubtitleText.Text = refreshedAt is DateTime at
                ? $"{devices.Count} 个设备 · 更新于 {at:HH:mm}"
                : $"{devices.Count} 个设备";

        BannerText.Visibility = string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible;
        BannerText.Text = error ?? "";

        Rows.Children.Clear();
        var showEmpty = hasRefreshed && devices.Count == 0;
        EmptyText.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = UiText.Empty;
        if (!showEmpty)
        {
            foreach (var device in devices)
                Rows.Children.Add(BuildRow(device, palette, text, muted));
        }
    }

    public new void Show()
    {
        _closeOnDeactivate = false;
        base.Show();
        Activate();
        _closeOnDeactivate = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_closeOnDeactivate)
            Hide();
    }

    private static Border BuildRow(PeripheralDevice device, Palette palette, Brush text, Brush muted)
    {
        var row = new Border
        {
            Padding = new Thickness(8, 7, 8, 7),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2, 1, 2, 1),
            Background = Brushes.Transparent,
        };
        var hover = palette.Brush(palette.Hover);
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = Glyph(device.Kind),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = muted,
        };
        var name = new TextBlock
        {
            Text = UiText.NameLabel(device.DisplayName),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var kind = new TextBlock
        {
            Text = UiText.KindLabel(device.Kind),
            FontSize = 11,
            Foreground = muted,
            Margin = new Thickness(0, 1, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(name);
        stack.Children.Add(kind);
        if (!string.IsNullOrWhiteSpace(device.Note))
        {
            stack.Children.Add(new TextBlock
            {
                Text = device.Note,
                FontSize = 11,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        var tone = palette.Tone(BatteryMath.Tone(device.BatteryPercent));
        var cell = DeviceListComposition.Describe(device);
        var battery = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        battery.Children.Add(new TextBlock
        {
            Text = cell.Text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = tone,
        });
        if (cell.Bolt)
        {
            battery.Children.Add(new Path
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
        Grid.SetColumn(stack, 1);
        Grid.SetColumn(battery, 2);
        grid.Children.Add(glyph);
        grid.Children.Add(stack);
        grid.Children.Add(battery);
        row.Child = grid;
        return row;
    }

    private static void StyleButton(Button button, Palette palette)
    {
        button.Background = Brushes.Transparent;
        button.Foreground = palette.Brush(palette.Text);
        button.BorderBrush = palette.Brush(palette.Line);
        button.BorderThickness = new Thickness(1);
        button.FontFamily = new FontFamily("Microsoft YaHei UI");
    }

    private static string Glyph(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => "\uE144",
        DeviceKind.Mouse => "\uE962",
        DeviceKind.Headset => "\uE7F6",
        DeviceKind.Other => "\uE7FC",
        _ => "\uE9CE",
    };
}
