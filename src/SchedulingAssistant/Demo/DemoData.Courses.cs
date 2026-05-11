using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Course> Courses =
    [
        new()
        {
            Id           = "demo-course-1",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG105",
            Title        = "Introduction to Physical Geography",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-2",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG106",
            Title        = "Introduction to Human Geography",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-3",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG111",
            Title        = "World Regional Geography",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-4",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG112",
            Title        = "Earth's Natural Environments",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-5",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG201",
            Title        = "Geomorphology",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-6",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG202",
            Title        = "Climatology",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-7",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG210",
            Title        = "Biogeography",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-8",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG219",
            Title        = "Urban Geography",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-9",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG220",
            Title        = "Economic Geography",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-10",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG301",
            Title        = "Advanced Geographic Information Systems",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-11",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG305",
            Title        = "Glacial and Periglacial Geomorphology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-12",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG306",
            Title        = "Synoptic Meteorology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-13",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG307",
            Title        = "Fluvial Geomorphology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-14",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG308",
            Title        = "Geography of Urbanization",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-15",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG309",
            Title        = "Spatial Statistics",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-16",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG310",
            Title        = "Environmental Remote Sensing",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-17",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG312",
            Title        = "Historical Geography",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-18",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG319",
            Title        = "Landscape Ecology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-19",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG320",
            Title        = "Geography of Development",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-20",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG330",
            Title        = "Water Resource Management",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-21",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG333",
            Title        = "Advanced Cartography and Visualization",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-22",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG335",
            Title        = "Geography of Energy and Resources",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-23",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG340",
            Title        = "Environmental Impact Assessment",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-24",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG350",
            Title        = "Quaternary Environments",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-25",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG357",
            Title        = "Digital Terrain Analysis",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-26",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG360",
            Title        = "Political Ecology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-27",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG370",
            Title        = "Geography of Global Trade",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-28",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG380",
            Title        = "Applied Climatology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-29",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG383",
            Title        = "Geography of Tourism and Recreation",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-30",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG385",
            Title        = "Special Topics (300-level) [29]",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-31",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG390",
            Title        = "Special Topics (300-level) [30]",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-32",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG401",
            Title        = "Research Methods in Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-33",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG403",
            Title        = "Senior Seminar in Physical Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-34",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG406",
            Title        = "Senior Seminar in Human Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-35",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG407",
            Title        = "Advanced Spatial Analysis",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-36",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG408",
            Title        = "Climate Change Science and Policy",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-37",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG409",
            Title        = "Geographic Thought and Philosophy",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-38",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG410",
            Title        = "Advanced Environmental Modeling",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-39",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG412",
            Title        = "Directed Studies in Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-40",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG414",
            Title        = "Honours Thesis in Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-41",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG415",
            Title        = "Advanced Topics in Geomorphology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-42",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG416",
            Title        = "Advanced Topics in Urban Systems",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-43",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG418",
            Title        = "Land Use Planning and Policy",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-44",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG419",
            Title        = "Advanced Remote Sensing Applications",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-45",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG420",
            Title        = "Environmental Governance",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-46",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG420",
            Title        = "Capstone Project in Geography",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-47",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG420",
            Title        = "Special Topics (400-level) [46]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-48",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG425",
            Title        = "Special Topics (400-level) [47]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-49",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG426",
            Title        = "Special Topics (400-level) [48]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-50",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG427",
            Title        = "Special Topics (400-level) [49]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-51",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG430",
            Title        = "Special Topics (400-level) [50]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-52",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG433",
            Title        = "Special Topics (400-level) [51]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-53",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG442",
            Title        = "Special Topics (400-level) [52]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-54",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG448",
            Title        = "Special Topics (400-level) [53]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-55",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG477",
            Title        = "Special Topics (400-level) [54]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-56",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG496",
            Title        = "Special Topics (400-level) [55]",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-57",
            SubjectId    = "demo-subj-1",
            CalendarCode = "GEOG499",
            Title        = "Special Topics (400-level) [56]",
            IsActive     = true,
            Level        = "400"
        }
    ];
}
