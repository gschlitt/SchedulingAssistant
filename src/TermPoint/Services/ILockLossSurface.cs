namespace TermPoint.Services;

/// <summary>
/// The window-side objects the lost-write-lock handler works on (spec item 9.1, step 9.1c): the root view model
/// of the open database and its data connection, CAPTURED TOGETHER AT ONE MOMENT by
/// <see cref="ISessionHost.CaptureLockLossSurface"/> and then used, unchanged, for the whole demotion. The old
/// handler read both once at its start (<c>DataContext as MainWindowViewModel</c> and
/// <c>App.Services?.GetService&lt;IDatabaseContext&gt;() as DatabaseContext</c>) and used those same two objects
/// throughout, even though the demotion awaits and a switch could have replaced them meanwhile; this object keeps
/// exactly that behaviour without the lifecycle referencing a view model or a database context type.
///
/// <para>Each member is the verbatim use the handler made of the captured view model (<c>vm</c>) or connection
/// (<c>dbContext</c>), including the null-propagation: either may be absent, and an absent one makes the
/// corresponding operations do nothing. <see cref="MainWindow"/> implements it; a test supplies a recording fake.</para>
/// </summary>
public interface ILockLossSurface
{
    /// <summary>
    /// True when a data connection was captured (there was a database context in the container). The handler
    /// only reopens the connection when this is true.
    /// </summary>
    bool HasConnection { get; }

    /// <summary>
    /// True when a root view model was captured (the window's data context was the main view model). The handler
    /// only reloads the section list when this is true.
    /// </summary>
    bool HasViewModel { get; }

    /// <summary>
    /// Shows <paramref name="text"/> in the window's persistent save-error banner, without auto-dismiss. Does
    /// nothing when no view model was captured.
    /// </summary>
    /// <param name="text">The banner text.</param>
    void ShowSaveError(string text);

    /// <summary>
    /// Closes the captured data connection (so the working copy can be handed over without a sharing violation).
    /// Does nothing when no connection was captured.
    /// </summary>
    void CloseConnection();

    /// <summary>
    /// Reopens the captured data connection on <paramref name="path"/>. Only call it when
    /// <see cref="HasConnection"/> is true.
    /// </summary>
    /// <param name="path">The working copy to reconnect to.</param>
    /// <exception cref="InvalidOperationException">The file could not be opened (for example another process has it locked).</exception>
    void ReinitializeConnection(string path);

    /// <summary>
    /// Reloads the section list of the captured view model from the database. Only call it when
    /// <see cref="HasViewModel"/> is true.
    /// </summary>
    /// <exception cref="Exception">Any failure of the reload; the handler logs it and carries on.</exception>
    void ReloadSections();
}
