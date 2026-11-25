namespace PeripheralBattery.Core;

/// <summary>
/// Brand and model names are returned only for IDs checked against public USB ID lists.
/// An unknown product id becomes the vendor name, never a guessed model.
/// </summary>
public static class KnownDevices
{
    private static readonly Dictionary<int, string> Vendors = new()
    {
        [0x046D] = "罗技",
        [0x1532] = "雷蛇",
        [0x045E] = "微软",
        [0x054C] = "索尼",
        [0x05AC] = "苹果",
        [0x1B1C] = "美商海盗船",
        [0x1038] = "赛睿",
        [0x0B05] = "华硕",
        [0x17EF] = "联想",
        [0x03F0] = "惠普",
        [0x413C] = "戴尔",
        [0x3434] = "Keychron",
        [0x0951] = "金士顿",
        [0x2DC8] = "八位堂",
    };

    private static readonly Dictionary<(int Vid, int Pid), string> Models = new()
    {
        [(0x046D, 0xB017)] = "罗技 MX Master",
        [(0x046D, 0xB019)] = "罗技 MX Master 2S",
        [(0x046D, 0xB023)] = "罗技 MX Master 3",
        [(0x046D, 0x4082)] = "罗技 MX Master 3",
        [(0x046D, 0xB034)] = "罗技 MX Master 3S",
        [(0x046D, 0xB042)] = "罗技 MX Master 4",
        [(0x05AC, 0x0269)] = "苹果妙控鼠标 2",
    };

    public static bool TryGetModel(int vendorId, int productId, out string model) =>
        Models.TryGetValue((vendorId, productId), out model!);

    public static bool TryGetVendor(int vendorId, out string vendor) =>
        Vendors.TryGetValue(vendorId, out vendor!);
}
