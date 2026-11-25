using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>One course's sections in the slate being shown ("BMEG 230 L1 + T2"): the course and its section codes.</summary>
public sealed class AccessSlatePartViewModel
{
    /// <summary>The course's calendar code.</summary>
    public string CourseCode { get; }

    /// <summary>The slate's sections of this course, joined for display ("L1 + T2"), each with its pin state and id.</summary>
    public IReadOnlyList<AccessSlateSectionViewModel> Sections { get; }

    /// <summary>Creates a part.</summary>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="sections">Its sections in the slate.</param>
    public AccessSlatePartViewModel(string courseCode, IReadOnlyList<AccessSlateSectionViewModel> sections)
    {
        CourseCode = courseCode;
        Sections = sections;
    }
}

/// <summary>A section named in the stepper: its code, its id (clicking selects it) and whether it is pinned.</summary>
/// <param name="SectionId">The section's id.</param>
/// <param name="Code">The section's code ("L1").</param>
/// <param name="IsPinned">True when pinned (the view draws a pin before the code).</param>
public sealed record AccessSlateSectionViewModel(string SectionId, string Code, bool IsPinned);

/// <summary>
/// The legend bar above the grid while a watch is open (the r9 wireframe's <c>picbar</c>): the watch's name and
/// subset, the pinned sections with Unpin all, Step through slates (or the stepper: ‹ Slate i of N ›, the slate's
/// sections by course, "mixes campuses", Clear), Mark all conflicts, Show other courses, and Close. Properties and
/// commands only; the views draw it. Singleton, redrawn whenever the picture or the access state changes.
/// </summary>
public sealed partial class AccessLegendViewModel : ViewModelBase
{
    private readonly AccessPicture _picture;
    private readonly AccessState _state;
    private readonly ICampusRepository _campusRepo;

    /// <summary>True while a watch or a course is open: the bar shows.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>The watch's name, and " · 3 of 5 courses" when a subset is ticked.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>True when some section is pinned.</summary>
    [ObservableProperty] private bool _hasPins;

    /// <summary>The start of the pin pill ("Pinned: "); the chips follow.</summary>
    public string PinnedLead => AccessText.PinnedLeadText;

    /// <summary>The pinned sections as chips (name and ✕).</summary>
    public ObservableCollection<AccessPinChipViewModel> PinChips { get; } = [];

    /// <summary>The Unpin all button's text.</summary>
    public string UnpinAllText => AccessText.UnpinAllText;

    /// <summary>Unpins every section.</summary>
    public IRelayCommand UnpinAllCommand { get; }

    /// <summary>True when the "Step through slates" button shows: there are slates to step through, and the bar is not stepping.</summary>
    [ObservableProperty] private bool _canStartStepping;

    /// <summary>The "Step through slates" button's text.</summary>
    public string StepThroughText => AccessText.StepThroughSlatesText;

    /// <summary>Starts stepping at the first slate.</summary>
    public IRelayCommand StartSteppingCommand { get; }

    /// <summary>True while stepping: the stepper shows instead of the button.</summary>
    [ObservableProperty] private bool _isStepping;

    /// <summary>"Slate 3 of 12", or "Slate 3 of more than 10,000".</summary>
    [ObservableProperty] private string _positionText = string.Empty;

    /// <summary>True when there is a previous slate.</summary>
    [ObservableProperty] private bool _canGoPrevious;

    /// <summary>True when there is a next slate.</summary>
    [ObservableProperty] private bool _canGoNext;

    /// <summary>Goes to the previous slate.</summary>
    public IRelayCommand PreviousCommand { get; }

    /// <summary>Goes to the next slate.</summary>
    public IRelayCommand NextCommand { get; }

    /// <summary>The slate being shown, course by course.</summary>
    [ObservableProperty] private IReadOnlyList<AccessSlatePartViewModel> _slateParts = [];

    /// <summary>True when the slate being shown puts in-person sections on more than one campus.</summary>
    [ObservableProperty] private bool _mixesCampuses;

    /// <summary>The "mixes campuses" tag's text.</summary>
    public string MixesCampusesText => AccessText.MixesCampusesText;

    /// <summary>The tag's tooltip, naming the campuses.</summary>
    [ObservableProperty] private string _mixesCampusesTip = string.Empty;

    /// <summary>The Clear button's text (stops stepping).</summary>
    public string ClearText => AccessText.ClearSlateText;

    /// <summary>Stops stepping.</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>
    /// The quiet line when the filters or the subject focus hide some of the open watch's sections, e.g. "2 of
    /// Cohort 1's sections are hidden by the filters."; empty when none is hidden.
    /// </summary>
    [ObservableProperty] private string _hiddenSectionsText = string.Empty;

    /// <summary>True while <see cref="HiddenSectionsText"/> has something to say.</summary>
    public bool HasHiddenSections => HiddenSectionsText.Length > 0;

