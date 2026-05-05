using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClaw.Shared.Capabilities;

/// <summary>
/// Speech-to-text node capability. Allows the OpenClaw agent to
/// trigger microphone listening and receive transcribed text.
/// </summary>
public sealed class SttCapability : NodeCapabilityBase
{
    public const string ListenCommand = "stt.listen";
    public const string StatusCommand = "stt.status";

    private static readonly string[] _commands = [ListenCommand, StatusCommand];

    public override string Category => "stt";
    public override IReadOnlyList<string> Commands => _commands;

    /// <summary>
    /// Fired when the agent requests listening. The handler should start
    /// the microphone, wait for speech + silence, transcribe, and return
    /// the text result.
    /// </summary>
    public event Func<SttListenArgs, CancellationToken, Task<SttListenResult>>? ListenRequested;

    /// <summary>
    /// Fired when the agent queries STT status (model loaded, etc.).
    /// </summary>
    public event Func<Task<SttStatusResult>>? StatusRequested;

    public SttCapability(IOpenClawLogger logger) : base(logger) { }

    public override Task<NodeInvokeResponse> ExecuteAsync(NodeInvokeRequest request)
        => ExecuteAsync(request, CancellationToken.None);

    public override async Task<NodeInvokeResponse> ExecuteAsync(
        NodeInvokeRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            ListenCommand => await HandleListenAsync(request, cancellationToken),
            StatusCommand => await HandleStatusAsync(),
            _ => Error($"Unknown command: {request.Command}")
        };
    }

    private async Task<NodeInvokeResponse> HandleListenAsync(
        NodeInvokeRequest request,
        CancellationToken cancellationToken)
    {
        if (ListenRequested == null)
            return Error("STT listen not available");

        var timeoutMs = GetIntArg(request.Args, "timeoutMs", 30000);
        if (timeoutMs < 1000) timeoutMs = 1000;
        if (timeoutMs > 120000) timeoutMs = 120000;

        var language = GetStringArg(request.Args, "language", "auto") ?? "auto";

        var args = new SttListenArgs
        {
            TimeoutMs = timeoutMs,
            Language = language
        };

        Logger.Info($"stt.listen: timeoutMs={timeoutMs}, language={language}");

        try
        {
            var result = await ListenRequested(args, cancellationToken);
            return Success(new
            {
                text = result.Text,
                language = result.Language,
                durationMs = result.DurationMs,
                segments = result.Segments
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error("Listen canceled");
        }
        catch (TimeoutException)
        {
            return Error("No speech detected within timeout");
        }
        catch (Exception ex)
        {
            Logger.Error("STT listen failed", ex);
            return Error($"Listen failed: {ex.Message}");
        }
    }

    private async Task<NodeInvokeResponse> HandleStatusAsync()
    {
        if (StatusRequested == null)
            return Error("STT status not available");

        try
        {
            var result = await StatusRequested();
            return Success(new
            {
                modelLoaded = result.ModelLoaded,
                modelName = result.ModelName,
                isListening = result.IsListening
            });
        }
        catch (Exception ex)
        {
            Logger.Error("STT status failed", ex);
            return Error($"Status failed: {ex.Message}");
        }
    }
}

public sealed class SttListenArgs
{
    public int TimeoutMs { get; set; } = 30000;
    public string Language { get; set; } = "auto";
}

public sealed class SttListenResult
{
    public string Text { get; set; } = "";
    public string? Language { get; set; }
    public int DurationMs { get; set; }
    public List<SttSegment>? Segments { get; set; }
}

public sealed class SttSegment
{
    public string Text { get; set; } = "";
    public int StartMs { get; set; }
    public int EndMs { get; set; }
}

public sealed class SttStatusResult
{
    public bool ModelLoaded { get; set; }
    public string? ModelName { get; set; }
    public bool IsListening { get; set; }
}
