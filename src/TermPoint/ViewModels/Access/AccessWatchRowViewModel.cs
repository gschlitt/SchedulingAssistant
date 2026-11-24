using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// One row of the Access tab's list: a watch, its mark (▲ attention, ● fragile, ○ ok) and its health line.
/// Immutable; the tab rebuilds its rows when the access state changes.
/// </summary>
public sealed class AccessWatchRowViewModel
{
    /// <summary>The watch's name (its tag's name).</summary>
    public string Name { get; }

    /// <summary>The one-line health, e.g. "3 slates · 1 section stranded · 2 conflicts" (see <see cref="AccessText.HealthLine"/>).</summary>
    public string HealthLine { get; }

    /// <summary>The worst health of the watch's semesters.</summary>
    public AccessHealth Health { get; }

    /// <summary>True when the watch needs a look: no full slate, a missing component, or too big to check.</summary>
    public bool IsAttention => Health is AccessHealth.Attention or AccessHealth.TooLarge;

    /// <summary>True when a full slate exists but depends on one section, or every slate mixes campuses.</summary>
    public bool IsFragile => Health == AccessHealth.Fragile;

    /// <summary>True when nothing needs a look.</summary>
    public bool IsOk => Health == AccessHealth.Ok;

    /// <summary>The row's mark: "▲" for attention, "●" for fragile, "○" for ok.</summary>
    public string Mark => IsAttention ? "▲" : IsFragile ? "●" : "○";

    /// <summary>Creates a row.</summary>
    /// <param name="name">The watch's name.</param>
    /// <param name="healthLine">The health line.</param>
    /// <param name="health">The watch's health.</param>
    public AccessWatchRowViewModel(string name, string healthLine, AccessHealth health)
    {
        Name = name;
        HealthLine = healthLine;
        Health = health;
    }
}
