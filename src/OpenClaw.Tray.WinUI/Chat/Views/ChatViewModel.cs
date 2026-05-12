using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace OpenClawTray.Chat.Views;

/// <summary>
/// View-model that bridges an <see cref="IChatDataProvider"/> snapshot to an
/// <see cref="ObservableCollection{ChatItemViewModel}"/> consumed by the
/// native ChatView ListView. Diffs every snapshot into the collection so
/// existing items are mutated in place (no flicker) and new items are
/// appended (so <c>ItemsUpdatingScrollMode="KeepLastItemInView"</c>
/// auto-scrolls to the bottom).
/// </summary>
public sealed class ChatViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IChatDataProvider _provider;
    private readonly DispatcherQueue _dispatcher;
    private readonly object _gate = new();

    private string? _activeThreadId;
    private ChatThread? _activeThread;
    private string[] _availableModels = Array.Empty<string>();
    private string? _connectionStatus;
    private bool _turnActive;
    private string? _currentIntent;
    private string? _selectedModel;
    private bool _isLoaded;
    private bool _allowAllPermissions;
    private bool _disposed;

    public ObservableCollection<ChatItemViewModel> Items { get; } = new();
    public ObservableCollection<ChatThreadSummaryViewModel> Threads { get; } = new();

    public ChatThread? ActiveThread
    {
        get => _activeThread;
        private set { if (!ReferenceEquals(_activeThread, value)) { _activeThread = value; OnChanged(); OnChanged(nameof(HeaderTitle)); OnChanged(nameof(HeaderBreadcrumb)); } }
    }

    public string HeaderTitle => _activeThread?.DisplayTitle ?? "OpenClaw Chat";
    public string HeaderBreadcrumb
    {
        get
        {
            if (_activeThread is null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_activeThread.Cwd)) parts.Add($"\uD83D\uDCC1 {_activeThread.Cwd}");
            if (!string.IsNullOrEmpty(_activeThread.Repository)) parts.Add(_activeThread.Repository!);
            if (!string.IsNullOrEmpty(_activeThread.Branch)) parts.Add($"\uD83D\uDD00 {_activeThread.Branch}");
            return string.Join("  \u00B7  ", parts);
        }
    }

    public string? ConnectionStatus { get => _connectionStatus; private set { if (_connectionStatus != value) { _connectionStatus = value; OnChanged(); } } }
    public bool TurnActive { get => _turnActive; private set { if (_turnActive != value) { _turnActive = value; OnChanged(); OnChanged(nameof(CanSend)); OnChanged(nameof(CanStop)); } } }
    public string? CurrentIntent { get => _currentIntent; private set { if (_currentIntent != value) { _currentIntent = value; OnChanged(); } } }
    public bool IsLoaded { get => _isLoaded; private set { if (_isLoaded != value) { _isLoaded = value; OnChanged(); } } }

    public string[] AvailableModels { get => _availableModels; private set { _availableModels = value; OnChanged(); } }

    public string? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (_selectedModel == value) return;
            _selectedModel = value;
            OnChanged();
            if (_activeThreadId is { } tid && value is { Length: > 0 } m)
                _ = SafeAsync(() => _provider.SetModelAsync(tid, m));
        }
    }

    public bool AllowAllPermissions
    {
        get => _allowAllPermissions;
        set
        {
            if (_allowAllPermissions == value) return;
            _allowAllPermissions = value;
            OnChanged();
            if (_activeThreadId is { } tid)
                _ = SafeAsync(() => _provider.SetPermissionModeAsync(tid, value));
        }
    }

    public bool CanSend => IsLoaded && !TurnActive && _activeThreadId is not null;
    public bool CanStop => TurnActive && _activeThreadId is not null;

    public Func<string, Task>? OnReadAloud { get; set; }
    public string ProviderDisplayName => _provider.DisplayName;
    public string? ActiveThreadId => _activeThreadId;
    public IChatDataProvider Provider => _provider;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? AppendedItem;

    public ChatViewModel(
        IChatDataProvider provider,
        DispatcherQueue dispatcher,
        string? initialThreadId,
        Func<string, Task>? onReadAloud)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _activeThreadId = initialThreadId;
        OnReadAloud = onReadAloud;

        _provider.Changed += OnProviderChanged;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var snap = await _provider.LoadAsync(ct).ConfigureAwait(false);
        Post(() => ApplySnapshot(snap, initial: true));
    }

    public async Task SendAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_activeThreadId is null)
        {
            // Auto-create a thread if none active.
            var thread = await _provider.CreateThreadAsync(text).ConfigureAwait(false);
            _activeThreadId = thread.Id;
            return;
        }
        await _provider.SendMessageAsync(_activeThreadId, text).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        if (_activeThreadId is null) return Task.CompletedTask;
        return _provider.StopResponseAsync(_activeThreadId);
    }

    public Task SelectThreadAsync(string threadId)
    {
        _activeThreadId = threadId;
        OnChanged(nameof(CanSend));
        // Force the next snapshot to repaint timeline.
        Post(RebuildFromCurrentSnapshot);
        return Task.CompletedTask;
    }

    public Task RespondToPermissionAsync(string requestId, bool allow)
    {
        if (_activeThreadId is null) return Task.CompletedTask;
        return _provider.RespondToPermissionAsync(_activeThreadId, requestId, allow);
    }

    public Task<ChatThread> CreateThreadAsync(string? initialMessage = null)
        => _provider.CreateThreadAsync(initialMessage);

    public Task DeleteThreadAsync(string threadId)
        => _provider.DeleteThreadAsync(threadId);

    public Task ReadAloudAsync(string text)
        => OnReadAloud is null ? Task.CompletedTask : OnReadAloud(text);

    private ChatDataSnapshot? _lastSnapshot;

    private void OnProviderChanged(object? sender, ChatDataChangedEventArgs e)
    {
        var snap = e.Snapshot;
        Post(() => ApplySnapshot(snap, initial: false));
    }

    private void RebuildFromCurrentSnapshot()
    {
        if (_lastSnapshot is { } snap) ApplySnapshot(snap, initial: false, force: true);
    }

    private void ApplySnapshot(ChatDataSnapshot snap, bool initial, bool force = false)
    {
        _lastSnapshot = snap;
        ConnectionStatus = snap.ConnectionStatus;
        AvailableModels = snap.AvailableModels ?? Array.Empty<string>();

        // Keep thread list in sync.
        SyncThreads(snap);

        if (_activeThreadId is null && snap.DefaultThreadId is { } def)
        {
            _activeThreadId = def;
        }

        if (_activeThreadId is { } tid && snap.Timelines.TryGetValue(tid, out var timeline))
        {
            ActiveThread = snap.Threads.FirstOrDefault(t => t.Id == tid);
            CurrentIntent = timeline.CurrentIntent;
            TurnActive = timeline.TurnActive;

            if (_activeThread?.Model is { Length: > 0 } m && _selectedModel is null)
            {
                _selectedModel = m;
                OnChanged(nameof(SelectedModel));
            }

            DiffItems(timeline, force);
        }
        else
        {
            ActiveThread = null;
            CurrentIntent = null;
            TurnActive = false;
            if (Items.Count > 0) Items.Clear();
        }

        IsLoaded = true;
    }

    private void SyncThreads(ChatDataSnapshot snap)
    {
        // Map by id, replace contents.
        var existing = Threads.ToDictionary(t => t.Id);
        var keep = new HashSet<string>();
        foreach (var t in snap.Threads)
        {
            keep.Add(t.Id);
            if (existing.TryGetValue(t.Id, out var vm))
            {
                vm.Update(t);
            }
            else
            {
                Threads.Add(new ChatThreadSummaryViewModel(t));
            }
        }
        for (var i = Threads.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Threads[i].Id)) Threads.RemoveAt(i);
        }
    }

    private void DiffItems(ChatTimelineState timeline, bool force)
    {
        // Build the "wanted" sequence: timeline entries plus an optional
        // permission card and an optional typing indicator at the tail.
        var wanted = new List<(string Key, ChatItemViewModel? Existing, Func<ChatItemViewModel> Factory, Action<ChatItemViewModel>? Update)>();

        foreach (var entry in timeline.Entries)
        {
            wanted.Add((entry.Id, null, () => CreateForEntry(entry), vm => vm.UpdateFrom(entry)));
        }

        if (timeline.PendingPermission is { } pending)
        {
            var key = $"perm:{pending.RequestId}";
            wanted.Add((key, null, () => new PermissionItemViewModel(key, pending), null));
        }

        if (timeline.TurnActive
            && (timeline.Entries.Count == 0
                || timeline.Entries[^1].Kind is not (ChatTimelineItemKind.Assistant or ChatTimelineItemKind.Reasoning)))
        {
            wanted.Add(("__typing__", null, () => new TypingItemViewModel(), null));
        }

        // Map current items by id.
        var byId = new Dictionary<string, (int Index, ChatItemViewModel Vm)>();
        for (var i = 0; i < Items.Count; i++) byId[Items[i].Id] = (i, Items[i]);

        // Iterate wanted; reorder/insert/update minimally.
        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            if (i < Items.Count && Items[i].Id == w.Key)
            {
                // Same position — update in place.
                if (w.Update is { } upd) upd(Items[i]);
                continue;
            }

            if (byId.TryGetValue(w.Key, out var found))
            {
                // Move existing item to position i.
                var vm = found.Vm;
                Items.RemoveAt(found.Index);
                Items.Insert(i, vm);
                if (w.Update is { } upd) upd(vm);
                // Rebuild index.
                byId.Clear();
                for (var k = 0; k < Items.Count; k++) byId[Items[k].Id] = (k, Items[k]);
            }
            else
            {
                var newVm = w.Factory();
                Items.Insert(i, newVm);
                byId.Clear();
                for (var k = 0; k < Items.Count; k++) byId[Items[k].Id] = (k, Items[k]);
                if (i == Items.Count - 1) AppendedItem?.Invoke(this, EventArgs.Empty);
            }
        }

        // Trim extras at the tail.
        while (Items.Count > wanted.Count) Items.RemoveAt(Items.Count - 1);
    }

    private static ChatItemViewModel CreateForEntry(ChatTimelineItem entry)
    {
        return entry.Kind switch
        {
            ChatTimelineItemKind.User => new UserItemViewModel(entry),
            ChatTimelineItemKind.Assistant => new AssistantItemViewModel(entry),
            ChatTimelineItemKind.Reasoning => new ReasoningItemViewModel(entry),
            ChatTimelineItemKind.ToolCall => new ToolCallItemViewModel(entry),
            ChatTimelineItemKind.Status => new StatusItemViewModel(entry),
            _ => new RawItemViewModel(entry)
        };
    }

    private void Post(Action a)
    {
        if (_dispatcher.HasThreadAccess) { a(); return; }
        _dispatcher.TryEnqueue(() => a());
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatViewModel] op failed: {ex.Message}"); }
    }

    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.Changed -= OnProviderChanged;
    }
}

/// <summary>Lightweight thread row for an optional thread picker / sidebar.</summary>
public sealed class ChatThreadSummaryViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private ChatThreadStatus _status;

    public string Id { get; }
    public string Title { get => _title; set { if (_title != value) { _title = value; OnChanged(); } } }
    public ChatThreadStatus Status { get => _status; set { if (_status != value) { _status = value; OnChanged(); } } }

    public ChatThreadSummaryViewModel(ChatThread t)
    {
        Id = t.Id;
        Update(t);
    }

    public void Update(ChatThread t)
    {
        Title = t.DisplayTitle;
        Status = t.Status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
