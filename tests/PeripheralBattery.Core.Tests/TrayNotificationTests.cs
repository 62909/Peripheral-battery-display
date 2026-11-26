using Xunit;

namespace PeripheralBattery.Core.Tests;

public class TrayNotificationTests
{
    [Theory]
    [InlineData(0x0202, TrayActivation.Primary)]
    [InlineData(0x0203, TrayActivation.Primary)]
    [InlineData(0x0400, TrayActivation.Primary)]
    [InlineData(0x0401, TrayActivation.Primary)]
    [InlineData(0x0205, TrayActivation.Menu)]
    [InlineData(0x007B, TrayActivation.Menu)]
    [InlineData(0x00010000 | 0x0205, TrayActivation.Menu)]
    [InlineData(0x00010000 | 0x007B, TrayActivation.Menu)]
    [InlineData(0x0204, TrayActivation.None)]
    [InlineData(0x0200, TrayActivation.None)]
    [InlineData(0x0402, TrayActivation.None)]
    public void Low_word_selects_the_list_or_the_menu(int lParam, TrayActivation expected)
    {
        Assert.Equal(expected, TrayNotification.Decode(lParam));
    }

    [Fact]
    public void Bits_above_int32_do_not_hide_a_right_click()
    {
        var packed = (1L << 32) | TrayNotification.WmRButtonUp;
        Assert.Equal(TrayActivation.Menu, TrayNotification.Decode(packed));
    }
}
