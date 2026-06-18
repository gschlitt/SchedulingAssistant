using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Holds the single free-text scheduling note for the currently selected instructor and
/// semester. The note auto-saves when the editing TextBox loses focus (see
/// <see cref="Save"/>), so there is no explicit Save button.
/// </summary>
public partial class SchedulingNotesViewModel : ViewModelBase
{
    private readonly ISchedulingNoteRepository _repo;
    private readonly WriteLockService _lockService;

    private string _instructorId = string.Empty;
    private string _semesterId = string.Empty;

    /// <summary>The last text loaded from or written to the database, used to skip no-op saves.</summary>
    private string _persistedText = string.Empty;

    /// <summary>The editable note body, bound two-way to the TextBox.</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>Gates editing in read-only (non-writer) mode.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    public SchedulingNotesViewModel(ISchedulingNoteRepository repo, WriteLockService lockService)
    {
        _repo = repo;
        _lockService = lockService;
    }

    /// <summary>
    /// Points the view model at a new (instructor, semester) pair and loads its note.
    /// Pass empty strings to clear the editor (no instructor/semester selected).
    /// </summary>
    public void SetContext(string instructorId, string semesterId)
    {
        _instructorId = instructorId;
        _semesterId = semesterId;
        Load();
    }

    private void Load()
    {
        try
        {
            LastErrorMessage = null;
            if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId))
            {
                _persistedText = string.Empty;
                Text = string.Empty;
                return;
            }

            var note = _repo.Get(_semesterId, _instructorId);
            _persistedText = note?.Text ?? string.Empty;
            Text = _persistedText;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error loading scheduling notes: {ex.Message}";
        }
    }

    /// <summary>
    /// Persists the current note text if it has changed. Wired to the TextBox's LostFocus
    /// via <c>LostFocusCommandBehavior</c>; safe to call when nothing changed (it no-ops).
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (!IsWriteEnabled) return;
        if (string.IsNullOrEmpty(_instructorId) || string.IsNullOrEmpty(_semesterId)) return;

        var current = Text ?? string.Empty;
        if (current == _persistedText) return;

        try
        {
            LastErrorMessage = null;
            _repo.Save(new SchedulingNote
            {
                InstructorId = _instructorId,
                SemesterId = _semesterId,
                Text = current
            });
            _persistedText = current;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Error saving scheduling notes: {ex.Message}";
        }
    }
}
