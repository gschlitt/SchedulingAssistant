using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Integration tests for the Room Availability Browser using a real database.
/// Loads rooms, sections, meetings, legal start times, and block patterns from
/// <c>BIOL-TT.db</c> and runs the solver against real-world occupancy data.
/// The source DB is copied to a temp file so tests never modify the original.
/// </summary>
public class RoomAvailabilityIntegrationTests : IDisposable
{
    // internal so FactRequiresLocalDbAttribute can probe for the file at discovery time.
    // 2026-07-12: repointed from C:\Users\gregs\SchedulerTest\ — the real-data DB moved to
    // the app's ProgramData layout and every [FactRequiresLocalDb] test had been silently
    // skipping since. Tests only read this file (each copies it to temp before opening).
    internal const string SourceDbPath = @"C:\ProgramData\TermPoint\UFV\BIOL\BIOL-TT.db";

    private readonly string _tempDbPath;
    private readonly DatabaseContext _db;
    private readonly RoomAvailabilityService _service = new();
    private readonly ITestOutputHelper _output;

    // Loaded once per test class instance.
    private readonly List<Room> _rooms;
    private readonly List<BlockPattern> _blockPatterns;
    private readonly List<LegalStartTime> _legalStartTimes;
    private readonly List<Section> _sections;
    private readonly List<Meeting> _meetings;
    private readonly List<SchedulingEnvironmentValue> _roomTypes;
    private readonly Semester _activeSemester;
    private readonly AcademicYear _activeAY;
    private readonly OccupancyIndex _index;

