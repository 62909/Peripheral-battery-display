namespace PeripheralBattery.Core;

/// <summary>
/// What a notification-area callback should do. The shell passes the mouse or
/// keyboard notification in <c>lParam</c>. Older shells store that message in the
/// whole value. Windows 11 and <c>NOTIFYICON_VERSION_4</c> store it in the low
/// word, with an icon id or other data above it. A right click is
/// <c>WM_RBUTTONUP</c> or <c>WM_CONTEXTMENU</c>.
/// </summary>
public enum TrayActivation
{
    None = 0,
    Primary = 1,
    Menu = 2,
}

public static class TrayNotification
{
    public const int WmContextMenu = 0x007B;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmRButtonUp = 0x0205;
    public const int NinSelect = 0x0400;
    public const int NinKeySelect = 0x0401;

    public static TrayActivation Decode(long lParam)
    {
        var message = unchecked((int)(lParam & 0xFFFF));
        return message switch
        {
            WmLButtonUp or WmLButtonDblClk or NinSelect or NinKeySelect => TrayActivation.Primary,
            WmRButtonUp or WmContextMenu => TrayActivation.Menu,
            _ => TrayActivation.None,
        };
    }
}
