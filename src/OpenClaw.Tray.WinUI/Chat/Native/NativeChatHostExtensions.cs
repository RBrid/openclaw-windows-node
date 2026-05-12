using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Chat;

namespace OpenClawTray.Chat.Native;

/// <summary>
/// Helper for hosting the native <see cref="NativeChatRoot"/> inside an existing
/// XAML window/page. Replaces <c>ReactorChatHostExtensions.MountReactorChat</c>.
///
/// The native chat surface lives directly in the XAML tree (no separate render
/// loop), so mounting is just "set the target Border's Child to a new
/// NativeChatRoot". The returned <see cref="IDisposable"/> clears the Child
/// and detaches event subscriptions when the page/window unloads.
/// </summary>
public static class NativeChatHostExtensions
{
    /// <summary>
    /// Builds a "post to UI thread" callback suitable for
    /// <see cref="OpenClawChatDataProvider"/>'s <c>post</c> argument.
    /// Same behavior as the (now-deleted) <c>ReactorChatHostExtensions.AsPost</c>:
    /// runs synchronously when called on the dispatcher's thread, otherwise
    /// posts to the queue. Falls through with a Debug.WriteLine when the
    /// queue rejects the work item (typically: app shutdown).
    /// </summary>
    public static Action<Action> AsPost(this DispatcherQueue dispatcher) =>
        action =>
        {
            if (dispatcher.HasThreadAccess)
            {
                action();
                return;
            }

            if (!dispatcher.TryEnqueue(() => action()))
                System.Diagnostics.Debug.WriteLine("Dropped chat UI update because DispatcherQueue rejected the work item.");
        };

    /// <summary>
    /// Mount <see cref="NativeChatRoot"/> into <paramref name="target"/>.
    /// Returns an <see cref="IDisposable"/> that detaches the root and clears
    /// the Border's child when the page/window unloads.
    /// </summary>
    public static IDisposable MountNativeChat(
        this Window window,
        Border target,
        IChatDataProvider provider,
        string? initialThreadId = null,
        Func<string, Task>? onReadAloud = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(provider);

        var root = new NativeChatRoot(provider, initialThreadId, onReadAloud);
        target.Child = root;
        return new Mount(target, root);
    }

    private sealed class Mount : IDisposable
    {
        private Border? _target;
        private NativeChatRoot? _root;

        public Mount(Border target, NativeChatRoot root)
        {
            _target = target;
            _root = root;
        }

        public void Dispose()
        {
            var target = _target;
            var root = _root;
            _target = null;
            _root = null;
            if (target is not null && ReferenceEquals(target.Child, root))
            {
                target.Child = null;
            }
        }
    }
}
