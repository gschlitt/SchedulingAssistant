namespace SchedulingAssistant.Services;

/// <summary>
/// Provides a notification mechanism when sections are changed externally
/// (e.g., via the schedule grid context menu). Both SectionListViewModel and
/// ScheduleGridViewModel subscribe to this to stay in sync.
/// </summary>
public class SectionChangeNotifier
{
    public event Action? SectionChanged;

    public void NotifySectionChanged()
    {
        SectionChanged?.Invoke();
    }
}
