namespace TermPoint.Services.SectionImport;

/// <summary>
/// The identity of a section within one import file: Subject + Course Number + Section Code, as the
/// user typed them, compared after normalisation. Subject and section code ignore case and spacing
/// (<see cref="NameMatch.Comparer"/>); the course number ignores all spaces
/// (<see cref="NameMatch.CodeComparer"/>), so <c>CHEM</c>/<c>101</c>/<c>A</c> equals
/// <c>chem</c>/<c>10 1</c>/<c>a</c>.
/// </summary>
/// <param name="Subject">The Subject cell, trimmed.</param>
/// <param name="CourseNumber">The Course Number cell, trimmed.</param>
/// <param name="SectionCode">The Section Code cell, trimmed.</param>
public sealed record SectionKey(string Subject, string CourseNumber, string SectionCode)
{
    /// <summary>
    /// The section as a message names it, from the key as typed: subject and course number run
    /// together, then the section code (<c>CHEM101 A</c>).
    /// </summary>
    public string Label => $"{Subject}{CourseNumber} {SectionCode}";

    /// <summary>True when the other key names the same section after normalisation.</summary>
    /// <param name="other">The key to compare with. May be null.</param>
    /// <returns>True when all three parts match.</returns>
    public bool Equals(SectionKey? other) =>
        other is not null
        && NameMatch.Comparer.Equals(Subject, other.Subject)
        && NameMatch.CodeComparer.Equals(CourseNumber, other.CourseNumber)
        && NameMatch.Comparer.Equals(SectionCode, other.SectionCode);

    /// <summary>A hash that agrees with <see cref="Equals(SectionKey?)"/>.</summary>
    /// <returns>The hash code of the normalised parts.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(
            NameMatch.Comparer.GetHashCode(Subject),
            NameMatch.CodeComparer.GetHashCode(CourseNumber),
            NameMatch.Comparer.GetHashCode(SectionCode));
}
