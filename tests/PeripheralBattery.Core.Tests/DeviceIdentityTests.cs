using Xunit;

namespace PeripheralBattery.Core.Tests;

public class DeviceIdentityTests
{
    [Theory]
    [InlineData("罗技 MX Keys", DeviceKind.Keyboard)]
    [InlineData("MX Master 3", DeviceKind.Mouse)]
    [InlineData("WH-1000XM5 Hands-Free AG", DeviceKind.Headset)]
    [InlineData("AirPods Pro", DeviceKind.Headset)]
    [InlineData("Xbox Wireless Controller", DeviceKind.Other)]
    [InlineData("客厅设备", DeviceKind.Unknown)]
    [InlineData("", DeviceKind.Unknown)]
    public void Name_classification(string name, DeviceKind expected)
    {
        Assert.Equal(expected, DeviceIdentity.ClassifyName(name));
    }

    [Fact]
    public void Generic_hid_name_is_not_a_model_but_still_classifies()
    {
        Assert.False(DeviceIdentity.IsMeaningfulName("HID-compliant mouse"));
        Assert.Equal(DeviceKind.Mouse, DeviceIdentity.ClassifyName("HID-compliant mouse"));
        Assert.False(DeviceIdentity.IsMeaningfulName("AA:BB:CC:DD:EE:FF"));
        Assert.False(DeviceIdentity.IsMeaningfulName("Bluetooth LE Device"));
        Assert.False(DeviceIdentity.IsMeaningfulName("未知"));
    }

    [Fact]
    public void Profile_suffix_is_removed_before_naming()
    {
        Assert.Equal("WH-1000XM4", DeviceIdentity.StripProfileSuffix("WH-1000XM4 Hands-Free AG"));
        Assert.Equal("WH-1000XM4", DeviceIdentity.ResolveDisplayName("WH-1000XM4 Avrcp Transport", null, null, null));
    }

    [Fact]
    public void Vid_pid_names_only_known_models()
    {
        Assert.Equal("罗技 MX Master 3", DeviceIdentity.ResolveDisplayName(null, 0x046D, 0xB023, null));
        Assert.Equal("罗技", DeviceIdentity.ResolveDisplayName("HID-compliant mouse", 0x046D, 0x9999, null));
        Assert.Null(DeviceIdentity.ResolveDisplayName(null, 0x1234, 0x5678, null));
        Assert.Equal("客厅键盘", DeviceIdentity.ResolveDisplayName("WH-1000XM4", 0x054C, null, "客厅键盘"));
    }

    [Fact]
    public void Windows_name_wins_over_the_id_table()
    {
        Assert.Equal("办公鼠标", DeviceIdentity.ResolveDisplayName("办公鼠标", 0x046D, 0xB023, null));
    }

    [Fact]
    public void Kind_hint_wins_over_the_name_until_an_override()
    {
        Assert.Equal(DeviceKind.Mouse, DeviceIdentity.ResolveKind("MX Keys", DeviceKind.Mouse, null));
        Assert.Equal(DeviceKind.Keyboard, DeviceIdentity.ResolveKind("MX Keys", DeviceKind.Unknown, null));
        Assert.Equal(DeviceKind.Headset, DeviceIdentity.ResolveKind("MX Keys", DeviceKind.Mouse, DeviceKind.Headset));
    }

