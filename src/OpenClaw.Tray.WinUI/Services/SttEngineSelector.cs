using System;

namespace OpenClawTray.Services;

/// <summary>
/// Pure decision function: given a user's preferred STT engine and the
/// current readiness of each engine, pick which one will actually serve
/// the next call and (if a fallback occurred) report why.
///
/// Extracted from the inline logic in NodeService.OnStt* so the
/// selection rules can be unit-tested without spinning up VoiceService
/// or SpeechToTextService (both of which require WinUI / NAudio /
/// real device access).
///
/// Rules — these are the contract pinned by SttEngineSelectorTests:
///
/// 1. Default to Whisper when the preferred engine string is null,
///    empty, or unrecognized.
/// 2. Whisper preference + Whisper ready → Whisper, no fallback.
/// 3. Whisper preference + Whisper NOT ready + WinRT ready
///    → WinRT, fallbackReason = "whisper-model-not-ready".
///    This is the "happy degradation" case while a Whisper model
///    downloads on first launch.
/// 4. Whisper preference + neither engine ready → Whisper,
///    fallbackReason = "whisper-and-winrt-unavailable".
///    The capability layer will surface a clear error; we still
///    report the user's preference unchanged.
/// 5. WinRT preference + WinRT ready → WinRT, no fallback.
///    NEVER silently upgrade to Whisper here — explicit user choice
///    is honored even if Whisper happens to be ready.
/// 6. WinRT preference + WinRT NOT ready → WinRT,
///    fallbackReason = "winrt-unavailable". Again, no silent upgrade.
/// </summary>
internal static class SttEngineSelector
{
    public const string FallbackWhisperModelNotReady = "whisper-model-not-ready";
    public const string FallbackWinRtUnavailable = "winrt-unavailable";
    public const string FallbackBothUnavailable = "whisper-and-winrt-unavailable";

    public readonly record struct Pick(string Engine, string? FallbackReason);

    public static Pick PickEngine(string? preferredEngine, bool whisperReady, bool winRtReady)
    {
        var preferred = string.IsNullOrWhiteSpace(preferredEngine)
            ? SharedConstants.EngineWhisper
            : preferredEngine!.Trim().ToLowerInvariant();

        // Treat unknown values as "whisper preference" — the system shouldn't
        // hard-fail when a stale/typo SttEngine value lands in settings.json.
        if (preferred != SharedConstants.EngineWhisper && preferred != SharedConstants.EngineWinRt)
        {
            preferred = SharedConstants.EngineWhisper;
        }

        if (preferred == SharedConstants.EngineWhisper)
        {
            if (whisperReady)
                return new Pick(SharedConstants.EngineWhisper, null);
            if (winRtReady)
                return new Pick(SharedConstants.EngineWinRt, FallbackWhisperModelNotReady);
            // Both unavailable: report the preference unchanged with a
            // diagnostic reason. The dispatch layer will fail the call.
            return new Pick(SharedConstants.EngineWhisper, FallbackBothUnavailable);
        }

        // preferred == winrt: explicit user choice; never upgrade silently.
        if (winRtReady)
            return new Pick(SharedConstants.EngineWinRt, null);
        return new Pick(SharedConstants.EngineWinRt, FallbackWinRtUnavailable);
    }

    // The engine identifier strings live on SttCapability in OpenClaw.Shared,
    // but mirroring them here keeps the helper free of any direct dependency
    // outside its own assembly. The values are pinned identical by
    // SttEngineSelectorTests.MirroredConstantsMatchSttCapability.
    internal static class SharedConstants
    {
        public const string EngineWhisper = "whisper";
        public const string EngineWinRt = "winrt";
    }
}
