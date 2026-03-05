using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Manages CRUD for instructor commitments (non-teaching time blocks) for a specific instructor in a specific semester.
/// </summary>
public partial class CommitmentsManagementViewModel : ViewModelBase
{
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SectionChangeNotifier _changeNotifier;
    private string _instructorId = string.Empty;
    private string _semesterId = string.Empty;

    [ObservableProperty] private ObservableCollection<InstructorCommitment> _commitments = new();
    [ObservableProperty] private InstructorCommitment? _selectedCommitment;
    [ObservableProperty] private CommitmentEditViewModel? _editVm;
    [ObservableProperty] private string? _lastErrorMessage;

    public CommitmentsManagementViewModel(InstructorCommitmentRepository commitmentRepo, SectionChangeNotifier changeNotifier)
    {
        _commitmentRepo = commitmentRepo;
        _changeNotifier = changeNotifier;
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
            _changeNotifier.NotifySectionChanged();
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error deleting commitment: {ex.Message}";
        }
    }
}
