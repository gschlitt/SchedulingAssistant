using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// Test double for <see cref="ISectionCardState"/>: set <see cref="Heading"/> to simulate an open section card
/// (null means none is open). Setting it raises <see cref="OpenCardChanged"/>, like the real Section List.
/// </summary>
public sealed class FakeSectionCardState : ISectionCardState
{
    private string? _heading;

    /// <summary>The heading reported as open; null when no card is open. Setting it raises <see cref="OpenCardChanged"/>.</summary>
    public string? Heading
    {
        get => _heading;
        set { _heading = value; OpenCardChanged?.Invoke(); }
    }

    /// <inheritdoc />
    public string? OpenCardHeading => Heading;

    /// <inheritdoc />
    public event Action? OpenCardChanged;
}
