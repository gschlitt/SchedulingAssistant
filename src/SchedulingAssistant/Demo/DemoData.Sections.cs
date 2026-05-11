using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Section> Sections =
    [
        new()
        {
            Id            = "demo-sec-1",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-29",
            SectionCode   = "ON1",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-8", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-2",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-17",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-3",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-5",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-4",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-5",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-5",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-31",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-23", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-6",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-31",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-23", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-7",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-12",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-6", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-8",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-12",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 690, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-6", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-9",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-15",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-10",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-15",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-11",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-42",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-12",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-48",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-13",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-48",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-14",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-9",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-15",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-9",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-16",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-19",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-17",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-39",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-31", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-18",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-40",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-19",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-41",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-20",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-21",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-22",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-23",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "W#D",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-24",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-25",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-26",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-6",
            SectionCode   = "WS3",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 960, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-27",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "L#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-1", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-28",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "L#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-1", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-29",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "LK1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" },
                new() { Day = 2, StartMinutes = 1050, DurationMinutes = 50, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-30",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-38", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-31",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-32",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-33",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#D",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-34",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#F",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-35",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#G",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-36",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#H",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-37",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#I",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-38",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "W#J",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-39",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-38", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-40",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-38", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-41",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-4",
            SectionCode   = "WS3",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-6", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-42",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-1",
            SectionCode   = "ON1",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-8", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-43",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-1",
            SectionCode   = "ON2",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-20", Workload = 3m } ]
        },
        new()
        {
            Id            = "demo-sec-44",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-1",
            SectionCode   = "ON3",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-45",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-1",
            SectionCode   = "ON4",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-25", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-46",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-1",
            SectionCode   = "ON5",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-16", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-47",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-43",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-48",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-43",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-49",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-50",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-51",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-52",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "W#D",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-53",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "W#E",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-54",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-34", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-55",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-3",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" },
                new() { Day = 2, StartMinutes = 1050, DurationMinutes = 50, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-56",
            SemesterId    = "demo-sem-2",
            CourseId      = "demo-course-54",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-57",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-9",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-58",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-9",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-59",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-6",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" },
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-60",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-6",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-61",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-1",
            SectionCode   = "ON1",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-25", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-62",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-1",
            SectionCode   = "ON2",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-4", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-63",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-1",
            SectionCode   = "ON3",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-64",
            SemesterId    = "demo-sem-3",
            CourseId      = "demo-course-1",
            SectionCode   = "ON4",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-65",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "LK1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 690, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-31", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-66",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "LK1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-1", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-67",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-68",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-69",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 1140, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-13", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-70",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-71",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 1050, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-31", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-72",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-5",
            SectionCode   = "WS3",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-73",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-16",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-6", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-74",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-15",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" },
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-75",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-15",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-2" },
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-76",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-11",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-6", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-77",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-9",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-78",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-9",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-79",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-9",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-80",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-9",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-32", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-81",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-19",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-33", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-82",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-21",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-83",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-24",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-2",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-21", Workload = 1.5m } ]
        },
        new()
        {
            Id            = "demo-sec-84",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-38",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-20", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-85",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-38",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-38", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-86",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-32",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-3", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-87",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-88",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-23", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-89",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-90",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "W#D",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90 },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90 }
            ],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-91",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-92",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-7",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-38", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-93",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-50",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-94",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-26",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-3", MeetingTypeId = "demo-mt-3" },
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-19", Workload = 1.57m } ]
        },
        new()
        {
            Id            = "demo-sec-95",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON1",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-20", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-96",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON2",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-20", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-97",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON3",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-25", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-98",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON4",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-4", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-99",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON5",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-14", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-100",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-1",
            SectionCode   = "ON6",
            SectionTypeId = "demo-st-3",
            Schedule = [],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 2m } ]
        },
        new()
        {
            Id            = "demo-sec-101",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-35",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-5", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-102",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-30",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-16", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-103",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "L#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-104",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "L#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-105",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "LK1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-1",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "even" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-18", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-106",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-107",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-108",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#C",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-109",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#D",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-19", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-110",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#E",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 1090, DurationMinutes = 190, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-111",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#F",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-112",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#G",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-113",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#H",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-114",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#I",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-34", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-115",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#J",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 1090, DurationMinutes = 190, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-116",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#K",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-117",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#L",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-19", Workload = 0.5m } ]
        },
        new()
        {
            Id            = "demo-sec-118",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#M",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 820, DurationMinutes = 230, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-30", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-119",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "W#N",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-24", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-120",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "odd" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-121",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS2",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "even" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-34", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-122",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS3",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "even" }
            ],
            InstructorAssignments = []
        },
        new()
        {
            Id            = "demo-sec-123",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS4",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 510, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "odd" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-124",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS5",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "odd" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-7", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-125",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS6",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "odd" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-126",
            SemesterId    = "demo-sem-1",
            CourseId      = "demo-course-3",
            SectionCode   = "WS7",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 1050, DurationMinutes = 230 }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-26", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-127",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-45",
            SectionCode   = "ON1",
            SectionTypeId = "demo-st-4",
            Schedule =
            [
                new() { Day = 5, StartMinutes = 690, DurationMinutes = 180, MeetingTypeId = "demo-mt-2" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-28", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-128",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-4",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-129",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-4",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-2", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-130",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-4",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 690, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "even" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-17", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-131",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-47",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 4, StartMinutes = 870, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 1m } ]
        },
        new()
        {
            Id            = "demo-sec-132",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-3",
            SectionCode   = "W#A",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-133",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-3",
            SectionCode   = "W#B",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, RoomId = "demo-room-4", MeetingTypeId = "demo-mt-3" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 0.75m } ]
        },
        new()
        {
            Id            = "demo-sec-134",
            SemesterId    = "demo-sem-4",
            CourseId      = "demo-course-3",
            SectionCode   = "WS1",
            SectionTypeId = "demo-st-1",
            CampusId      = "demo-campus-2",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 3, StartMinutes = 780, DurationMinutes = 90, MeetingTypeId = "demo-mt-1" },
                new() { Day = 5, StartMinutes = 600, DurationMinutes = 90, MeetingTypeId = "demo-mt-1", Frequency = "even" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-39", Workload = 1m } ]
        }
    ];
}
