using System.Globalization;
using System.Text.RegularExpressions;

namespace PeripheralBattery.Core;

public static class DeviceIdentity
{
    private static readonly HashSet<string> GenericExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "bluetooth",
        "bluetooth device",
        "bluetooth le device",
        "bluetooth peripheral",
        "bluetooth peripheral device",
        "low energy",
        "peripheral",
        "input device",
        "usb input device",
        "usb composite device",
        "usb device",
        "hid device",
        "unknown",
        "unknown device",
        "generic bluetooth adapter",
        "generic bluetooth radio",
        "microsoft bluetooth enumerator",
        "keyboard",
        "mouse",
        "headset",
        "headphone",
        "headphones",
        "earphone",
        "earphones",
        "未知",
        "蓝牙",
        "蓝牙设备",
        "蓝牙外围设备",
        "低功耗蓝牙设备",
        "蓝牙无线电",
        "键盘",
        "鼠标",
        "耳机",
        "耳麦",
    };

    private static readonly string[] ProfileSuffixes =
    [
        " Hands-Free AG",
        " Hands-Free",
        " Handsfree",
        " Avrcp Transport",
        " Avrcp",
        " A2DP SNK",
        " A2DP",
    ];

    public static string? StripProfileSuffix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var text = name.Trim();
        bool removed;
        do
        {
            removed = false;
            foreach (var suffix in ProfileSuffixes)
            {
                if (!text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                text = text[..^suffix.Length].Trim();
                removed = true;
                break;
            }
        }
        while (removed && text.Length > 0);

        return text.Length == 0 ? null : text;
    }

    public static bool IsMacAddress(string text)
    {
        var trimmed = text.Trim();
        return Regex.IsMatch(trimmed, "^[0-9A-Fa-f]{2}([:-][0-9A-Fa-f]{2}){5}$")
            || Regex.IsMatch(trimmed, "^[0-9A-Fa-f]{12}$");
    }

    public static bool IsRadioOrEnumerator(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var text = name.Trim();
        if (text.Contains("Enumerator", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Bluetooth Radio", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("蓝牙无线电", StringComparison.Ordinal))
            return true;
        if (text.EndsWith("Protocol TDI", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Equals("Generic Bluetooth Adapter", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsReceiverName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return name.Contains("receiver", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dongle", StringComparison.OrdinalIgnoreCase)
            || name.Contains("接收器", StringComparison.Ordinal);
    }

    public static bool IsMeaningfulName(string? name)
    {
        var text = StripProfileSuffix(name);
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return false;
        if (IsRadioOrEnumerator(text) || IsMacAddress(text))
            return false;
        if (GenericExact.Contains(text))
            return false;
        if (text.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.StartsWith("USB Input", StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.StartsWith("符合 HID", StringComparison.Ordinal))
            return false;
        if (Regex.IsMatch(text, "^Bluetooth(\\s+LE)?(\\s+Device)?(\\s+\\d+)?$", RegexOptions.IgnoreCase))
            return false;
        return true;
    }

    public static string? PreferReportedName(string? product, string? manufacturer)
    {
        var productName = StripProfileSuffix(product);
        if (IsMeaningfulName(productName))
            return productName;
        var maker = StripProfileSuffix(manufacturer);
        if (IsMeaningfulName(maker))
            return maker;
        return productName;
    }

    public static string? ResolveDisplayName(
        string? windowsName,
        int? vendorId,
        int? productId,
        string? overrideName)
    {
        if (IsMeaningfulName(overrideName))
            return overrideName!.Trim();

        var reported = StripProfileSuffix(windowsName);
        if (IsMeaningfulName(reported))
            return reported;

        if (vendorId is int vid && productId is int pid && KnownDevices.TryGetModel(vid, pid, out var model))
            return model;
        if (vendorId is int vendor && KnownDevices.TryGetVendor(vendor, out var brand))
            return brand;
        return null;
    }

    public static DeviceKind ClassifyName(string? name)
    {
        var text = StripProfileSuffix(name);
        if (string.IsNullOrWhiteSpace(text))
            return DeviceKind.Unknown;

        var lower = text.ToLowerInvariant();
        var keyboard = ContainsAny(lower, "keyboard", "键盘", "mx keys", "keychron", "kbd");
        var mouse = ContainsAny(lower, "mouse", "鼠标", "mx master", "mx anywhere", "trackball", "轨迹球");
        var headset = ContainsAny(lower, "headset", "headphone", "earphone", "earbud", "airpod", "耳机", "耳麦", "头戴", "buds", "wh-1000", "wf-1000");
        var other = ContainsAny(lower, "gamepad", "controller", "手柄", "控制器", "xbox", "dualsense", "dualshock");

        var specific = (keyboard ? 1 : 0) + (mouse ? 1 : 0) + (headset ? 1 : 0);
        if (specific > 1)
            return DeviceKind.Other;
        if (keyboard)
            return DeviceKind.Keyboard;
        if (mouse)
            return DeviceKind.Mouse;
        if (headset)
            return DeviceKind.Headset;
        if (other)
            return DeviceKind.Other;
        return DeviceKind.Unknown;
    }

    public static DeviceKind ResolveKind(string? windowsName, DeviceKind hint, DeviceKind? overrideKind)
    {
        if (overrideKind is DeviceKind forced && forced != DeviceKind.Unknown)
            return forced;
        if (hint != DeviceKind.Unknown)
            return hint;
        return ClassifyName(windowsName);
    }

    public static DeviceKind FromBluetoothClass(int major, int minor)
    {
        const int audio = 4;
        const int peripheral = 5;
        const int wearable = 7;
        if (major == peripheral)
        {
            var hi = minor & 0x30;
            if (hi == 0x30)
                return DeviceKind.Other;
            if (hi == 0x10)
                return DeviceKind.Keyboard;
            if (hi == 0x20)
                return DeviceKind.Mouse;
            return DeviceKind.Other;
        }

        if (major == audio)
        {
            if (minor is 1 or 2 or 6 or 7)
                return DeviceKind.Headset;
            return DeviceKind.Other;
        }

        if (major == wearable)
            return DeviceKind.Other;
        return DeviceKind.Unknown;
    }

    public static (int Major, int Minor) DecodeClassOfDevice(uint classOfDevice)
    {
        var major = (int)((classOfDevice >> 8) & 0x1F);
        var minor = (int)((classOfDevice >> 2) & 0x3F);
        return (major, minor);
    }

    public static bool ShouldListBluetoothClass(int major) => major is not (1 or 2 or 3);

    public static DeviceKind FromBleAppearance(int appearance)
    {
        if (appearance <= 0)
            return DeviceKind.Unknown;
        var category = (appearance >> 6) & 0x3FF;
        var sub = appearance & 0x3F;
        if (category != 0x0F)
            return DeviceKind.Unknown;
        return sub switch
        {
            0x01 => DeviceKind.Keyboard,
            0x02 => DeviceKind.Mouse,
            0x03 or 0x04 => DeviceKind.Other,
            _ => DeviceKind.Other,
        };
    }

    public static DeviceKind FromHidCollection(int usagePage, int usage)
    {
        if (usagePage != 0x01)
            return DeviceKind.Unknown;
        return usage switch
        {
            0x02 => DeviceKind.Mouse,
            0x06 => DeviceKind.Keyboard,
            0x04 or 0x05 => DeviceKind.Other,
            _ => DeviceKind.Unknown,
        };
    }

    public static bool IsBluetoothHidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        return path.Contains("bthenum", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bthle", StringComparison.OrdinalIgnoreCase)
            || path.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldIncludeHid(string? devicePath, DeviceKind kind, bool hasStandardBatteryCap, int usagePage)
    {
        if (hasStandardBatteryCap)
            return true;
        if (!IsBluetoothHidPath(devicePath))
            return false;
        if (usagePage >= 0xFF00)
            return false;
        return kind is DeviceKind.Keyboard or DeviceKind.Mouse or DeviceKind.Other;
    }

    public static bool ShouldListNode(string? name, int? majorClass, int? batteryPercent, DeviceKind kindHint)
    {
        if (IsRadioOrEnumerator(name))
            return false;
        if (majorClass is int major && !ShouldListBluetoothClass(major))
            return false;
        var percent = BatteryMath.NormalizePercent(batteryPercent);
        if (IsReceiverName(name) && percent is null)
            return false;
        if (percent is not null)
            return true;
        if (kindHint != DeviceKind.Unknown)
            return true;
        if (majorClass is 4 or 5 or 7)
            return true;
        return IsMeaningfulName(name);
    }

    public static string? NormalizeBluetoothAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
            return null;
        hex = hex.ToUpperInvariant();
        return string.Create(17, hex, static (span, value) =>
        {
            for (var i = 0; i < 6; i++)
            {
                if (i > 0)
                    span[i * 3 - 1] = ':';
                span[i * 3] = value[i * 2];
                span[i * 3 + 1] = value[i * 2 + 1];
            }
        });
    }

    public static string FormatBluetoothAddress(ulong address)
    {
        Span<char> chars = stackalloc char[17];
        for (var i = 0; i < 6; i++)
        {
            var shift = (5 - i) * 8;
            var b = (byte)((address >> shift) & 0xFF);
            if (i > 0)
                chars[i * 3 - 1] = ':';
            b.TryFormat(chars.Slice(i * 3, 2), out _, "X2", CultureInfo.InvariantCulture);
        }

        return new string(chars);
    }

    public static string? TryExtractBluetoothAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var dev = Regex.Match(text, "Dev_([0-9A-Fa-f]{12})", RegexOptions.IgnoreCase);
        if (dev.Success)
            return NormalizeBluetoothAddress(dev.Groups[1].Value);
        var mac = Regex.Match(text, "([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})");
        if (mac.Success)
            return NormalizeBluetoothAddress(mac.Groups[1].Value);
        return null;
    }

    public static (int? VendorId, int? ProductId) ParseVidPid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);
        var vid = MatchId(text, "vid_([0-9A-Fa-f]{4})")
            ?? MatchId(text, "vid&[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})");
        var pid = MatchId(text, "pid_([0-9A-Fa-f]{4})")
            ?? MatchId(text, "pid&[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})");
        return (vid, pid);
    }

    public static bool TryParseKind(string? text, out DeviceKind kind)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "keyboard" or "键盘":
                kind = DeviceKind.Keyboard;
                return true;
            case "mouse" or "鼠标":
                kind = DeviceKind.Mouse;
                return true;
            case "headset" or "耳机" or "耳麦":
                kind = DeviceKind.Headset;
                return true;
            case "other" or "其他":
                kind = DeviceKind.Other;
                return true;
            case "unknown" or "未知":
                kind = DeviceKind.Unknown;
                return true;
            default:
                kind = DeviceKind.Unknown;
                return false;
        }
    }

    private static int? MatchId(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        return int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
