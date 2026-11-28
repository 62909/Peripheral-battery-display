using PeripheralBattery.Core;

namespace PeripheralBattery.Sources;

/// <summary>
/// Logitech HID++ and Razer are still absent.
/// Add a class that implements <see cref="IVendorBatterySource"/> and yield it here.
/// Return a null battery when the protocol does not provide one.
/// </summary>
internal static class VendorSourceRegistry
{
    public static IEnumerable<IVendorBatterySource> Create()
    {
        yield return new AkkoHuaxiSource();
        yield return new AtkOfficialSource();
        yield return new XiberiaHeadsetSource();
    }
}
