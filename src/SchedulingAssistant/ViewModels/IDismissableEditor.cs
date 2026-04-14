namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Implemented by flyout-hosted ViewModels that support inline editing.
/// <para>
/// When the user presses Escape while a flyout is open, <see cref="MainWindowViewModel.CloseFlyout"/>
/// checks this interface before closing the flyout. If an editor is active, it is dismissed first;
/// the flyout closes only on a subsequent Escape with no editor open. This gives Escape a natural
/// "inner then outer" feel without relying on Avalonia routed-event phase ordering.
/// </para>
/// </summary>
public interface IDismissableEditor
{
    /// <summary>
    /// Dismisses the currently active inline editor, if any.
    /// </summary>
    /// <returns>
    /// <c>true</c> if an editor was open and has been dismissed (the flyout should stay open);
    /// <c>false</c> if no editor was active (the flyout may close).
    /// </returns>
    bool DismissActiveEditor();
}
