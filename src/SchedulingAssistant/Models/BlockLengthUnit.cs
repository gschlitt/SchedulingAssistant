namespace SchedulingAssistant.Models;

/// <summary>
/// Controls how block lengths are displayed and entered throughout the application.
/// The underlying storage is always in hours; this is a display-only preference.
/// </summary>
public enum BlockLengthUnit
{
    Hours   = 0,
    Minutes = 1,
}
