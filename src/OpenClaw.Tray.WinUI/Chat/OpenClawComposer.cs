using ChatSample.Chat.Model;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace OpenClawTray.Chat;

/// <summary>
/// Three-row composer surface that mirrors Kenny Hong's <c>ChatShell</c> XAML
/// design (kenehong/native-chat-v2):
///
/// <list type="number">
///   <item><description>Row 1 — three compact <see cref="Microsoft.UI.Xaml.Controls.ComboBox"/>es:
///     <c>Channel</c> (agent identity), <c>Model</c>, and <c>Reasoning</c> mode.</description></item>
///   <item><description>Row 2 — multi-line message <see cref="Microsoft.UI.Xaml.Controls.TextBox"/>
///     with <c>Message Assistant (Enter to send)</c> placeholder.</description></item>
///   <item><description>Row 3 — four right-aligned action buttons (transparent attach / mic / more,
///     plus a filled accent <c>Send</c> button).</description></item>
/// </list>
///
/// Replaces the original <c>InputBar</c> + <c>StatusBar</c> pair from the
/// vendored Reactor sample so our chat surface no longer carries two
/// separate footer rows. The status, working indicator, and permission
/// banner that <c>InputBar</c> used to render are preserved here above the
/// composer, scoped via <see cref="Expr"/>.
/// </summary>
public record OpenClawComposerProps(
    string ConnectionState,
    bool TurnActive,
    ChatPermissionRequest? PendingPermission,
    string ChannelLabel,
    string[] AvailableModels,
    string? CurrentModel,
    Action<string> OnSend,
    Action OnStop,
    Action<string, bool> OnPermissionResponse,
    Action<string> OnModelChanged,
    Action<bool> OnPermissionsChanged);

public sealed class OpenClawComposer : Component<OpenClawComposerProps>
{
    private static readonly string[] s_reasoningOptions = new[] { "Default", "Auto", "Maximum" };

