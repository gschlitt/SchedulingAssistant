using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Manages the CRUD list of InstructorCommitment records for one instructor in one semester.
/// This VM is embedded inside InstructorListViewModel and is re-contextualized (via SetContext)
/// whenever the user selects a different instructor or the semester changes.
///
/// "Commitments" are non-teaching time obligations — committee meetings, office hours,
/// department duties, etc. They are stored in the InstructorCommitments table and displayed
/// on the Schedule Grid when an instructor overlay is active (as red overlay cards).
///
/// After any successful write (insert, update, delete), _changeNotifier.NotifySectionChanged()
/// is called. This fires a shared event that ScheduleGridViewModel subscribes to, causing the
/// grid to reload and immediately reflect the change.
/// </summary>
public partial class CommitmentsManagementViewModel : ViewModelBase
{
    private readonly IInstructorCommitmentRepository _commitmentRepo;

    // SectionChangeNotifier is the shared singleton that bridges changes in this flyout
    // to the Schedule Grid. Despite its name ("Section"), it is used for any data change
    // that should cause the grid to refresh — including commitment CRUD.
    private readonly SectionChangeNotifier _changeNotifier;
    private readonly WriteLockService _lockService;

    private string _instructorId = string.Empty;
    private string _semesterId = string.Empty;

    [ObservableProperty] private ObservableCollection<InstructorCommitment> _commitments = new();
    [ObservableProperty] private InstructorCommitment? _selectedCommitment;

    /// <summary>Non-null while an Add or Edit form is open inline. Null when no form is active.</summary>
    [ObservableProperty] private CommitmentEditViewModel? _editVm;

    /// <summary>Exposed to XAML to bind button panels' IsEnabled in read-only mode.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    public CommitmentsManagementViewModel(IInstructorCommitmentRepository commitmentRepo, SectionChangeNotifier changeNotifier, WriteLockService lockService)
    {
        _commitmentRepo = commitmentRepo;
        _changeNotifier = changeNotifier;
        _lockService = lockService;
    }

    /// <summary>
    /// Called by InstructorListViewModel whenever the selected instructor or semester changes.
    /// Reloads the commitment list without firing the grid-refresh notifier (since no data
    /// has actually changed — the user just switched context).
    /// </summary>
    public void SetContext(string instructorId, string semesterId)
    {
        _instructorId = instructorId;
        _semesterId = semesterId;
        Load(fireEvent: false);
    }

    /// <summary>
    /// Reloads the commitment list from the database for the current instructor+semester.
    /// The fireEvent parameter is unused at the moment but kept for symmetry with patterns
    /// elsewhere in the codebase that may want a conditional notification in future.
    /// </summary>
    private void Load(bool fireEvent = true)
    {
        try
        {
            LastErrorMessage = null;
            if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId))
            {
                Commitments.Clear();
                SelectedCommitment = null;
                return;
            }

            var dbCommitments = _commitmentRepo.GetByInstructor(_semesterId, _instructorId);
            Commitments.Clear();
            foreach (var c in dbCommitments)
                Commitments.Add(c);
            SelectedCommitment = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error loading commitments: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Add()
    {
        // Seed a new commitment with sensible defaults: Monday at 8:00–8:30 AM.
        // The inline editor lets the user change all fields before saving.
        var commitment = new InstructorCommitment
        {
            Id = Guid.NewGuid().ToString(),
            InstructorId = _instructorId,
            SemesterId = _semesterId,
            Day = 1,
            StartMinutes = 480,   // 8:00 AM
            EndMinutes = 510      // 8:30 AM
        };

        EditVm = new CommitmentEditViewModel(commitment,
            onCancel: () => EditVm = null,
            onSave: async c =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId))
                    {
                        LastErrorMessage = "Error: No instructor or semester selected. Please select both before saving.";
                        return;
                    }
                    c.InstructorId = _instructorId;
                    c.SemesterId = _semesterId;
                    _commitmentRepo.Insert(c);
                    Load();
                    EditVm = null;
                    // Notify the Schedule Grid to reload so the new commitment card
                    // appears immediately if this instructor's overlay is active.
                    _changeNotifier.NotifySectionChanged();
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"Error saving commitment: {ex.Message}";
                }
            });
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedCommitment is null) return;

        // Re-fetch from DB to get the latest state before opening the editor.
        var dbCommitment = _commitmentRepo.GetById(SelectedCommitment.Id);
        if (dbCommitment is null) return;

        EditVm = new CommitmentEditViewModel(dbCommitment,
            onCancel: () => EditVm = null,
            onSave: async c =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId))
                    {
                        LastErrorMessage = "Error: No instructor or semester selected. Please select both before saving.";
                        return;
                    }
                    c.InstructorId = _instructorId;
                    c.SemesterId = _semesterId;
                    _commitmentRepo.Update(c);
                    Load();
                    EditVm = null;
                    // Notify the Schedule Grid to reload so the updated commitment card
                    // (new time, new name, etc.) is reflected immediately.
                    _changeNotifier.NotifySectionChanged();
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"Error updating commitment: {ex.Message}";
                }
            });
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedCommitment is null) return;

        try
        {
            _commitmentRepo.Delete(SelectedCommitment.Id);
            Load();
            // Notify the Schedule Grid to reload so the deleted commitment card
            // disappears immediately if this instructor's overlay is active.
            _changeNotifier.NotifySectionChanged();
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error deleting commitment: {ex.Message}";
        }
    }
}
