using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Chat;

namespace OpenClawTray.Chat.Native;

/// <summary>
/// Native WinUI 3 chat timeline. Replaces the Reactor-based OpenClawChatTimeline.
/// Uses ScrollView + ItemsRepeater. Sticky-bottom behavior comes from
/// <c>ScrollView.VerticalAnchorRatio="1.0"</c>: when the user is at max scroll offset,
/// the bottom stays pinned as new entries arrive; if they scroll up to read history,
/// new entries do NOT yank them down — they see a jump-to-bottom FAB with an unread-count
/// pill instead.
///
/// MVP scope: User and Assistant bubbles only. Phase 3 will add tool chips, reasoning
/// panels, status rows, permission prompts, and hover-revealed actions.
///
/// This control is intentionally not yet wired into ChatWindow/ChatPage — that switch
/// happens in Phase 4.
/// </summary>
public sealed partial class ChatTimelineView : UserControl
{
    private readonly ObservableCollection<ChatTimelineItem> _entries = new();
    private readonly ChatScrollController _scrollController = new();
    private bool _suppressAppendNotifications;

    public ChatTimelineView()
    {
        InitializeComponent();
        Repeater.ItemsSource = _entries;
        _entries.CollectionChanged += OnEntriesCollectionChanged;
        _scrollController.Changed += OnScrollControllerChanged;
        Scroller.ViewChanged += OnScrollerViewChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateJumpButton();
    }

    /// <summary>
    /// Backing controller. Exposed so callers (and tests via internals visibility)
    /// can observe IsAnchored / UnreadCount and reset on thread switch.
    /// </summary>
    internal ChatScrollController ScrollController => _scrollController;

    /// <summary>
    /// Replace the timeline contents. Use when switching threads or replaying history.
    /// Bypasses the append-notification path so the scroll controller does not light up
    /// the unread badge during a wholesale reload, then resets the controller to a clean
    /// anchored state.
    /// </summary>
    public void SetEntries(IEnumerable<ChatTimelineItem> entries)
    {
        _suppressAppendNotifications = true;
        try
        {
            _entries.Clear();
            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }
        }
        finally
        {
            _suppressAppendNotifications = false;
        }
        _scrollController.Reset();
    }

    /// <summary>
    /// Reconcile against a new immutable snapshot of timeline entries. The reducer
    /// produces a fresh ChatTimelineState on every event; this method diffs the new
    /// list against the current ObservableCollection by index and applies minimal
    /// mutations so ItemsRepeater can keep realized elements stable.
    /// </summary>
    public void SyncEntries(IReadOnlyList<ChatTimelineItem> next)
    {
        // Update / replace existing slots.
        var minCount = Math.Min(next.Count, _entries.Count);
        for (var i = 0; i < minCount; i++)
        {
            var current = _entries[i];
            var incoming = next[i];
            if (!ReferenceEquals(current, incoming) && !current.Equals(incoming))
            {
                _entries[i] = incoming;
            }
        }

        // Trim trailing entries the new snapshot no longer has.
        while (_entries.Count > next.Count)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        // Append new tail entries.
        for (var i = _entries.Count; i < next.Count; i++)
        {
            _entries.Add(next[i]);
        }
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressAppendNotifications)
        {
            return;
        }
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            _scrollController.NotifyEntriesAppended(e.NewItems.Count);
        }
    }

    private void OnScrollerViewChanged(ScrollView sender, object args)
    {
        _scrollController.UpdateScrollPosition(sender.VerticalOffset, sender.ScrollableHeight);
    }

    private void OnScrollControllerChanged(object? sender, EventArgs e)
    {
        UpdateJumpButton();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initial state: empty timeline = anchored, no FAB.
        UpdateJumpButton();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Scroller.ViewChanged -= OnScrollerViewChanged;
        _scrollController.Changed -= OnScrollControllerChanged;
        _entries.CollectionChanged -= OnEntriesCollectionChanged;
    }

    private void OnJumpToBottomClick(object sender, RoutedEventArgs e)
    {
        Scroller.ScrollTo(0, Scroller.ScrollableHeight);
        _scrollController.NotifyJumpedToBottom();
    }

    private void UpdateJumpButton()
    {
        if (_scrollController.IsAnchored)
        {
            JumpButton.Visibility = Visibility.Collapsed;
            UnreadPill.Visibility = Visibility.Collapsed;
            return;
        }

        JumpButton.Visibility = Visibility.Visible;
        if (_scrollController.UnreadCount > 0)
        {
            UnreadPill.Text = _scrollController.UnreadCount > 99 ? "99+" : _scrollController.UnreadCount.ToString();
            UnreadPill.Visibility = Visibility.Visible;
        }
        else
        {
            UnreadPill.Visibility = Visibility.Collapsed;
        }
    }
}
