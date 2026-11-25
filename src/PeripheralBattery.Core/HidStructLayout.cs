using System.Runtime.InteropServices;

namespace PeripheralBattery.Core;

/// <summary>
/// Managed copies of the HID parser structs. Tests lock field offsets so a
/// bad layout cannot silently mis-read a battery report on Windows.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HidValueCaps
{
    public ushort UsagePage;
    public byte ReportId;
    public byte IsAlias;
    public ushort BitField;
    public ushort LinkCollection;
    public ushort LinkUsage;
    public ushort LinkUsagePage;
    public byte IsRange;
    public byte IsStringRange;
    public byte IsDesignatorRange;
    public byte IsAbsolute;
    public byte HasNull;
    public byte Reserved;
    public ushort BitSize;
    public ushort ReportCount;
    public ushort Reserved2a;
    public ushort Reserved2b;
    public ushort Reserved2c;
    public ushort Reserved2d;
    public ushort Reserved2e;
    public uint UnitsExp;
    public uint Units;
    public int LogicalMin;
    public int LogicalMax;
    public int PhysicalMin;
    public int PhysicalMax;
    public ushort UsageMin;
    public ushort UsageMax;
    public ushort StringMin;
    public ushort StringMax;
    public ushort DesignatorMin;
    public ushort DesignatorMax;
    public ushort DataIndexMin;
    public ushort DataIndexMax;

    public bool ContainsUsage(ushort usage)
    {
        if (IsRange == 0)
            return UsageMin == usage;
        return usage >= UsageMin && usage <= UsageMax;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct HidCaps
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;
    public ushort Reserved0;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public ushort Reserved4;
    public ushort Reserved5;
    public ushort Reserved6;
    public ushort Reserved7;
    public ushort Reserved8;
    public ushort Reserved9;
    public ushort Reserved10;
    public ushort Reserved11;
    public ushort Reserved12;
    public ushort Reserved13;
    public ushort Reserved14;
    public ushort Reserved15;
    public ushort Reserved16;
    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}

[StructLayout(LayoutKind.Sequential)]
public struct HidAttributes
{
    public int Size;
    public ushort VendorId;
    public ushort ProductId;
    public ushort VersionNumber;
}
