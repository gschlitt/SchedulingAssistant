/// <summary>
/// Controls what the Section List does when the Schedule Grid filter passes a section.
/// </summary>
public enum FilterPassBehavior
{
    /// <summary>Matching cards receive a visual highlight tint. Collapsed state is unchanged.</summary>
    Highlight,

    /// <summary>Matching cards are expanded; all others are collapsed.</summary>
    Expand,
}
