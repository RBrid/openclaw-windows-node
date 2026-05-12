using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenClawTray.Chat;
using OpenClawTray.Chat.Explorations;
using OpenClawTray.Chat.Views;
using WinUIEx;

namespace OpenClawTray.Windows;

/// <summary>
/// Live preview of the native chat surface using <see cref="FakeChatDataProvider"/>
/// (no real backend). Replaces the deleted Reactor explorations panel — left
/// pane shows simple theme/density toggles bound to <see cref="ChatExplorationState"/>,
/// right pane shows the real <see cref="ChatView"/> with demo content so all
/// templates (user, assistant, tool calls, status, permission, raw) render
/// instantly without needing live gateway data.
/// </summary>
public sealed class ChatExplorationsWindow : WindowEx
{
    private IDisposable? _chatHost;

    public ChatExplorationsWindow()
    {
        Title = "Chat explorations";
        this.SetWindowSize(1100, 720);
        SystemBackdrop = new MicaBackdrop();

        var settingsPane = BuildSettingsPane();
        Grid.SetColumn(settingsPane, 0);

        var splitter = new Border
        {
            Width = 1,
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ControlStrokeColorDefaultBrush"],
        };
        Grid.SetColumn(splitter, 1);

        var chatTarget = new Border();
        Grid.SetColumn(chatTarget, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(settingsPane);
        grid.Children.Add(splitter);
        grid.Children.Add(chatTarget);

        Content = grid;

        _chatHost = ((Window)this).MountChatView(
            chatTarget,
            new FakeChatDataProvider(),
            initialThreadId: null);

        Closed += (_, _) =>
        {
            try { _chatHost?.Dispose(); } catch { /* tear-down race — non-fatal */ }
            _chatHost = null;
        };
    }

    private static FrameworkElement BuildSettingsPane()
    {
        var stack = new StackPanel
        {
            Padding = new Thickness(20),
            Spacing = 12
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Chat preview",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Live preview of the native ChatView using FakeChatDataProvider. Toggle the controls below to bias styling defaults the next time the live tray chat reloads.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        // Theme.
        var themeLabel = new TextBlock { Text = "Theme", Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(themeLabel);
        var themeBox = new ComboBox { Width = 200 };
        themeBox.Items.Add("System");
        themeBox.Items.Add("Light");
        themeBox.Items.Add("Dark");
        themeBox.SelectedIndex = (int)ChatExplorationState.PreviewTheme;
        themeBox.SelectionChanged += (_, _) =>
        {
            ChatExplorationState.PreviewTheme = (ChatPreviewTheme)themeBox.SelectedIndex;
        };
        stack.Children.Add(themeBox);

        // Variation.
        var varLabel = new TextBlock { Text = "Variation", Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(varLabel);
        var varBox = new ComboBox { Width = 200 };
        varBox.Items.Add("Calm");
        varBox.Items.Add("Compact");
        varBox.Items.Add("Plain");
        varBox.SelectedIndex = (int)ChatExplorationState.Variation;
        varBox.SelectionChanged += (_, _) =>
        {
            ChatExplorationState.Variation = (ChatVariation)varBox.SelectedIndex;
        };
        stack.Children.Add(varBox);

        // Density.
        var densityLabel = new TextBlock { Text = "Padding density", Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(densityLabel);
        var densityBox = new ComboBox { Width = 200 };
        densityBox.Items.Add("Cozy");
        densityBox.Items.Add("Comfortable");
        densityBox.Items.Add("Compact");
        densityBox.SelectedIndex = (int)ChatExplorationState.PaddingDensity;
        densityBox.SelectionChanged += (_, _) =>
        {
            ChatExplorationState.PaddingDensity = (ChatPaddingDensity)densityBox.SelectedIndex;
        };
        stack.Children.Add(densityBox);

        // Show timestamps / avatars.
        var ts = new ToggleSwitch
        {
            Header = "Show timestamps",
            IsOn = ChatExplorationState.ShowTimestamps
        };
        ts.Toggled += (_, _) => ChatExplorationState.ShowTimestamps = ts.IsOn;
        stack.Children.Add(ts);

        var avatars = new ToggleSwitch
        {
            Header = "Show avatars",
            IsOn = ChatExplorationState.ShowAvatars
        };
        avatars.Toggled += (_, _) => ChatExplorationState.ShowAvatars = avatars.IsOn;
        stack.Children.Add(avatars);

        return new ScrollViewer { Content = stack };
    }
}
