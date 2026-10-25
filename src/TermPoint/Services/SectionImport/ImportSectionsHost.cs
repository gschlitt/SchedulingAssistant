#if !BROWSER
using System.ComponentModel;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// The semester an import is aimed at, as the flyout names it: "Sections will be added to Fall 2026 (2026-2027)."
/// </summary>
/// <param name="SemesterId">The id of the semester the sections are added to.</param>
/// <param name="TermName">The semester's name, as the picker shows it.</param>
/// <param name="AcademicYearName">The name of the semester's academic year.</param>
public sealed record ImportTarget(string SemesterId, string TermName, string AcademicYearName);

/// <summary>
/// What the Import Sections flyout needs to know about the main window, and nothing else: whether the
/// session may write, which semester is shown, whether a section is open for editing, how to refresh the
/// section views after a write, and a way to hear when any of those change. A narrow seam, so the flyout's
/// view model can be tested without a main window (see <see cref="ImportSectionsHost"/> for the real one).
/// </summary>
public interface IImportSectionsHost
{
    /// <summary>True when this session holds the write lock (the same fact as <c>MainWindowViewModel.IsWriteEnabled</c>).</summary>
    bool IsWriteEnabled { get; }

    /// <summary>How many semesters are shown (zero, one or several).</summary>
    int ShownSemesterCount { get; }

    /// <summary>True when a section is open for editing in the section list.</summary>
    bool IsEditingSection { get; }

    /// <summary>
    /// The semester to import into: the shown semester when exactly one is shown; null when none or
    /// several are shown.
    /// </summary>
    ImportTarget? Target { get; }

    /// <summary>
    /// Reloads the section list, grid and workload from the database, as after any section change. Called
    /// once after an import has been written.
    /// </summary>
    void ReloadSections();

    /// <summary>
    /// Starts telling <paramref name="onChanged"/> whenever write access, the shown semesters or the
    /// section-editing state changes, on the UI thread.
    /// </summary>
    /// <param name="onChanged">Called after any of the three changes.</param>
    /// <returns>Dispose it to stop listening (the flyout does this when it is closed).</returns>
    IDisposable WatchChanges(Action onChanged);
}

/// <summary>
/// The real <see cref="IImportSectionsHost"/>: reads the write lock, the <see cref="SemesterContext"/> and the
/// section list, which are the same sources the main window's own Import Sections command reads, so the menu
/// item and the flyout always agree (<see cref="ImportAvailability"/>).
/// </summary>
public sealed class ImportSectionsHost : IImportSectionsHost
{
    private readonly WriteLockService _lockService;
    private readonly SemesterContext _semesterContext;
    private readonly SectionListViewModel _sectionList;

    /// <summary>Creates the host over the app's singletons.</summary>
    /// <param name="lockService">Says whether this session may write, and raises <see cref="WriteLockService.LockStateChanged"/>.</param>
    /// <param name="semesterContext">The shown semesters.</param>
    /// <param name="sectionList">Says whether an editor is open, and reloads the section views.</param>
    public ImportSectionsHost(WriteLockService lockService, SemesterContext semesterContext, SectionListViewModel sectionList)
    {
        _lockService = lockService;
        _semesterContext = semesterContext;
        _sectionList = sectionList;
    }

    /// <inheritdoc />
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <inheritdoc />
    public int ShownSemesterCount => _semesterContext.SelectedSemesters.Count;

    /// <inheritdoc />
    public bool IsEditingSection => _sectionList.IsEditing;

    /// <inheritdoc />
    public ImportTarget? Target
    {
        get
        {
            if (_semesterContext.SelectedSemesters.Count != 1) return null;
            var semester = _semesterContext.SelectedSemesters[0].Semester;
            // Look the year up by the semester's own year id, so the line is right whatever the picker shows.
            var year = _semesterContext.AcademicYears.FirstOrDefault(y => y.Id == semester.AcademicYearId)
                       ?? _semesterContext.SelectedAcademicYear;
            return new ImportTarget(semester.Id, semester.Name, year?.Name ?? string.Empty);
        }
    }

    /// <inheritdoc />
    public void ReloadSections() => _sectionList.ReloadFromDatabase();

    /// <inheritdoc />
    public IDisposable WatchChanges(Action onChanged)
    {
        void OnLock() => onChanged();
        void OnContext(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SemesterContext.SelectedSemesters)) onChanged();
        }
        void OnList(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SectionListViewModel.IsEditing)) onChanged();
        }

        _lockService.LockStateChanged += OnLock;
        _semesterContext.PropertyChanged += OnContext;
        _sectionList.PropertyChanged += OnList;
        return new Subscription(() =>
        {
            _lockService.LockStateChanged -= OnLock;
            _semesterContext.PropertyChanged -= OnContext;
            _sectionList.PropertyChanged -= OnList;
        });
    }

    /// <summary>An <see cref="IDisposable"/> that runs an action once, when first disposed.</summary>
    private sealed class Subscription : IDisposable
    {
        private Action? _onDispose;

        /// <summary>Creates the subscription.</summary>
        /// <param name="onDispose">Detaches the handlers.</param>
        public Subscription(Action onDispose) => _onDispose = onDispose;

        /// <summary>Detaches the handlers (a second call does nothing).</summary>
        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
#endif
