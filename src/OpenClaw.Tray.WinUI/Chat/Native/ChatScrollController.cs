// Pure-C# state machine for the chat timeline scroll behavior.
// Lives in the Tray.WinUI project for proximity to the view, but has zero WinUI dependencies
// so it can be compile-included into the net10.0 test project (see OpenClaw.Tray.Tests.csproj).
//
// The controller answers two questions for the view:
//   1. Should incoming messages keep the scroll pinned to the bottom?
//      ("Anchored" - true when the user is at max scroll offset within tolerance.)
//   2. How many new entries arrived while the user was scrolled up?
//      ("UnreadCount" - shown in a pill on the jump-to-bottom FAB.)
//
// Inputs are pushed in by the view: scroll position changes, new entries appended,
// and explicit "jumped to bottom" notifications. Output is the Changed event,
// emitted only when IsAnchored or UnreadCount actually changes.

namespace OpenClawTray.Chat.Native;

public sealed class ChatScrollController
{
    public const double DefaultBottomTolerancePixels = 4.0;

    public bool IsAnchored { get; private set; } = true;
    public int UnreadCount { get; private set; }

    public event EventHandler? Changed;

    /// <summary>
    /// Update from the view's ScrollView ViewChanged event.
    /// Anchored = current vertical offset is within <paramref name="tolerance"/> of the maximum
    /// scrollable offset. When the scrollable height is zero (content fits in viewport),
    /// we are by definition anchored.
    /// </summary>
    public void UpdateScrollPosition(double verticalOffset, double scrollableHeight, double tolerance = DefaultBottomTolerancePixels)
    {
        var anchored = scrollableHeight <= 0 || verticalOffset >= scrollableHeight - tolerance;
        if (anchored == IsAnchored)
        {
            return;
        }

        IsAnchored = anchored;
        if (IsAnchored && UnreadCount != 0)
        {
            UnreadCount = 0;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Notify the controller that one or more entries were appended to the timeline.
    /// Only increments the unread count when the user is not anchored to the bottom;
    /// while anchored, new entries scroll into view automatically (via ScrollView's
    /// VerticalAnchorRatio="1.0") and there is nothing for the user to "miss".
    /// </summary>
    public void NotifyEntriesAppended(int count = 1)
    {
        if (count <= 0 || IsAnchored)
        {
            return;
        }

        UnreadCount += count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called by the view after it programmatically scrolls to the bottom (jump-to-bottom
    /// click). Resets unread count and forces IsAnchored=true. Distinct from
    /// UpdateScrollPosition because we want the visual state to update immediately
    /// without waiting for the ScrollView ViewChanged event to fire.
    /// </summary>
    public void NotifyJumpedToBottom()
    {
        var fired = false;
        if (!IsAnchored)
        {
            IsAnchored = true;
            fired = true;
        }
        if (UnreadCount != 0)
        {
            UnreadCount = 0;
            fired = true;
        }
        if (fired)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reset to initial state (anchored, zero unread). Use when switching threads.
    /// </summary>
    public void Reset()
    {
        var fired = !IsAnchored || UnreadCount != 0;
        IsAnchored = true;
        UnreadCount = 0;
        if (fired)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