    partial void OnHiddenSectionsTextChanged(string value) => OnPropertyChanged(nameof(HasHiddenSections));

    /// <summary>
    /// Sets the hidden-sections line after the grid drew (only the grid knows what it drew).
    /// </summary>
    /// <param name="count">How many of the open watch's sections with meeting times are not on the grid.</param>
    /// <param name="byFocus">Whether the subject focus hides at least one of them.</param>
    public void SetHiddenSections(int count, bool byFocus) =>
        HiddenSectionsText = !_picture.IsOpen || count == 0 ? string.Empty
            : AccessText.HiddenWatchSectionsText(count, _picture.Name, byFocus);

    /// <summary>Whether Mark all conflicts is on (the same per-computer setting as the Access tab's box).</summary>
    public bool MarkAllConflicts
    {
        get => _state.MarkAllConflicts;
        set
        {
            if (_state.MarkAllConflicts == value) return;
            _state.MarkAllConflicts = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The Close button's text.</summary>
    public string CloseText => AccessText.CloseWatchText;

    /// <summary>Closes the open watch (the grid goes back to its usual sections).</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Creates the legend bar and fills it from the picture.</summary>
    /// <param name="picture">The open watch, pins and stepping.</param>
    /// <param name="state">The access results (and the Mark all conflicts setting).</param>
    /// <param name="campusRepo">Campus names for "mixes campuses".</param>
    public AccessLegendViewModel(AccessPicture picture, AccessState state, ICampusRepository campusRepo)
    {
        _picture = picture;
        _state = state;
        _campusRepo = campusRepo;
        UnpinAllCommand = new RelayCommand(_picture.ClearPins);
        StartSteppingCommand = new RelayCommand(_picture.StartStepping);
        PreviousCommand = new RelayCommand(() => _picture.Step(-1));
        NextCommand = new RelayCommand(() => _picture.Step(1));
        ClearCommand = new RelayCommand(_picture.StopStepping);
        CloseCommand = new RelayCommand(_picture.Close);
        // Never unsubscribed: singletons that live as long as the services of one database.
        _picture.Changed += Refresh;
        _state.Changed += Refresh;
        Refresh();
    }

    /// <summary>
    /// Redraws the bar from the picture. A failure is logged and leaves the bar as it was: it must never break the
    /// change that raised it.
    /// </summary>
    public void Refresh()
    {
        try
        {
            Rebuild();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessLegendViewModel.Refresh");
        }
    }

    /// <summary>Reads the picture and sets every property.</summary>
    private void Rebuild()
    {
        var inputs = _picture.Inputs;
        var a = _picture.Analysis;
        IsVisible = _picture.IsOpen && inputs is not null && a is not null;
        OnPropertyChanged(nameof(MarkAllConflicts));
        if (!IsVisible || inputs is null || a is null)
        {
            Title = string.Empty;
            PinChips.Clear();
            HasPins = false;
            CanStartStepping = IsStepping = false;
            SlateParts = [];
            MixesCampuses = false;
            return;
        }

        Title = AccessText.LegendTitle(_picture.Name, a);

        PinChips.Clear();
        foreach (var id in _picture.PinnedSectionIds)
        {
            var section = inputs.Sections.FirstOrDefault(s => s.Id == id);
            if (section is null) continue;
            PinChips.Add(new AccessPinChipViewModel(id, AccessText.SectionLabel(inputs, section), () => _picture.TogglePin(id)));
        }
        HasPins = PinChips.Count > 0;

        IsStepping = _picture.IsStepping;
        CanStartStepping = _picture.CanStep && !IsStepping;
        if (!IsStepping || _picture.CurrentSlate is not { } slate)
        {
            SlateParts = [];
            MixesCampuses = false;
            PositionText = string.Empty;
            CanGoPrevious = CanGoNext = false;
            return;
        }

        int index = _picture.SlateIndex;
        PositionText = AccessText.SlatePosition(index, _picture.Slates.Count, a.SlateCountCapped);
        CanGoPrevious = index > 0;
        CanGoNext = index < _picture.Slates.Count - 1;
        SlateParts = a.ChosenCourseIds
            .Select(c => (Course: c, Mine: slate.Where(s => s.CourseId == c).ToList()))
            .Where(x => x.Mine.Count > 0)
            .Select(x => new AccessSlatePartViewModel(inputs.CourseCode(x.Course),
                x.Mine.Select(s => new AccessSlateSectionViewModel(s.Id, s.SectionCode, _picture.IsPinned(s.Id))).ToList()))
            .ToList();
        var campusIds = slate.Where(CampusRule.IsInPerson).Select(s => s.CampusId).Distinct().ToList();
        MixesCampuses = campusIds.Count > 1;
        if (MixesCampuses)
        {
            var names = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
            MixesCampusesTip = AccessText.MixesCampusesTip(
                campusIds.Select(id => id is not null && names.TryGetValue(id, out var n) ? n : "another campus")
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }
}
