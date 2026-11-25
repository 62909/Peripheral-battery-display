using System.IO;
using Microsoft.Win32;

namespace PeripheralBattery.Native;

internal static class StartupRegistration
{
    private const string ValueName = "PeripheralBattery";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
                return;
            if (!enabled)
            {
                if (key.GetValue(ValueName) is string current
                    && current.Contains("PeripheralBattery", StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
