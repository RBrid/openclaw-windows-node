using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace OpenClaw.Shared.Audio;

/// <summary>
/// Wraps Whisper.net for speech-to-text transcription.
/// Lazily loads the model on first use and caches the factory.
/// Thread-safe: concurrent calls are serialized by a semaphore.
/// </summary>
public sealed class SpeechToTextService : IDisposable
{
    private readonly IOpenClawLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public bool IsModelLoaded => _factory != null;
    public string? LoadedModelPath => _loadedModelPath;

    public SpeechToTextService(IOpenClawLogger logger)
    {
        _logger = logger;
    }

    /// <summary>Load (or reload) the Whisper model from disk.</summary>
    public void LoadModel(string modelPath)
    {
        if (!System.IO.File.Exists(modelPath))
            throw new System.IO.FileNotFoundException($"Whisper model not found: {modelPath}");

        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
        _logger.Info($"Whisper model loaded: {modelPath}");
    }

    /// <summary>Unload the current model and free memory.</summary>
    public void UnloadModel()
    {
        _factory?.Dispose();
        _factory = null;
        _loadedModelPath = null;
        _logger.Info("Whisper model unloaded");
    }

    /// <summary>
    /// Transcribe raw 16 kHz mono PCM float samples.
    /// Returns all detected segments.
    /// </summary>
    public async Task<List<TranscriptionResult>> TranscribeAsync(
        float[] samples,
        string language = "auto",
        CancellationToken cancellationToken = default)
    {
        if (_factory == null)
            throw new InvalidOperationException("No Whisper model is loaded. Call LoadModel first.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var builder = _factory.CreateBuilder()
                .WithLanguage(language == "auto" ? "auto" : language)
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2));

            using var processor = builder.Build();

            using var wavStream = PcmToWavStream(samples, 16000);

            var results = new List<TranscriptionResult>();
            await foreach (var segment in processor.ProcessAsync(wavStream, cancellationToken))
            {
                var text = segment.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    results.Add(new TranscriptionResult
                    {
                        Text = text,
                        Start = segment.Start,
                        End = segment.End,
                        Language = language
                    });
                }
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Convert raw 16-bit PCM float samples to a WAV MemoryStream.
    /// Whisper.net processes WAV streams natively.
    /// </summary>
    private static System.IO.MemoryStream PcmToWavStream(float[] samples, int sampleRate)
    {
        var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        int bitsPerSample = 16;
        short channels = 1;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataSize = samples.Length * blockAlign;

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);

        // fmt subchunk
        writer.Write("fmt "u8);
        writer.Write(16); // subchunk size
        writer.Write((short)1); // PCM format
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);

        // data subchunk
        writer.Write("data"u8);
        writer.Write(dataSize);

        // Convert float [-1.0, 1.0] to int16
        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1.0f, 1.0f);
            var int16 = (short)(clamped * 32767);
            writer.Write(int16);
        }

        writer.Flush();
        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _gate.Dispose();
    }
}
