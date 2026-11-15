namespace TermPoint.Licensing;

/// <summary>
/// Abstraction over the system clock, allowing date overrides for testing
/// license expiry and trial period logic.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current UTC date and time.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// The current calendar date in the user's local time zone. Licence expiry is compared against this,
    /// so a licence that "expires July 1" works through the end of July 1 local time.
    /// </summary>
    DateOnly Today { get; }
}

/// <summary>
/// Default clock that delegates to <see cref="DateTime.UtcNow"/>.
/// </summary>
public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

#if DEBUG
/// <summary>
/// Clock pinned to a fixed date, used for debugging license expiry and trial logic.
/// Activated by setting the TERMPOINT_DEBUG_DATE environment variable (e.g. "2027-08-01").
/// </summary>
public class FixedClock : IClock
{
    public DateTime UtcNow { get; }

    /// <summary>The date of the fixed instant (time-zone independent, so tests are stable on any machine).</summary>
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public FixedClock(DateTime fixedUtcDate) => UtcNow = fixedUtcDate;

    /// <summary>
    /// Returns a <see cref="FixedClock"/> if TERMPOINT_DEBUG_DATE is set and valid,
    /// otherwise returns a <see cref="SystemClock"/>.
    /// </summary>
    public static IClock FromEnvironmentOrSystem()
    {
        var envDate = Environment.GetEnvironmentVariable("TERMPOINT_DEBUG_DATE");
        if (envDate is not null && DateTime.TryParse(envDate, out var parsed))
            return new FixedClock(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return new SystemClock();
    }
}
#endif
