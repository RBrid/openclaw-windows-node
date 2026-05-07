using ChatSample.Chat.Model;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using OpenClaw.Shared;
using OpenClawTray.Chat;
using OpenClawTray.Helpers;
using OpenClawTray.Services;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinUIEx;

namespace OpenClawTray.Windows;

public sealed partial class ChatWindow : WindowEx
{
    private readonly string _gatewayUrl;
    private readonly string _token;
    private readonly string _chatUrl;
    private IDisposable? _reactorHost;
    private bool _webViewInitialized;
    private bool _webViewMode;
    public bool IsClosed { get; private set; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int val, int size);

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT2 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT2 rcMonitor;
        public RECT2 rcWork;
        public int dwFlags;
    }

    public ChatWindow(string gatewayUrl, string token)
    {
        _gatewayUrl = gatewayUrl;
        _token = token;
        _chatUrl = BuildChatUrl(gatewayUrl, token);
        InitializeComponent();

        this.SetWindowSize(480, 640);
        this.SetIcon(IconHelper.GetStatusIconPath(ConnectionStatus.Connected));

        // Set as tool window (hidden from taskbar) + remove system caption
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        var wStyle = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, wStyle & ~WS_CAPTION & ~WS_THICKFRAME);

        // Rounded corners (Windows 11)
        var cornerPref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Auto-hide when clicking outside the panel
        Activated += OnWindowActivated;

        // Hide instead of close — preserves Reactor state for instant reopen
        Closed += OnWindowClosing;

        // Subscribe to global SettingsChanged so the surface swaps when the
        // user toggles "Use standard Gateway Chat interface" while the
        // pre-warmed window is alive.
        if (App.Current is App app)
            app.SettingsChanged += OnAppSettingsChanged;

        ApplyChatSurface();
    }

    private void OnAppSettingsChanged(object? sender, EventArgs e) => ApplyChatSurface();

    private void ApplyChatSurface()
    {
        var useLegacy = (App.Current as App)?.Settings?.UseLegacyWebChat ?? false;
        if (useLegacy)
            ShowWebViewSurface();
        else
            ShowReactorSurface();
    }

    private void ShowReactorSurface()
    {
        _webViewMode = false;
        WebView.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        TryMountReactorChat();
    }

    private void ShowWebViewSurface()
    {
        _webViewMode = true;

        // Tear down Reactor so the WebView2 owns the row.
        var host = _reactorHost;
        _reactorHost = null;
        try { host?.Dispose(); } catch { /* tear-down race — non-fatal */ }

        ChatHost.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Collapsed;

        if (_webViewInitialized)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(_chatUrl))
                WebView.CoreWebView2?.Navigate(_chatUrl);
            return;
        }

        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;

            await GatewayChatHelper.InitializeWebView2Async(WebView);
            _webViewInitialized = true;

            WebView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess)
                {
                    WebView.Visibility = Visibility.Collapsed;
                    ErrorPanel.Visibility = Visibility.Visible;
                    ErrorText.Text = e.WebErrorStatus switch
                    {
                        CoreWebView2WebErrorStatus.CannotConnect or
                        CoreWebView2WebErrorStatus.ConnectionReset or
                        CoreWebView2WebErrorStatus.ServerUnreachable or
                        CoreWebView2WebErrorStatus.Timeout =>
                            "The gateway is not reachable. Check that it is running and try again.",
                        _ => $"Unable to load chat. Please try again. ({e.WebErrorStatus})"
                    };
                }
                else
                {
                    ErrorPanel.Visibility = Visibility.Collapsed;
                    WebView.Visibility = Visibility.Visible;
                }
            };

            WebView.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(_chatUrl))
                WebView.CoreWebView2.Navigate(_chatUrl);
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = $"WebView2 failed: {ex.Message}";
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        if (!_webViewInitialized || string.IsNullOrEmpty(_chatUrl)) return;
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Visible;
        WebView.CoreWebView2?.Navigate(_chatUrl);
    }

    private void TryMountReactorChat()
    {
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
        _reactorHost = ((Window)this).MountReactorChat(ChatHost, provider);
    }

    private static string BuildChatUrl(string gatewayUrl, string token)
    {
        return GatewayChatUrlBuilder.TryBuildChatUrl(gatewayUrl, token, out var url, out _)
            ? url
            : $"http://127.0.0.1:19001?token={Uri.EscapeDataString(token)}";
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            this.Hide();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }

    /// <summary>Position near the system tray and show with animation.</summary>
    public void ShowNearTray()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        GetCursorPos(out POINT pt);

        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref mi);
        var work = mi.rcWork;

        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        int panelWPx = (int)(480 * scale);
        int panelHPx = (int)(640 * scale);

        int margin = 8;
        int x = work.Right - panelWPx - margin;
        int y = work.Bottom - panelHPx - margin;

        this.Move(x, y);
        this.SetWindowSize(480, 640);

        // Provider may have arrived after construction — re-apply surface so
        // a Reactor-mode window swaps placeholder → live tree on first show.
        ApplyChatSurface();

        this.Show();
        SetForegroundWindow(hwnd);
    }

    /// <summary>Show near tray. Reactor renders synchronously so no animation gating needed.</summary>
    public void ShowNearTrayAnimated() => ShowNearTray();

    private void OnWindowClosing(object sender, WindowEventArgs args)
    {
        // Intercept close → hide instead (keeps Reactor state warm).
        args.Handled = true;
        this.Hide();
    }

    /// <summary>Actually close and dispose (called on app shutdown).</summary>
    public void ForceClose()
    {
        Closed -= OnWindowClosing;
        if (App.Current is App app)
            app.SettingsChanged -= OnAppSettingsChanged;
        IsClosed = true;
        try { _reactorHost?.Dispose(); } catch { }
        _reactorHost = null;
        Close();
    }

    private void OnPopout(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_chatUrl)) return;
        try { Process.Start(new ProcessStartInfo(_chatUrl) { UseShellExecute = true }); } catch { }
    }
}