    [Fact]
    public void Bluetooth_class_and_appearance()
    {
        Assert.Equal(DeviceKind.Keyboard, DeviceIdentity.FromBluetoothClass(5, 0x10));
        Assert.Equal(DeviceKind.Mouse, DeviceIdentity.FromBluetoothClass(5, 0x20));
        Assert.Equal(DeviceKind.Other, DeviceIdentity.FromBluetoothClass(5, 0x30));
        Assert.Equal(DeviceKind.Headset, DeviceIdentity.FromBluetoothClass(4, 0x06));
        Assert.Equal(DeviceKind.Unknown, DeviceIdentity.FromBluetoothClass(1, 0));
        Assert.False(DeviceIdentity.ShouldListBluetoothClass(2));

        Assert.Equal(DeviceKind.Keyboard, DeviceIdentity.FromBleAppearance(0x03C1));
        Assert.Equal(DeviceKind.Mouse, DeviceIdentity.FromBleAppearance(0x03C2));
        Assert.Equal(DeviceKind.Unknown, DeviceIdentity.FromBleAppearance(0));

        Assert.Equal(DeviceKind.Keyboard, DeviceIdentity.FromHidCollection(0x01, 0x06));
        Assert.Equal(DeviceKind.Mouse, DeviceIdentity.FromHidCollection(0x01, 0x02));
        Assert.Equal(DeviceKind.Unknown, DeviceIdentity.FromHidCollection(0x0C, 0x01));
    }

    [Fact]
    public void Class_of_device_bits_match_the_bluetooth_layout()
    {
        uint keyboard = (5u << 8) | (0x10u << 2);
        Assert.Equal((5, 0x10), DeviceIdentity.DecodeClassOfDevice(keyboard));
    }

    [Fact]
    public void Addresses_and_ids_normalize()
    {
        Assert.Equal("AA:BB:CC:DD:EE:FF", DeviceIdentity.NormalizeBluetoothAddress("aa-bb-cc-dd-ee-ff"));
        Assert.Equal("AA:BB:CC:DD:EE:FF", DeviceIdentity.NormalizeBluetoothAddress("aabbccddeeff"));
        Assert.Null(DeviceIdentity.NormalizeBluetoothAddress("zz"));
        Assert.Equal("AA:BB:CC:DD:EE:FF", DeviceIdentity.FormatBluetoothAddress(0x0000AABBCCDDEEFF));
        Assert.Equal("DE:AD:BE:EF:00:11", DeviceIdentity.TryExtractBluetoothAddress(@"BTHLE\Dev_deadbeef0011"));
        Assert.Equal((0x046D, 0xB023), DeviceIdentity.ParseVidPid(@"\\?\hid#vid_046d&pid_b023#x"));
        Assert.Equal((0x046D, 0xB023), DeviceIdentity.ParseVidPid("VID&0002046D_PID&0000B023"));
    }

    [Fact]
    public void Inclusion_rules_skip_radios_receivers_and_wired_hid_without_battery()
    {
        Assert.False(DeviceIdentity.ShouldListNode("Microsoft Bluetooth Enumerator", null, null, DeviceKind.Unknown));
        Assert.False(DeviceIdentity.ShouldListNode("Pixel", 2, 90, DeviceKind.Unknown));
        Assert.False(DeviceIdentity.ShouldListNode("Logitech USB Receiver", 5, null, DeviceKind.Unknown));
        Assert.True(DeviceIdentity.ShouldListNode("WH-1000XM5 Hands-Free AG", 4, null, DeviceKind.Unknown));
        Assert.True(DeviceIdentity.ShouldListNode(null, null, 10, DeviceKind.Unknown));

        Assert.False(DeviceIdentity.ShouldIncludeHid(@"\\?\hid#vid_046d&pid_c52b", DeviceKind.Mouse, false, 0x01));
        Assert.True(DeviceIdentity.ShouldIncludeHid(@"\\?\hid#vid_046d&pid_b023&bthle_device", DeviceKind.Mouse, false, 0x01));
        Assert.True(DeviceIdentity.IsBluetoothHidPath(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}"));
        Assert.False(DeviceIdentity.ShouldIncludeHid(@"\\?\hid#bthle#vendor", DeviceKind.Unknown, false, 0xFF00));
        Assert.True(DeviceIdentity.ShouldIncludeHid(@"\\?\hid#vid_046d&pid_c52b", DeviceKind.Unknown, true, 0x85));
    }
}
