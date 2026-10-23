using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// Builds a small in-memory academic world (years, semesters, courses, sections) and turns it into a
/// <see cref="TeachingHistory"/>, for the pure focus tests (spec item 20, step 20.1). No database. Years are
/// named like real ones ("2025-2026") so <see cref="AcademicYear.StartYear"/> parses; each year gets one "Fall"
/// semester on first use, and further semesters of a year can be named explicitly.
/// </summary>
internal sealed class FocusWorld
{
    /// <summary>Academic years by name.</summary>
    public Dictionary<string, AcademicYear> Years { get; } = new();

    /// <summary>Semesters by "year name/semester name".</summary>
    public Dictionary<string, Semester> Semesters { get; } = new();

    /// <summary>Courses by subject id (one course per subject, id "course-" + subject id).</summary>
    public Dictionary<string, Course> Courses { get; } = new();

    /// <summary>Every section added by <see cref="Teach"/>.</summary>
    public List<Section> Sections { get; } = new();

    /// <summary>Returns the academic year with this name, creating it when new.</summary>
    /// <param name="name">The year name, for example "2025-2026".</param>
    public AcademicYear Year(string name)
    {
        if (!Years.TryGetValue(name, out var year))
            Years[name] = year = new AcademicYear { Id = "ay-" + name, Name = name };
        return year;
    }

    /// <summary>Returns the semester of the given year, creating it when new.</summary>
    /// <param name="yearName">The year name.</param>
    /// <param name="semesterName">The semester name.</param>
    public Semester Semester(string yearName, string semesterName = "Fall")
    {
        var key = yearName + "/" + semesterName;
        if (!Semesters.TryGetValue(key, out var semester))
            Semesters[key] = semester = new Semester
            {
                Id = "sem-" + key, AcademicYearId = Year(yearName).Id, Name = semesterName,
            };
        return semester;
    }

    /// <summary>Returns the course of a subject, creating it when new.</summary>
    /// <param name="subjectId">The subject id.</param>
    public Course CourseOf(string subjectId)
    {
        if (!Courses.TryGetValue(subjectId, out var course))
            Courses[subjectId] = course = new Course
            {
                Id = "course-" + subjectId, SubjectId = subjectId, CalendarCode = subjectId + "101",
            };
        return course;
    }

    /// <summary>
    /// Adds one section of the subject's course, in the named semester, taught by the given instructors.
    /// </summary>
    /// <param name="subjectId">The subject of the section's course.</param>
    /// <param name="yearName">The academic year.</param>
    /// <param name="instructorIds">The instructors assigned to the section.</param>
    /// <param name="semesterName">The semester within the year; "Fall" by default.</param>
    /// <returns>The new section.</returns>
    public Section Teach(string subjectId, string yearName, string[] instructorIds, string semesterName = "Fall")
    {
        var section = new Section
        {
            Id = "sec-" + (Sections.Count + 1),
            CourseId = CourseOf(subjectId).Id,
            SemesterId = Semester(yearName, semesterName).Id,
            SectionCode = "A" + (Sections.Count + 1),
            InstructorAssignments = instructorIds.Select(i => new InstructorAssignment { InstructorId = i }).ToList(),
        };
        Sections.Add(section);
        return section;
    }

    /// <summary>Convenience: one instructor teaches one section.</summary>
    /// <param name="instructorId">The instructor.</param>
    /// <param name="subjectId">The subject.</param>
    /// <param name="yearName">The academic year.</param>
    /// <param name="semesterName">The semester within the year.</param>
    public Section Teach(string instructorId, string subjectId, string yearName, string semesterName = "Fall") =>
        Teach(subjectId, yearName, [instructorId], semesterName);

    /// <summary>Builds the teaching history from everything added so far.</summary>
    public TeachingHistory Build() =>
        TeachingHistory.Build(Sections, Courses.Values, Semesters.Values, Years.Values);

    /// <summary>The id of the named academic year (creating it when new), for the "viewed year" argument.</summary>
    /// <param name="name">The year name.</param>
    public string YearId(string name) => Year(name).Id;

    /// <summary>A new active instructor with this id.</summary>
    /// <param name="id">The instructor id.</param>
    /// <param name="isActive">Whether the instructor is active.</param>
    public static Instructor Instructor(string id, bool isActive = true) =>
        new() { Id = id, LastName = id, IsActive = isActive };
}
