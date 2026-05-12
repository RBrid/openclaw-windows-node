using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Chat;

namespace OpenClawTray.Chat.Native;

/// <summary>
/// Native WinUI 3 root for the OpenClaw chat surface. Replaces the Reactor-based
/// <c>OpenClawChatRoot</c>. Owns the provider subscription, marshals snapshot updates
/// to the UI thread, and projects the current thread's timeline into
/// <see cref="ChatTimelineView"/>.
///
/// Composition (top-to-bottom):
/// header → timeline (or empty/loading placeholder) → thinking row → composer.
///
/// MVP scope: render the default thread, send messages, stop in-flight turns,
/// show connection badge, show thinking indicator. Permission prompts and
/// per-thread switching UI come from the surrounding chrome (NavigationView /
/// tray popup), so they are not implemented here.
/// </summary>
public sealed partial class NativeChatRoot : UserControl
{
    private readonly IChatDataProvider _provider;
    private readonly Func<string, Task>? _onReadAloud;
    private readonly DispatcherQueue _dispatcher;
    private string? _selectedThreadId;
    private ChatDataSnapshot? _snapshot;
    private bool _disposed;

    public NativeChatRoot(IChatDataProvider provider, string? initialThreadId = null, Func<string, Task>? onReadAloud = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _selectedThreadId = initialThreadId;
        _onReadAloud = onReadAloud;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("NativeChatRoot must be constructed on a UI thread.");
        InitializeComponent();
        Composer.SendRequested += OnSendRequested;
        Composer.StopRequested += OnStopRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ApplyEmptyOrLoading(loading: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _provider.Changed += OnProviderChanged;
        try
        {
            var snapshot = await _provider.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            // Don't blow up the surface on initial load failure — the provider
            // will fire Changed when it reconciles, and the badge will show
            // the disconnected state in the meantime.
            System.Diagnostics.Debug.WriteLine($"NativeChatRoot initial load failed: {ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        _disposed = true;
        _provider.Changed -= OnProviderChanged;
        Composer.SendRequested -= OnSendRequested;
        Composer.StopRequested -= OnStopRequested;
    }

    private void OnProviderChanged(object? sender, ChatDataChangedEventArgs e)
    {
        // Provider events may fire on background threads; marshal to UI.
        if (_dispatcher.HasThreadAccess)
        {
            ApplySnapshot(e.Snapshot);
        }
        else
        {
            var snapshot = e.Snapshot;
            _dispatcher.TryEnqueue(() => ApplySnapshot(snapshot));
        }
    }

    private void ApplySnapshot(ChatDataSnapshot snapshot)
    {
        if (_disposed) return;
        _snapshot = snapshot;

        // Stick with the user's existing selection if it's still in the snapshot;
        // otherwise fall back to the provider-suggested default.
        if (_selectedThreadId is null
            || Array.Find(snapshot.Threads, t => t.Id == _selectedThreadId) is null)
        {
            _selectedThreadId = snapshot.DefaultThreadId
                ?? (snapshot.Threads.Length > 0 ? snapshot.Threads[0].Id : null);
        }

        var thread = _selectedThreadId is { } id
            ? Array.Find(snapshot.Threads, t => t.Id == id)
            : null;
        var timelineState = thread is not null
            && snapshot.Timelines.TryGetValue(thread.Id, out var tl)
                ? tl
                : ChatTimelineState.Initial();

        UpdateHeader(thread, snapshot.ConnectionStatus);
        UpdateConnectionBadge(snapshot.ConnectionStatus);
        UpdateTimeline(thread, timelineState);
        UpdateThinking(timelineState);
        UpdateComposer(snapshot.ConnectionStatus, timelineState);

        // Lazy-load history when a thread is first selected. Same behavior as
        // the Reactor root: ask the native provider to fetch history if it
        // hasn't already.
        if (thread is not null && _provider is OpenClawChatDataProvider native)
        {
            _ = native.LoadHistoryAsync(thread.Id, force: false);
        }
    }

    private void UpdateHeader(ChatThread? thread, string? connectionStatus)
    {
        ThreadTitle.Text = thread?.DisplayTitle ?? string.Empty;
    }

    private void UpdateConnectionBadge(string? connectionStatus)
    {
        var (text, brushKey) = ResolveBadge(connectionStatus);
        ConnectionBadgeText.Text = text;
        ConnectionBadge.Background = (Brush)Application.Current.Resources[brushKey];
    }

    private static (string text, string brushKey) ResolveBadge(string? status)
    {
        if (status is null) return ("Disconnected", "SystemFillColorCriticalBackgroundBrush");
        if (status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
            return ("Connected", "SystemFillColorSuccessBackgroundBrush");
        if (status.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase))
            return ("Connecting", "SystemFillColorCautionBackgroundBrush");
        return ("Disconnected", "SystemFillColorCriticalBackgroundBrush");
    }

    private void UpdateTimeline(ChatThread? thread, ChatTimelineState state)
    {
        if (thread is null)
        {
            ApplyEmptyOrLoading(loading: false);
            Timeline.Visibility = Visibility.Collapsed;
            return;
        }

        // Hide placeholders, show the timeline.
        EmptyState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Collapsed;
        Timeline.Visibility = Visibility.Visible;

        if (state.Entries.Count == 0)
        {
            // Conversation exists but has no messages yet → show empty hint as a
            // soft overlay; keep the timeline mounted so the first incoming
            // message lands in the right control.
            EmptyState.Visibility = Visibility.Visible;
        }

        Timeline.SyncEntries(state.Entries);
    }

    private void ApplyEmptyOrLoading(bool loading)
    {
        Timeline.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        LoadingState.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateThinking(ChatTimelineState state)
    {
        // Show "thinking…" between the user's send and the first assistant chunk.
        var lastIsAssistant = state.Entries.Count > 0
            && state.Entries[state.Entries.Count - 1].Kind == ChatTimelineItemKind.Assistant;
        ThinkingRow.Visibility = state.TurnActive && !lastIsAssistant
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateComposer(string? connectionStatus, ChatTimelineState state)
    {
        var connected = connectionStatus is not null
            && connectionStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
        Composer.IsConnected = connected;
        Composer.TurnActive = state.TurnActive;
    }

    private async void OnSendRequested(object? sender, string text)
    {
        if (_selectedThreadId is null)
        {
            // No thread yet — try to create one.
            try
            {
                var newThread = await _provider.CreateThreadAsync(initialMessage: text).ConfigureAwait(true);
                _selectedThreadId = newThread.Id;
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NativeChatRoot CreateThread failed: {ex.Message}");
                return;
            }
        }

        try
        {
            await _provider.SendMessageAsync(_selectedThreadId, text).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NativeChatRoot SendMessage failed: {ex.Message}");
        }
    }

    private async void OnStopRequested(object? sender, EventArgs e)
    {
        if (_selectedThreadId is null) return;
        try
        {
            await _provider.StopResponseAsync(_selectedThreadId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NativeChatRoot StopResponse failed: {ex.Message}");
        }
    }
}
