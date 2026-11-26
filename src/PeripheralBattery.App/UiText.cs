using PeripheralBattery.Core;

namespace PeripheralBattery;

internal static class UiText
{
    public const string AppName = "外设电量";
    public const string Refresh = "刷新";
    public const string OpenList = "打开列表";
    public const string PlaceOnTaskbar = "放到任务栏";
    public const string AdjustPosition = "调整位置";
    public const string FinishPlacement = "完成放置";
    public const string RemoveFromTaskbar = "从任务栏移除";
    public const string LaunchAtStartup = "开机自动启动";
    public const string DemoMode = "演示模式";
    public const string Exit = "退出";
    public const string Unknown = "未知";
    public const string Reading = "正在读取…";
    public const string Empty = "还没有读到外设。设备需要已经配对并连接；这一版读不到的电量会显示为未知。";
    public const string PlaceHint = "拖到任务栏空白处。右键可以选择「完成放置」，或从任务栏移除。";
    public const string GattFailed = "蓝牙电量服务没读到";
    public const string PropertyFailed = "系统记录的蓝牙电量没读到";
    public const string HidFailed = "HID 电量报告没读到";
    public const string VendorFailed = "厂商读取没完成";

    public static string KindLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Headset => "耳机",
        DeviceKind.Other => "其他",
        _ => Unknown,
    };

    public static string NameLabel(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Unknown : name;

    public static string BatteryLabel(int? percent) =>
        percent is int value ? $"{value}%" : Unknown;

    public static string TrayTip(int? percent, int count, bool demo)
    {
        var head = demo ? "外设电量（演示）" : "外设电量";
        var body = percent is int value ? $"{value}%" : Unknown;
        return count == 0 ? $"{head}：未发现外设" : $"{head}：{body}，{count} 个设备";
    }
}
