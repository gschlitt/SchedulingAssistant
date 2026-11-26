namespace TermPoint.Services;

/// <summary>
/// The narrow slice of <c>CheckoutService</c> the undo history (<see cref="SectionHistory"/>) listens to: the unsaved
/// indicator's two events, the save's own success event, the edit count and the dirty marker. It exists because
/// <c>CheckoutService</c> is not part of the WASM demo build, while <see cref="SectionHistory"/> is; the demo passes no
/// source at all.
/// </summary>
internal interface ISaveSignals
{
    /// <summary>Raised on the UI thread when the first write after a checkout or a save happens (the "unsaved" indicator turns on).</summary>
    event Action? BecameDirty;

    /// <summary>Raised on the UI thread after every successful save (the indicator turns off).</summary>
    event Action? SaveCompleted;

    /// <summary>
    /// Raised on the UI thread right after <see cref="SaveCompleted"/>, with the edit count read before the save's
    /// snapshot and whether the save kept the dirty marker (an edit arrived while it ran).
    /// </summary>
    event Action<long, bool>? SaveSucceeded;

    /// <summary>The number of user writes made this session (see <c>CheckoutService.NoteEdit</c>).</summary>
    long EditGeneration { get; }

    /// <summary>True when the working copy has a dirty marker: unsaved changes exist (for example, restored after a crash).</summary>
    bool HasDirtyMarker { get; }
}
