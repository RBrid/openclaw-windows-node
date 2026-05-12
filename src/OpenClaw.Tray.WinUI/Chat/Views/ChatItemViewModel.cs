using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace OpenClawTray.Chat.Views;

/// <summary>
/// Base view-model for a single row in the native chat ListView. Mirrors a
/// <see cref="ChatTimelineItem"/>; concrete subclasses correspond to the
/// kinds in <see cref="ChatTimelineItemKind"/>.
/// </summary>
public abstract class ChatItemViewModel : INotifyPropertyChanged
{
    public string Id { get; }
    protected ChatItemViewModel(string id) { Id = id; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    /// <summary>Sync mutable state from a fresh timeline item; return true if anything changed.</summary>
    public abstract bool UpdateFrom(ChatTimelineItem item);
}

public sealed class UserItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }

    public UserItemViewModel(ChatTimelineItem item) : base(item.Id) { _text = item.Text; }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        if (_text == item.Text) return false;
        Text = item.Text;
        return true;
    }
}

public sealed class AssistantItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    private bool _isStreaming;
    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }
    public bool IsStreaming { get => _isStreaming; set { if (_isStreaming != value) { _isStreaming = value; OnChanged(); } } }

    public AssistantItemViewModel(ChatTimelineItem item) : base(item.Id)
    {
        _text = item.Text;
        _isStreaming = item.IsStreaming;
    }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        var changed = false;
        if (_text != item.Text) { Text = item.Text; changed = true; }
        if (_isStreaming != item.IsStreaming) { IsStreaming = item.IsStreaming; changed = true; }
        return changed;
    }
}

public sealed class ReasoningItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }

    public ReasoningItemViewModel(ChatTimelineItem item) : base(item.Id) { _text = item.Text; }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        if (_text == item.Text) return false;
        Text = item.Text;
        return true;
    }
}

public sealed class ToolCallItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    private string? _toolName;
    private string? _toolOutput;
    private ChatToolCallStatus? _status;
    private string? _intentSummary;
    private string? _argsPretty;

    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }
    public string? ToolName { get => _toolName; set { if (_toolName != value) { _toolName = value; OnChanged(); } } }
    public string? ToolOutput { get => _toolOutput; set { if (_toolOutput != value) { _toolOutput = value; OnChanged(); OnChanged(nameof(HasOutput)); } } }
    public ChatToolCallStatus? Status { get => _status; set { if (_status != value) { _status = value; OnChanged(); OnChanged(nameof(StatusGlyph)); OnChanged(nameof(IsInProgress)); } } }
    public string? IntentSummary { get => _intentSummary; set { if (_intentSummary != value) { _intentSummary = value; OnChanged(); } } }
    public string? ArgsPretty { get => _argsPretty; set { if (_argsPretty != value) { _argsPretty = value; OnChanged(); OnChanged(nameof(HasArgs)); } } }

    public bool HasOutput => !string.IsNullOrEmpty(_toolOutput);
    public bool HasArgs => !string.IsNullOrEmpty(_argsPretty);
    public bool IsInProgress => _status == ChatToolCallStatus.InProgress;

    public string StatusGlyph => _status switch
    {
        ChatToolCallStatus.Success => "\uE73E",
        ChatToolCallStatus.Error => "\uEA39",
        ChatToolCallStatus.InProgress => "\uE895",
        _ => "\uE7BA"
    };

    public ToolCallItemViewModel(ChatTimelineItem item) : base(item.Id) { Apply(item); }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        var before = (Text: _text, Out: _toolOutput, Status: _status, Args: _argsPretty, Intent: _intentSummary, Name: _toolName);
        Apply(item);
        return before.Text != _text || before.Out != _toolOutput || before.Status != _status
               || before.Args != _argsPretty || before.Intent != _intentSummary || before.Name != _toolName;
    }

    private void Apply(ChatTimelineItem item)
    {
        Text = item.Text;
        ToolName = item.ToolName;
        ToolOutput = item.ToolOutput;
        Status = item.ToolResult;
        IntentSummary = item.IntentSummary;
        ArgsPretty = FormatArgs(item.ToolArgs);
    }

    private static string? FormatArgs(JsonObject? args)
    {
        if (args is null || args.Count == 0) return null;
        try
        {
            return args.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return null;
        }
    }
}

public sealed class StatusItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    private ChatTone? _tone;
    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }
    public ChatTone? Tone { get => _tone; set { if (_tone != value) { _tone = value; OnChanged(); OnChanged(nameof(ToneBrushKey)); } } }

    public string ToneBrushKey => _tone switch
    {
        ChatTone.Error => "SystemFillColorCriticalBrush",
        ChatTone.Warning => "SystemFillColorCautionBrush",
        ChatTone.Success => "SystemFillColorSuccessBrush",
        ChatTone.Dim => "TextFillColorTertiaryBrush",
        _ => "TextFillColorSecondaryBrush"
    };

    public StatusItemViewModel(ChatTimelineItem item) : base(item.Id)
    {
        _text = item.Text;
        _tone = item.Tone;
    }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        var changed = false;
        if (_text != item.Text) { Text = item.Text; changed = true; }
        if (_tone != item.Tone) { Tone = item.Tone; changed = true; }
        return changed;
    }
}

public sealed class RawItemViewModel : ChatItemViewModel
{
    private string _text = string.Empty;
    public string Text { get => _text; set { if (_text != value) { _text = value; OnChanged(); } } }

    public RawItemViewModel(ChatTimelineItem item) : base(item.Id) { _text = item.Text; }

    public override bool UpdateFrom(ChatTimelineItem item)
    {
        if (_text == item.Text) return false;
        Text = item.Text;
        return true;
    }
}

public sealed class PermissionItemViewModel : ChatItemViewModel
{
    private string _toolName = string.Empty;
    private string _detail = string.Empty;
    private string _kind = string.Empty;
    private bool _isResolved;

    public string ToolName { get => _toolName; set { if (_toolName != value) { _toolName = value; OnChanged(); } } }
    public string Detail { get => _detail; set { if (_detail != value) { _detail = value; OnChanged(); } } }
    public string Kind { get => _kind; set { if (_kind != value) { _kind = value; OnChanged(); } } }
    public bool IsResolved { get => _isResolved; set { if (_isResolved != value) { _isResolved = value; OnChanged(); } } }
    public string RequestId { get; }

    public PermissionItemViewModel(string id, ChatPermissionRequest request) : base(id)
    {
        RequestId = request.RequestId;
        _toolName = request.ToolName;
        _detail = request.Detail;
        _kind = request.PermissionKind;
    }

    public override bool UpdateFrom(ChatTimelineItem item) => false;
}

public sealed class TypingItemViewModel : ChatItemViewModel
{
    public TypingItemViewModel() : base("__typing__") { }
    public override bool UpdateFrom(ChatTimelineItem item) => false;
}
