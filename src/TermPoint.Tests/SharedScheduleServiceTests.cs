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
            set.Sections.Add(new SharedSection
            {
                CourseCode = $"COURSE{i}",
                SectionCode = "A",
                Meetings = new()
                {
                    new SharedMeeting { Day = 1, StartMinutes = 480, DurationMinutes = 50 }
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
    public void BuildBlocks_ReturnsCorrectBlocks()
    {
        var set = new SharedScheduleSet
        {
            SourceLabel = "Chemistry",
            Sections = new()
            {
                new SharedSection
                {
                    CourseCode = "CHEM101", SectionCode = "A",
                    Notes = "Lab goggles",
                    Meetings = new()
                    {
                        new SharedMeeting { Day = 1, StartMinutes = 480, DurationMinutes = 50 },
                        new SharedMeeting { Day = 3, StartMinutes = 480, DurationMinutes = 50, Frequency = "odd" }
                    }
                }
            }
        };
        _service.Add(set);

        var blocks = _service.BuildBlocks("sem1", "Fall 2025", "#C65D1E");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("CHEM101 A", blocks[0].Label);
        Assert.Equal("Chemistry", blocks[0].SourceLabel);
        Assert.Equal("Lab goggles", blocks[0].Notes);
        Assert.Equal(1, blocks[0].Day);
        Assert.Equal(480, blocks[0].StartMinutes);
        Assert.Equal(530, blocks[0].EndMinutes);
        Assert.Equal("sem1", blocks[0].SemesterId);
        Assert.Equal("Fall 2025", blocks[0].SemesterName);
        Assert.Equal("#C65D1E", blocks[0].SemesterColor);
        Assert.Equal("(odd)", blocks[1].FrequencyAnnotation);
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
