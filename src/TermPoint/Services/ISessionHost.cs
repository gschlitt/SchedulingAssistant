using TermPoint.Exceptions;

namespace TermPoint.Services;

/// <summary>
/// The second of the two seams between the database-session lifecycle and the window it runs in (spec item
/// 9.1): everything the lifecycle uses to TOUCH the window or the dependency-injection container.
/// <see cref="MainWindow"/> implements it by delegating to <c>App</c> and to its own window code; a test supplies
/// a fake that opens a plain database context on the working file.
///
/// <para>Every member runs on the UI thread and is a thin wrapper of exactly what the call sites did before
/// the seam existed.</para>
/// </summary>
public interface ISessionHost
{
    /// <summary>
    /// Builds the full service container over <paramref name="workingPath"/> and returns the opened database
    /// (today <c>App.InitializeServices</c>, whose root view model the host wraps in an <see cref="IOpenedDatabase"/>).
    /// Disposes any previous container first.
    /// </summary>
    /// <param name="workingPath">Path to the local database file: D' (writer) or D'' (reader).</param>
    /// <param name="restoreAcademicYearId">Academic year ID to restore. Defaults to the settings value.</param>
    /// <param name="restoreSemesterIds">Semester IDs to restore. Defaults to the settings value.</param>
    /// <returns>The opened database, with the services built over <paramref name="workingPath"/>.</returns>
    /// <exception cref="DatabaseInUseException">
    /// The local copy could not be examined on any of the integrity-check attempts (another program on this
    /// computer has it open). The services have been built but nothing was read; the caller must release them
    /// (<see cref="ReleaseServices"/>) if it gives up.
    /// </exception>
    /// <exception cref="DatabaseCorruptException">The local copy shows positive evidence of damage (the services are built as above).</exception>
    IOpenedDatabase OpenServices(
        string workingPath,
        string? restoreAcademicYearId = null,
        IReadOnlySet<string>? restoreSemesterIds = null);

    /// <summary>
    /// Disposes the current container (closing its database connection) AND clears <c>App.Services</c>, returning
    /// the application to the "no database open" state it is in at startup (today <c>App.ReleaseServices</c>).
    /// Safe to call when no container exists or it is already disposed.
    /// </summary>
    void ReleaseServices();

    /// <summary>
    /// Disposes the current container (closing its database connection) WITHOUT clearing <c>App.Services</c>.
    /// This is what the window-close teardown and the end of a session after a restore do today; it is
    /// deliberately distinct from <see cref="ReleaseServices"/> and the two must not be merged.
    /// </summary>
    void DisposeServices();

    /// <summary>
    /// Stops the periodic backup timer if a backup service exists in the current container. Call it BEFORE the
    /// container is disposed. The caller keeps its own surrounding error handling.
    /// </summary>
    void StopBackupSession();

    /// <summary>
    /// Closes every secondary top-level window the main window owns: the detached-panel windows and the
    /// floating workflow sticky notes. Reattach is suppressed so they do not try to rejoin a window that is
    /// itself tearing down.
    /// </summary>
    void CloseSecondaryWindows();

    /// <summary>
    /// True while any panel has an inline editor open (a section, a course and so on), so unsaved typing may
    /// exist that a session-ending action would discard.
    /// </summary>
    bool IsInlineEditorOpen { get; }

    /// <summary>
    /// Wires a freshly opened database to the window: data context, panels, curtain lift and the rest of the
    /// per-database setup.
    /// </summary>
    /// <param name="dbPath">The canonical path of the shared database that was opened (D).</param>
    /// <param name="opened">The opened database returned by <see cref="OpenServices"/>.</param>
    /// <returns>A task that completes when the window is wired up.</returns>
    Task SetupWindowAsync(string dbPath, IOpenedDatabase opened);

    /// <summary>
    /// Covers the window with the loading curtain and clears its data context, for the state in which no
    /// database is open (so a stale view of a closed database cannot be seen or used). Idempotent.
    /// </summary>
    void CoverWindowForNoDatabase();

    /// <summary>
    /// True from the moment a database has been completely wired to the window (<see cref="SetupWindowAsync"/>
    /// sets it as its last step) until the next switch starts taking it apart, or its wiring fails part-way.
    /// Tracked explicitly, never inferred from the container or the data context: whether a refused or failed
    /// switch may leave the user on the current database, or must send them to the database chooser because
    /// nothing is open, hangs on it (see <see cref="DatabaseSwitchRecovery.Decide"/>). False at launch, during
    /// the startup wizard, and after any failed switch. The lifecycle reads it and clears it; the window sets it.
    /// </summary>
    bool IsDatabaseOpen { get; set; }

    /// <summary>
    /// Closes the main window (today the <c>Close()</c> calls on the startup give-up paths). The window's
    /// closing handler then runs as it always has.
    /// </summary>
    void CloseWindow();

    /// <summary>
    /// Asks the application to shut down (today the classic-desktop lifetime's <c>Shutdown()</c>), with no
    /// window involved. A no-op when the application has no classic-desktop lifetime.
    /// </summary>
    void ShutDown();

    /// <summary>
    /// The window half of the last-resort failure path when the database chooser itself cannot be shown after a
    /// failed switch: restores the window from any minimised state, shows the startup-error dialog for
    /// <paramref name="ex"/>, then shuts the application down. The caller logs the exception first.
    /// </summary>
    /// <param name="ex">The failure to show to the user.</param>
    /// <returns>A task that completes when the error dialog has closed and the shutdown has been requested.</returns>
    Task ReportFatalAndShutDownAsync(Exception ex);

    /// <summary>
    /// Reads the current academic year and semester selection from the open container, so it can be restored
    /// after the container is rebuilt without touching the saved settings. Reports nothing when no container
    /// exists (the wizard's existing-database path, before any database was opened) or no academic year is
    /// selected.
    /// </summary>
    /// <param name="yearId">The selected academic year's ID, or null when nothing was captured.</param>
    /// <param name="semesterIds">The selected semesters' IDs, or null when nothing was captured.</param>
    /// <returns>True when a selection was captured; false otherwise (both outputs are then null).</returns>
    bool TryCaptureSemesterSelection(out string? yearId, out IReadOnlySet<string>? semesterIds);
}
