using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Shared;
using OpenClawTray.Services;
using OpenClawTray.Windows;
using System;
using System.Linq;
using System.Threading;

namespace OpenClawTray.Pages;

public sealed partial class VoiceSettingsPage : Page
{
    private HubWindow? _hub;
    private VoiceService? _voiceService;
    private bool _suppressEvents;
    private CancellationTokenSource? _downloadCts;

    public VoiceSettingsPage()
    {
        InitializeComponent();
    }

    public void Initialize(HubWindow hub, VoiceService? voiceService)
    {
        _hub = hub;
        _voiceService = voiceService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (_hub?.Settings == null) return;
        _suppressEvents = true;

        try
        {
            var settings = _hub.Settings;

            SttEnabledToggle.IsOn = settings.NodeSttEnabled;

            // Select model in combo
            for (int i = 0; i < ModelCombo.Items.Count; i++)
            {
                if (ModelCombo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), settings.SttModelName, StringComparison.OrdinalIgnoreCase))
                {
                    ModelCombo.SelectedIndex = i;
                    break;
                }
            }

            // Select language
            for (int i = 0; i < LanguageCombo.Items.Count; i++)
            {
                if (LanguageCombo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), settings.SttLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageCombo.SelectedIndex = i;
                    break;
                }
            }
            if (LanguageCombo.SelectedIndex < 0)
                LanguageCombo.SelectedIndex = 0; // auto

            SilenceSlider.Value = settings.SttSilenceTimeout;
            TtsResponseToggle.IsOn = settings.VoiceTtsEnabled;
            AudioFeedbackToggle.IsOn = settings.VoiceAudioFeedback;

            LoadTtsSettings(settings);
            UpdateModelStatus();
            UpdateCardVisibility();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void UpdateModelStatus()
    {
        if (_voiceService == null || _hub?.Settings == null) return;

        var modelName = _hub.Settings.SttModelName;
        if (_voiceService.CheckModelDownloaded(modelName))
        {
            ModelStatusText.Text = "✅ Model ready";
            DownloadButtonText.Text = "Re-download";
        }
        else
        {
            ModelStatusText.Text = "⬇️ Download required";
            DownloadButtonText.Text = "Download Model";
        }
    }

    private void UpdateCardVisibility()
    {
        ModelCard.Opacity = SttEnabledToggle.IsOn ? 1.0 : 0.5;
        ModelCard.IsHitTestVisible = SttEnabledToggle.IsOn;
    }

    private void OnSttToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.NodeSttEnabled = SttEnabledToggle.IsOn;
        _hub.Settings.Save();
        UpdateCardVisibility();
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;

        if (ModelCombo.SelectedItem is ComboBoxItem item && item.Tag is string modelName)
        {
            _hub.Settings.SttModelName = modelName;
            _hub.Settings.Save();
            UpdateModelStatus();
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;

        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _hub.Settings.SttLanguage = lang;
            _hub.Settings.Save();
        }
    }

    private void OnSilenceChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.SttSilenceTimeout = (float)SilenceSlider.Value;
        _hub.Settings.Save();
    }

    private void OnTtsResponseToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.VoiceTtsEnabled = TtsResponseToggle.IsOn;
        _hub.Settings.Save();
    }

    private void OnAudioFeedbackToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.VoiceAudioFeedback = AudioFeedbackToggle.IsOn;
        _hub.Settings.Save();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (_voiceService == null || _hub?.Settings == null) return;

        // Cancel any in-progress download
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();

        DownloadButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        ModelStatusText.Text = "Downloading...";

        try
        {
            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.total > 0)
                    {
                        var pct = (double)p.downloaded / p.total * 100;
                        DownloadProgress.Value = pct;
                        ModelStatusText.Text = $"Downloading... {pct:F0}%";
                    }
                });
            });

            await _voiceService.DownloadModelAsync(
                _hub.Settings.SttModelName,
                progress,
                _downloadCts.Token);

            ModelStatusText.Text = "✅ Model ready";
            DownloadButtonText.Text = "Re-download";
        }
        catch (OperationCanceledException)
        {
            ModelStatusText.Text = "Download canceled";
        }
        catch (Exception ex)
        {
            ModelStatusText.Text = $"❌ {ex.Message}";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── TTS Voice Selection ──

    private void LoadTtsSettings(SettingsManager settings)
    {
        // Provider
        var provider = settings.TtsProvider;
        for (int i = 0; i < TtsProviderCombo.Items.Count; i++)
        {
            if (TtsProviderCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            {
                TtsProviderCombo.SelectedIndex = i;
                break;
            }
        }
        if (TtsProviderCombo.SelectedIndex < 0)
            TtsProviderCombo.SelectedIndex = 0;

        // Windows voices
        PopulateWindowsVoices(settings);

        // ElevenLabs
        ElevenLabsApiKeyBox.Password = settings.TtsElevenLabsApiKey ?? "";
        ElevenLabsVoiceIdBox.Text = settings.TtsElevenLabsVoiceId ?? "";
        ElevenLabsModelBox.Text = settings.TtsElevenLabsModel ?? "";

        UpdateTtsProviderVisibility();
    }

    private void PopulateWindowsVoices(SettingsManager settings)
    {
        WindowsVoiceCombo.Items.Clear();

        try
        {
            var voices = global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices;
            int selectedIdx = 0;

            foreach (var voice in voices)
            {
                var label = $"{voice.DisplayName} ({voice.Language})";
                var item = new ComboBoxItem { Content = label, Tag = voice.Id };
                WindowsVoiceCombo.Items.Add(item);

                // Match current setting
                if (!string.IsNullOrEmpty(settings.TtsElevenLabsVoiceId) &&
                    (string.Equals(voice.Id, settings.TtsElevenLabsVoiceId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(voice.DisplayName, settings.TtsElevenLabsVoiceId, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedIdx = WindowsVoiceCombo.Items.Count - 1;
                }
            }

            if (WindowsVoiceCombo.Items.Count > 0)
                WindowsVoiceCombo.SelectedIndex = selectedIdx;
        }
        catch (Exception ex)
        {
            WindowsVoiceCombo.Items.Add(new ComboBoxItem { Content = $"Error loading voices: {ex.Message}", IsEnabled = false });
        }
    }

    private void UpdateTtsProviderVisibility()
    {
        var isElevenLabs = TtsProviderCombo.SelectedItem is ComboBoxItem item &&
            string.Equals(item.Tag?.ToString(), "elevenlabs", StringComparison.OrdinalIgnoreCase);

        WindowsVoicePanel.Visibility = isElevenLabs ? Visibility.Collapsed : Visibility.Visible;
        ElevenLabsPanel.Visibility = isElevenLabs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTtsProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;

        if (TtsProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string provider)
        {
            _hub.Settings.TtsProvider = provider;
            _hub.Settings.Save();
        }
        UpdateTtsProviderVisibility();
    }

    private void OnWindowsVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;

        if (WindowsVoiceCombo.SelectedItem is ComboBoxItem item && item.Tag is string voiceId)
        {
            // Store the selected Windows voice in TtsElevenLabsVoiceId field
            // (reused for Windows voice selection when provider is "windows")
            _hub.Settings.TtsElevenLabsVoiceId = voiceId;
            _hub.Settings.Save();
        }
    }

    private async void OnPreviewVoiceClick(object sender, RoutedEventArgs e)
    {
        if (_hub?.Settings == null) return;

        PreviewVoiceButton.IsEnabled = false;
        PreviewVoiceButton.Content = "▶ Playing...";

        try
        {
            var tts = new TextToSpeechService(new AppLogger(), _hub.Settings);
            try
            {
                await tts.SpeakAsync(new OpenClaw.Shared.Capabilities.TtsSpeakArgs
                {
                    Text = "Hello! I'm Molty, your voice assistant. How can I help you today?",
                    Provider = _hub.Settings.TtsProvider,
                    VoiceId = WindowsVoiceCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null,
                    Interrupt = true
                });
            }
            finally
            {
                tts.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Show error inline
            PreviewVoiceButton.Content = $"❌ {ex.Message}";
            await System.Threading.Tasks.Task.Delay(3000);
        }
        finally
        {
            PreviewVoiceButton.IsEnabled = true;
            PreviewVoiceButton.Content = "▶ Preview Voice";
        }
    }

    private void OnElevenLabsKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.TtsElevenLabsApiKey = ElevenLabsApiKeyBox.Password;
        _hub.Settings.Save();
    }

    private void OnElevenLabsVoiceIdChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.TtsElevenLabsVoiceId = ElevenLabsVoiceIdBox.Text;
        _hub.Settings.Save();
    }

    private void OnElevenLabsModelChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _hub?.Settings == null) return;
        _hub.Settings.TtsElevenLabsModel = ElevenLabsModelBox.Text;
        _hub.Settings.Save();
    }
}
