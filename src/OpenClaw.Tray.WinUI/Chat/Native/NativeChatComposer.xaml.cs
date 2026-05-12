using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OpenClawTray.Chat.Native;
using Windows.System;

namespace OpenClawTray.Chat.Native;

/// <summary>
/// Native WinUI 3 chat composer. Replaces the Reactor-based OpenClawComposer.
/// Single-row layout: multi-line TextBox + Send/Stop button.
///
/// MVP scope: text input + Send. Phase 2 polish (channels, model picker,
/// reasoning mode, attach/mic) will be added later as a row above the
/// TextBox if/when those features are needed.
///
/// The host (NativeChatRoot) wires SendRequested, sets TurnActive when a
/// turn is in progress (to swap Send → Stop), and IsConnected to gate
/// the input.
/// </summary>
public sealed partial class NativeChatComposer : UserControl
{
    public event EventHandler<string>? SendRequested;
    public event EventHandler? StopRequested;

    private bool _turnActive;
    private bool _isConnected = true;

    public NativeChatComposer()
    {
        InitializeComponent();
        UpdateButtonAffordance();
    }

    /// <summary>
    /// True while the assistant is generating a response. Swaps Send → Stop
    /// affordance and routes the button click to <see cref="StopRequested"/>.
    /// </summary>
    public bool TurnActive
    {
        get => _turnActive;
        set
        {
            if (_turnActive == value) return;
            _turnActive = value;
            UpdateButtonAffordance();
        }
    }

    /// <summary>
    /// False when the gateway is disconnected. Disables input + send.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            InputBox.IsEnabled = value;
            UpdateButtonAffordance();
            InputBox.PlaceholderText = value
                ? "Message Assistant (Enter to send)"
                : "Not connected — open Connection settings to pair";
        }
    }

    public void Focus() => InputBox.Focus(FocusState.Programmatic);

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter sends; Shift+Enter inserts a newline.
        if (e.Key == VirtualKey.Enter && !IsShiftDown())
        {
            e.Handled = true;
            TrySend();
        }
    }

    private static bool IsShiftDown()
    {
        var window = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (window & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        => UpdateButtonAffordance();

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (_turnActive)
            StopRequested?.Invoke(this, EventArgs.Empty);
        else
            TrySend();
    }

    private void TrySend()
    {
        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text) || !_isConnected || _turnActive) return;
        InputBox.Text = string.Empty;
        SendRequested?.Invoke(this, text);
    }

    private void UpdateButtonAffordance()
    {
        if (_turnActive)
        {
            SendIcon.Glyph = "\uE71A"; // stop
            SendLabel.Text = "Stop";
            SendButton.IsEnabled = true;
            return;
        }

        SendIcon.Glyph = "\uE724"; // send
        SendLabel.Text = "Send";
        SendButton.IsEnabled = _isConnected && !string.IsNullOrWhiteSpace(InputBox.Text);
    }
}