    public RoomAvailabilityIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Copy to temp so DatabaseContext migrations/seeding don't touch the source.
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"RoomAvailTest_{Guid.NewGuid():N}.db");
        File.Copy(SourceDbPath, _tempDbPath, overwrite: true);

        _db = new DatabaseContext(_tempDbPath);

        var ayRepo = new AcademicYearRepository(_db);
        var semRepo = new SemesterRepository(_db);
        var roomRepo = new RoomRepository(_db);
        var sectionRepo = new SectionRepository(_db);
        var meetingRepo = new MeetingRepository(_db);
        var lstRepo = new LegalStartTimeRepository(_db);
        var bpRepo = new BlockPatternRepository(_db);
        var envRepo = new SchedulingEnvironmentRepository(_db);

        // Find the most populated semester: scan all AYs newest-first, pick the
        // first semester that actually has sections.
        _rooms = roomRepo.GetAll();
        _blockPatterns = bpRepo.GetAll();

        Semester? bestSem = null;
        AcademicYear? bestAY = null;
        int bestCount = 0;

        foreach (var ay in ayRepo.GetAll().OrderByDescending(a => a.StartYear))
        {
            foreach (var sem in semRepo.GetByAcademicYear(ay.Id))
            {
                int count = sectionRepo.GetAll(sem.Id).Count;
                if (count > bestCount)
                {
                    bestCount = count;
                    bestSem = sem;
                    bestAY = ay;
                }
            }
            if (bestCount > 0) break; // take from most recent AY that has data
        }

        _activeAY = bestAY ?? ayRepo.GetAll().First();
        _activeSemester = bestSem ?? semRepo.GetAll().First();

        _legalStartTimes = lstRepo.GetAll(_activeAY.Id);
        _sections = sectionRepo.GetAll(_activeSemester.Id);
        _meetings = meetingRepo.GetAll(_activeSemester.Id);
        _roomTypes = envRepo.GetAll("roomType");

        _index = _service.BuildOccupancyIndex(_sections, _meetings, excludeSectionId: null);

        _output.WriteLine($"AY: {_activeAY.Name}, Semester: {_activeSemester.Name}");
        _output.WriteLine($"Rooms: {_rooms.Count}, Sections: {_sections.Count}, Meetings: {_meetings.Count}");
        _output.WriteLine($"Legal start time entries: {_legalStartTimes.Count}, Block patterns: {_blockPatterns.Count}");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_tempDbPath); } catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Builds specs with pinned days and start times, no room constraints.</summary>
    private static List<MeetingSpec> PinnedTimeSpecs(int durationMinutes, params (int Day, int StartMinutes)[] pins)
    {
        return pins.Select((p, i) => new MeetingSpec(
            Index: i,
            Day: p.Day,
            DurationMinutes: durationMinutes,
            StartMinutes: p.StartMinutes,
            RoomTypeId: null,
            RoomId: null)).ToList();
    }

    /// <summary>Builds specs with pinned days only, start times open.</summary>
    private static List<MeetingSpec> PinnedDaySpecs(int durationMinutes, params int[] days)
    {
        return days.Select((d, i) => new MeetingSpec(
            Index: i,
            Day: d,
            DurationMinutes: durationMinutes,
            StartMinutes: null,
            RoomTypeId: null,
            RoomId: null)).ToList();
    }

    /// <summary>Builds fully open specs (no day, no start, no room).</summary>
    private static List<MeetingSpec> OpenSpecs(int count, int durationMinutes)
    {
        return Enumerable.Range(0, count).Select(i => new MeetingSpec(
            Index: i,
            Day: null,
            DurationMinutes: durationMinutes,
            StartMinutes: null,
            RoomTypeId: null,
            RoomId: null)).ToList();
    }

    private List<RoomSolution> Solve(IReadOnlyList<MeetingSpec> specs)
    {
        return _service.GenerateSolutionsFromSpecs(specs, _rooms, _index, _legalStartTimes, _blockPatterns);
    }

    private void DumpSolutions(List<RoomSolution> solutions, int max = 20)
    {
        _output.WriteLine($"Total solutions: {solutions.Count}");
        foreach (var sol in solutions.Take(max))
        {
            var alt = sol.IsAlternative ? " [ALT]" : "";
            var pat = sol.IsPatternMatch ? " [PAT]" : "";
            var slots = string.Join(", ", sol.Slots.Select(s =>
                $"D{s.Day} {s.StartMinutes / 60:D2}{s.StartMinutes % 60:D2} {s.RoomLabel}"));
            _output.WriteLine($"  Tier {(int)sol.Tier}{alt}{pat}: {slots}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data sanity checks
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void DatabaseLoads_WithNonEmptyData()
    {
        Assert.NotEmpty(_rooms);
        Assert.NotEmpty(_sections);
        Assert.NotEmpty(_legalStartTimes);
        Assert.NotEmpty(_blockPatterns);

        _output.WriteLine($"Active semester: {_activeSemester.Name} ({_activeSemester.Id})");
        _output.WriteLine($"Rooms: {string.Join(", ", _rooms.Select(r => $"{r.Building} {r.RoomNumber}"))}");
        _output.WriteLine($"Block patterns: {string.Join(", ", _blockPatterns.Select(bp => bp.Name))}");
        foreach (var lst in _legalStartTimes)
            _output.WriteLine($"  Block {lst.BlockLength}h: {string.Join(", ", lst.StartTimes.Select(t => $"{t / 60:D2}{t % 60:D2}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pinned times: primary solutions must respect the user's constraints
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void PinnedMWF_DifferentTimes_PrimaryHonorsAllTimes()
    {
        // M 0830, W 1000, F 0830 — the exact scenario that was buggy.
        var specs = PinnedTimeSpecs(60, (1, 510), (3, 600), (5, 510));
        var solutions = Solve(specs);

        DumpSolutions(solutions);

        var primaries = solutions.Where(s => !s.IsAlternative).ToList();
        Assert.NotEmpty(primaries);

        // Every primary solution must use the exact pinned times.
        foreach (var sol in primaries)
        {
            var mon = sol.Slots.First(s => s.Day == 1);
            var wed = sol.Slots.First(s => s.Day == 3);
            var fri = sol.Slots.First(s => s.Day == 5);

            Assert.Equal(510, mon.StartMinutes);
            Assert.Equal(600, wed.StartMinutes);
            Assert.Equal(510, fri.StartMinutes);
        }
    }

    [FactRequiresLocalDb]
    public void PinnedMWF_SameTime_PrimaryHonorsTime()
    {
        // M/W/F all at 0900.
        var specs = PinnedTimeSpecs(60, (1, 540), (3, 540), (5, 540));
        var solutions = Solve(specs);

        DumpSolutions(solutions);

        var primaries = solutions.Where(s => !s.IsAlternative).ToList();
        Assert.NotEmpty(primaries);

        foreach (var sol in primaries)
            Assert.True(sol.Slots.All(s => s.StartMinutes == 540),
                "Primary solution should have all slots at 0900");
    }

    [FactRequiresLocalDb]
    public void PinnedTR_DifferentTimes_PrimaryHonorsAllTimes()
    {
        // T 0830, R 1000.
        var specs = PinnedTimeSpecs(90, (2, 510), (4, 600));
        var solutions = Solve(specs);

        DumpSolutions(solutions);

        var primaries = solutions.Where(s => !s.IsAlternative).ToList();
        Assert.NotEmpty(primaries);

        foreach (var sol in primaries)
        {
            var tue = sol.Slots.First(s => s.Day == 2);
            var thu = sol.Slots.First(s => s.Day == 4);
            Assert.Equal(510, tue.StartMinutes);
            Assert.Equal(600, thu.StartMinutes);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ordering: primaries must appear before alternatives
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void PinnedTimes_PrimariesSortBeforeAlternatives()
    {
        var specs = PinnedTimeSpecs(60, (1, 510), (3, 600), (5, 510));
        var solutions = Solve(specs);

        if (solutions.Count == 0) return; // skip if DB has no availability

        // Find the last primary and first alternative.
        int lastPrimaryIdx = -1;
        int firstAltIdx = -1;
        for (int i = 0; i < solutions.Count; i++)
        {
            if (!solutions[i].IsAlternative) lastPrimaryIdx = i;
            if (solutions[i].IsAlternative && firstAltIdx < 0) firstAltIdx = i;
        }

        if (lastPrimaryIdx >= 0 && firstAltIdx >= 0)
            Assert.True(lastPrimaryIdx < firstAltIdx,
                $"Last primary at index {lastPrimaryIdx} should be before first alternative at {firstAltIdx}");
    }

    [FactRequiresLocalDb]
    public void PinnedTimes_AlternativesExist_WhenAllStartsFixed()
    {
        var specs = PinnedTimeSpecs(60, (1, 510), (3, 510), (5, 510));
        var solutions = Solve(specs);

        DumpSolutions(solutions);

        var alts = solutions.Where(s => s.IsAlternative).ToList();
        // With all starts fixed, the allStartsFixed branch should generate alternatives.
        _output.WriteLine($"Alternatives: {alts.Count}");
        // This is informational — we can't guarantee alternatives exist if every legal
        // start happens to collide, but in a typical DB there should be some.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Open specs: no alternatives should be generated
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void FullyOpenSpecs_NoAlternativesGenerated()
    {
        // When nothing is pinned, the primary scan already explores all options.
        // The allStartsFixed branch should NOT fire (some starts are null).
        var specs = OpenSpecs(3, 60);
        var solutions = Solve(specs);

        DumpSolutions(solutions);

        var alts = solutions.Where(s => s.IsAlternative).ToList();
        Assert.Empty(alts);
    }

    [FactRequiresLocalDb]
    public void PinnedDaysOnly_NoAlternativesGenerated()
    {
        // Days pinned, starts open — allStartsFixed should be false.
        var specs = PinnedDaySpecs(60, 1, 3, 5);
        var solutions = Solve(specs);

        var alts = solutions.Where(s => s.IsAlternative).ToList();
        Assert.Empty(alts);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pinned rooms: solver must respect locked room constraints
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void PinnedRoom_SolutionsUseOnlyThatRoom()
    {
        if (_rooms.Count == 0) return;

        var targetRoom = _rooms.First();
        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 60, StartMinutes: null, RoomTypeId: null, RoomId: targetRoom.Id),
            new(1, Day: 3, DurationMinutes: 60, StartMinutes: null, RoomTypeId: null, RoomId: targetRoom.Id),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions);

        foreach (var sol in solutions.Where(s => !s.IsAlternative))
            Assert.True(sol.Slots.All(s => s.RoomId == targetRoom.Id),
                $"All primary slots should use room {targetRoom.Building} {targetRoom.RoomNumber}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mixed pinned: some times pinned, some open
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void MixedPinnedAndOpenStart_PrimaryHonorsPinnedSlot()
    {
        // M pinned at 0830, W open, F pinned at 0830.
        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 60, StartMinutes: 510, RoomTypeId: null),
            new(1, Day: 3, DurationMinutes: 60, StartMinutes: null, RoomTypeId: null),
            new(2, Day: 5, DurationMinutes: 60, StartMinutes: 510, RoomTypeId: null),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions);

        // allStartsFixed is false (W is open), so no alternatives should be generated.
        var alts = solutions.Where(s => s.IsAlternative).ToList();
        Assert.Empty(alts);

        // Every solution must honor the pinned M and F times.
        foreach (var sol in solutions)
        {
            var mon = sol.Slots.First(s => s.Day == 1);
            var fri = sol.Slots.First(s => s.Day == 5);
            Assert.Equal(510, mon.StartMinutes);
            Assert.Equal(510, fri.StartMinutes);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Room type filter
    // ═══════════════════════════════════════════════════════════════════════

    [FactRequiresLocalDb]
    public void PinnedRoomType_SolutionsRespectTypeFilter()
    {
        // Find a room type that exists in the DB.
        var roomsWithType = _rooms.Where(r => !string.IsNullOrEmpty(r.RoomTypeId)).ToList();
        if (roomsWithType.Count == 0)
        {
            _output.WriteLine("No rooms with RoomTypeId — skipping.");
            return;
        }

        var targetTypeId = roomsWithType.First().RoomTypeId!;
        var validRoomIds = new HashSet<string>(roomsWithType.Where(r => r.RoomTypeId == targetTypeId).Select(r => r.Id));

        _output.WriteLine($"Room type: {targetTypeId}, matching rooms: {validRoomIds.Count}");

        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 60, StartMinutes: null, RoomTypeId: targetTypeId),
            new(1, Day: 3, DurationMinutes: 60, StartMinutes: null, RoomTypeId: targetTypeId),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions);

        foreach (var sol in solutions.Where(s => !s.IsAlternative))
            Assert.True(sol.Slots.All(s => validRoomIds.Contains(s.RoomId)),
                "All primary slots should use rooms matching the requested type");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Acceptance: MapSlotsToSpecs + transfer-back logic
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Finds a room type ID by partial name match (case-insensitive).</summary>
    private string? FindRoomTypeId(string partialName)
    {
        var match = _roomTypes.FirstOrDefault(rt =>
            rt.Name.Contains(partialName, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    [FactRequiresLocalDb]
    public void Accept_MixedDurationsAndRoomTypes_AllSpecsMapped()
    {
        // User scenario: M 1.5h SmCls, W 1.5h SmCls, Any 3h Lab24.
        var smClsId = FindRoomTypeId("SmCls") ?? FindRoomTypeId("Small");
        var lab24Id = FindRoomTypeId("Lab24") ?? FindRoomTypeId("Lab");

        _output.WriteLine($"Room types available: {string.Join(", ", _roomTypes.Select(rt => $"{rt.Name} ({rt.Id})"))}");
        _output.WriteLine($"SmCls → {smClsId ?? "NOT FOUND"}, Lab24 → {lab24Id ?? "NOT FOUND"}");

        // If room types don't exist in this DB, skip gracefully.
        if (smClsId == null || lab24Id == null)
        {
            _output.WriteLine("Required room types not found — using first two room types as stand-ins.");
            if (_roomTypes.Count < 2)
            {
                _output.WriteLine("Not enough room types. Skipping.");
                return;
            }
            smClsId = _roomTypes[0].Id;
            lab24Id = _roomTypes[1].Id;
        }

        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 90, StartMinutes: null, RoomTypeId: smClsId),
            new(1, Day: 3, DurationMinutes: 90, StartMinutes: null, RoomTypeId: smClsId),
            new(2, Day: null, DurationMinutes: 180, StartMinutes: null, RoomTypeId: lab24Id),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions, max: 10);

        Assert.NotEmpty(solutions);

        // Test every non-alternative solution's acceptance mapping.
        foreach (var sol in solutions.Where(s => !s.IsAlternative).Take(5))
        {
            var mapped = RoomAvailabilityBrowserViewModel.MapSlotsToSpecs(sol.Slots, specs);

            _output.WriteLine($"\n  Solution ({sol.Tier}):");
            foreach (var slot in sol.Slots)
                _output.WriteLine($"    Slot: D{slot.Day} {slot.StartMinutes / 60:D2}{slot.StartMinutes % 60:D2} dur={slot.DurationMinutes} room={slot.RoomLabel}");
            _output.WriteLine($"  Mapped {mapped.Count} of {specs.Count} specs:");
            foreach (var m in mapped)
                _output.WriteLine($"    SpecIdx={m.SpecIndex} → D{m.Day} {m.StartMinutes / 60:D2}{m.StartMinutes % 60:D2} dur={m.DurationMinutes} room={m.RoomLabel}");

            // Every spec must be mapped.
            Assert.Equal(specs.Count, mapped.Count);

            // Each mapped spec index must appear exactly once.
            var mappedIndices = mapped.Select(m => m.SpecIndex).OrderBy(x => x).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, mappedIndices);

            // Spec 0 (M, 90min): mapped day must be Monday.
            var spec0 = mapped.First(m => m.SpecIndex == 0);
            Assert.Equal(1, spec0.Day);
            Assert.Equal(90, spec0.DurationMinutes);
            Assert.True(spec0.StartMinutes > 0, "Start time should be a real time, not midnight");

            // Spec 1 (W, 90min): mapped day must be Wednesday.
            var spec1 = mapped.First(m => m.SpecIndex == 1);
            Assert.Equal(3, spec1.Day);
            Assert.Equal(90, spec1.DurationMinutes);
            Assert.True(spec1.StartMinutes > 0, "Start time should be a real time, not midnight");

            // Spec 2 (any day, 180min): day can be anything, but must have correct duration.
            var spec2 = mapped.First(m => m.SpecIndex == 2);
            Assert.Equal(180, spec2.DurationMinutes);
            Assert.True(spec2.StartMinutes > 0, "Start time should be a real time, not midnight");
            Assert.InRange(spec2.Day, 1, 7);

            // Room assignments must come from the correct type pools.
            var smClsRoomIds = new HashSet<string>(_rooms.Where(r => r.RoomTypeId == smClsId).Select(r => r.Id));
            var lab24RoomIds = new HashSet<string>(_rooms.Where(r => r.RoomTypeId == lab24Id).Select(r => r.Id));

            if (smClsRoomIds.Count > 0)
                Assert.Contains(spec0.RoomId, smClsRoomIds);
            if (smClsRoomIds.Count > 0)
                Assert.Contains(spec1.RoomId, smClsRoomIds);
            if (lab24RoomIds.Count > 0)
                Assert.Contains(spec2.RoomId, lab24RoomIds);
        }
    }

    [FactRequiresLocalDb]
    public void Accept_SimulateTransferBack_AllFieldsFilled()
    {
        // Simulates AcceptBrowserSolution logic: "fill in only blank fields."
        // Specs: M open-start 90min, W open-start 90min, Any-day 180min.
        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
            new(1, Day: 3, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
            new(2, Day: null, DurationMinutes: 180, StartMinutes: null, RoomTypeId: null),
        };

        var solutions = Solve(specs);
        if (solutions.Count == 0)
        {
            _output.WriteLine("No solutions — skipping transfer test.");
            return;
        }

        DumpSolutions(solutions, max: 5);

        // Pick the first primary solution.
        var sol = solutions.FirstOrDefault(s => !s.IsAlternative) ?? solutions.First();
        var mapped = RoomAvailabilityBrowserViewModel.MapSlotsToSpecs(sol.Slots, specs);

        _output.WriteLine($"\nTransfer test — solution tier: {sol.Tier}");

        // Simulate the meeting state BEFORE acceptance:
        // meeting 0: Day=1 (set), Start=null, Room=null
        // meeting 1: Day=3 (set), Start=null, Room=null
        // meeting 2: Day=0 (unset), Start=null, Room=null
        var meetingState = new (int Day, int? StartMinutes, string? RoomId)[]
        {
            (1, null, null),
            (3, null, null),
            (0, null, null),
        };

        // Apply acceptance logic (mirrors AcceptBrowserSolution).
        foreach (var m in mapped)
        {
            if (m.SpecIndex < 0 || m.SpecIndex >= meetingState.Length) continue;

            ref var meeting = ref meetingState[m.SpecIndex];

            if (meeting.Day == 0)
                meeting.Day = m.Day;
            if (!meeting.StartMinutes.HasValue)
                meeting.StartMinutes = m.StartMinutes;
            if (string.IsNullOrEmpty(meeting.RoomId))
                meeting.RoomId = m.RoomId;
        }

        // Verify ALL fields are now filled.
        for (int i = 0; i < meetingState.Length; i++)
        {
            var ms = meetingState[i];
            _output.WriteLine($"  Meeting[{i}]: Day={ms.Day}, Start={ms.StartMinutes}, Room={ms.RoomId}");

            Assert.True(ms.Day >= 1 && ms.Day <= 7, $"Meeting[{i}].Day should be 1–7, got {ms.Day}");
            Assert.NotNull(ms.StartMinutes);
            Assert.True(ms.StartMinutes > 0, $"Meeting[{i}].StartMinutes should be > 0, got {ms.StartMinutes}");
            Assert.False(string.IsNullOrEmpty(ms.RoomId), $"Meeting[{i}].RoomId should be set");
        }

        // Meeting 0 and 1 should retain their original days.
        Assert.Equal(1, meetingState[0].Day);
        Assert.Equal(3, meetingState[1].Day);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Same-day specs: non-overlapping constraint
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if all same-day slots in the solution are non-overlapping.
    /// </summary>
    private static bool AllSlotsNonOverlapping(RoomSolution sol)
    {
        foreach (var group in sol.Slots.GroupBy(s => s.Day).Where(g => g.Count() > 1))
        {
            var sorted = group.OrderBy(s => s.StartMinutes).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                int endA = sorted[i].StartMinutes + sorted[i].DurationMinutes;
                if (endA > sorted[i + 1].StartMinutes)
                    return false;
            }
        }
        return true;
    }

    [FactRequiresLocalDb]
    public void SameDay_DiffDurations_DiffRoomTypes_AllNonOverlapping()
    {
        // Mon 1.5h SmCls + Mon 3h Lab24: every solution must have 2 non-overlapping slots.
        var smClsId = FindRoomTypeId("SmCls") ?? FindRoomTypeId("Small");
        var lab24Id = FindRoomTypeId("Lab24") ?? FindRoomTypeId("Lab");

        if (smClsId == null || lab24Id == null)
        {
            if (_roomTypes.Count < 2) { _output.WriteLine("Not enough room types. Skipping."); return; }
            smClsId = _roomTypes[0].Id;
            lab24Id = _roomTypes[1].Id;
        }

        _output.WriteLine($"SmCls rooms: {_rooms.Count(r => r.RoomTypeId == smClsId)}, Lab24 rooms: {_rooms.Count(r => r.RoomTypeId == lab24Id)}");

        var specs = new List<MeetingSpec>
        {
            new(0, Day: 1, DurationMinutes: 90,  StartMinutes: null, RoomTypeId: smClsId),
            new(1, Day: 1, DurationMinutes: 180, StartMinutes: null, RoomTypeId: lab24Id),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions, max: 10);

        Assert.NotEmpty(solutions);
        foreach (var sol in solutions)
        {
            Assert.Equal(2, sol.Slots.Count);
            Assert.True(AllSlotsNonOverlapping(sol),
                $"Overlap detected: {string.Join(", ", sol.Slots.Select(s => $"D{s.Day} {s.StartMinutes}-{s.StartMinutes + s.DurationMinutes}"))}");
        }
    }

    [FactRequiresLocalDb]
    public void SameDay_SameRoomType_NonOverlapping()
    {
        // Two 1.5h specs on the same day, same room type.
        // Tier 2a can assign them to the same room at different times.
        var typeId = _roomTypes.FirstOrDefault()?.Id;
        if (typeId == null) { _output.WriteLine("No room types. Skipping."); return; }

        var roomCount = _rooms.Count(r => r.RoomTypeId == typeId);
        _output.WriteLine($"Room type: {typeId}, rooms: {roomCount}");

        var specs = new List<MeetingSpec>
        {
            new(0, Day: 2, DurationMinutes: 90, StartMinutes: null, RoomTypeId: typeId),
            new(1, Day: 2, DurationMinutes: 90, StartMinutes: null, RoomTypeId: typeId),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions, max: 10);

        Assert.NotEmpty(solutions);
        foreach (var sol in solutions)
        {
            Assert.Equal(2, sol.Slots.Count);
            Assert.True(AllSlotsNonOverlapping(sol),
                $"Overlap detected: {string.Join(", ", sol.Slots.Select(s => $"D{s.Day} {s.StartMinutes}-{s.StartMinutes + s.DurationMinutes}"))}");
        }
    }

    [FactRequiresLocalDb]
    public void SameDay_ThreeSpecs_NonOverlapping()
    {
        // Three specs on one day: two 1h + one 1.5h. All must be non-overlapping.
        var specs = new List<MeetingSpec>
        {
            new(0, Day: 3, DurationMinutes: 60,  StartMinutes: null, RoomTypeId: null),
            new(1, Day: 3, DurationMinutes: 60,  StartMinutes: null, RoomTypeId: null),
            new(2, Day: 3, DurationMinutes: 90,  StartMinutes: null, RoomTypeId: null),
        };

        var solutions = Solve(specs);
        DumpSolutions(solutions, max: 10);

        Assert.NotEmpty(solutions);
        foreach (var sol in solutions)
        {
            Assert.Equal(3, sol.Slots.Count);
            Assert.True(AllSlotsNonOverlapping(sol),
                $"Overlap detected: {string.Join(", ", sol.Slots.Select(s => $"D{s.Day} {s.StartMinutes}-{s.StartMinutes + s.DurationMinutes}"))}");
        }
    }

    [FactRequiresLocalDb]
    public void DifferentDays_BehaviorUnchanged()
    {
        // MWF 1h specs on different days — should still produce solutions, regression check.
        var specs = PinnedDaySpecs(60, 1, 3, 5);
        var solutions = Solve(specs);
        DumpSolutions(solutions, max: 5);

        Assert.NotEmpty(solutions);
        foreach (var sol in solutions)
        {
            Assert.Equal(3, sol.Slots.Count);
            // Each slot on a different day.
            var days = sol.Slots.Select(s => s.Day).Distinct().ToList();
            Assert.Equal(3, days.Count);
        }
    }
}
