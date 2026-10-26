namespace TermPoint.Services.SectionImport;

/// <summary>
/// The one rule for when "Import Sections" can be used. It drives both the menu item (its enabled state and
/// its disabled tooltip) and the flyout's Import button, because any of the three conditions can change while
/// the flyout is open: the lock is lost, another semester is chosen, or an editor is opened from a detached
/// panel. A pure function of three facts, so it is the same in the main window and the flyout and needs no
/// window to test.
///
/// <para><b>Why an open section editor blocks it.</b> The import ends with a section-list reload. A reload
/// while an editor is open would close it, and its unapplied changes would be lost (rule 3 of the shared
/// database rules: never discard unsaved work).</para>
/// </summary>
public static class ImportAvailability
{
    /// <summary>Tooltip when the session cannot write (the banner already says why).</summary>
    public const string ReadOnlyReason = "TermPoint is read-only right now, so sections can't be imported.";

    /// <summary>Tooltip when no semester is shown.</summary>
    public const string NoSemesterReason = "Select a semester to import sections into it.";

    /// <summary>Tooltip when more than one semester is shown.</summary>
    public const string SeveralSemestersReason = "Select just one semester to import sections into it.";

    /// <summary>Tooltip when a section is open for editing.</summary>
    public const string EditingReason = "Finish editing the section first.";

    /// <summary>
    /// Says why importing is unavailable, or that it is available. The conditions are checked in the order
    /// read-only, no semester, several semesters, section being edited, and the first one that applies wins.
    /// </summary>
    /// <param name="isWriteEnabled">True when this session holds the write lock (<c>MainWindowViewModel.IsWriteEnabled</c>).</param>
    /// <param name="shownSemesterCount">How many semesters are shown (<c>SemesterContext.SelectedSemesters.Count</c>).</param>
    /// <param name="isEditingSection">True when a section is open for editing (<c>SectionListViewModel.IsEditing</c>).</param>
    /// <returns>Null when importing is available; otherwise the plain sentence to show as the tooltip.</returns>
    public static string? WhyUnavailable(bool isWriteEnabled, int shownSemesterCount, bool isEditingSection)
    {
        if (!isWriteEnabled) return ReadOnlyReason;
        if (shownSemesterCount == 0) return NoSemesterReason;
        if (shownSemesterCount > 1) return SeveralSemestersReason;
        if (isEditingSection) return EditingReason;
        return null;
    }
}
