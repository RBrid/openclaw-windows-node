using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace OpenClawTray.Chat;

/// <summary>
/// Lightweight in-process Windows TTS helper used by the assistant bubble's
/// hover-revealed Speak icon. Independent of the gateway's <c>tts.speak</c>
/// capability path (which is only wired when node mode is enabled) so the
/// icon works in pure operator mode too.
///
/// Single instance per process; <c>SpeakAsync</c> stops any previous
/// utterance before starting a new one (interrupt-on-click).
/// </summary>
internal static class ChatSpeakHelper
{
    private static MediaPlayer? s_player;
    private static readonly object s_gate = new();

    public static async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Cap to a reasonable length so a click doesn't accidentally play
        // a half-hour novel (assistant bubbles can be large).
        if (text.Length > 4000) text = text[..4000];

        try
        {
            using var synth = new SpeechSynthesizer();
            using var stream = await synth.SynthesizeTextToStreamAsync(text);

            MediaPlayer player;
            lock (s_gate)
            {
                s_player?.Pause();
                s_player?.Dispose();
                s_player = new MediaPlayer();
                player = s_player;
            }
            player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            player.Play();
        }
        catch
        {
            // Best-effort — TTS failure shouldn't crash the chat surface.
        }
    }

    public static void Stop()
    {
        lock (s_gate)
        {
            try { s_player?.Pause(); } catch { }
            try { s_player?.Dispose(); } catch { }
            s_player = null;
        }
    }
}
