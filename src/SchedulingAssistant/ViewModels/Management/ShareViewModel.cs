using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the File → Share flyout.
/// Generates a .tpconfig file from the current database so the user can share
/// their scheduling configuration with colleagues who are setting up a new instance.
///
/// The file is written to the same folder as the currently open database.
/// Block lengths, legal start times, semester definitions, and block patterns are
/// drawn from the currently selected academic year; campuses and section prefixes
/// span all years.
/// </summary>
public partial class ShareViewModel : ViewModelBase
{
    private readonly ILegalStartTimeRepository _legalStartTimeRepo;
    private readonly ICampusRepository         _campusRepo;
    private readonly ISectionPrefixRepository  _prefixRepo;
    private readonly ISemesterRepository       _semesterRepo;
    private readonly IBlockPatternRepository   _patternRepo;
    private readonly SemesterContext           _semesterContext;

    /// <summary>Feedback shown below the button after an attempt to write the file.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>True when the last write attempt succeeded (status shown in green).</summary>
    [ObservableProperty] private bool _isSuccess;

    /// <summary>True when there is a status message to display.</summary>
    [ObservableProperty] private bool _hasStatus;

    public ShareViewModel(
        ILegalStartTimeRepository legalStartTimeRepo,
        ICampusRepository         campusRepo,
        ISectionPrefixRepository  prefixRepo,
        ISemesterRepository       semesterRepo,
        IBlockPatternRepository   patternRepo,
        SemesterContext           semesterContext)
    {
        _legalStartTimeRepo = legalStartTimeRepo;
        _campusRepo         = campusRepo;
        _prefixRepo         = prefixRepo;
        _semesterRepo       = semesterRepo;
        _patternRepo        = patternRepo;
        _semesterContext    = semesterContext;
    }

    /// <summary>
    /// Builds a <see cref="TpConfigData"/> snapshot from the live database and writes it
    /// as a .tpconfig file to the folder that contains the currently open database.
    ///
    /// Block lengths / legal start times, semester definitions, and block patterns come
    /// from the currently selected academic year. Campuses and section prefixes are drawn
    /// from the full set (they are institution-wide, not year-scoped).
    ///
    /// On success, sets <see cref="StatusMessage"/> to the path written.
    /// On failure, sets <see cref="StatusMessage"/> to the error description.
    /// </summary>
    [RelayCommand]
    private void CreateTpConfigFile()
    {
        var dbPath = AppSettings.Current.DatabasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            SetStatus("No database is currently open.", success: false);
            return;
        }

        var dbFolder = Path.GetDirectoryName(dbPath);
        if (string.IsNullOrWhiteSpace(dbFolder))
        {
            SetStatus("Could not determine the database folder.", success: false);
            return;
        }

        var ay = _semesterContext.SelectedAcademicYear;
        if (ay is null)
        {
            SetStatus("No academic year is selected. Please select an academic year and try again.", success: false);
            return;
        }

        try
        {
            var config = SnapshotConfig(ay.Id);

            // Filename uses institution abbreviation (falls back to "config").
            var abbrev = AppSettings.Current.InstitutionAbbrev;
            var path   = TpConfigService.Write(dbFolder, config, abbrev);

            if (path is not null)
                SetStatus($"File written to:\n{path}", success: true);
            else
                SetStatus("The file could not be written. Check the application log for details.", success: false);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "ShareViewModel.CreateTpConfigFile failed");
            SetStatus($"An error occurred: {ex.Message}", success: false);
        }
    }

    /// <summary>
    /// Builds a <see cref="TpConfigData"/> snapshot from the current database for the given
    /// academic year. Used both for writing the file and as the source for the New Database
    /// config-transfer flow.
    /// </summary>
    /// <param name="academicYearId">The academic year to snapshot block lengths and semesters from.</param>
    internal TpConfigData SnapshotConfig(string academicYearId)
    {
        // ── Block lengths + legal start times ────────────────────────────────
        var legalStartTimes = _legalStartTimeRepo.GetAll(academicYearId);
        var blockLengths = legalStartTimes
            .Select(lst => new TpConfigBlockLength
            {
                Hours      = lst.BlockLength,
                StartTimes = lst.StartTimes
            })
            .Where(bl => bl.StartTimes.Count > 0)
            .ToList();

        // ── Semesters (in sort order) ────────────────────────────────────────
        var semesters = _semesterRepo.GetByAcademicYear(academicYearId);
        var semesterDefs = semesters
            .Select(s => new TpConfigSemesterDef { Name = s.Name, Color = s.Color })
            .ToList();

        // ── Campuses (all, institution-wide) ─────────────────────────────────
        var campuses       = _campusRepo.GetAll();
        var campusNames    = campuses.Select(c => c.Name).ToList();
        var campusIdToName = campuses.ToDictionary(c => c.Id, c => c.Name);

        // ── Section prefixes (all) ────────────────────────────────────────────
        var sectionPrefixes = _prefixRepo.GetAll()
            .Select(p => new TpConfigSectionPrefix
            {
                Prefix     = p.Prefix,
                CampusName = p.CampusId is not null
                             && campusIdToName.TryGetValue(p.CampusId, out var cn) ? cn : null
            })
            .ToList();

        // ── Block patterns (day patterns, all) ───────────────────────────────
        var blockPatterns = _patternRepo.GetAll()
            .Select(bp => new TpConfigBlockPattern { Name = bp.Name, Days = bp.Days })
            .ToList();

        var settings = AppSettings.Current;

        return new TpConfigData
        {
            BlockLengths     = blockLengths,
            SemesterDefs     = semesterDefs,
            Campuses         = campusNames,
            SectionPrefixes  = sectionPrefixes,
            BlockPatterns    = blockPatterns,
            IncludeSaturday  = settings.IncludeSaturday,
            IncludeSunday    = settings.IncludeSunday
        };
    }

    /// <summary>Updates the status properties in one call.</summary>
    /// <param name="message">The message to display.</param>
    /// <param name="success">True for a success (green) message; false for an error (red) message.</param>
    private void SetStatus(string message, bool success)
    {
        StatusMessage = message;
        IsSuccess     = success;
        HasStatus     = true;
    }
}
