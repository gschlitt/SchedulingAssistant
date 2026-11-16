using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// Test double for <see cref="ISectionCardState"/>: set <see cref="Heading"/> to simulate an open section card
/// (null means none is open).
/// </summary>
public sealed class FakeSectionCardState : ISectionCardState
{
    /// <summary>The heading reported as open; null when no card is open.</summary>
    public string? Heading { get; set; }

    /// <inheritdoc />
    public string? OpenCardHeading => Heading;
}
