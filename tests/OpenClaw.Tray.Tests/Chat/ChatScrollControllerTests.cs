using OpenClawTray.Chat.Native;
using Xunit;

namespace OpenClaw.Tray.Tests.Chat;

public class ChatScrollControllerTests
{
    [Fact]
    public void Initial_state_is_anchored_with_zero_unread()
    {
        var c = new ChatScrollController();
        Assert.True(c.IsAnchored);
        Assert.Equal(0, c.UnreadCount);
    }

    [Fact]
    public void NotifyEntriesAppended_does_not_increment_unread_when_anchored()
    {
        var c = new ChatScrollController();
        c.NotifyEntriesAppended(3);
        Assert.True(c.IsAnchored);
        Assert.Equal(0, c.UnreadCount);
    }

    [Fact]
    public void Scrolling_up_breaks_anchor()
    {
        var c = new ChatScrollController();
        var changed = 0;
        c.Changed += (_, _) => changed++;
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        Assert.False(c.IsAnchored);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void NotifyEntriesAppended_increments_unread_when_not_anchored()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        Assert.False(c.IsAnchored);

        c.NotifyEntriesAppended();
        c.NotifyEntriesAppended(2);
        Assert.Equal(3, c.UnreadCount);
    }

    [Fact]
    public void Scrolling_back_to_bottom_re_anchors_and_clears_unread()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        c.NotifyEntriesAppended(5);
        Assert.False(c.IsAnchored);
        Assert.Equal(5, c.UnreadCount);

        c.UpdateScrollPosition(verticalOffset: 1000, scrollableHeight: 1000);
        Assert.True(c.IsAnchored);
        Assert.Equal(0, c.UnreadCount);
    }

    [Fact]
    public void NotifyJumpedToBottom_anchors_and_clears_unread_in_one_event()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        c.NotifyEntriesAppended(7);
        var changed = 0;
        c.Changed += (_, _) => changed++;

        c.NotifyJumpedToBottom();

        Assert.True(c.IsAnchored);
        Assert.Equal(0, c.UnreadCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Reset_returns_to_initial_state()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        c.NotifyEntriesAppended(2);

        c.Reset();

        Assert.True(c.IsAnchored);
        Assert.Equal(0, c.UnreadCount);
    }

    [Fact]
    public void UpdateScrollPosition_with_zero_scrollable_height_is_anchored()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        Assert.False(c.IsAnchored);

        // Content shrunk back into viewport.
        c.UpdateScrollPosition(verticalOffset: 0, scrollableHeight: 0);

        Assert.True(c.IsAnchored);
    }

    [Fact]
    public void UpdateScrollPosition_within_tolerance_counts_as_anchored()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 998, scrollableHeight: 1000); // within default 4px tol
        Assert.True(c.IsAnchored);
    }

    [Fact]
    public void Repeated_anchored_updates_do_not_fire_Changed()
    {
        var c = new ChatScrollController();
        var changed = 0;
        c.Changed += (_, _) => changed++;

        c.UpdateScrollPosition(verticalOffset: 1000, scrollableHeight: 1000);
        c.UpdateScrollPosition(verticalOffset: 999, scrollableHeight: 1000);
        c.UpdateScrollPosition(verticalOffset: 1000, scrollableHeight: 1000);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void NotifyEntriesAppended_with_zero_or_negative_count_is_noop()
    {
        var c = new ChatScrollController();
        c.UpdateScrollPosition(verticalOffset: 100, scrollableHeight: 1000);
        var changed = 0;
        c.Changed += (_, _) => changed++;

        c.NotifyEntriesAppended(0);
        c.NotifyEntriesAppended(-3);

        Assert.Equal(0, c.UnreadCount);
        Assert.Equal(0, changed);
    }
}
