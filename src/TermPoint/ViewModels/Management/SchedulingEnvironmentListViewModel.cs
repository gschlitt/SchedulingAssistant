using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using System.Collections.ObjectModel;
using System.Data.Common;

namespace TermPoint.ViewModels.Management;

public partial class SchedulingEnvironmentListViewModel : ViewModelBase, IDismissableEditor, IUnappliedChangesHost, IDisposable
{
    private readonly ISchedulingEnvironmentRepository _repo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository? _roomRepo;
    private readonly IDatabaseContext _db;
    private readonly SectionListViewModel _sectionListVm;
    private readonly IDialogService _dialog;
    private readonly string _type;
    private readonly WriteLockService _lockService;
    private readonly AccessWatchToggler? _watchToggler;
    private readonly GridChangeNotifier? _changeNotifier;
    private readonly AccessSetupService? _setupService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    public string DisplayName { get; }

    /// <summary>
    /// True only for the Tags category: the list shows the eye column, which turns a tag's access watch on or off
    /// (<see cref="ToggleWatchCommand"/>).
    /// </summary>
    public bool ShowWatchColumn => _type == SchedulingEnvironmentTypes.Tag;

    /// <summary>
    /// Turns the access watch on a tag on or off through <see cref="AccessWatchToggler"/> (which saves the tag, makes
    /// the access state stale and redraws the grid), then reloads the list so the row redraws. Does nothing when
    /// this computer cannot save or no toggler was given. It writes only the tag, never a section, so the
    /// open-section-card rule does not apply.
    /// </summary>
    /// <param name="tag">The tag's row.</param>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void ToggleWatch(SchedulingEnvironmentValue? tag)
    {
        if (tag is null || _watchToggler is not { CanToggle: true } toggler) return;
        LastErrorMessage = null;

        // The first watch before the access setup (revision 8f): the wizard opens first, and the watch is made only
        // when it finishes. Cancelling makes no watch.
        if (!tag.IsAccessWatch && _setupService is { IsSetUp: false, CanSave: true } setup)
        {
            var tagId = tag.Id;
            setup.RequestSetupWizard(() => toggler.SetWatched(tagId, true));
            return;
        }

        try
        {
            toggler.SetWatched(tag.Id, !tag.IsAccessWatch);
            var selectedId = SelectedItem?.Id;
            Load();
            SelectedItem = Items.FirstOrDefault(x => x.Id == selectedId);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SchedulingEnvironmentListViewModel.ToggleWatch");
            LastErrorMessage = WriteFailureText.For(ex, "The watch could not be changed. No changes were made. Please try again.");
        }
    }

    /// <summary>
    /// Optional explanatory sentence shown below the title, above the list.
    /// Null or empty means no description row is rendered.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// When true, the "Section Code Abbreviation" field is shown in the edit form and
    /// as a column in the list. Currently only true for the Campus property type.
    /// </summary>
    public bool ShowAbbreviation { get; }

    [ObservableProperty] private ObservableCollection<SchedulingEnvironmentValue> _items = new();
    [ObservableProperty] private SchedulingEnvironmentValue? _selectedItem;
    [ObservableProperty] private SchedulingEnvironmentEditViewModel? _editVm;

