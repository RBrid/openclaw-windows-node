using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenClawTray.Chat.Views;

/// <summary>
/// Picks a per-kind <see cref="DataTemplate"/> for items in the chat ListView.
/// Templates are owned by <c>ChatView.xaml</c> and assigned to this selector
/// from the page's <c>Loaded</c> handler (so the selector can be referenced
/// from XAML without the templates having to live as static page resources).
/// </summary>
public sealed class ChatItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ReasoningTemplate { get; set; }
    public DataTemplate? ToolCallTemplate { get; set; }
    public DataTemplate? StatusTemplate { get; set; }
    public DataTemplate? RawTemplate { get; set; }
    public DataTemplate? PermissionTemplate { get; set; }
    public DataTemplate? TypingTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        UserItemViewModel => UserTemplate,
        AssistantItemViewModel => AssistantTemplate,
        ReasoningItemViewModel => ReasoningTemplate,
        ToolCallItemViewModel => ToolCallTemplate,
        StatusItemViewModel => StatusTemplate,
        PermissionItemViewModel => PermissionTemplate,
        TypingItemViewModel => TypingTemplate,
        RawItemViewModel => RawTemplate,
        _ => RawTemplate
    };
}
