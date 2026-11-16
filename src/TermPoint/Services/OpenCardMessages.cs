namespace TermPoint.Services;

/// <summary>
/// The wording shown when a save is refused because a section card is open in the Section List. Kept in one place
/// so every refusing feature says the same thing.
/// <para>
/// approved wording (Greg, 2026-11-16); do not change without his OK.
/// </para>
/// </summary>
public static class OpenCardMessages
{
    /// <summary>
    /// The refusal text: tells the user to finish the open card first.
    /// approved wording (Greg, 2026-11-16); do not change without his OK.
    /// </summary>
    /// <param name="heading">Heading of the open card (as <see cref="ISectionCardState.OpenCardHeading"/>).</param>
    /// <returns>"Finish editing {heading} in the Section List first."</returns>
    public static string FinishFirst(string heading) =>
        $"Finish editing {heading} in the Section List first.";

    /// <summary>
    /// The refusal text for a drag: <see cref="FinishFirst"/> plus what to do before dragging again.
    /// approved wording (Greg, 2026-11-16); do not change without his OK.
    /// </summary>
    /// <param name="heading">Heading of the open card.</param>
    /// <returns>The <see cref="FinishFirst"/> text followed by " Apply or Cancel it there, then drag again."</returns>
    public static string FinishFirstToDrag(string heading) =>
        FinishFirst(heading) + " Apply or Cancel it there, then drag again.";

    /// <summary>
    /// Asks the card state whether a save must be refused right now.
    /// </summary>
    /// <param name="state">The Section List's card state.</param>
    /// <returns>The <see cref="FinishFirst"/> text when a card is open; <c>null</c> when saving is fine.</returns>
    public static string? WhyRefused(ISectionCardState state) =>
        state.OpenCardHeading is { } heading ? FinishFirst(heading) : null;
}
