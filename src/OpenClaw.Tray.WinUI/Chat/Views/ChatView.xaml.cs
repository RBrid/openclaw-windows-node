using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using global::Windows.System;
using global::Windows.ApplicationModel.DataTransfer;

namespace OpenClawTray.Chat.Views;

public sealed partial class ChatView : UserControl, IDisposable
{
    private ChatViewModel? _vm;
    private bool _disposed;

    public ChatView()
    {
        this.InitializeComponent();
        this.Unloaded += OnUnloaded;
    }

    public ChatViewModel? ViewModel => _vm;

    /// <summary>
    /// Bind the view to a ChatViewModel and kick off the initial load.
    /// Safe to call multiple times — the previous binding is detached first.
    /// </summary>
    public void Bind(ChatViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        if (ReferenceEquals(_vm, vm)) return;

        Detach();
        _vm = vm;
        TimelineList.ItemsSource = vm.Items;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.Items.CollectionChanged += OnItemsChanged;
        vm.AppendedItem += OnAppendedItem;
        SyncHeader();
        SyncComposer();
        UpdateMessageCount();
        UpdateModelPicker();
        UpdateSendEnabled();

        _ = vm.LoadAsync();
    }

    private void Detach()
    {
        if (_vm is null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Items.CollectionChanged -= OnItemsChanged;
        _vm.AppendedItem -= OnAppendedItem;
        _vm = null;
        TimelineList.ItemsSource = null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.HeaderTitle):
            case nameof(ChatViewModel.HeaderBreadcrumb):
            case nameof(ChatViewModel.CurrentIntent):
                SyncHeader();
                break;
            case nameof(ChatViewModel.AvailableModels):
            case nameof(ChatViewModel.SelectedModel):
                UpdateModelPicker();
                break;
            case nameof(ChatViewModel.AllowAllPermissions):
                AllowAllToggle.IsOn = _vm?.AllowAllPermissions ?? false;
                break;
            case nameof(ChatViewModel.IsLoaded):
            case nameof(ChatViewModel.TurnActive):
            case nameof(ChatViewModel.CanSend):
            case nameof(ChatViewModel.CanStop):
                SyncComposer();
                break;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateMessageCount();

    private void OnAppendedItem(object? sender, EventArgs e)
    {
        // ItemsStackPanel.ItemsUpdatingScrollMode=KeepLastItemInView keeps us
        // pinned automatically, but if the user is scrolled away we still
        // honour their position. Force-scroll only when an assistant or user
        // bubble is appended at the very bottom.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_vm is null || _vm.Items.Count == 0) return;
            try { TimelineList.ScrollIntoView(_vm.Items[^1]); }
            catch { /* layout race — non-fatal */ }
        });
    }

    private void SyncHeader()
    {
        if (_vm is null) return;
        HeaderTitleText.Text = _vm.HeaderTitle;
        HeaderBreadcrumbText.Text = _vm.HeaderBreadcrumb;
        if (_vm.CurrentIntent is { Length: > 0 } intent)
        {
            IntentText.Text = $"\u26A1 {intent}";
            IntentText.Visibility = Visibility.Visible;
        }
        else
        {
            IntentText.Visibility = Visibility.Collapsed;
        }
    }

    private void SyncComposer()
    {
        if (_vm is null) return;
        var loaded = _vm.IsLoaded;
        EmptyOverlay.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = _vm.CanStop ? Visibility.Visible : Visibility.Collapsed;
        UpdateSendEnabled();
    }

    private void UpdateSendEnabled()
    {
        if (_vm is null) { SendButton.IsEnabled = false; return; }
        SendButton.IsEnabled = _vm.CanSend && !string.IsNullOrWhiteSpace(InputBox.Text);
    }

    private void UpdateMessageCount()
    {
        if (_vm is null) { MessageCountText.Text = string.Empty; return; }
        var count = 0;
        foreach (var item in _vm.Items)
        {
            if (item is UserItemViewModel or AssistantItemViewModel) count++;
        }
        MessageCountText.Text = count > 0 ? $"\uD83D\uDCAC {count}" : string.Empty;
    }

    private void UpdateModelPicker()
    {
        if (_vm is null) return;
        var current = _vm.SelectedModel;
        ModelPicker.ItemsSource = _vm.AvailableModels;
        if (current is { Length: > 0 } && _vm.AvailableModels.Contains(current))
            ModelPicker.SelectedItem = current;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) => UpdateSendEnabled();

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                     & global::Windows.UI.Core.CoreVirtualKeyStates.Down)
                    == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (shift) return; // newline
        e.Handled = true;
        _ = SendCurrentAsync();
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentAsync();
    }

    private async Task SendCurrentAsync()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text) || _vm is null) return;
        InputBox.Text = string.Empty;
        UpdateSendEnabled();
        try { await _vm.SendAsync(text.Trim()); }
        catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatView] send failed: {ex.Message}"); }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        try { await _vm.StopAsync(); }
        catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatView] stop failed: {ex.Message}"); }
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        if (ModelPicker.SelectedItem is string m) _vm.SelectedModel = m;
    }

    private void OnAllowAllToggled(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.AllowAllPermissions = AllowAllToggle.IsOn;
    }

    private void OnAssistantCopy(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string text)
        {
            try
            {
                var pkg = new DataPackage();
                pkg.SetText(text);
                Clipboard.SetContent(pkg);
            }
            catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatView] copy failed: {ex.Message}"); }
        }
    }

    private async void OnAssistantReadAloud(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is FrameworkElement fe && fe.Tag is string text && !string.IsNullOrWhiteSpace(text))
        {
            try { await _vm.ReadAloudAsync(text); }
            catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatView] read aloud failed: {ex.Message}"); }
        }
    }

    private async void OnPermissionAllow(object sender, RoutedEventArgs e)
        => await RespondToPermissionAsync(sender, allow: true);

    private async void OnPermissionDeny(object sender, RoutedEventArgs e)
        => await RespondToPermissionAsync(sender, allow: false);

    private async Task RespondToPermissionAsync(object sender, bool allow)
    {
        if (_vm is null) return;
        if (sender is FrameworkElement fe && fe.Tag is string requestId)
        {
            try
            {
                if (fe.DataContext is PermissionItemViewModel pvm) pvm.IsResolved = true;
                await _vm.RespondToPermissionAsync(requestId, allow);
            }
            catch (Exception ex) { OpenClawTray.Services.Logger.Warn($"[ChatView] permission resp failed: {ex.Message}"); }
        }
    }
}

/// <summary>
/// Tiny attached behaviour reserved for an upcoming hover-fade refinement of
/// the assistant action strip. Currently inert — the assistant action strip is
/// always visible.
/// </summary>
public static class HoverFade
{
    public static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached", typeof(bool), typeof(HoverFade),
            new PropertyMetadata(false));

    public static bool GetIsAttached(DependencyObject d) => (bool)d.GetValue(IsAttachedProperty);
    public static void SetIsAttached(DependencyObject d, bool value) => d.SetValue(IsAttachedProperty, value);
}
