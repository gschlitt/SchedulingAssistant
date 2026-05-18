namespace SchedulingAssistant.Services;

/// <summary>
/// Signals that non-section grid content has changed and the schedule grid should reload.
/// Used by management VMs (commitment CRUD, legal start time edits) to notify
/// <see cref="ViewModels.GridView.ScheduleGridViewModel"/>, which subscribes to
/// <see cref="GridContentChanged"/> and triggers a full grid refresh.
///
/// Section data changes (inserts, updates, deletes) are not routed through this
/// class — they are handled by <see cref="SectionStore"/>, which maintains the shared
/// section cache and fires <see cref="SectionStore.SectionsChanged"/> to all subscribers.
/// </summary>
public class GridChangeNotifier
{
    public event Action? GridContentChanged;

    public void NotifyGridContentChanged()
    {
        GridContentChanged?.Invoke();
    }
}
