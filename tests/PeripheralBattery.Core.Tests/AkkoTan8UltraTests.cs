using Xunit;

namespace PeripheralBattery.Core.Tests;

public class AkkoTan8UltraTests
{
    [Theory]
    [InlineData(0x38EE, 0x0023, true)]
    [InlineData(0x38EE, 0x0024, true)]
    [InlineData(0x38EE, 0x000C, false)]
    [InlineData(0x38EE, 0x000F, false)]
    [InlineData(0x3151, 0x5006, false)]
    [InlineData(0x046D, 0xB023, false)]
    public void Only_the_shared_tan8_usb_ids_are_candidates(int vid, int pid, bool candidate)
    {
        Assert.Equal(candidate, AkkoTan8Ultra.IsCandidate(vid, pid));
    }

    [Fact]
    public void Vendor_collection_is_ff1c_0092_only()
    {
        Assert.True(AkkoTan8Ultra.IsVendorCollection(0xFF1C, 0x0092));
        Assert.False(AkkoTan8Ultra.IsVendorCollection(0xFF1C, 0x0001));
        Assert.False(AkkoTan8Ultra.IsVendorCollection(0x0001, 0x0002));
        Assert.False(AkkoTan8Ultra.IsVendorCollection(0xFFFF, 0x0002));
    }

