namespace PeripheralBattery.Core;

/// <summary>
/// Product rule for a link the official protocol already marks wired or 2.4G.
/// A shown 0–100 level on a wired link is charging, even when the official
/// field is not. An official charging flag stays set on any link.
/// A 2.4G or wireless link keeps the official flag. An unknown link does not
/// gain a bolt from this rule.
/// </summary>
public static class WiredCharging
{
    public static bool? Apply(bool? official, bool? wired, int? percent)
    {
        if (official == true)
            return true;
        if (wired == true && percent is >= 0 and <= 100)
            return true;
        return official;
    }
}
