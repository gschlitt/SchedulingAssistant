using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Display model for one instructor row in the CSV import preview list.
/// Mutable properties (<see cref="ResolvedMatch"/>, <see cref="Skip"/>) allow the
/// operator to resolve ambiguous matches before importing.
/// </summary>
public partial class InstructorPreviewRow : ObservableObject
{
    /// <summary>Formatted display name, e.g. "Smith, John" or "Chen, W."</summary>
    public string DisplayName { get; }

    /// <summary>Match outcome from CsvImportMatcher: Exact (matched), Unmatched (new), or Ambiguous.</summary>
    public MatchStatus Status { get; }

    /// <summary>Candidate database records when <see cref="Status"/> is Ambiguous.</summary>
    public List<Instructor>? Candidates { get; }

    /// <summary>The operator's chosen match from <see cref="Candidates"/> (Ambiguous rows only).</summary>
    [ObservableProperty] private Instructor? _resolvedMatch;

    /// <summary>True when the operator elects to skip this row entirely.</summary>
    [ObservableProperty] private bool _skip;

    /// <summary>The original parsed CSV row, used for entity creation on import.</summary>
    public InstructorRow Row { get; }

    /// <summary>
    /// The existing database instructor this row matched (Exact status only).
    /// Null for Unmatched/Ambiguous rows.
    /// </summary>
    public Instructor? ExactMatch { get; }

    /// <summary>
    /// Options shown in the ambiguity ComboBox: the candidates formatted as display strings,
    /// plus a "Create new" sentinel. Index-aligned with <see cref="Candidates"/>.
    /// </summary>
    public List<string>? CandidateOptions { get; }

    public InstructorPreviewRow(InstructorRow row, MatchResult<Instructor> match)
    {
        Row = row;
        Status = match.Status;
        Candidates = match.Status == MatchStatus.Ambiguous ? match.Candidates : null;
        ExactMatch = match.Status == MatchStatus.Exact ? match.Resolved : null;

        var last = row.LastName.Trim();
        var first = row.FirstName.Trim();
        DisplayName = string.IsNullOrEmpty(first) ? last : $"{last}, {first}";

        if (Candidates is { Count: > 0 })
        {
            CandidateOptions = Candidates
                .Select(c => FormatCandidate(c))
                .Append("Create new instructor")
                .ToList();
        }
    }

    /// <summary>Selected index in the CandidateOptions ComboBox. -1 = nothing selected.</summary>
    [ObservableProperty] private int _selectedCandidateIndex = -1;

    /// <summary>
    /// When <see cref="SelectedCandidateIndex"/> changes, update <see cref="ResolvedMatch"/>
    /// to the corresponding candidate, or null if "Create new" was chosen.
    /// </summary>
    partial void OnSelectedCandidateIndexChanged(int value)
    {
        if (Candidates is null || value < 0)
        {
            ResolvedMatch = null;
            return;
        }

        ResolvedMatch = value < Candidates.Count ? Candidates[value] : null;
    }

    /// <summary>
    /// Status label shown in the preview: "(new)", "(matched)", or "(ambiguous)".
    /// </summary>
    public string StatusLabel => Status switch
    {
        MatchStatus.Exact => "(matched)",
        MatchStatus.Unmatched => "(new)",
        MatchStatus.Ambiguous => "(ambiguous)",
        _ => ""
    };

    private static string FormatCandidate(Instructor c)
    {
        var name = string.IsNullOrEmpty(c.FirstName) ? c.LastName : $"{c.LastName}, {c.FirstName}";
        return string.IsNullOrEmpty(c.Initials) ? name : $"{name} ({c.Initials})";
    }
}