    [Fact]
    public void Identity_wrapper_and_battery_commands_match_the_driver_table()
    {
        AssertCommand(AkkoTan8Ultra.StartPayload(), AkkoTan8Ultra.StartCommand, AkkoTan8Ultra.WrapperLengthByte);
        AssertCommand(AkkoTan8Ultra.EndPayload(), AkkoTan8Ultra.EndCommand, AkkoTan8Ultra.WrapperLengthByte);
        AssertCommand(AkkoTan8Ultra.IdentityPayload(), AkkoTan8Ultra.IdentityCommand, AkkoTan8Ultra.IdentityLengthByte);
        AssertCommand(AkkoTan8Ultra.BatteryPayload(), AkkoTan8Ultra.BatteryCommand, AkkoTan8Ultra.BatteryLengthByte);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    public void Only_cid_2_on_these_ids_is_tan8_ultra(int cid, bool accept)
    {
        var identity = Identity(cid);
        Assert.Equal(accept, AkkoTan8Ultra.IsThisModel(0x38EE, 0x0024, identity));
        Assert.Equal(accept, AkkoTan8Ultra.IsThisModel(0x38EE, 0x0023, identity));
        Assert.Equal(accept, AkkoTan8Ultra.AllowsBatteryQuery(0x38EE, 0x0024, identity));
        if (accept)
        {
            Assert.Equal(AkkoTan8Ultra.ModelName, AkkoTan8Ultra.ModelNameFor(0x38EE, 0x0024, identity));
            Assert.Equal("空影 TAN8 Ultra", AkkoTan8Ultra.ModelNameFor(0x38EE, 0x0023, identity));
        }
        else
        {
            Assert.Null(AkkoTan8Ultra.ModelNameFor(0x38EE, 0x0024, identity));
            Assert.Null(AkkoTan8Ultra.ModelNameFor(0x38EE, 0x0023, identity));
        }
    }

    [Fact]
    public void Other_usb_ids_are_not_labeled_even_when_the_payload_says_cid_2()
    {
        var identity = Identity(2);
        Assert.False(AkkoTan8Ultra.IsThisModel(0x38EE, 0x000F, identity));
        Assert.False(AkkoTan8Ultra.AllowsBatteryQuery(0x3151, 0x5006, identity));
        Assert.Null(AkkoTan8Ultra.ModelNameFor(0x38EE, 0x000C, identity));
    }

    [Fact]
    public void Identity_payload_must_echo_command_0x30_and_include_byte_0x0a()
    {
        Assert.True(AkkoTan8Ultra.TryReadCid(Identity(2), out var cid));
        Assert.Equal(2, cid);

        var shortFrame = new byte[10];
        shortFrame[2] = AkkoTan8Ultra.IdentityCommand;
        shortFrame[0x09] = 2;
        Assert.False(AkkoTan8Ultra.TryReadCid(shortFrame, out _));

        var wrongCommand = Identity(2);
        wrongCommand[2] = AkkoTan8Ultra.BatteryCommand;
        Assert.False(AkkoTan8Ultra.TryReadCid(wrongCommand, out _));
        Assert.False(AkkoTan8Ultra.AllowsBatteryQuery(0x38EE, 0x0024, wrongCommand));
        Assert.Null(AkkoTan8Ultra.ModelNameFor(0x38EE, 0x0024, ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(100)]
    public void Battery_byte_7_is_a_percentage(int level)
    {
        var report = AkkoTan8Ultra.ParseBattery(Battery(level, charge: 0));
        Assert.Equal(level, report.Percent);
        Assert.Equal((byte)0, report.ChargeState);
    }

    [Fact]
    public void Battery_above_100_is_unknown_and_is_not_clipped()
    {
        var report = AkkoTan8Ultra.ParseBattery(Battery(101, charge: 1));
        Assert.Null(report.Percent);
        Assert.Equal((byte)1, report.ChargeState);
    }

    [Fact]
    public void Charge_byte_is_not_a_percentage()
    {
        var charging = AkkoTan8Ultra.ParseBattery(Battery(40, charge: 1));
        var other = AkkoTan8Ultra.ParseBattery(Battery(40, charge: 2));
        var fullByte = AkkoTan8Ultra.ParseBattery(Battery(40, charge: 0xFF));
        Assert.Equal(40, charging.Percent);
        Assert.Equal(40, other.Percent);
        Assert.Equal(40, fullByte.Percent);
        Assert.Equal((byte)1, charging.ChargeState);
        Assert.Equal((byte)2, other.ChargeState);
        Assert.Equal((byte)0xFF, fullByte.ChargeState);
    }

    [Fact]
    public void Battery_frame_must_echo_0x1a_and_be_at_least_9_bytes()
    {
        Assert.Null(AkkoTan8Ultra.ParseBattery(Battery(50, 0, length: 8)).Percent);
        Assert.Null(AkkoTan8Ultra.ParseBattery(Battery(50, 0, length: 8)).ChargeState);

        var wrong = Battery(50, 0);
        wrong[2] = AkkoTan8Ultra.IdentityCommand;
        var parsed = AkkoTan8Ultra.ParseBattery(wrong);
        Assert.Null(parsed.Percent);
        Assert.Null(parsed.ChargeState);
        Assert.Null(AkkoTan8Ultra.ParseBattery(ReadOnlySpan<byte>.Empty).Percent);
    }

    [Fact]
    public void Windows_input_strips_report_id_4_before_indexing()
    {
        var payload = Battery(77, 1);
        var windows = new byte[1 + payload.Length];
        windows[0] = AkkoTan8Ultra.ReportId;
        payload.CopyTo(windows, 1);

        Assert.True(AkkoTan8Ultra.TryGetPayload(windows, out var stripped));
        Assert.Equal(77, AkkoTan8Ultra.ParseBattery(stripped).Percent);

        windows[0] = 0x05;
        Assert.False(AkkoTan8Ultra.TryGetPayload(windows, out _));
        Assert.False(AkkoTan8Ultra.TryGetPayload(payload, out _));
    }

    private static void AssertCommand(byte[] payload, byte command, byte lengthByte)
    {
        Assert.Equal(AkkoTan8Ultra.PayloadLength, payload.Length);
        Assert.Equal(0, payload[0]);
        Assert.Equal(0, payload[1]);
        Assert.Equal(command, payload[2]);
        Assert.Equal(lengthByte, payload[3]);
        Assert.All(payload.Skip(4), b => Assert.Equal(0, b));
    }

    private static byte[] Identity(int cid)
    {
        var payload = new byte[16];
        payload[2] = AkkoTan8Ultra.IdentityCommand;
        payload[3] = AkkoTan8Ultra.IdentityLengthByte;
        payload[0x0A] = (byte)cid;
        return payload;
    }

    private static byte[] Battery(int level, int charge, int length = 9)
    {
        var payload = new byte[length];
        if (length > 2)
            payload[2] = AkkoTan8Ultra.BatteryCommand;
        if (length > 3)
            payload[3] = AkkoTan8Ultra.BatteryLengthByte;
        if (length > 7)
            payload[7] = (byte)level;
        if (length > 8)
            payload[8] = (byte)charge;
        return payload;
    }
}
