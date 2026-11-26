using Xunit;

namespace PeripheralBattery.Core.Tests;

public class HidSightingTests
{
    [Fact]
    public void Mouse_collection_on_38ee_0022_is_name_and_ids_only()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0022, null, 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0022, "USB Optical Mouse", 0x0001, 0x0002),
        ]);

        Assert.Contains("USB Optical Mouse · 38EE:0022 · 0001:0002", text);
        Assert.DoesNotContain("%", text);
        Assert.Equal(1, text.Split('\n').Count(line => line.Contains("38EE:0022", StringComparison.Ordinal)));
    }

    [Fact]
    public void Ffa8_collection_is_listed_with_the_same_usb_id()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0022, "AKKO", 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0022, "AKKO", 0xFFA8, 0x0002),
        ]);

        Assert.Contains("AKKO · 38EE:0022 · 0001:0002、FFA8:0002", text);
        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void Other_vendors_stay_name_and_ids_without_a_model_from_the_table()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x046D, 0xB023, null, 0x0001, 0x0002),
            new HidInterfaceSighting(0x046D, 0xC52B, "USB Receiver", 0x0001, 0x0002),
        ]);

        Assert.Equal("这次刷新看到的 HID：\n046D:B023\nUSB Receiver · 046D:C52B", text);
        Assert.DoesNotContain("罗技", text);
        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void No_hid_interface_is_stated_instead_of_an_empty_string()
    {
        Assert.Equal("这次刷新没有看到 HID 接口。", HidSightingText.Format([]));
        Assert.Equal(
            "这次刷新没有看到 HID 接口。",
            HidSightingText.Format([new HidInterfaceSighting(null, null, "鼠标", 0x0001, 0x0002)]));
    }
}
