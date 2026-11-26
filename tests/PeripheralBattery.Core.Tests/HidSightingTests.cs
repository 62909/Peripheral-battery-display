using Xunit;

namespace PeripheralBattery.Core.Tests;

public class HidSightingTests
{
    [Fact]
    public void Standard_mouse_collection_on_38ee_0022_is_named_by_id_and_not_queried()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0022, null, 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0022, "USB Optical Mouse", 0x0001, 0x0002),
        ]);

        Assert.Contains("USB Optical Mouse · 38EE:0022 · 0001:0002", text);
        Assert.Contains("不是厂商集合 FF1C:0092，没有查询电量", text);
        Assert.DoesNotContain("空影", text);
        Assert.DoesNotContain("TAN8", text);
        Assert.DoesNotContain("%", text);

        var labeledByTheDevice = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0022, AkkoTan8Ultra.ModelName, 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0022, "Echo TAN8 Ultra", 0x0001, 0x0002),
        ]);
        Assert.DoesNotContain("空影", labeledByTheDevice);
        Assert.DoesNotContain("TAN8", labeledByTheDevice);
        Assert.Contains("38EE:0022 · 0001:0002", labeledByTheDevice);
        Assert.DoesNotContain("%", labeledByTheDevice);
        Assert.Equal(1, text.Split('\n').Count(line => line.Contains("38EE:0022", StringComparison.Ordinal)));
    }

    [Fact]
    public void Vendor_collection_on_a_documented_id_does_not_say_the_query_was_skipped()
    {
        var text = HidSightingText.Format(
        [
            new HidInterfaceSighting(0x38EE, 0x0024, null, 0x0001, 0x0002),
            new HidInterfaceSighting(0x38EE, 0x0024, null, 0xFF1C, 0x0092),
        ]);

        Assert.Contains("38EE:0024 · 0001:0002、FF1C:0092", text);
        Assert.DoesNotContain("没有查询电量", text);
        Assert.DoesNotContain(AkkoTan8Ultra.ModelName, text);
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
