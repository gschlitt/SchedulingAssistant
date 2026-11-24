using System.Text.RegularExpressions;

namespace TermPoint.Services.Access;

/// <summary>
/// The component-name list (Access Analyzer Q18): the section type names that look like a component a student
/// takes one section of. Only a suggestion: it pre-ticks the setup question and stands in for the stored answer
/// until the setup is done.
/// </summary>
public static class ComponentNames
{
    /// <summary>Whole-word, case-insensitive match against Q18's names and their short forms.</summary>
    private static readonly Regex NameList = new(
        @"\b(lecture|lec|laboratory|lab|tutorial|tut|discussion|dis|recitation|rec|seminar|sem|studio|stu|workshop|wks|practicum|pra)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a section type's name looks like a component: it contains one of Lecture/LEC, Laboratory/Lab,
    /// Tutorial/TUT, Discussion/DIS, Recitation/REC, Seminar/SEM, Studio/STU, Workshop/WKS, Practicum/PRA as a
    /// whole word, ignoring case. "Lab" and "Laboratory" match; "Online", "Thesis" and "Independent Study" do not.
    /// </summary>
    /// <param name="name">The section type's name.</param>
    /// <returns><c>true</c> when the name looks like a component; <c>false</c> for null or blank names.</returns>
    public static bool LooksLikeComponent(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NameList.IsMatch(name);
}
