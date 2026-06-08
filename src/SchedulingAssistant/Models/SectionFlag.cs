namespace SchedulingAssistant.Models;

/// <summary>
/// Advisory attention flag a user can apply to a section. Carries no scheduling
/// semantics — it is a purely visual cue meaning "this section needs attention"
/// (the reason, if any, is captured in the section's note). The three colors are
/// interchangeable; their meaning is whatever the administrator decides.
/// </summary>
public enum SectionFlag
{
    /// <summary>No flag.</summary>
    None = 0,
    Red,
    Blue,
    Green
}
