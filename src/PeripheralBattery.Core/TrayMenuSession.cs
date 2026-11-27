namespace PeripheralBattery.Core;

/// <summary>
/// One tray right-click is one menu. The shell can report that click twice,
/// as <see cref="TrayNotification.WmRButtonUp"/> and as
/// <see cref="TrayNotification.WmContextMenu"/>. Opening a second menu for the
/// second report cancels the menu the first report already painted.
/// The taskbar owner stays cleared for the whole session. Putting it back
/// while the popup is up joins this thread to Explorer again, and the popup
/// is then dismissed as a click outside.
/// </summary>
public readonly record struct TrayMenuSession(bool Active)
{
    public TrayMenuSession Note(TrayActivation activation) =>
        Active || activation != TrayActivation.Menu ? this : new TrayMenuSession(true);

    public TrayMenuSession End() => new(false);

    public bool RestoreTaskbarOwner => !Active;
}
