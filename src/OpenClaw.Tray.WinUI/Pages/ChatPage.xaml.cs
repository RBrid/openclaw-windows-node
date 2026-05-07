using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OpenClaw.Shared;
using OpenClawTray.Chat;
using OpenClawTray.Helpers;
using OpenClawTray.Services;
using OpenClawTray.Windows;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OpenClawTray.Pages;

public sealed partial class ChatPage : Page
{
    private HubWindow? _hub;
    private IDisposable? _reactorHost;
    private string? _chatUrl;
    private bool _webViewInitialized;
    private bool _webViewMode;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navCompletedHandler;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>? _navStartingHandler;

    public ChatPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Tear down Reactor (if mounted) and detach WebView2 nav handlers.
        var host = _reactorHost;
        _reactorHost = null;
        try { host?.Dispose(); } catch { /* tear-down race — non-fatal */ }

        if (WebView.CoreWebView2 != null)
        {
            if (_navCompletedHandler != null)
                WebView.CoreWebView2.NavigationCompleted -= _navCompletedHandler;
            if (_navStartingHandler != null)
                WebView.CoreWebView2.NavigationStarting -= _navStartingHandler;
        }

        if (_hub is not null)
            _hub.SettingsSaved -= OnSettingsSaved;
    }

    public void Initialize(HubWindow hub)
    {
        _hub = hub;

        // Compute a "open in browser" URL once so the toolbar button works
        // even when the gateway isn't fully reachable yet.
        if (hub.Settings is not null)
        {
            var gatewayUrl = hub.Settings.GetEffectiveGatewayUrl();
            if (!string.IsNullOrEmpty(gatewayUrl) &&
                GatewayChatUrlBuilder.TryBuildChatUrl(gatewayUrl, hub.Settings.Token, out var url, out _))
            {
                _chatUrl = url;
            }
        }

        // Re-mount on settings change so toggling "Use standard Gateway Chat
        // interface" swaps the surface live.
        hub.SettingsSaved -= OnSettingsSaved;
        hub.SettingsSaved += OnSettingsSaved;

        ApplyChatSurface();
    }

    private void OnSettingsSaved(object? sender, EventArgs e) => ApplyChatSurface();

    private void ApplyChatSurface()
    {
        if (_hub?.Settings is null) return;

        var useLegacy = _hub.Settings.UseLegacyWebChat;
        if (useLegacy)
            ShowWebViewSurface();
        else
            ShowReactorSurface();
    }

    private void ShowReactorSurface()
    {
        // Hide WebView2-specific UI; mount Reactor host (idempotent).
        _webViewMode = false;
        WebView.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        HomeButton.Visibility = Visibility.Collapsed;
        RefreshButton.Visibility = Visibility.Collapsed;
        DevToolsButton.Visibility = Visibility.Collapsed;

        if (_reactorHost is not null)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            ChatHost.Visibility = Visibility.Visible;
            return;
        }

        var provider = (App.Current as App)?.ChatProvider;
        if (provider is null)
        {
            PlaceholderPanel.Visibility = Visibility.Visible;
            ChatHost.Visibility = Visibility.Collapsed;
            return;
        }

        PlaceholderPanel.Visibility = Visibility.Collapsed;
        ChatHost.Visibility = Visibility.Visible;
        _reactorHost = ((Window)_hub!).MountReactorChat(ChatHost, provider);
    }

    private void ShowWebViewSurface()
    {
        // Tear down Reactor (so the WebView2 owns the row) and (re)init WebView2.
        _webViewMode = true;
        var host = _reactorHost;
        _reactorHost = null;
        try { host?.Dispose(); } catch { /* tear-down race — non-fatal */ }

        ChatHost.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        HomeButton.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible;
        DevToolsButton.Visibility = Visibility.Visible;

        if (_webViewInitialized)
        {
            // Already initialized — just show it (or re-navigate home).
            ErrorPanel.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(_chatUrl))
                WebView.CoreWebView2?.Navigate(_chatUrl);
            return;
        }

        if (_hub?.Settings is null) return;
        _ = InitializeWebViewAsync(_hub.Settings);
    }

    private async Task InitializeWebViewAsync(SettingsManager settings)
    {
        try
        {
            var gatewayUrl = settings.GetEffectiveGatewayUrl();
            if (string.IsNullOrEmpty(gatewayUrl))
            {
                PlaceholderPanel.Visibility = Visibility.Visible;
                return;
            }

            if (!GatewayChatHelper.TryBuildChatUrl(gatewayUrl, settings.Token, out var chatUrl, out var errorMessage))
            {
                PlaceholderPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = errorMessage;
                return;
            }

            _chatUrl = chatUrl;

            PlaceholderPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;

            await GatewayChatHelper.InitializeWebView2Async(WebView);
            _webViewInitialized = true;

            _navCompletedHandler = (s, e) =>
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;

                if (e.IsSuccess)
                {
                    // Hide the web Control UI sidebar — Hub NavigationView handles top-level nav.
                    _ = WebView.CoreWebView2.ExecuteScriptAsync(@"
                        (function() {
                            var style = document.createElement('style');
                            style.textContent = 'nav, [data-sidebar], .sidebar, aside { display: none !important; } main, [data-main], .main-content { margin-left: 0 !important; width: 100% !important; max-width: 100% !important; }';
                            document.head.appendChild(style);
                        })();
                    ");
                    ErrorPanel.Visibility = Visibility.Collapsed;
                    WebView.Visibility = Visibility.Visible;
                }
                else if (e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                         e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                         e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionReset ||
                         e.WebErrorStatus == CoreWebView2WebErrorStatus.ServerUnreachable)
                {
                    WebView.Visibility = Visibility.Collapsed;
                    ErrorPanel.Visibility = Visibility.Visible;
                    ErrorText.Text = $"Cannot connect to gateway at {gatewayUrl}\n\nMake sure the gateway is running.";
                }
            };
            WebView.CoreWebView2.NavigationCompleted += _navCompletedHandler;

            _navStartingHandler = (s, e) =>
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
            };
            WebView.CoreWebView2.NavigationStarting += _navStartingHandler;

            WebView.Visibility = Visibility.Visible;
            WebView.CoreWebView2.Navigate(_chatUrl);
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = $"WebView2 failed to initialize:\n{ex.Message}";
        }
    }

    private void OnHome(object sender, RoutedEventArgs e)
    {
        if (_webViewMode && _webViewInitialized && !string.IsNullOrEmpty(_chatUrl))
            WebView.CoreWebView2?.Navigate(_chatUrl);
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_webViewMode && _webViewInitialized)
            WebView.CoreWebView2?.Reload();
    }

    private void OnDevTools(object sender, RoutedEventArgs e)
    {
        if (_webViewMode && _webViewInitialized)
            WebView.CoreWebView2?.OpenDevToolsWindow();
    }

    private void OnPopout(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_chatUrl)) return;
        try { Process.Start(new ProcessStartInfo(_chatUrl) { UseShellExecute = true }); }
        catch { /* shell launch failed — silently ignore */ }
    }
}
