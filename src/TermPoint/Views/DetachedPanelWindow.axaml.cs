using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using System;

namespace TermPoint.Views;

public partial class DetachedPanelWindow : Window
{
    public static readonly StyledProperty<bool> HasHeaderContextProperty =
        AvaloniaProperty.Register<DetachedPanelWindow, bool>(nameof(HasHeaderContext), false);

    public bool HasHeaderContext
    {
        get => GetValue(HasHeaderContextProperty);
        set => SetValue(HasHeaderContextProperty, value);
    }

    public Action? OnReattach { get; init; }

    /// <summary>
    /// When true, <see cref="OnReattach"/> is NOT invoked on close. Set during app shutdown
    /// so closing this window doesn't try to reattach its content to a main window that is
    /// itself tearing down (its DI container may already be disposed).
    /// </summary>
    public bool SuppressReattach { get; set; }

    public DetachedPanelWindow()
    {
        InitializeComponent();
    }

    public void SetContent(string title, Control content, object? headerContext = null)
    {
        Title = title;
        this.FindControl<ContentControl>("ContentArea")!.Content = content;

        if (headerContext is not null)
        {
            HasHeaderContext = true;
            var headerContextArea = this.FindControl<Control>("HeaderContextArea");
            if (headerContextArea is not null)
            {
                dynamic dyn = headerContextArea;
                dyn.Content = headerContext;
            }
        }
    }

    /// <summary>
    /// Guards the second close pass. Set true once the user-initiated close has been
    /// intercepted (content cleared, window hidden, reattach + real disposal deferred).
    /// The subsequent programmatic <see cref="Window.Close"/> then falls straight through
    /// to a real dispose instead of being cancelled again.
    /// </summary>
    private bool _closeHandled;

    /// <summary>
    /// Clears the hosted content and header context, giving child views a null DataContext
    /// so their OnDataContextChanged unsubscribes PropertyChanged handlers from the
    /// long-lived ViewModels. Without this, every detach/reattach cycle leaks the entire
    /// discarded view tree (canvas, tile controls, timers, etc.) because the VM holds a
    /// strong reference via PropertyChanged. Idempotent.
    /// </summary>
    private void ClearContent()
    {
        var contentArea = this.FindControl<ContentControl>("ContentArea");
        if (contentArea is not null) contentArea.Content = null;

        var headerArea = this.FindControl<ContentPresenter>("HeaderContextArea");
        if (headerArea is not null) headerArea.Content = null;
    }

    /// <summary>
    /// Intercepts the user closing the window (the title-bar X) so the reattach never runs
    /// while this window is being disposed.
    /// <para>
    /// Avalonia disposes a closing window synchronously: OnClosing → CloseInternal →
    /// WindowImpl.Dispose(). If we rebuilt the panel inline during that teardown, the fresh
    /// ScheduleGridView's AccessPanelView carries grouped ("WatchMode") RadioButtons whose
    /// attach-time routed-event publish needs a lock the in-progress compositor teardown
    /// holds → UI-thread deadlock (0% CPU, "not responding"). Deferring the reattach with a
    /// Dispatcher.Post is NOT enough: the nested message pump inside Dispose() drains the
    /// dispatcher queue and runs the posted job while the dispose is still on the stack.
    /// </para>
    /// <para>
    /// So we cancel the close outright, clear + hide the window now (no dispose), then on a
    /// later tick — when no window dispose is in flight — run the reattach and finally close
    /// the now-empty window for real. Mirrors the Hide-not-Close remedy already used for the
    /// wizard and DatabaseChooserWindow against this same Avalonia 12 deadlock.
    /// </para>
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Shutdown path (CloseSecondaryWindows sets SuppressReattach) or our own second
        // pass: let the close proceed to a real dispose.
        if (SuppressReattach || _closeHandled)
            return;

        e.Cancel = true;      // don't dispose now
        _closeHandled = true;

        ClearContent();       // unsubscribe child views before they leave the tree
        Hide();               // remove from screen without disposing

        // Reattach and dispose off the close/dispose stack, on the next idle tick.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnReattach?.Invoke();   // rebuild the panel inline — now on a clean stack
            Close();                // _closeHandled == true → real dispose of the empty window
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Covers the shutdown path (OnClosing returned early) and the second pass; harmless
        // if content was already cleared in OnClosing.
        ClearContent();
        base.OnClosed(e);
    }
}