    /// <inheritdoc/>
    public IUnappliedChanges? FindUnappliedChanges() => UnappliedChangesFinder.First(EditVm);

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (EditVm is null) return false;
        EditVm.CancelCommand.Execute(null);
        return true;
    }

    public SchedulingEnvironmentListViewModel(
        string propertyType,
        string displayName,
        ISchedulingEnvironmentRepository repo,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IDatabaseContext db,
        SectionListViewModel sectionListVm,
        IDialogService dialog,
        WriteLockService lockService,
        bool showAbbreviation = false,
        string? description = null,
        IRoomRepository? roomRepo = null,
        AccessWatchToggler? watchToggler = null,
        GridChangeNotifier? changeNotifier = null,
        AccessSetupService? setupService = null)
    {
        _setupService = setupService;
        if (_setupService is not null) _setupService.CanSaveChanged += OnLockStateChanged;
        _watchToggler = watchToggler;
        _changeNotifier = changeNotifier;
        _type = propertyType;
        DisplayName = displayName;
        Description = description;
        _repo = repo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _db = db;
        _sectionListVm = sectionListVm;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        ShowAbbreviation = showAbbreviation;
        Load();
    }

    public void Load()
    {
        Items = new ObservableCollection<SchedulingEnvironmentValue>(_repo.GetAll(_type));
        if (!ShowComponentColumn) return;
        // Before the setup the boxes show the guess the analyzer counts by (nothing is stored yet).
        var guess = _setupService is { IsSetUp: false } s ? s.GuessedComponentTypeIds() : null;
        foreach (var v in Items) v.ComponentBoxChecked = guess?.Contains(v.Id) ?? v.IsComponent;
        OnPropertyChanged(nameof(ShowComponentGuessNote));
        ComponentRows = new ObservableCollection<SectionTypeRowViewModel>(
            Items.Select(v => new SectionTypeRowViewModel(this, v, v.ComponentBoxChecked)));
    }

    // ── Section Type: the Component box (Access Analyzer revision 8f) ────────

    /// <summary>True only for the Section Type category: the list shows the Component column (<see cref="ComponentRows"/>).</summary>
    public bool ShowComponentColumn => _type == SchedulingEnvironmentTypes.SectionType;

    /// <summary>One row per section type, in list order, with its Component box. Empty for other categories. Rebuilt by <see cref="Load"/>.</summary>
    [ObservableProperty] private ObservableCollection<SectionTypeRowViewModel> _componentRows = new();

    /// <summary>
    /// The row whose "Stop counting it?" ask is showing (under the list), or null. One at a time: the list is a
    /// DataGrid of the stored values, so the ask can't sit inside the row.
    /// </summary>
    [ObservableProperty] private SectionTypeRowViewModel? _pendingAsk;

    /// <summary>
    /// A Component box in the list was clicked (the cell shows the stored value, so it only changes after a save).
    /// Flips that type's row, which saves or asks (<see cref="OnComponentBoxChanged"/>). Ignored while an ask is open.
    /// </summary>
    /// <param name="value">The section type whose box was clicked.</param>
    [RelayCommand]
    private void ToggleComponent(SchedulingEnvironmentValue? value)
    {
        if (value is null || PendingAsk is not null || !CanChangeComponents) return;
        var row = ComponentRows.FirstOrDefault(r => r.Value.Id == value.Id);
        if (row is not null) row.IsComponent = !row.IsComponent;
    }

    /// <summary>The Component column heading's tooltip.</summary>
    public string ComponentHeadingTip => AccessText.ComponentHeadingTip;

    /// <summary>True on Section Type until a Component box is saved (the setup is not done): the boxes show a guess.</summary>
    public bool ShowComponentGuessNote => ShowComponentColumn && _setupService is { IsSetUp: false };

    /// <summary>The line saying the ticks are TermPoint's guess.</summary>
    public string ComponentGuessNote => AccessText.ComponentGuessNote;

    /// <summary>
    /// Whether the Component boxes can be changed: this computer can save. They work before the access-watch setup
    /// too; the first click there counts as the setup (<see cref="AccessSetupService.SetComponentBeforeSetup"/>).
    /// </summary>
    public bool CanChangeComponents => _setupService is { CanSave: true };

    /// <summary>
    /// A Component box was clicked. Ticking marks the type and, when any course has its components set, opens the
    /// wizard's "which courses have it?" step (otherwise every course still takes its components from its sections,
    /// so there is nothing to ask and the user stays in the list). Unticking takes the type off at once when no course lists it, otherwise shows the in-row ask first. Before the
    /// setup, the shown guess plus this change is saved as the setup (no course lists anything yet, so no ask). A
    /// refused or failed change puts the box back.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="previous">The box's state before the click.</param>
    internal void OnComponentBoxChanged(SectionTypeRowViewModel row, bool previous)
    {
        LastErrorMessage = null;
        if (!CanChangeComponents || _setupService is not { } setup)
        {
            row.SetWithoutSaving(previous);
            return;
        }
        try
        {
            if (!setup.IsSetUp)
            {
                setup.SetComponentBeforeSetup(row.Value.Id, row.IsComponent);
                ReloadKeepingSelection();
                if (row.IsComponent && setup.AnyCourseComponentsSet()) setup.RequestNewTypeWizard(row.Value.Id);
                return;
            }

            if (row.IsComponent)
            {
                setup.MarkComponent(row.Value.Id);
                ReloadKeepingSelection();
                if (setup.AnyCourseComponentsSet()) setup.RequestNewTypeWizard(row.Value.Id);
                return;
            }

            var listing = setup.CoursesListing(row.Value.Id);
            if (listing.Count == 0)
            {
                setup.UnmarkComponent(row.Value.Id);
                Load();
                return;
            }
            row.AskText = AccessText.StopCountingAsk(row.Value.Name, listing.Select(c => c.CalendarCode).ToList());
            row.IsAsking = true;
            PendingAsk = row;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SchedulingEnvironmentListViewModel.OnComponentBoxChanged");
            row.SetWithoutSaving(previous);
            LastErrorMessage = WriteFailureText.For(ex, "The change could not be saved. No changes were made. Please try again.");
        }
    }

    /// <summary>Reloads the list after a Component change, keeping the selected section type selected.</summary>
    private void ReloadKeepingSelection()
    {
        var selectedId = SelectedItem?.Id;
        Load();
        SelectedItem = Items.FirstOrDefault(x => x.Id == selectedId);
    }

    /// <summary>"Stop counting it": unmarks the type and removes it from every course that lists it, in one transaction.</summary>
    /// <param name="row">The row whose ask was confirmed.</param>
    internal void ConfirmUnmark(SectionTypeRowViewModel row)
    {
        LastErrorMessage = null;
        PendingAsk = null;
        if (_setupService is not { CanSave: true } setup) { CancelUnmark(row); return; }
        try
        {
            setup.UnmarkComponent(row.Value.Id);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SchedulingEnvironmentListViewModel.ConfirmUnmark");
            row.IsAsking = false;
            row.SetWithoutSaving(true);
            LastErrorMessage = WriteFailureText.For(ex, "The change could not be saved. No changes were made. Please try again.");
        }
    }

    /// <summary>Cancel on the ask: the box goes back to ticked and nothing is saved.</summary>
    /// <param name="row">The row.</param>
    internal void CancelUnmark(SectionTypeRowViewModel row)
    {
        PendingAsk = null;
        row.IsAsking = false;
        row.SetWithoutSaving(true);
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleWatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeComponents));
        foreach (var row in ComponentRows) row.RefreshEnabled();
    }

    /// <summary>
    /// Re-evaluates the Move Up/Down button states whenever the selection changes.
    /// </summary>
    partial void OnSelectedItemChanged(SchedulingEnvironmentValue? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => _lockService.IsWriter && SelectedItem != null && Items.IndexOf(SelectedItem) > 0;
    private bool CanMoveDown() => _lockService.IsWriter && SelectedItem != null && Items.IndexOf(SelectedItem) < Items.Count - 1;

    /// <summary>
    /// Moves the selected item one position earlier in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => ApplyMove(Items.IndexOf(SelectedItem!), -1);

    /// <summary>
    /// Moves the selected item one position later in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => ApplyMove(Items.IndexOf(SelectedItem!), +1);

    /// <summary>
    /// Reorders the item at <paramref name="index"/> by <paramref name="delta"/> positions
    /// (+1 or -1), then re-packs all sort orders as 0, 1, 2, … and saves every changed item.
    /// Reloads the list from DB so the UI reflects the stored order.
    /// </summary>
    /// <param name="index">Zero-based index of the item to move.</param>
    /// <param name="delta">Direction: -1 to move up, +1 to move down.</param>
    private void ApplyMove(int index, int delta)
    {
        var list = Items.ToList();
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(index + delta, item);

        // Re-pack sort orders densely (0, 1, 2, …) to avoid gaps accumulating over time.
        for (var i = 0; i < list.Count; i++)
            list[i].SortOrder = i;

        foreach (var v in list)
            _repo.Update(v);

        var selectedId = item.Id;
        Load();
        SelectedItem = Items.FirstOrDefault(v => v.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        // New items land at the end of the list.
        var value = new SchedulingEnvironmentValue
        {
            SortOrder = Items.Count > 0 ? Items.Max(i => i.SortOrder) + 1 : 0
        };
        EditVm = new SchedulingEnvironmentEditViewModel(value, isNew: true,
            onSave: v => { _repo.Insert(_type, v); Load(); SelectedItem = Items.FirstOrDefault(x => x.Id == v.Id); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new SchedulingEnvironmentValue
        {
            Id = SelectedItem.Id,
            Name = SelectedItem.Name,
            SectionCodeAbbreviation = SelectedItem.SectionCodeAbbreviation,
            SortOrder = SelectedItem.SortOrder,
            IsAccessWatch = SelectedItem.IsAccessWatch,
            WatchMultiCampus = SelectedItem.WatchMultiCampus,
            IsComponent = SelectedItem.IsComponent,
        }.WithExtensionDataFrom(SelectedItem); // Save writes the whole copy back: keep unknown JSON fields.
        EditVm = new SchedulingEnvironmentEditViewModel(copy, isNew: false,
            onSave: v => { _repo.Update(v); Load(); SelectedItem = Items.FirstOrDefault(x => x.Id == v.Id); EditVm = null; _sectionListVm.Reload(); _changeNotifier?.NotifyGridContentChanged(); },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name, excludeId: copy.Id),
            showAbbreviation: ShowAbbreviation);
    }

    /// <summary>
    /// Deletes the selected value after confirmation, clearing it from every section (or course, instructor
    /// or room) that references it, in one transaction.
    /// <para>
    /// Refuses, before asking anything, while a section card is open for editing or creating
    /// (<see cref="OpenCardMessages.WhyRefused"/>, shown in <see cref="ViewModelBase.LastErrorMessage"/>).
    /// Why: the delete rewrites sections and reloads the section list, which closes an open card and
    /// silently throws away whatever the user had typed into it. This applies to every value type,
    /// because every type reloads the list after a delete.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedItem is null) return;
        LastErrorMessage = null;

        var refusal = OpenCardMessages.WhyRefused(_sectionListVm);
        if (refusal is not null)
        {
            LastErrorMessage = refusal;
            return;
        }

        var id = SelectedItem.Id;
        var name = SelectedItem.Name;

        var affected = _type == SchedulingEnvironmentTypes.StaffType
            ? "all instructors that reference it"
            : _type == SchedulingEnvironmentTypes.Tag
                ? "all sections and courses that reference it"
                : _type == SchedulingEnvironmentTypes.RoomType
                    ? "all rooms that reference it"
                    : "all sections in all semesters that reference it";

        if (!await _dialog.Confirm($"Delete \"{name}\"?\n\nThis will also remove it from {affected}."))
            return;

        // Desktop builds wrap this in a DB transaction for atomicity; the WASM demo
        // uses in-memory repositories with no real connection, so tx is null there.
        DbTransaction? tx = _db.BeginWriteTransaction();
        try
        {
            var sections = _sectionRepo.GetAll();
            foreach (var section in sections)
            {
                var changed = false;
                switch (_type)
                {
                    case SchedulingEnvironmentTypes.Tag:
                        changed = section.TagIds.Remove(id);
                        break;
                    case SchedulingEnvironmentTypes.Resource:
                        changed = section.ResourceIds.Remove(id);
                        break;
                    case SchedulingEnvironmentTypes.SectionType:
                        if (section.SectionTypeId == id) { section.SectionTypeId = null; changed = true; }
                        break;
                    case SchedulingEnvironmentTypes.DeliveryType:
                        if (section.DeliveryTypeId == id) { section.DeliveryTypeId = null; changed = true; }
                        break;
                    case SchedulingEnvironmentTypes.Reserve:
                        var before = section.Reserves.Count;
                        section.Reserves.RemoveAll(r => r.ReserveId == id);
                        changed = section.Reserves.Count != before;
                        break;
                    case SchedulingEnvironmentTypes.MeetingType:
                        foreach (var sched in section.Schedule.Where(s => s.MeetingTypeId == id))
                        {
                            sched.MeetingTypeId = null;
                            changed = true;
                        }
                        break;
                    case SchedulingEnvironmentTypes.RoomType:
                        break;
                }
                if (changed) _sectionRepo.Update(section, tx);
            }

            if (_type == SchedulingEnvironmentTypes.StaffType)
            {
                var instructors = _instructorRepo.GetAll();
                foreach (var inst in instructors.Where(i => i.StaffTypeId == id))
                {
                    inst.StaffTypeId = null;
                    _instructorRepo.Update(inst, tx);
                }
            }

            // Tags: also remove the deleted tag from every course that references it.
            if (_type == SchedulingEnvironmentTypes.Tag)
            {
                var courses = _courseRepo.GetAll();
                foreach (var course in courses)
                {
                    if (course.TagIds.Remove(id))
                        _courseRepo.Update(course, tx);
                }
            }

            // Room types: clear the deleted type from every room that references it.
            if (_type == SchedulingEnvironmentTypes.RoomType && _roomRepo is not null)
            {
                var rooms = _roomRepo.GetAll();
                foreach (var room in rooms.Where(r => r.RoomTypeId == id))
                {
                    room.RoomTypeId = null;
                    _roomRepo.Update(room);
                }
            }

            _repo.Delete(id, tx);
            tx?.Commit();
            Load();
            _sectionListVm.Reload();
            // Names of tags and section types appear in the grid's filters and in the Access hovers and list.
            _changeNotifier?.NotifyGridContentChanged();
        }
        catch (Exception ex)
        {
            tx?.Rollback();
            App.Logger.LogError(ex, "SchedulingEnvironmentListViewModel.Delete");
            LastErrorMessage = WriteFailureText.For(ex, "The delete could not be completed. No changes were made. Please try again.");
        }
        finally
        {
            tx?.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lockService.LockStateChanged -= OnLockStateChanged;
        if (_setupService is not null) _setupService.CanSaveChanged -= OnLockStateChanged;
    }
}
