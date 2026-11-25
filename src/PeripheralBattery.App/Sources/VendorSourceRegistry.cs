using PeripheralBattery.Core;

namespace PeripheralBattery.Sources;

/// <summary>
/// v1 does not ship Logitech HID++, Razer, or other vendor protocols.
/// Add a class that implements <see cref="IVendorBatterySource"/> and yield it here.
/// Return a null battery when the protocol does not provide one.
/// </summary>
internal static class VendorSourceRegistry
{
    public static IEnumerable<IVendorBatterySource> Create()
    {
        yield break;
    }
}
