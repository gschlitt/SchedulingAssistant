using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace TermPoint.ViewModels;

/// <summary>
/// Base class for every editor view model with an Apply / Save button: implements <see cref="IUnappliedChanges"/>
/// once, so the snapshot comparison is the same in all of them (spec item 3.1).
///
/// <para>An editor derives from this class and supplies three things: <see cref="CaptureState"/> (the values it
/// would save, as comparable text built with <see cref="UnappliedChangesKey.Of"/>), <see cref="ApplyCommand"/> (its
/// Save command) and <see cref="CancelCommandForDiscard"/> (its Cancel command). It calls <see cref="TakeSnapshot"/>
/// as the LAST statement of its constructor (so every value is already in place) and <see cref="MarkApplied"/> in
/// its Save, right after the save callback has returned.</para>
/// </summary>
public abstract class UnappliedChangesEditor : ViewModelBase, IUnappliedChanges
{
    /// <summary>The state text taken when the editor opened; null until <see cref="TakeSnapshot"/> runs.</summary>
    private string? _snapshot;

    /// <summary>Set by <see cref="MarkApplied"/> when the save callback has returned; cleared at the start of each apply attempt.</summary>
    private bool _applied;

    /// <summary>
    /// The values this editor would save, as comparable text (use <see cref="UnappliedChangesKey.Of"/>). Compared with
    /// the text captured at open, so include exactly the user-editable values, normalised the way Save stores them
    /// (trimmed text, ids rather than objects).
    /// </summary>
    /// <returns>The comparable state text.</returns>
    protected abstract string CaptureState();

    /// <summary>The editor's Save / Apply command, run by <see cref="TryApplyAsync"/>.</summary>
    protected abstract ICommand ApplyCommand { get; }

    /// <summary>The editor's Cancel command, run by <see cref="DiscardUnapplied"/>.</summary>
    protected abstract ICommand CancelCommandForDiscard { get; }

    /// <summary>
    /// Records the editor's current values as "as opened". Call as the last statement of the constructor.
    /// </summary>
    protected void TakeSnapshot() => _snapshot = CaptureState();

    /// <summary>
    /// Records that the save callback returned. Call from Save right after the callback (and only on the path that
    /// really saved: a validation refusal returns before it).
    /// </summary>
    protected void MarkApplied() => _applied = true;

    /// <inheritdoc/>
    public bool HasUnappliedChanges =>
        _snapshot is not null && !string.Equals(CaptureState(), _snapshot, StringComparison.Ordinal);

    /// <inheritdoc/>
    public async Task<bool> TryApplyAsync()
    {
        // The same gate the button has: a disabled Save (invalid input) is a refusal, and the editor's own
        // validation text is already showing.
        if (!ApplyCommand.CanExecute(null)) return false;

        _applied = false;
        try
        {
            if (ApplyCommand is IAsyncRelayCommand asyncCommand)
                await asyncCommand.ExecuteAsync(null);
            else
                ApplyCommand.Execute(null);
        }
        catch (Exception ex)
        {
            // A save that throws (a failed write) is a refusal: the close or switch must stop, not carry on.
            App.Logger?.LogError(ex, "UnappliedChangesEditor.TryApplyAsync");
            return false;
        }
        return _applied;
    }

    /// <inheritdoc/>
    public void DiscardUnapplied() => CancelCommandForDiscard.Execute(null);
}
