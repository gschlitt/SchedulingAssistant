using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Manages CRUD for releases for a specific instructor in a specific semester.
/// Fires ReleasesChanged event when releases are modified so parent can refresh workload display.
/// </summary>
public partial class ReleaseManagementViewModel : ViewModelBase
{
    private readonly ReleaseRepository _releaseRepo;
    private string _instructorId = string.Empty;
    private string _semesterId = string.Empty;

    [ObservableProperty] private ObservableCollection<ReleaseWorkload> _releases = new();
    [ObservableProperty] private ReleaseWorkload? _selectedRelease;
    [ObservableProperty] private ReleaseEditViewModel? _editVm;
    [ObservableProperty] private string? _lastErrorMessage;

    /// <summary>Fired when releases are added/edited/deleted to notify parent to refresh workload display.</summary>
    public event Action? ReleasesChanged;

    public ReleaseManagementViewModel(ReleaseRepository releaseRepo)
    {
        _releaseRepo = releaseRepo;
    }

    public void SetContext(string instructorId, string semesterId)
    {
        _instructorId = instructorId;
        _semesterId = semesterId;
        Load(fireEvent: false);
    }

    private void Load(bool fireEvent = true)
    {
        try
        {
            LastErrorMessage = null;
            if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId))
            {
                Releases.Clear();
                return;
            }

            var dbReleases = _releaseRepo.GetByInstructor(_semesterId, _instructorId);
            var workloads = dbReleases
                .Select(r => new ReleaseWorkload { Id = r.Id, Title = r.Title, WorkloadValue = r.WorkloadValue })
                .ToList();
            Releases = new ObservableCollection<ReleaseWorkload>(workloads);

            if (fireEvent)
                ReleasesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error loading releases: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Add()
    {
        var release = new Release
        {
            Id = Guid.NewGuid().ToString(),
            InstructorId = _instructorId,
            SemesterId = _semesterId
        };

        EditVm = new ReleaseEditViewModel(release,
            onCancel: () => EditVm = null,
            onSave: async r =>
            {
                try
                {
                    r.InstructorId = _instructorId;
                    r.SemesterId = _semesterId;
                    _releaseRepo.Insert(r);
                    Load();
                    EditVm = null;
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"Error saving release: {ex.Message}";
                }
            });
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedRelease is null) return;

        var dbRelease = _releaseRepo.GetById(SelectedRelease.Id);
        if (dbRelease is null) return;

        EditVm = new ReleaseEditViewModel(dbRelease,
            onCancel: () => EditVm = null,
            onSave: async r =>
            {
                try
                {
                    r.InstructorId = _instructorId;
                    r.SemesterId = _semesterId;
                    _releaseRepo.Update(r);
                    Load();
                    EditVm = null;
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"Error updating release: {ex.Message}";
                }
            });
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRelease is null) return;

        try
        {
            _releaseRepo.Delete(SelectedRelease.Id);
            Load();
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error deleting release: {ex.Message}";
        }
    }
}
