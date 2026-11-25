using PeripheralBattery.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace PeripheralBattery.Sources;

/// <summary>
/// Bluetooth LE Battery Service 0x180F, characteristic Battery Level 0x2A19.
/// A value outside 0–100 is left unknown.
/// </summary>
internal sealed class GattBatterySource : IPeripheralSource
{
    public string SourceId => "gatt";
    public int Priority => SourcePriority.Gatt;

    public async Task<IReadOnlyList<RawDeviceReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var readings = new List<RawDeviceReading>();
        var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
        var devices = await DeviceInformation.FindAllAsync(selector);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var info in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLEDevice? device = null;
            try
            {
                device = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (device is null)
                    continue;
                var name = DeviceIdentity.PreferReportedName(device.Name, info.Name);
                if (DeviceIdentity.IsRadioOrEnumerator(name))
                    continue;

                var battery = await ReadBatteryAsync(device, cancellationToken);
                var kind = DeviceIdentity.ClassifyName(name);
                if (battery is null && kind == DeviceKind.Unknown && !DeviceIdentity.IsMeaningfulName(name))
                    continue;

                readings.Add(new RawDeviceReading
                {
                    SourceId = SourceId,
                    Priority = Priority,
                    BluetoothAddress = device.BluetoothAddress == 0
                        ? DeviceIdentity.TryExtractBluetoothAddress(info.Id)
                        : DeviceIdentity.FormatBluetoothAddress(device.BluetoothAddress),
                    InstanceId = info.Id,
                    WindowsName = name,
                    KindHint = kind,
                    BatteryPercent = battery,
                });
            }
            finally
            {
                device?.Dispose();
            }
        }

        return readings;
    }

    private static async Task<int?> ReadBatteryAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
    {
        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
        cancellationToken.ThrowIfCancellationRequested();
        if (services.Status != GattCommunicationStatus.Success)
            return null;

        var opened = new List<GattDeviceService>();
        try
        {
            foreach (var service in services.Services)
            {
                opened.Add(service);
                var characteristics = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached);
                if (characteristics.Status != GattCommunicationStatus.Success)
                    continue;
                foreach (var characteristic in characteristics.Characteristics)
                {
                    var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (read.Status != GattCommunicationStatus.Success || read.Value is null)
                        continue;
                    var bytes = new byte[read.Value.Length];
                    var reader = DataReader.FromBuffer(read.Value);
                    reader.ReadBytes(bytes);
                    var level = BatteryParsing.ParseGattBatteryLevel(bytes);
                    if (level is not null)
                        return level;
                }
            }

            return null;
        }
        finally
        {
            foreach (var service in opened)
                service.Dispose();
        }
    }
}
