namespace TermPoint.Models;

/// <summary>
/// The three kinds of <see cref="SubjectFocus"/>. The numeric values are PERSISTED in
/// <c>settings.json</c> (via <see cref="SavedFocus"/>, serialized like the other settings enums), so they must
/// never be renumbered or reused.
/// </summary>
public enum FocusKind
{
    /// <summary>No focus: every subject is in focus and nothing is hidden. The default for a small department.</summary>
    All = 0,

    /// <summary>Nothing is in focus: the section list shows its empty state until the user chooses subjects.</summary>
    None = 1,

    /// <summary>Exactly the subjects named by <see cref="SubjectFocus.SubjectIds"/> are in focus (never empty).</summary>
    Subjects = 2,
}

/// <summary>
/// An immutable "what am I working on" value (spec item 20, load by subject): All, None, or a non-empty set of
/// subjects. Levels play no part. Two foci with the same kind and the same subject ids are equal, whatever order
/// the ids were given in.
/// <para>
/// Build one with <see cref="All"/>, <see cref="None"/> or <see cref="ForSubjects"/>; the constructor is private so
/// the invariant "<see cref="SubjectIds"/> is empty unless <see cref="Kind"/> is <see cref="FocusKind.Subjects"/>,
/// and non-empty when it is" always holds.
/// </para>
/// </summary>
public sealed class SubjectFocus : IEquatable<SubjectFocus>
{
    /// <summary>The shared "every subject" value.</summary>
    public static SubjectFocus All { get; } = new(FocusKind.All, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>The shared "nothing in focus" value.</summary>
    public static SubjectFocus None { get; } = new(FocusKind.None, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Which kind of focus this is.</summary>
    public FocusKind Kind { get; }

    /// <summary>
    /// The ids of the subjects in focus. Empty unless <see cref="Kind"/> is <see cref="FocusKind.Subjects"/>.
    /// Read-only: the focus is a value and is replaced, never edited.
    /// </summary>
    public IReadOnlySet<string> SubjectIds { get; }

    /// <summary>Private so every instance goes through the factory methods and honours the invariant.</summary>
    /// <param name="kind">The kind of focus.</param>
    /// <param name="subjectIds">The subject ids; already de-duplicated, and empty unless <paramref name="kind"/> is Subjects.</param>
    private SubjectFocus(FocusKind kind, IReadOnlySet<string> subjectIds)
    {
        Kind = kind;
        SubjectIds = subjectIds;
    }

    /// <summary>
    /// Builds a focus on the given subjects. A null or empty sequence (and one holding only blank ids) normalises to
    /// <see cref="None"/>, because "a set of no subjects" and "nothing in focus" mean the same thing.
    /// </summary>
    /// <param name="subjectIds">The subject ids to focus on; duplicates are ignored.</param>
    /// <returns>A <see cref="FocusKind.Subjects"/> focus, or <see cref="None"/> when no usable id was given.</returns>
    public static SubjectFocus ForSubjects(IEnumerable<string>? subjectIds)
    {
        var set = new HashSet<string>(
            (subjectIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        return set.Count == 0 ? None : new SubjectFocus(FocusKind.Subjects, set);
    }

    /// <summary>
    /// Rebuilds a focus from its persisted shape. A <see cref="FocusKind.Subjects"/> record with no ids normalises to
    /// <see cref="None"/>; an unrecognised kind (written by a newer version) falls back to <see cref="All"/>, the
    /// safe choice that hides nothing.
    /// </summary>
    /// <param name="saved">The persisted focus.</param>
    /// <returns>The equivalent focus.</returns>
    public static SubjectFocus FromSaved(SavedFocus saved) => saved.Kind switch
    {
        FocusKind.None => None,
        FocusKind.Subjects => ForSubjects(saved.SubjectIds),
        _ => All,
    };

    /// <summary>Converts this focus to the shape stored in <c>settings.json</c>.</summary>
    /// <returns>A new <see cref="SavedFocus"/>; its ids are sorted so the saved file is stable.</returns>
    public SavedFocus ToSaved() => new()
    {
        Kind = Kind,
        SubjectIds = SubjectIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
    };

    /// <summary>True when <paramref name="subjectId"/> is one of the focused subjects (only ever true for a Subjects focus).</summary>
    /// <param name="subjectId">The subject id to test.</param>
    public bool ContainsSubject(string subjectId) => SubjectIds.Contains(subjectId);

    /// <inheritdoc/>
    public bool Equals(SubjectFocus? other) =>
        other is not null && Kind == other.Kind && SubjectIds.SetEquals(other.SubjectIds);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as SubjectFocus);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Order-independent, to agree with the set equality above.
        var hash = (int)Kind;
        foreach (var id in SubjectIds) hash ^= StringComparer.Ordinal.GetHashCode(id);
        return hash;
    }

    /// <summary>A short description for logs and test failure messages.</summary>
    public override string ToString() => Kind == FocusKind.Subjects
        ? $"Subjects({string.Join(",", SubjectIds.OrderBy(id => id, StringComparer.Ordinal))})"
        : Kind.ToString();
}
