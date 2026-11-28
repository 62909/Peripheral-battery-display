using System.Runtime.InteropServices;
using Xunit;

namespace PeripheralBattery.Core.Tests;

public class SettingsAndLayoutTests
{
    [Fact]
    public void Defaults_leave_startup_and_taskbar_widget_off()
    {
        var settings = new UserSettings();
        Assert.False(settings.LaunchAtStartup);
        Assert.False(settings.DemoMode);
        Assert.False(settings.TaskbarWidgetEnabled);
        Assert.Equal(TaskbarPlacement.PrimaryMonitor, settings.AnchorMonitor);
        Assert.Equal(TaskbarPlacement.DefaultFraction, settings.AnchorFraction);
    }

    [Fact]
    public void Settings_round_trip_and_bad_files_fall_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pb-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new UserSettings
            {
                LaunchAtStartup = false,
                DemoMode = true,
                TaskbarWidgetEnabled = true,
                AnchorMonitor = @"\\.\DISPLAY2",
                AnchorFraction = 0.25,
            };
            UserSettingsStore.Save(path, settings);
            var loaded = UserSettingsStore.Load(path);
            Assert.False(loaded.LaunchAtStartup);
            Assert.True(loaded.DemoMode);
            Assert.True(loaded.TaskbarWidgetEnabled);
            Assert.Equal(@"\\.\DISPLAY2", loaded.AnchorMonitor);
            Assert.Equal(0.25, loaded.AnchorFraction);

            File.WriteAllText(path, "{ not json");
            var fallback = UserSettingsStore.Load(path);
            Assert.False(fallback.TaskbarWidgetEnabled);
            Assert.Equal(TaskbarPlacement.DefaultFraction, fallback.AnchorFraction);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Identity_override_file_normalizes_keys_and_ignores_invented_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pb-override-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity-overrides.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
            {
              "bt:aa-bb-cc-dd-ee-01": { "displayName": "客厅键盘", "kind": "键盘", "batteryPercent": 1 },
              "dev:hid-1": { "displayName": "  ", "kind": "nope" }
            }
            """);
            var store = new JsonIdentityOverrideStore(path);
            var hit = store.Find("bt:AA:BB:CC:DD:EE:01");
            Assert.NotNull(hit);
            Assert.Equal("客厅键盘", hit!.DisplayName);
            Assert.Equal(DeviceKind.Keyboard, hit.Kind);
            var blank = store.Find("dev:hid-1");
            Assert.NotNull(blank);
            Assert.Null(blank!.DisplayName);
            Assert.Null(blank.Kind);
            Assert.Null(store.Find("missing"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Hid_struct_offsets_match_the_windows_headers()
    {
        Assert.Equal(0, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.UsagePage)).ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.ReportId)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.IsRange)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.LogicalMin)).ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.LogicalMax)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<HidValueCaps>(nameof(HidValueCaps.UsageMin)).ToInt32());
        Assert.Equal(72, Marshal.SizeOf<HidValueCaps>());

        Assert.Equal(2, Marshal.OffsetOf<HidButtonCaps>(nameof(HidButtonCaps.ReportId)).ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<HidButtonCaps>(nameof(HidButtonCaps.LinkCollection)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<HidButtonCaps>(nameof(HidButtonCaps.IsRange)).ToInt32());
        Assert.Equal(15, Marshal.OffsetOf<HidButtonCaps>(nameof(HidButtonCaps.IsAbsolute)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<HidButtonCaps>(nameof(HidButtonCaps.UsageMin)).ToInt32());
        Assert.Equal(72, Marshal.SizeOf<HidButtonCaps>());

        Assert.Equal(0, Marshal.OffsetOf<HidCaps>(nameof(HidCaps.Usage)).ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<HidCaps>(nameof(HidCaps.UsagePage)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<HidCaps>(nameof(HidCaps.FeatureReportByteLength)).ToInt32());
        Assert.Equal(64, Marshal.SizeOf<HidCaps>());

        Assert.Equal(4, Marshal.OffsetOf<HidAttributes>(nameof(HidAttributes.VendorId)).ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<HidAttributes>(nameof(HidAttributes.ProductId)).ToInt32());

        var ranged = new HidValueCaps { IsRange = 1, UsageMin = 0x60, UsageMax = 0x66 };
        Assert.True(ranged.ContainsUsage(0x64));
        Assert.False(ranged.ContainsUsage(0x10));
        var single = new HidValueCaps { IsRange = 0, UsageMin = 0x64 };
        Assert.True(single.ContainsUsage(0x64));
    }
}
