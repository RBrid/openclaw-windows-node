namespace OpenClawTray.Chat;

/// <summary>
/// Per-entry metadata maintained by <see cref="OpenClawChatDataProvider"/>
/// in parallel to the vendored <see cref="ChatSample.Chat.Model.ChatTimelineItem"/>.
/// Tracks values that the upstream <c>ChatTimelineItem</c> record doesn't
/// carry — specifically the wall-clock timestamp of when the entry was
/// created and which model was active at that moment — so the timeline
/// renderer can show a "<c>&lt;sender&gt; · &lt;time&gt; · &lt;model&gt;</c>"
/// footer beneath each message.
/// </summary>
/// <param name="Timestamp">
/// Local-time timestamp of when the entry was created. <c>null</c> when the
/// source event didn't carry a timestamp (e.g. live UI-only status entries).
/// </param>
/// <param name="Model">
/// Snapshot of the model name active when the entry was created (typically
/// taken from <see cref="OpenClaw.Shared.SessionInfo.Model"/>). <c>null</c>
/// when the model is unknown.
/// </param>
public sealed record ChatEntryMetadata(DateTimeOffset? Timestamp, string? Model);
