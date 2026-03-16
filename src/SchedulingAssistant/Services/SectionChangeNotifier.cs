namespace SchedulingAssistant.Services;

/// <summary>
/// Signals that non-section grid content has changed and the schedule grid should reload.
/// Currently used exclusively for instructor commitment CRUD
/// (<c>CommitmentsManagementViewModel</c> calls <see cref="NotifySectionChanged"/> after
/// any insert, update, or delete; <c>ScheduleGridViewModel</c> subscribes to trigger a
/// full grid refresh including commitment blocks).
///
/// Section data changes (inserts, updates, deletes) are no longer routed through this
/// class — they are handled by <see cref="SectionStore"/>, which maintains the shared
/// section cache and fires <see cref="SectionStore.SectionsChanged"/> to all subscribers.
/// </summary>
public class SectionChangeNotifier
{
    public event Action? SectionChanged;

    public void NotifySectionChanged()
    {
        SectionChanged?.Invoke();
    }
}
