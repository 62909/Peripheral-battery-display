using Xunit;

namespace PeripheralBattery.Core.Tests;

public class TrayMenuSessionTests
{
    [Fact]
    public void Button_up_and_context_menu_are_one_session()
    {
        var session = new TrayMenuSession();
        session = session.Note(TrayNotification.Decode(TrayNotification.WmRButtonUp));
        var again = session.Note(TrayNotification.Decode(TrayNotification.WmContextMenu));

        Assert.Equal(session, again);
        Assert.False(again.RestoreTaskbarOwner);

        var closed = again.End();
        Assert.True(closed.RestoreTaskbarOwner);
        Assert.NotEqual(again, closed);
    }

    [Fact]
    public void A_primary_click_does_not_start_a_menu()
    {
        var session = new TrayMenuSession();
        var next = session.Note(TrayNotification.Decode(TrayNotification.WmLButtonUp));

        Assert.Equal(session, next);
        Assert.True(next.RestoreTaskbarOwner);
    }

    [Fact]
    public void A_new_click_after_close_starts_another_session()
    {
        var closed = new TrayMenuSession().Note(TrayActivation.Menu).End();
        var again = closed.Note(TrayActivation.Menu);
        Assert.False(again.RestoreTaskbarOwner);
    }

    [Fact]
    public void Ending_a_closed_session_stays_closed()
    {
        var session = new TrayMenuSession().End();
        Assert.True(session.RestoreTaskbarOwner);
        Assert.Equal(session, session.Note(TrayActivation.None));
    }
}
