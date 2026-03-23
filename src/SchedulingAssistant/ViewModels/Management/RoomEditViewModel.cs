using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the inline Add/Edit form in the Rooms management panel.
/// Communicates results back to the parent list via callbacks.
/// </summary>
public partial class RoomEditViewModel : ViewModelBase
{
    [ObservableProperty] private string _building = string.Empty;
    [ObservableProperty] private string _roomNumber = string.Empty;
    [ObservableProperty] private int _capacity;
    [ObservableProperty] private string _features = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    /// <summary>Campus choices for the campus dropdown, including a leading "(none)" sentinel.</summary>
    public List<CampusOption> CampusOptions { get; }

    /// <summary>Currently selected campus option; null falls back to "(none)".</summary>
    [ObservableProperty] private CampusOption? _selectedCampus;

    /// <summary>"Add Room" or "Edit Room" depending on <see cref="IsNew"/>.</summary>
    public string Title => IsNew ? "Add Room" : "Edit Room";

    /// <summary>True when adding a new room; false when editing an existing one.</summary>
    public bool IsNew { get; }

    private readonly Room _room;
    private readonly Action<Room> _onSave;
    private readonly Action _onCancel;

    /// <param name="room">The model object to populate on save.</param>
    /// <param name="isNew">True when adding; false when editing.</param>
    /// <param name="campusOptions">Campus choices including the leading "(none)" entry.</param>
    /// <param name="onSave">Called with the updated model when the user saves.</param>
    /// <param name="onCancel">Called when the user cancels.</param>
    public RoomEditViewModel(
        Room room,
        bool isNew,
        List<CampusOption> campusOptions,
        Action<Room> onSave,
        Action onCancel)
    {
        _room = room;
        IsNew = isNew;
        CampusOptions = campusOptions;
        _onSave = onSave;
        _onCancel = onCancel;

        Building   = room.Building;
        RoomNumber = room.RoomNumber;
        Capacity   = room.Capacity;
        Features   = room.Features;
        Notes      = room.Notes;

        SelectedCampus = campusOptions.FirstOrDefault(c => c.Id == room.CampusId)
                         ?? campusOptions[0]; // "(none)" sentinel
    }

    /// <summary>Writes field values back to the model and invokes the save callback.</summary>
    [RelayCommand]
    private void Save()
    {
        _room.Building   = Building.Trim();
        _room.RoomNumber = RoomNumber.Trim();
        _room.Capacity   = Capacity;
        _room.Features   = Features.Trim();
        _room.Notes      = Notes.Trim();
        _room.CampusId   = SelectedCampus?.Id; // null when "(none)" is selected
        _onSave(_room);
    }

    /// <summary>Discards changes and invokes the cancel callback.</summary>
    [RelayCommand]
    private void Cancel() => _onCancel();
}
