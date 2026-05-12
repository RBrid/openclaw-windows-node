using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClawTray.Chat.Views;

namespace OpenClawTray.Chat;

/// <summary>
/// Mounts the native WinUI <see cref="ChatView"/> into a host <see cref="Border"/>.
/// Replaces the previous <c>ReactorChatHostExtensions</c> entry point that
/// hosted the deleted Reactor-based chat tree.
/// </summary>
public static class ChatViewHost
{
    /// <summary>
    /// Builds an "post to UI thread" callback suitable for
    /// <c>OpenClawChatDataProvider</c>'s <c>post</c> argument from a
    /// dispatcher queue. Mirrors the previous <c>AsPost</c> helper so the
    /// provider keeps the same construction shape.
    /// </summary>
    public static Action<Action> AsPost(this DispatcherQueue dispatcher) =>
        action =>
        {
            if (dispatcher.HasThreadAccess) { action(); return; }
            if (!dispatcher.TryEnqueue(() => action()))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[ChatViewHost] Dropped chat UI update — DispatcherQueue rejected the work item.");
            }
        };

    /// <summary>
    /// Mount a native <see cref="ChatView"/> bound to <paramref name="provider"/>
    /// into <paramref name="target"/>. Returns an <see cref="IDisposable"/>
    /// that releases the view (detaches from the provider) when the page
    /// or window unloads.
    /// </summary>
    public static IDisposable MountChatView(
        this Window window,
        Border target,
        IChatDataProvider provider,
        string? initialThreadId = null,
        Func<string, Task>? onReadAloud = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(provider);

        var dq = window.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        var vm = new ChatViewModel(provider, dq, initialThreadId, onReadAloud);
        var view = new ChatView();
        view.Bind(vm);
        target.Child = view;

        return new Mount(target, view, vm);
    }

    private sealed class Mount : IDisposable
    {
        private readonly Border _target;
        private readonly ChatView _view;
        private readonly ChatViewModel _vm;
        private bool _disposed;

        public Mount(Border target, ChatView view, ChatViewModel vm)
        {
            _target = target;
            _view = view;
            _vm = vm;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _view.Dispose(); } catch { /* tear-down race — non-fatal */ }
            try { _vm.Dispose(); } catch { /* tear-down race — non-fatal */ }
            try { if (ReferenceEquals(_target.Child, _view)) _target.Child = null; }
            catch { /* tear-down race — non-fatal */ }
        }
    }
}
