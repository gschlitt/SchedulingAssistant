using System;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Represents one overflow entry shown in the More flyout's left rail.
/// Created by <see cref="MoreMenuViewModel"/> from the currently-hidden low-priority items.
/// </summary>
public sealed class MoreMenuEntry
{
    /// <summary>
    /// Stable identifier matching the x:Name of the source control in the top menu bar
    /// (e.g. "NavSchedulingEnvironment"). Used by the panel → VM pipeline.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label shown in the left-rail list.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// CLR type of the management ViewModel to instantiate when this entry is selected.
    /// Resolved through the DI container.
    /// </summary>
    public required Type ViewModelType { get; init; }
}
