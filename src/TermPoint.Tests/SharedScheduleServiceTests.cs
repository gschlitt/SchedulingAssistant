using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class SharedScheduleServiceTests
{
    private readonly SharedScheduleService _service = new();

    private static SharedScheduleSet MakeSet(string label, int sectionCount = 1)
    {
        var set = new SharedScheduleSet { SourceLabel = label };
        for (int i = 0; i < sectionCount; i++)
        {
            set.Sections.Add(new Section
            {
                IsShared = true,
                CourseId = null,
                DisplayCourseCode = $"COURSE{i}",
                SectionCode = "A",
                SourceLabel = label,
                Schedule = new()
                {
                    new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 50 }
                }
            });
        }
        return set;
    }

    [Fact]
    public void Add_FiresChanged()
    {
        int fireCount = 0;
        _service.Changed += () => fireCount++;

        _service.Add(MakeSet("Chemistry"));

        Assert.Equal(1, fireCount);
        Assert.True(_service.HasAny);
        Assert.Single(_service.Sets);
    }

    [Fact]
    public void Dismiss_RemovesCorrectSet_FiresChanged()
    {
        var chem = MakeSet("Chemistry");
        var bio = MakeSet("Biology");
        _service.Add(chem);
        _service.Add(bio);

        int fireCount = 0;
        _service.Changed += () => fireCount++;

        _service.Dismiss(chem);

        Assert.Equal(1, fireCount);
        Assert.Single(_service.Sets);
        Assert.Equal("Biology", _service.Sets[0].SourceLabel);
    }

    [Fact]
    public void DismissAll_ClearsAll_FiresChanged()
    {
        _service.Add(MakeSet("Chemistry"));
        _service.Add(MakeSet("Biology"));

        int fireCount = 0;
        _service.Changed += () => fireCount++;

        _service.DismissAll();

        Assert.Equal(1, fireCount);
        Assert.False(_service.HasAny);
        Assert.Empty(_service.Sets);
    }

    [Fact]
    public void DismissAll_WhenEmpty_DoesNotFire()
    {
        int fireCount = 0;
        _service.Changed += () => fireCount++;

        _service.DismissAll();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void GetSectionsForSemester_ReturnsSectionsMatchingSemester()
    {
        var set = new SharedScheduleSet
        {
            SourceLabel = "Chemistry",
            Sections = new()
            {
                new Section
                {
                    Id = "shared1",
                    IsShared = true,
                    SemesterId = "sem1",
                    CourseId = null,
                    DisplayCourseCode = "CHEM101",
                    SectionCode = "A",
                    SourceLabel = "Chemistry",
                    Schedule = new()
                    {
                        new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 50 },
                        new SectionDaySchedule { Day = 3, StartMinutes = 480, DurationMinutes = 50, Frequency = "odd" }
                    }
                },
                new Section
                {
                    Id = "shared2",
                    IsShared = true,
                    SemesterId = "sem2",
                    CourseId = null,
                    DisplayCourseCode = "CHEM102",
                    SectionCode = "B",
                    SourceLabel = "Chemistry"
                }
            }
        };
        _service.Add(set);

        var result = _service.GetSectionsForSemester("sem1");

        Assert.Single(result);
        Assert.Equal("shared1", result[0].Id);
        Assert.True(result[0].IsShared);
        Assert.Equal("CHEM101", result[0].DisplayCourseCode);
        Assert.Equal(2, result[0].Schedule.Count);
    }

    [Fact]
    public void HasAny_TracksState()
    {
        Assert.False(_service.HasAny);

        var set = MakeSet("Test");
        _service.Add(set);
        Assert.True(_service.HasAny);

        _service.Dismiss(set);
        Assert.False(_service.HasAny);
    }
}