    public override Element Render()
    {
        var inputState = UseState("", threadSafe: true);

        var sendAction = () =>
        {
            var msg = inputState.Value?.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            Props.OnSend(msg);
            inputState.Set("");
        };
        var sendActionRef = UseRef<Action>(sendAction);
        sendActionRef.Current = sendAction;

        var isConnected = Props.ConnectionState == "connected";
        var placeholder = Props.ConnectionState switch
        {
            "connected" => "Message Assistant (Enter to send)",
            "connecting" => "Connecting…",
            _ => "Not connected"
        };

        // ── Row 1: three compact dropdowns ─────────────────────────────
        var channelOptions = new[] { Props.ChannelLabel ?? "main" };
        var channelCombo = ComboBox(channelOptions, 0, _ => { /* read-only for now */ })
            .Set(cb =>
            {
                cb.MinWidth = 80;
                cb.Height = 28;
                cb.FontSize = 11;
                cb.Padding = new Thickness(8, 0, 8, 0);
                cb.CornerRadius = new CornerRadius(4);
            }).VAlign(VerticalAlignment.Center);

        var models = Props.AvailableModels;
        var modelIndex = models is { Length: > 0 } && Props.CurrentModel is { } cur
            ? Array.IndexOf(models, cur) : -1;
        if (modelIndex < 0 && models is { Length: > 0 }) modelIndex = 0;
        var modelDisplay = models is { Length: > 0 } ? models : new[] { Props.CurrentModel ?? "model" };

        var modelCombo = ComboBox(modelDisplay, Math.Max(modelIndex, 0), idx =>
        {
            if (models is { Length: > 0 } && idx >= 0 && idx < models.Length)
                Props.OnModelChanged(models[idx]);
        }).Set(cb =>
        {
            cb.MinWidth = 140;
            cb.Height = 28;
            cb.FontSize = 11;
            cb.Padding = new Thickness(8, 0, 8, 0);
            cb.CornerRadius = new CornerRadius(4);
        }).VAlign(VerticalAlignment.Center);

        var reasoningCombo = ComboBox(s_reasoningOptions, 0, _ => { /* not yet wired */ })
            .Set(cb =>
            {
                cb.MinWidth = 100;
                cb.Height = 28;
                cb.FontSize = 11;
                cb.Padding = new Thickness(8, 0, 8, 0);
                cb.CornerRadius = new CornerRadius(4);
            }).VAlign(VerticalAlignment.Center);

        var dropdownsRow = (FlexRow(channelCombo, modelCombo, reasoningCombo)
            with { ColumnGap = 4 });

        // ── Row 2: multi-line composer textbox ─────────────────────────
        var textbox = TextField(inputState.Value, v => inputState.Set(v))
            .Set(tb =>
            {
                tb.PlaceholderText = placeholder;
                tb.AcceptsReturn = false;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MinHeight = 56;
                tb.IsEnabled = isConnected;
                tb.CornerRadius = new CornerRadius(6);
            })
            .OnMount(fe =>
            {
                var t = (Microsoft.UI.Xaml.Controls.TextBox)fe;
                t.KeyDown += (s, e) =>
                {
                    if (e.Key == global::Windows.System.VirtualKey.Enter)
                    {
                        e.Handled = true;
                        sendActionRef.Current();
                    }
                };
            });

        // ── Row 3: action icons (right-aligned) ────────────────────────
        Element IconButton(string glyph, string tip, Action onClick)
            => Button(
                TextBlock(glyph)
                    .Set(t =>
                    {
                        t.FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons");
                        t.FontSize = 14;
                    }),
                onClick)
            .Set(b =>
            {
                b.Padding = new Thickness(8, 4, 8, 4);
                b.MinWidth = 32; b.MinHeight = 28;
                b.CornerRadius = new CornerRadius(4);
            })
            .Resources(r => r
                .Set("ButtonBackground", new SolidColorBrush(Colors.Transparent))
                .Set("ButtonBackgroundPointerOver", Ref("SubtleFillColorSecondaryBrush"))
                .Set("ButtonBackgroundPressed", Ref("SubtleFillColorTertiaryBrush"))
                .Set("ButtonBorderBrush", new SolidColorBrush(Colors.Transparent))
                .Set("ButtonBorderBrushPointerOver", new SolidColorBrush(Colors.Transparent))
                .Set("ButtonBorderBrushPressed", new SolidColorBrush(Colors.Transparent)))
            .AutomationName(tip);

        var attachBtn = IconButton("\uE723", "Attach", () => { });
        var voiceBtn = IconButton("\uE720", "Voice", () => { });
        var moreBtn = IconButton("\uE712", "More", () => { });

        // Send button (filled accent blue with white glyph) or Stop button when turn active.
        Element actionBtn = Props.TurnActive
            ? Button(
                TextBlock("\uE71A")
                    .Set(t =>
                    {
                        t.FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons");
                        t.FontSize = 14;
                    })
                    .Foreground(new SolidColorBrush(Colors.White)),
                Props.OnStop
            ).Set(b =>
            {
                b.Padding = new Thickness(10, 4, 10, 4);
                b.MinWidth = 36; b.MinHeight = 28;
                b.CornerRadius = new CornerRadius(4);
                b.Background = ChatSample.Chat.UI.Res.Get("SystemFillColorCriticalBrush");
            }).AutomationName("Stop")
            : Button(
                TextBlock("\uE724")
                    .Set(t =>
                    {
                        t.FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons");
                        t.FontSize = 14;
                    })
                    .Foreground(new SolidColorBrush(Colors.White)),
                sendAction
            ).Set(b =>
            {
                b.Padding = new Thickness(10, 4, 10, 4);
                b.MinWidth = 36; b.MinHeight = 28;
                b.CornerRadius = new CornerRadius(4);
                b.IsEnabled = isConnected && !string.IsNullOrWhiteSpace(inputState.Value);
                // Kenny's exact accent for Send: #0078D4
                b.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
            }).AutomationName("Send");

        var actionsRow = Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
            (FlexRow(attachBtn, voiceBtn, moreBtn, actionBtn)
                with { ColumnGap = 4 })
            .HAlign(HorizontalAlignment.Right)
            .Grid(row: 0, column: 1)
        );

        // ── Optional working / permission banners above the composer ──
        Element workingBanner = Props.TurnActive
            ? (FlexRow(
                ProgressRing().Size(16, 16),
                Caption("Assistant is working…").Foreground(SecondaryText)
              ) with { ColumnGap = 8 }).Padding(16, 8, 16, 0)
            : Empty();

        Element permissionBanner = Props.PendingPermission is { } perm
            ? Border(
                HStack(8,
                    TextBlock($"⚠ {perm.ToolName}: {perm.Detail}")
                        .Set(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.CharacterEllipsis; })
                        .HAlign(HorizontalAlignment.Stretch),
                    Button("Allow", () => Props.OnPermissionResponse(perm.RequestId, true))
                        .Background(Accent).Set(b => { b.CornerRadius = new CornerRadius(4); b.Padding = new Thickness(12, 4, 12, 4); b.MinWidth = 0; b.MinHeight = 0; }),
                    Button("Deny", () => Props.OnPermissionResponse(perm.RequestId, false))
                        .Set(b => { b.CornerRadius = new CornerRadius(4); b.Padding = new Thickness(12, 4, 12, 4); b.MinWidth = 0; b.MinHeight = 0; })
                ).Padding(12, 8, 12, 8)
              ).Background(SubtleFill).CornerRadius(8).WithBorder(DividerStroke, 1).Margin(12, 4, 12, 4)
            : Empty();

        return VStack(0,
            workingBanner,
            permissionBanner,
            Border(
                VStack(8, dropdownsRow, textbox, actionsRow)
            ).Padding(14, 12, 14, 12)
             .Set(b =>
             {
                 // Top divider only — mirrors Kenny's ChatShell ComposerBorder.
                 b.BorderThickness = new Thickness(0, 1, 0, 0);
                 b.BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStrokeColorDefaultBrush"];
             })
        );
    }
}
