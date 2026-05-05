using OpenClaw.Shared.Capabilities;
using OpenClawTray.Services;

namespace OpenClaw.Tray.Tests;

/// <summary>
/// Pins the engine-selection rules that drive every stt.* dispatch in
/// NodeService. Whisper is the default; transparent fallback to WinRT is
/// allowed only when the user preferred Whisper but it's not ready.
/// Explicit WinRT preference must never silently upgrade to Whisper —
/// even if Whisper finishes downloading mid-session.
///
/// See SttEngineSelector for the full rule table.
/// </summary>
public sealed class SttEngineSelectorTests
{
    // === Whisper preference ===

    [Fact]
    public void WhisperPreferred_BothReady_PicksWhisper_NoFallback()
    {
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWhisper, whisperReady: true, winRtReady: true);
        Assert.Equal(SttCapability.EngineWhisper, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Fact]
    public void WhisperPreferred_OnlyWhisperReady_PicksWhisper_NoFallback()
    {
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWhisper, whisperReady: true, winRtReady: false);
        Assert.Equal(SttCapability.EngineWhisper, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Fact]
    public void WhisperPreferred_WhisperNotReady_WinRtReady_FallsBackToWinRt()
    {
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWhisper, whisperReady: false, winRtReady: true);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Equal(SttEngineSelector.FallbackWhisperModelNotReady, pick.FallbackReason);
    }

    [Fact]
    public void WhisperPreferred_NeitherReady_KeepsWhisperPreference_WithDiagnosticReason()
    {
        // Both engines down: report the user's preference unchanged so the
        // dispatch layer can fail with a clear "Whisper not available" path.
        // The fallback reason makes it obvious we tried both.
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWhisper, whisperReady: false, winRtReady: false);
        Assert.Equal(SttCapability.EngineWhisper, pick.Engine);
        Assert.Equal(SttEngineSelector.FallbackBothUnavailable, pick.FallbackReason);
    }

    // === WinRT preference (explicit user choice — no silent upgrade) ===

    [Fact]
    public void WinRtPreferred_WinRtReady_PicksWinRt_NoFallback()
    {
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWinRt, whisperReady: false, winRtReady: true);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Fact]
    public void WinRtPreferred_BothReady_StillPicksWinRt_NoSilentUpgradeToWhisper()
    {
        // This is the key invariant: explicit user choice is honored.
        // A user who picked WinRT does NOT get silently upgraded to Whisper
        // when the model finishes downloading.
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWinRt, whisperReady: true, winRtReady: true);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Fact]
    public void WinRtPreferred_WinRtNotReady_KeepsWinRtPreference_WithDiagnosticReason()
    {
        // Even when WinRT is unavailable, do NOT fall back to Whisper —
        // the user explicitly opted out of it. Surface a clear diagnostic.
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWinRt, whisperReady: true, winRtReady: false);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Equal(SttEngineSelector.FallbackWinRtUnavailable, pick.FallbackReason);
    }

    [Fact]
    public void WinRtPreferred_NeitherReady_KeepsWinRtPreference_WithDiagnosticReason()
    {
        var pick = SttEngineSelector.PickEngine(SttCapability.EngineWinRt, whisperReady: false, winRtReady: false);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Equal(SttEngineSelector.FallbackWinRtUnavailable, pick.FallbackReason);
    }

    // === Default / unknown / case insensitivity ===

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmpty_TreatedAsWhisperPreference(string? input)
    {
        var pick = SttEngineSelector.PickEngine(input, whisperReady: true, winRtReady: true);
        Assert.Equal(SttCapability.EngineWhisper, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Theory]
    [InlineData("WHISPER")]
    [InlineData("Whisper")]
    [InlineData("  whisper  ")]
    public void CaseAndWhitespaceInsensitive_ForWhisper(string input)
    {
        var pick = SttEngineSelector.PickEngine(input, whisperReady: true, winRtReady: true);
        Assert.Equal(SttCapability.EngineWhisper, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Theory]
    [InlineData("WINRT")]
    [InlineData("WinRt")]
    [InlineData("  winrt  ")]
    public void CaseAndWhitespaceInsensitive_ForWinRt(string input)
    {
        var pick = SttEngineSelector.PickEngine(input, whisperReady: true, winRtReady: true);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Null(pick.FallbackReason);
    }

    [Theory]
    [InlineData("azure")]
    [InlineData("openai")]
    [InlineData("garbage")]
    public void Unknown_TreatedAsWhisperPreference_NoSurprises(string input)
    {
        // A typo or stale value in settings.json must not hard-fail the
        // selector. Default to Whisper preference and let the normal
        // fallback rules apply.
        var pick = SttEngineSelector.PickEngine(input, whisperReady: false, winRtReady: true);
        Assert.Equal(SttCapability.EngineWinRt, pick.Engine);
        Assert.Equal(SttEngineSelector.FallbackWhisperModelNotReady, pick.FallbackReason);
    }

    [Fact]
    public void MirroredConstantsMatchSttCapability()
    {
        // SttEngineSelector mirrors the engine identifiers locally to keep
        // the helper free of circular deps. If SttCapability ever renames
        // these, this test pins the rename so the selector can't drift.
        Assert.Equal(SttCapability.EngineWhisper, SttEngineSelector.SharedConstants.EngineWhisper);
        Assert.Equal(SttCapability.EngineWinRt, SttEngineSelector.SharedConstants.EngineWinRt);
    }
}
