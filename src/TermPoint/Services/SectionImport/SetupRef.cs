using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// A reference from an import draft to a record: either one that already exists in the database or a
/// <b>new</b> one the import will create. A new record is fully built when it is planned (fresh
/// <c>Id</c>, name as first typed, next <c>SortOrder</c>), so every draft that names it shares the same
/// <see cref="SetupRef{T}"/> instance and the same <c>Id</c>. Used for subjects, section types, meeting
/// types, tags, campuses and rooms; <see cref="CourseRef"/> and <see cref="InstructorRef"/> extend it.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public class SetupRef<T> where T : class
{
    /// <summary>The existing record, or the new record that will be inserted.</summary>
    public T Record { get; }

    /// <summary>True when the record does not exist yet and the import will create it.</summary>
    public bool IsNew { get; }

    /// <summary>
    /// For a new record, the row of the file that named it first; null for an existing record.
    /// </summary>
    public int? FirstRow { get; }

    /// <summary>Creates a reference.</summary>
    /// <param name="record">The existing or the newly built record.</param>
    /// <param name="isNew">True when the record is new.</param>
    /// <param name="firstRow">For a new record, the row that named it first.</param>
    public SetupRef(T record, bool isNew, int? firstRow)
    {
        Record = record;
        IsNew = isNew;
        FirstRow = firstRow;
    }

    /// <summary>Creates a reference to a record that already exists.</summary>
    /// <param name="record">The existing record.</param>
    /// <returns>A reference whose <see cref="IsNew"/> is false.</returns>
    public static SetupRef<T> Existing(T record) => new(record, isNew: false, firstRow: null);

    /// <summary>Creates a reference to a record the import will create.</summary>
    /// <param name="record">The fully built new record.</param>
    /// <param name="firstRow">The row of the file that named it first.</param>
    /// <returns>A reference whose <see cref="IsNew"/> is true.</returns>
    public static SetupRef<T> New(T record, int firstRow) => new(record, isNew: true, firstRow: firstRow);
}

/// <summary>
/// A reference to a course: an existing <see cref="Course"/>, or a new one the import will create
/// (its code, title and subject are already set on <see cref="SetupRef{T}.Record"/>).
/// </summary>
public sealed class CourseRef : SetupRef<Course>
{
    /// <summary>
    /// For a new course, the reference to its subject (existing or new); null for an existing course,
    /// whose subject is whatever the database says.
    /// </summary>
    public SetupRef<Subject>? Subject { get; }

    /// <summary>Creates a course reference.</summary>
    /// <param name="course">The existing or newly built course.</param>
    /// <param name="isNew">True when the course is new.</param>
    /// <param name="firstRow">For a new course, the row that named it first.</param>
    /// <param name="subject">For a new course, its subject reference.</param>
    public CourseRef(Course course, bool isNew, int? firstRow, SetupRef<Subject>? subject)
        : base(course, isNew, firstRow)
    {
        Subject = subject;
    }
}

/// <summary>
/// A reference to an instructor: an existing <see cref="Instructor"/>, or a new one the import will
/// create (already built, with generated initials, on <see cref="SetupRef{T}.Record"/>).
/// </summary>
public sealed class InstructorRef : SetupRef<Instructor>
{
    /// <summary>Creates an instructor reference.</summary>
    /// <param name="instructor">The existing or newly built instructor.</param>
    /// <param name="isNew">True when the instructor is new.</param>
    /// <param name="firstRow">For a new instructor, the row that named them first.</param>
    public InstructorRef(Instructor instructor, bool isNew, int? firstRow)
        : base(instructor, isNew, firstRow)
    {
    }
}
