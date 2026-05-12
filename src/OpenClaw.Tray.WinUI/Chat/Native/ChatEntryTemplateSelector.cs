using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Chat;

namespace OpenClawTray.Chat.Native;

/// <summary>
/// Selects per-entry templates for the native chat timeline based on
/// <see cref="ChatTimelineItem.Kind"/>. Templates are supplied by the consuming
/// view in XAML so visual fidelity stays in markup.
///
/// The MVP only switches User vs Assistant (others fall back to Default). Phase 3
/// will add ToolCall, ToolResult, Reasoning, Status, and PermissionPrompt templates.
/// </summary>
public sealed class ChatEntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ToolCallTemplate { get; set; }
    public DataTemplate? ReasoningTemplate { get; set; }
    public DataTemplate? StatusTemplate { get; set; }
    public DataTemplate? RawTemplate { get; set; }
    public DataTemplate? DefaultTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is ChatTimelineItem entry ? Pick(entry.Kind) : DefaultTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    private DataTemplate? Pick(ChatTimelineItemKind kind) => kind switch
    {
        ChatTimelineItemKind.User       => UserTemplate       ?? DefaultTemplate,
        ChatTimelineItemKind.Assistant  => AssistantTemplate  ?? DefaultTemplate,
        ChatTimelineItemKind.ToolCall   => ToolCallTemplate   ?? DefaultTemplate,
        ChatTimelineItemKind.Reasoning  => ReasoningTemplate  ?? DefaultTemplate,
        ChatTimelineItemKind.Status     => StatusTemplate     ?? DefaultTemplate,
        ChatTimelineItemKind.Raw        => RawTemplate        ?? DefaultTemplate,
        _ => DefaultTemplate
    };
}
