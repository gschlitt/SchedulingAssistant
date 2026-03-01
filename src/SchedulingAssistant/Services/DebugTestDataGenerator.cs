using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Services;

/// <summary>
/// Debug-only utility for generating random test sections with realistic data.
/// </summary>
#if DEBUG
public class DebugTestDataGenerator
{
    private readonly Random _random = new();
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalStartTimeRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionPropertyRepository _propertyRepo;

    public DebugTestDataGenerator(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        LegalStartTimeRepository legalStartTimeRepo,
        BlockPatternRepository blockPatternRepo,
        SectionPropertyRepository propertyRepo)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _legalStartTimeRepo = legalStartTimeRepo;
        _blockPatternRepo = blockPatternRepo;
        _propertyRepo = propertyRepo;
    }

    public List<Section> GenerateSections(int count, string semesterId)
    {
        var sections = new List<Section>();
        var courses = _courseRepo.GetAllActive();

        if (courses.Count == 0)
        {
            App.Logger.LogWarning("No active courses available for test data generation", "GenerateSections");
            return sections;
        }

        var instructors = _instructorRepo.GetAll();
        var rooms = _roomRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll();
        var blockPatterns = _blockPatternRepo.GetAll();
        var sectionTypes = _propertyRepo.GetAll(SectionPropertyTypes.SectionType);
        var campuses = _propertyRepo.GetAll(SectionPropertyTypes.Campus);
        var tags = _propertyRepo.GetAll(SectionPropertyTypes.Tag);
        var resources = _propertyRepo.GetAll(SectionPropertyTypes.Resource);
        var reserves = _propertyRepo.GetAll(SectionPropertyTypes.Reserve);
        var meetingTypes = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType);

        for (int i = 0; i < count; i++)
        {
            var section = new Section { SemesterId = semesterId };

            // Pick a random course
            var course = courses[_random.Next(courses.Count)];
            section.CourseId = course.Id;

            // Generate unique section code
            section.SectionCode = GenerateUniqueCode(course.Id, semesterId);

            // Generate schedule from block patterns or random times
            if (blockPatterns.Count > 0)
            {
                var pattern = blockPatterns[_random.Next(blockPatterns.Count)];
                var legalStartTime = legalStartTimes[_random.Next(legalStartTimes.Count)];
                var startMinutes = legalStartTime.StartTimes[_random.Next(legalStartTime.StartTimes.Count)];
                section.Schedule = GenerateScheduleFromPattern(pattern, startMinutes, legalStartTime.BlockLength, meetingTypes, rooms);
            }
            else
            {
                // Random meetings without pattern
                section.Schedule = GenerateRandomSchedule(legalStartTimes, meetingTypes, rooms);
            }

            // Randomize properties
            RandomizeProperties(section, instructors, sectionTypes, campuses, tags, resources, reserves);

            sections.Add(section);
        }

        return sections;
    }

    private string GenerateUniqueCode(string courseId, string semesterId)
    {
        // Try codes like A1, A2, A3... B1, B2, B3... etc
        char prefix = 'A';
        int suffix = 1;

        while (prefix <= 'Z')
        {
            var code = $"{prefix}{suffix}";
            if (!_sectionRepo.ExistsBySectionCode(semesterId, courseId, code, excludeId: null))
                return code;

            suffix++;
            if (suffix > 999)
            {
                prefix++;
                suffix = 1;
            }
        }

        // Fallback: use GUID suffix if we've exhausted A-Z
        return $"X{Guid.NewGuid().ToString()[..8]}";
    }

    private List<SectionDaySchedule> GenerateScheduleFromPattern(
        BlockPattern pattern,
        int startMinutes,
        double blockLengthHours,
        List<SectionPropertyValue> meetingTypes,
        List<Room> rooms)
    {
        var schedule = new List<SectionDaySchedule>();
        var durationMinutes = (int)(blockLengthHours * 60);

        foreach (var day in pattern.Days)
        {
            var meeting = new SectionDaySchedule
            {
                Day = day,
                StartMinutes = startMinutes,
                DurationMinutes = durationMinutes,
                MeetingTypeId = meetingTypes.Count > 0 ? (PickRandomly(meetingTypes)?.Id) : null,
                RoomId = rooms.Count > 0 ? (PickRandomly(rooms)?.Id) : null
            };
            schedule.Add(meeting);
        }

        return schedule;
    }

    private List<SectionDaySchedule> GenerateRandomSchedule(
        List<LegalStartTime> legalStartTimes,
        List<SectionPropertyValue> meetingTypes,
        List<Room> rooms)
    {
        var schedule = new List<SectionDaySchedule>();

        // 0-3 random meetings
        int meetingCount = _random.Next(4); // 0, 1, 2, or 3

        for (int i = 0; i < meetingCount; i++)
        {
            var day = _random.Next(1, 6); // Monday-Friday
            var legalStartTime = legalStartTimes[_random.Next(legalStartTimes.Count)];
            var startMinutes = legalStartTime.StartTimes[_random.Next(legalStartTime.StartTimes.Count)];

            var meeting = new SectionDaySchedule
            {
                Day = day,
                StartMinutes = startMinutes,
                DurationMinutes = (int)(legalStartTime.BlockLength * 60),
                MeetingTypeId = meetingTypes.Count > 0 ? (PickRandomly(meetingTypes)?.Id) : null,
                RoomId = rooms.Count > 0 ? (PickRandomly(rooms)?.Id) : null
            };
            schedule.Add(meeting);
        }

        return schedule;
    }

    private void RandomizeProperties(
        Section section,
        List<Instructor> instructors,
        List<SectionPropertyValue> sectionTypes,
        List<SectionPropertyValue> campuses,
        List<SectionPropertyValue> tags,
        List<SectionPropertyValue> resources,
        List<SectionPropertyValue> reserves)
    {
        // Instructors: 70% have 1-2, 30% have none
        if (_random.NextDouble() < 0.7 && instructors.Count > 0)
        {
            int instructorCount = _random.Next(1, Math.Min(3, instructors.Count + 1));
            var selectedInstructors = new HashSet<string>();

            for (int i = 0; i < instructorCount; i++)
            {
                var instructor = instructors[_random.Next(instructors.Count)];
                selectedInstructors.Add(instructor.Id);
            }

            foreach (var instructorId in selectedInstructors)
            {
                var workload = _random.NextDouble() switch
                {
                    < 0.25 => 0.25m,
                    < 0.5 => 0.5m,
                    < 0.75 => 0.75m,
                    _ => 1.0m
                };
                section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = instructorId, Workload = workload });
            }
        }

        // Section type: 50% have one, 50% don't
        if (_random.NextDouble() < 0.5 && sectionTypes.Count > 0)
            section.SectionTypeId = sectionTypes[_random.Next(sectionTypes.Count)].Id;

        // Campus: 50% have one, 50% don't
        if (_random.NextDouble() < 0.5 && campuses.Count > 0)
            section.CampusId = campuses[_random.Next(campuses.Count)].Id;

        // Tags: 30% have 1-3, 70% have none
        if (_random.NextDouble() < 0.3 && tags.Count > 0)
        {
            int tagCount = _random.Next(1, Math.Min(4, tags.Count + 1));
            var selectedTags = new HashSet<string>();

            for (int i = 0; i < tagCount; i++)
            {
                var tag = tags[_random.Next(tags.Count)];
                selectedTags.Add(tag.Id);
            }

            section.TagIds = selectedTags.ToList();
        }

        // Resources: 30% have 1-2, 70% have none
        if (_random.NextDouble() < 0.3 && resources.Count > 0)
        {
            int resourceCount = _random.Next(1, Math.Min(3, resources.Count + 1));
            var selectedResources = new HashSet<string>();

            for (int i = 0; i < resourceCount; i++)
            {
                var resource = resources[_random.Next(resources.Count)];
                selectedResources.Add(resource.Id);
            }

            section.ResourceIds = selectedResources.ToList();
        }

        // Reserves: 30% have one code with count 1-5, 70% have none
        if (_random.NextDouble() < 0.3 && reserves.Count > 0)
        {
            var reserve = reserves[_random.Next(reserves.Count)];
            var code = _random.Next(1, 6); // 1-5
            section.Reserves.Add(new SectionReserve { ReserveId = reserve.Id, Code = code });
        }
    }

    /// <summary>
    /// Pick a random element from a list, or null if nullable is true and random chance hits.
    /// </summary>
    private T? PickRandomly<T>(List<T> items, bool nullable = true) where T : class
    {
        if (items.Count == 0)
            return null;

        if (nullable && _random.NextDouble() < 0.2) // 20% chance of null
            return null;

        return items[_random.Next(items.Count)];
    }
}
#endif
