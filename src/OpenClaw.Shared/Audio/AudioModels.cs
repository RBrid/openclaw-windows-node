using System;

namespace OpenClaw.Shared.Audio;

/// <summary>Result of a speech-to-text transcription segment.</summary>
public sealed class TranscriptionResult
{
    public string Text { get; init; } = "";
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Language { get; init; } = "en";
}

/// <summary>Voice-activity detection event.</summary>
public sealed class VadEvent
{
    public bool IsSpeaking { get; init; }
    public float Probability { get; init; }
}

/// <summary>Configuration for the audio pipeline.</summary>
public sealed class AudioPipelineOptions
{
    /// <summary>Path to the Whisper GGML model file.</summary>
    public string ModelPath { get; init; } = "";

    /// <summary>Language code for STT (e.g. "en", "auto").</summary>
    public string Language { get; init; } = "auto";

    /// <summary>Seconds of silence before a speech segment is finalized.</summary>
    public float SilenceTimeoutSeconds { get; init; } = 1.5f;

    /// <summary>Optional audio device ID. Null = system default microphone.</summary>
    public string? DeviceId { get; init; }

    /// <summary>VAD probability threshold (0.0–1.0). Audio above this is considered speech.</summary>
    public float VadThreshold { get; init; } = 0.3f;
}

/// <summary>Pipeline state.</summary>
public enum AudioPipelineState
{
    Stopped,
    Starting,
    Listening,
    Processing,
    Error
}
