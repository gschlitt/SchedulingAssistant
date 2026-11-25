using CommunityToolkit.Mvvm.Input;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>The pages of the Access tab.</summary>
public enum AccessTabPage
{
    /// <summary>The list of watches, with the session's notices.</summary>
    List,
    /// <summary>The detail of the open watch, or of a course checked on its own.</summary>
    Detail,
    /// <summary>Check all courses.</summary>
    Check,
}

/// <summary>
/// One notice in the Access tab (rule 32): its sentence, whether it is bad news, Open (opens the watch in the semester
/// the notice is about) and ✕ (dismisses it for this session). Immutable; the tab rebuilds its notices when they change.
/// </summary>
public sealed class AccessNoticeViewModel
{
    /// <summary>The notice's id in <see cref="TermPoint.Services.Access.AccessState.Notices"/>.</summary>
    public int Id { get; }

    /// <summary>The sentence, e.g. "Moving CHEM 213 A means Chem Y2 has no full slate. Open Chem Y2 for more detail.".</summary>
    public string Text { get; }

    /// <summary>True for bad news (drawn as a problem); false for the good news that a watch has a full slate again.</summary>
    public bool IsBad { get; }

    /// <summary>Opens the watch the notice is about.</summary>
    public IRelayCommand OpenCommand { get; }

    /// <summary>Dismisses the notice.</summary>
    public IRelayCommand DismissCommand { get; }

    /// <summary>Creates a notice row.</summary>
    /// <param name="id">The notice's id.</param>
    /// <param name="text">The sentence.</param>
    /// <param name="isBad">Whether it is bad news.</param>
    /// <param name="open">What Open does.</param>
    /// <param name="dismiss">What ✕ does.</param>
    public AccessNoticeViewModel(int id, string text, bool isBad, Action open, Action dismiss)
    {
        Id = id;
        Text = text;
        IsBad = isBad;
        OpenCommand = new RelayCommand(open);
        DismissCommand = new RelayCommand(dismiss);
    }
}

/// <summary>
/// One row of the Access tab's list: a watch, its mark (▲ attention, ● fragile, ○ ok) and its health line.
/// Immutable; the tab rebuilds its rows when the access state changes.
/// </summary>
public sealed class AccessWatchRowViewModel
{
    /// <summary>The watch's name (its tag's name).</summary>
    public string Name { get; }

    /// <summary>The watched tag's id (what opening the row opens).</summary>
    public string TagId { get; }

    /// <summary>The semester the row speaks of (the watch's worst one), which opening the row looks at first.</summary>
    public string SemesterId { get; }

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
    /// <param name="tagId">The watched tag's id.</param>
    /// <param name="semesterId">The semester the row speaks of.</param>
    public AccessWatchRowViewModel(string name, string healthLine, AccessHealth health, string tagId = "", string semesterId = "")
    {
        TagId = tagId;
        SemesterId = semesterId;
        Name = name;
        HealthLine = healthLine;
        Health = health;
    }
}
