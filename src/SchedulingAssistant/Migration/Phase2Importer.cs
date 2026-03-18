// ─────────────────────────────────────────────────────────────────────────────
// ONE-TIME MIGRATION UTILITY — DELETE AFTER USE
//
// Phase 2: reads the JSON files produced by Phase 1 and inserts the data
// into the new SchedulingAssistant schema.
//
// Import order:
//   1. Reference data from XYZ_UNITS JSON files:
//        section property values (staff types, tags, section types, meeting
//        types, resources, reserves), rooms, subjects, courses, instructors.
//   2. Schedule data from XYZ_FLOW_YEARS JSON files:
//        academic years, semesters, sections.
//
// Call RunAsync with dryRun=true to preview what would be imported without
// writing anything.  Review the report, then call again with dryRun=false.
// ─────────────────────────────────────────────────────────────────────────────

#if DEBUG
using Newtonsoft.Json.Linq;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace SchedulingAssistant.Migration;

/// <summary>
/// Translates old-format JSON files (produced by <see cref="MigrationRunner"/>
/// Phase 1) into the new SchedulingAssistant schema and optionally writes the
/// result to an open <see cref="DatabaseContext"/>.
/// </summary>
public class Phase2Importer
{
    // ── Repositories ──────────────────────────────────────────────────────────

    private readonly IDatabaseContext            _db;
    private readonly ISectionPropertyRepository _propRepo;
    private readonly IInstructorRepository      _instructorRepo;
    private readonly IRoomRepository            _roomRepo;
    private readonly ISubjectRepository         _subjectRepo;
    private readonly ICourseRepository          _courseRepo;
    private readonly IAcademicYearRepository    _ayRepo;
    private readonly ISemesterRepository        _semesterRepo;
    private readonly ISectionRepository         _sectionRepo;

    // ── Name → ID lookup caches (case-insensitive) ────────────────────────────
    // Pre-loaded from the DB, then extended as new rows are inserted so that
    // cross-file duplicates are deduplicated within a single run.

    private readonly Dictionary<string, string> _tagByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _staffTypeByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sectionTypeByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _meetingTypeByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resourceByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _reserveByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _campusByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _roomByName
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _subjectByAbbr
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reverse of _subjectByAbbr: maps Subject.Id → CalendarAbbreviation.
    /// Used to reconstruct the composite course lookup key when loading existing
    /// courses from the DB (where only SubjectId is directly available).
    /// </summary>
    private readonly Dictionary<string, string> _subjectIdToAbbr
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps "{subjectAbbreviation}|{courseNumber}" → Course.Id.
    /// The composite key prevents collisions when two subjects share the same
    /// course number (e.g. FLOW 101 and HIST 101 are distinct courses).
    /// </summary>
    private readonly Dictionary<string, string> _courseByKey
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps old-system instructor Key (TTId) to the new Instructor.Id.
    /// Built only during the current run; old keys are not persisted.
    /// </summary>
    private readonly Dictionary<string, string> _instructorByOldKey
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps instructor old Key → (WorkLoadObjectKey → WorkLoadUnits).
    /// WorkLoadObjectKey from the instructor's WorkLoadRecord matches the
    /// section's TTId in the year JSON, giving us the per-instructor
    /// workload credit for each section they teach.
    /// Only IsSectionType == true entries are stored.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, double>> _workloadByInstructorKey
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps Course.Id to its level string (e.g. "100", "200").
    /// Populated from existing DB courses in <see cref="LoadExistingData"/> and
    /// extended as new courses are inserted, so sections can inherit the level
    /// without a second DB round-trip.
    /// </summary>
    private readonly Dictionary<string, string> _levelByCourseId
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps academic-year name (e.g. "2025-2026") to new AcademicYear.Id.</summary>
    private readonly Dictionary<string, string> _ayByName
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps "{ayId}|{semesterName}" to new Semester.Id.</summary>
    private readonly Dictionary<string, string> _semesterByKey
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Per-run counters ──────────────────────────────────────────────────────

    private int _propInserted;
    private int _roomsInserted;
    private int _subjectsInserted;
    private int _coursesInserted;
    private int _instructorsInserted;
    private int _ayInserted;
    private int _semestersInserted;
    private int _sectionsInserted;
    private int _sectionsSkipped;
    private readonly List<string> _warnings = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="db">Open DatabaseContext for the target database.</param>
    public Phase2Importer(IDatabaseContext db)
    {
        _db             = db;
        _propRepo       = new SectionPropertyRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _roomRepo       = new RoomRepository(db);
        _subjectRepo    = new SubjectRepository(db);
        _courseRepo     = new CourseRepository(db);
        _ayRepo         = new AcademicYearRepository(db);
        _semesterRepo   = new SemesterRepository(db);
        _sectionRepo    = new SectionRepository(db);
    }

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Runs the Phase 2 import (or dry run).
    /// </summary>
    /// <param name="unitJsonDir">
    ///   Folder containing one JSON file per academic unit,
    ///   produced by Phase 1 (XYZ_UNITS conversion).
    /// </param>
    /// <param name="yearsJsonDir">
    ///   Folder containing one JSON file per academic year,
    ///   produced by Phase 1 (XYZ_FLOW_YEARS conversion).
    /// </param>
    /// <param name="dryRun">
    ///   When true, reads and translates all data but writes nothing.
    ///   Returns a report showing exactly what would be imported.
    /// </param>
    /// <returns>Human-readable import report.</returns>
    public async Task<string> RunAsync(string unitJsonDir, string yearsJsonDir, bool dryRun)
    {
        ResetState();

        var report = new StringBuilder();
        report.AppendLine(dryRun
            ? "════════════  DRY RUN — nothing will be written  ════════════"
            : "════════════════════  IMPORTING DATA  ═══════════════════════");
        report.AppendLine();

        // ── Load JSON files ──────────────────────────────────────────────────

        var unitObjects = await LoadJsonFilesAsync(unitJsonDir, "unit", report);
        var yearObjects = await LoadJsonFilesAsync(yearsJsonDir, "year", report);
        report.AppendLine();

        if (unitObjects.Count == 0 && yearObjects.Count == 0)
        {
            report.AppendLine("No JSON files found in either folder.");
            return report.ToString();
        }

        // ── Seed lookup caches from the existing DB ──────────────────────────

        LoadExistingData();

        // ── Phase 2a: Reference data ─────────────────────────────────────────

        report.AppendLine("── Phase 2a: Reference data ─────────────────────────────────");
        report.AppendLine();

        foreach (var unitObj in unitObjects)
        {
            var (refMap, root) = BuildRefMap(unitObj);

            ImportStaffTypes(root, refMap, dryRun);
            ImportNamedPropertyValues(root, refMap, "UnitTags",     SectionPropertyTypes.Tag,         _tagByName,         dryRun);
            ImportNamedPropertyValues(root, refMap, "SectionTypes", SectionPropertyTypes.SectionType, _sectionTypeByName, dryRun);
            ImportNamedPropertyValues(root, refMap, "MeetingTypes", SectionPropertyTypes.MeetingType, _meetingTypeByName, dryRun);
            ImportNamedPropertyValues(root, refMap, "Resources",    SectionPropertyTypes.Resource,    _resourceByName,    dryRun);
            ImportNamedPropertyValues(root, refMap, "Reserves",     SectionPropertyTypes.Reserve,     _reserveByName,     dryRun);
            ImportRoomsFromUnit(root, refMap, dryRun);
            ImportSubjectsAndCoursesFromUnit(root, refMap, dryRun);
            ImportInstructorsFromUnit(root, refMap, dryRun);
        }

        AppendLine(report, "Section property values", _propInserted);
        AppendLine(report, "Rooms",                   _roomsInserted);
        AppendLine(report, "Subjects",                _subjectsInserted);
        AppendLine(report, "Courses",                 _coursesInserted);
        AppendLine(report, "Instructors",             _instructorsInserted);
        report.AppendLine();

        // ── Phase 2b: Schedule data ──────────────────────────────────────────

        report.AppendLine("── Phase 2b: Schedule data ──────────────────────────────────");
        report.AppendLine();

        foreach (var yearObj in yearObjects)
        {
            var (refMap, root) = BuildRefMap(yearObj);
            ImportYearAndSections(root, refMap, dryRun);
        }

        AppendLine(report, "Academic years", _ayInserted);
        AppendLine(report, "Semesters",      _semestersInserted);
        AppendLine(report, "Sections",       _sectionsInserted);
        if (_sectionsSkipped > 0)
            report.AppendLine($"  {"Sections skipped",-28}  {_sectionsSkipped,4}");
        report.AppendLine();

        // ── Warnings ─────────────────────────────────────────────────────────

        if (_warnings.Count > 0)
        {
            report.AppendLine($"── {_warnings.Count} warning(s) ────────────────────────────────────");
            foreach (var w in _warnings.Take(50))
                report.AppendLine("  ⚠  " + w);
            if (_warnings.Count > 50)
                report.AppendLine($"  … and {_warnings.Count - 50} more.");
            report.AppendLine();
        }

        report.AppendLine(dryRun
            ? "Review the above and click Import to commit."
            : "Import complete.");

        return report.ToString();
    }

    // ── File loading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all *.json files from <paramref name="dir"/>, parses each as a
    /// Newtonsoft <see cref="JObject"/>, and returns the list.  Parse errors
    /// are appended to <paramref name="report"/> rather than thrown.
    /// </summary>
    /// <param name="dir">Folder to scan.</param>
    /// <param name="label">Human label used in error messages (e.g. "unit").</param>
    /// <param name="report">StringBuilder to receive progress and error lines.</param>
    /// <returns>Parsed JObjects in file-name order.</returns>
    private static async Task<List<JObject>> LoadJsonFilesAsync(
        string dir, string label, StringBuilder report)
    {
        var results = new List<JObject>();

        if (!Directory.Exists(dir))
        {
            report.AppendLine($"  Folder not found ({label}): {dir}");
            return results;
        }

        var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToArray();
        report.AppendLine($"  {files.Length} {label} file(s) found in: {dir}");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                results.Add(JObject.Parse(json));
            }
            catch (Exception ex)
            {
                report.AppendLine($"  ERROR parsing {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return results;
    }

    // ── $id / $ref resolution ─────────────────────────────────────────────────

    /// <summary>
    /// Walks the JObject tree to build a <c>$id → JToken</c> map.
    /// Returns both the map and the (unchanged) root for convenience.
    /// </summary>
    /// <param name="root">The root JObject to walk.</param>
    private static (Dictionary<string, JToken> refMap, JObject root) BuildRefMap(JObject root)
    {
        var map = new Dictionary<string, JToken>();
        WalkForIds(root, map);
        return (map, root);
    }

    private static void WalkForIds(JToken token, Dictionary<string, JToken> map)
    {
        if (token is JObject obj)
        {
            if (obj["$id"] is JValue idVal)
                map[idVal.ToString()] = obj;
            foreach (var prop in obj.Properties())
                WalkForIds(prop.Value, map);
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                WalkForIds(item, map);
        }
    }

    /// <summary>
    /// If <paramref name="token"/> is a single-property <c>{"$ref":"NNN"}</c>
    /// object, looks up "NNN" in <paramref name="refMap"/> and returns the
    /// referenced token.  Otherwise returns the token unchanged.
    /// Returns a null token if <paramref name="token"/> itself is null.
    /// </summary>
    /// <param name="token">Token to potentially resolve.</param>
    /// <param name="refMap">$id → JToken map built by <see cref="BuildRefMap"/>.</param>
    private static JToken? Resolve(JToken? token, Dictionary<string, JToken> refMap)
    {
        // Treat C# null and JSON null identically — callers use ?. for safe child access.
        if (token is null || token.Type == JTokenType.Null) return null;
        if (token is JObject obj && obj.Count == 1 && obj["$ref"] is JValue refVal)
            return refMap.TryGetValue(refVal.ToString(), out var target) ? target : token;
        return token;
    }

    /// <summary>
    /// Returns the items of a collection token, handling two common serialisation forms:
    /// <list type="bullet">
    ///   <item>JSON.NET <c>PreserveReferencesHandling.All</c> format:
    ///     <c>{"$id":"N","$values":[…]}</c></item>
    ///   <item>Plain JSON array: <c>[…]</c></item>
    /// </list>
    /// Any <c>$ref</c> on the wrapper itself is resolved first.
    /// Returns an empty sequence when the token is null or unrecognised.
    /// </summary>
    /// <param name="token">The token that may be or contain a collection.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    private static IEnumerable<JToken> GetValues(JToken? token, Dictionary<string, JToken> refMap)
    {
        if (token is null) return Enumerable.Empty<JToken>();
        var resolved = Resolve(token, refMap);
        if (resolved is null) return Enumerable.Empty<JToken>();

        // Form 1: plain JArray
        if (resolved is JArray plainArr) return plainArr;

        // Form 2: { "$id": "N", "$values": [...] } — JSON.NET reference-tracking format
        if (resolved is JObject resObj && resObj["$values"] is JArray valArr) return valArr;

        // Form 3: double-wrapped — { "$id": "N", "SomeList": { "$id": "M", "$values": [...] } }
        // Seen in XYZ_UNITS where e.g. Courses → { CourseList → { $values:[...] } }.
        // Walk the first non-metadata child that is itself a collection.
        if (resolved is JObject wrapperObj)
        {
            foreach (var prop in wrapperObj.Properties())
            {
                if (prop.Name.StartsWith("$")) continue;
                var inner = Resolve(prop.Value, refMap);
                if (inner is JArray innerArr) return innerArr;
                if (inner is JObject innerObj && innerObj["$values"] is JArray innerValArr) return innerValArr;
            }
        }

        return Enumerable.Empty<JToken>();
    }

    // ── Existing-data loader ──────────────────────────────────────────────────

    /// <summary>
    /// Pre-loads all existing entities from the DB into the lookup caches.
    /// This allows the importer to deduplicate against data that was already
    /// in the database before this run.
    /// </summary>
    private void LoadExistingData()
    {
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.Tag))         _tagByName[v.Name]         = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.StaffType))   _staffTypeByName[v.Name]   = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.SectionType)) _sectionTypeByName[v.Name] = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.MeetingType)) _meetingTypeByName[v.Name] = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.Resource))    _resourceByName[v.Name]    = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.Reserve))     _reserveByName[v.Name]     = v.Id;
        foreach (var v in _propRepo.GetAll(SectionPropertyTypes.Campus))      _campusByName[v.Name]      = v.Id;

        foreach (var r in _roomRepo.GetAll())
        {
            // Use the display key that RoomRepository sorts by
            var key = string.IsNullOrWhiteSpace(r.Building)
                ? r.RoomNumber
                : $"{r.Building} {r.RoomNumber}";
            if (!string.IsNullOrWhiteSpace(key))
                _roomByName[key] = r.Id;
        }

        foreach (var s in _subjectRepo.GetAll())
            if (!string.IsNullOrWhiteSpace(s.CalendarAbbreviation))
            {
                _subjectByAbbr[s.CalendarAbbreviation] = s.Id;
                _subjectIdToAbbr[s.Id]                 = s.CalendarAbbreviation;
            }

        foreach (var c in _courseRepo.GetAll())
        {
            if (string.IsNullOrWhiteSpace(c.CalendarCode)) continue;
            if (!_subjectIdToAbbr.TryGetValue(c.SubjectId, out var abbr)) continue;
            // CalendarCode is the full code e.g. "FLOW101"; strip the abbreviation prefix
            // to get back the course-number part ("101") that forms the composite key.
            var courseNum = c.CalendarCode.StartsWith(abbr, StringComparison.OrdinalIgnoreCase)
                ? c.CalendarCode[abbr.Length..]
                : c.CalendarCode;
            _courseByKey[$"{abbr}|{courseNum}"] = c.Id;

            // Cache level so sections can inherit it without a second DB round-trip.
            if (!string.IsNullOrEmpty(c.Level))
                _levelByCourseId[c.Id] = c.Level;
        }

        foreach (var ay in _ayRepo.GetAll())
            if (!string.IsNullOrWhiteSpace(ay.Name))
                _ayByName[ay.Name] = ay.Id;

        foreach (var sem in _semesterRepo.GetAll())
            _semesterByKey[$"{sem.AcademicYearId}|{sem.Name}"] = sem.Id;

        // Note: instructor old keys are not stored in the new DB, so
        // _instructorByOldKey is populated only during the current run.
    }

    // ── Section property value importers ──────────────────────────────────────

    /// <summary>
    /// Imports StaffTypes from a unit.  In the old format these are plain
    /// strings inside a <c>{ "$values": [...] }</c> wrapper (not Name objects).
    /// </summary>
    /// <param name="unit">Parsed unit JObject.</param>
    /// <param name="refMap">$id/$ref resolution map for this unit.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportStaffTypes(JObject unit, Dictionary<string, JToken> refMap, bool dryRun)
    {
        var token = unit["StaffTypes"];
        if (token is null) return;

        var resolved = Resolve(token, refMap);
        if (resolved is null) return;

        IEnumerable<JToken> items =
            resolved is JObject r && r["$values"] is JArray valArr ? valArr :
            resolved is JArray directArr         ? directArr :
            Enumerable.Empty<JToken>();

        foreach (var item in items)
        {
            // Old format: plain string, e.g. "Type B"
            var name = item.Type == JTokenType.String
                ? item.Value<string>()
                : (item as JObject)?["Name"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(name)) continue;
            EnsurePropertyValue(SectionPropertyTypes.StaffType, name, _staffTypeByName, dryRun);
        }
    }

    /// <summary>
    /// Imports a collection of named section property values from a unit array.
    /// Each item is expected to have a <c>Name</c> field.
    /// </summary>
    /// <param name="unit">Parsed unit JObject.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    /// <param name="unitArrayKey">
    ///   Property name on the unit that holds the array (e.g. "UnitTags").
    /// </param>
    /// <param name="propertyType">
    ///   SectionPropertyTypes constant (e.g. <see cref="SectionPropertyTypes.Tag"/>).
    /// </param>
    /// <param name="cache">The name→id dict to check / update.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportNamedPropertyValues(
        JObject unit,
        Dictionary<string, JToken> refMap,
        string unitArrayKey,
        string propertyType,
        Dictionary<string, string> cache,
        bool dryRun)
    {
        var token = unit[unitArrayKey];
        if (token is null) return;

        foreach (var item in GetValues(token, refMap))
        {
            var resolved = Resolve(item, refMap);
            var name = resolved["Name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;
            EnsurePropertyValue(propertyType, name, cache, dryRun);
        }
    }

    /// <summary>
    /// Inserts a <see cref="SectionPropertyValue"/> if its name is not already
    /// in <paramref name="cache"/>.  Updates the cache on insert.
    /// </summary>
    /// <param name="type">SectionPropertyTypes constant.</param>
    /// <param name="name">The display name to insert.</param>
    /// <param name="cache">The name→id dict to check / update.</param>
    /// <param name="dryRun">When true, build the cache but do not write to DB.</param>
    private void EnsurePropertyValue(
        string type,
        string name,
        Dictionary<string, string> cache,
        bool dryRun)
    {
        if (cache.ContainsKey(name)) return;

        var value = new SectionPropertyValue { Name = name, SortOrder = cache.Count };
        if (!dryRun)
            _propRepo.Insert(type, value);

        cache[name] = value.Id;
        _propInserted++;
    }

    // ── Room importer ─────────────────────────────────────────────────────────

    /// <summary>
    /// Imports rooms from a unit's <c>Rooms</c> array.
    /// The old room name (e.g. "D119") is stored verbatim in
    /// <see cref="Room.RoomNumber"/>; <see cref="Room.Building"/> is left blank
    /// for manual cleanup after migration.
    /// </summary>
    /// <param name="unit">Parsed unit JObject.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportRoomsFromUnit(JObject unit, Dictionary<string, JToken> refMap, bool dryRun)
    {
        var token = unit["Rooms"];
        if (token is null) return;

        foreach (var item in GetValues(token, refMap))
        {
            var resolved = Resolve(item, refMap);
            var name = resolved["Name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (_roomByName.ContainsKey(name)) continue;

            var room = new Room { RoomNumber = name };
            if (!dryRun)
                _roomRepo.Insert(room);

            _roomByName[name] = room.Id;
            _roomsInserted++;
        }
    }

    // ── Subject and course importer ───────────────────────────────────────────

    /// <summary>
    /// Imports subjects (from the <c>Subjects</c> array) and courses (from the
    /// <c>Courses</c> array) within a single unit file.
    ///
    /// Course CalendarCode = Subject.Abbreviation + CrsCode.Name, e.g. "FLOW101".
    /// </summary>
    /// <param name="unit">Parsed unit JObject.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportSubjectsAndCoursesFromUnit(
        JObject unit, Dictionary<string, JToken> refMap, bool dryRun)
    {
        // ── Subjects ──────────────────────────────────────────────────────────
        var subjectsToken = unit["Subjects"];
        if (subjectsToken != null)
        {
            foreach (var item in GetValues(subjectsToken, refMap))
            {
                var resolved = Resolve(item, refMap);
                var abbr     = resolved["Abbreviation"]?.Value<string>();
                var name     = resolved["Name"]?.Value<string>() ?? abbr ?? "";
                if (string.IsNullOrWhiteSpace(abbr)) continue;
                EnsureSubject(abbr, name, dryRun);
            }
        }

        // ── Courses ───────────────────────────────────────────────────────────
        var unitName     = unit["Name"]?.Value<string>() ?? "?";
        var coursesToken = unit["Courses"];
        if (coursesToken is null)
        {
            _warnings.Add($"[{unitName}] unit[\"Courses\"] key is null/missing — no courses parsed");
            return;
        }

        var courseItems    = GetValues(coursesToken, refMap).ToList();
        int cInserted      = 0;   // inserted this unit
        int cSkippedNull   = 0;   // missing abbr or courseNum
        int cSkippedDupe   = 0;   // already in cache

        foreach (var item in courseItems)
        {
            var resolved = Resolve(item, refMap);

            var title    = resolved["Name"]?.Value<string>() ?? "";
            var isActive = resolved["IsActive"]?.Value<bool>() ?? true;

            var subjectToken = Resolve(resolved["Subject"], refMap);
            var abbr         = subjectToken?["Abbreviation"]?.Value<string>();
            var subjectName  = subjectToken?["Name"]?.Value<string>() ?? abbr ?? "";

            var crsCodeToken = Resolve(resolved["CrsCode"], refMap);
            var courseNum    = crsCodeToken?["Name"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(abbr) || string.IsNullOrWhiteSpace(courseNum))
            {
                cSkippedNull++;
                continue;
            }

            // Composite key: "{subjectAbbr}|{courseNum}" — prevents collisions between
            // subjects that share the same course number (e.g. FLOW 101 vs HIST 101).
            var courseKey = $"{abbr}|{courseNum}";
            if (_courseByKey.ContainsKey(courseKey)) { cSkippedDupe++; continue; }

            var subjectId = EnsureSubject(abbr, subjectName, dryRun);

            // Collect course tags
            var tagIds = new List<string>();
            var courseTagsToken = resolved["CourseTags"];
            if (courseTagsToken != null)
            {
                foreach (var tagToken in GetValues(courseTagsToken, refMap))
                {
                    var tagResolved = Resolve(tagToken, refMap);
                    var tagName     = tagResolved["Name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(tagName)) continue;
                    EnsurePropertyValue(SectionPropertyTypes.Tag, tagName, _tagByName, dryRun);
                    if (_tagByName.TryGetValue(tagName, out var tid))
                        tagIds.Add(tid);
                }
            }

            var level = CourseLevelParser.ParseLevel(courseNum) ?? string.Empty;

            var course = new Course
            {
                SubjectId    = subjectId,
                CalendarCode = abbr + courseNum,   // full code e.g. "FLOW101" — abbr prefix + numeric part
                Title        = title,
                IsActive     = isActive,
                TagIds       = tagIds,
                Level        = level
            };

            if (!dryRun)
                _courseRepo.Insert(course);

            _courseByKey[courseKey] = course.Id;

            // Cache level so sections can inherit it during Phase 2b.
            if (!string.IsNullOrEmpty(level))
                _levelByCourseId[course.Id] = level;
            _coursesInserted++;
            cInserted++;
        }

        _warnings.Add($"[{unitName}] Courses: found={courseItems.Count}, inserted={cInserted}, skipped_null={cSkippedNull}, skipped_dupe={cSkippedDupe}");
    }

    /// <summary>
    /// Finds an existing Subject by abbreviation or creates a new one.
    /// Returns the subject's Id.  Increments <see cref="_subjectsInserted"/>
    /// only on creation.
    /// </summary>
    /// <param name="abbreviation">Calendar abbreviation, e.g. "FLOW".</param>
    /// <param name="name">Full name, e.g. "Flower Arranging".</param>
    /// <param name="dryRun">When true, build the cache but do not write to DB.</param>
    private string EnsureSubject(string abbreviation, string name, bool dryRun)
    {
        if (_subjectByAbbr.TryGetValue(abbreviation, out var existing))
            return existing;

        var subject = new Subject { Name = name, CalendarAbbreviation = abbreviation };
        if (!dryRun)
            _subjectRepo.Insert(subject);

        _subjectByAbbr[abbreviation]    = subject.Id;
        _subjectIdToAbbr[subject.Id]    = abbreviation;
        _subjectsInserted++;
        return subject.Id;
    }

    // ── Instructor importer ───────────────────────────────────────────────────

    /// <summary>
    /// Imports instructors (staff members) from a unit's <c>Staff</c> array.
    ///
    /// The old <c>Key</c> / <c>TTId</c> is stored in memory only
    /// (<see cref="_instructorByOldKey"/>) so that sections processed later in
    /// the same run can link to the correct new Instructor.Id.  It is NOT
    /// persisted to the database.
    /// </summary>
    /// <param name="unit">Parsed unit JObject.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportInstructorsFromUnit(JObject unit, Dictionary<string, JToken> refMap, bool dryRun)
    {
        var unitNameI  = unit["Name"]?.Value<string>() ?? "?";
        var staffToken = unit["Staff"];
        if (staffToken is null)
        {
            _warnings.Add($"[{unitNameI}] unit[\"Staff\"] key is null/missing — no instructors parsed");
            return;
        }

        var staffItems   = GetValues(staffToken, refMap).ToList();
        int iInserted    = 0;
        int iSkippedNull = 0;   // missing Key/TTId
        int iSkippedDupe = 0;   // already in cache

        foreach (var item in staffItems)
        {
            var resolved = Resolve(item, refMap);

            var oldKey    = resolved["Key"]?.Value<string>()
                         ?? resolved["TTId"]?.Value<string>();
            var initials  = resolved["Abbreviation"]?.Value<string>() ?? "";
            var firstName = resolved["FirstName"]?.Value<string>() ?? "";
            var lastName  = resolved["Surname"]?.Value<string>() ?? "";
            var email     = resolved["EmailAddress"]?.Value<string>() ?? "";
            var isActive  = resolved["IsActive"]?.Value<bool>() ?? true;
            var contract  = resolved["Contract"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(oldKey)) { iSkippedNull++; continue; }
            if (_instructorByOldKey.ContainsKey(oldKey)) { iSkippedDupe++; continue; }

            // Ensure staff type exists
            string? staffTypeId = null;
            if (!string.IsNullOrWhiteSpace(contract))
            {
                EnsurePropertyValue(SectionPropertyTypes.StaffType, contract, _staffTypeByName, dryRun);
                staffTypeId = _staffTypeByName.GetValueOrDefault(contract);
            }

            var instructor = new Instructor
            {
                FirstName   = firstName,
                LastName    = lastName,
                Initials    = initials,
                Email       = email,
                IsActive    = isActive,
                StaffTypeId = staffTypeId
            };

            if (!dryRun)
                _instructorRepo.Insert(instructor);

            _instructorByOldKey[oldKey] = instructor.Id;

            // ── Workload lookup ───────────────────────────────────────────────
            // WorkLoadRecord is an object whose properties are semester keys
            // (e.g. "Fall2024-2025").  Each value holds a $values array of
            // workload entries.  We collect every IsSectionType == true entry
            // so that ImportSection can find the credit for this instructor
            // when it processes sections later.
            var workloadLookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (resolved["WorkLoadRecord"] is JObject workloadRecord)
            {
                foreach (var semProp in workloadRecord.Properties())
                {
                    if (semProp.Name.StartsWith("$")) continue;
                    var semValue = Resolve(semProp.Value, refMap);
                    foreach (var entry in GetValues(semValue, refMap))
                    {
                        var e       = Resolve(entry, refMap);
                        if (e?["IsSectionType"]?.Value<bool>() != true) continue;
                        var wlKey   = e["WorkLoadObjectKey"]?.Value<string>();
                        var wlUnits = e["WorkLoadUnits"]?.Value<double>();
                        if (!string.IsNullOrWhiteSpace(wlKey) && wlUnits.HasValue)
                            workloadLookup[wlKey] = wlUnits.Value;
                    }
                }
            }
            _workloadByInstructorKey[oldKey] = workloadLookup;

            _instructorsInserted++;
            iInserted++;
        }

        _warnings.Add($"[{unitNameI}] Staff: found={staffItems.Count}, inserted={iInserted}, skipped_null={iSkippedNull}, skipped_dupe={iSkippedDupe}");
    }

    // ── Academic year / semester / section importer ───────────────────────────

    /// <summary>
    /// Imports one year JSON file: creates the AcademicYear if it does not
    /// exist, then iterates its Semesters (an object with named properties,
    /// not an array) and imports each semester's sections.
    /// </summary>
    /// <param name="yearRoot">Parsed year JObject.</param>
    /// <param name="refMap">$id/$ref resolution map for this year file.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportYearAndSections(JObject yearRoot, Dictionary<string, JToken> refMap, bool dryRun)
    {
        var yearName = yearRoot["Name"]?.Value<string>()
                    ?? yearRoot["Key"]?.Value<string>()
                    ?? "";

        if (string.IsNullOrWhiteSpace(yearName)) return;

        var ayId = EnsureAcademicYear(yearName, dryRun);

        // Semesters is an OBJECT with named properties (e.g. "Fall2025-2026"),
        // NOT a $values array.
        if (yearRoot["Semesters"] is not JObject semestersObj) return;

        foreach (var semProp in semestersObj.Properties())
        {
            if (semProp.Name.StartsWith("$")) continue; // skip $id / $ref metadata

            var semToken = Resolve(semProp.Value, refMap);
            if (semToken is not JObject semObj) continue;

            var semFullName = semObj["Name"]?.Value<string>() ?? semProp.Name;
            var seasonName  = ExtractSeasonName(semFullName, yearName);
            var semId       = EnsureSemester(ayId, seasonName, dryRun);

            var sectionListToken = semObj["SectionList"];
            if (sectionListToken is null) continue;

            foreach (var secToken in GetValues(sectionListToken, refMap))
            {
                var secResolved = Resolve(secToken, refMap);
                ImportSection(secResolved, refMap, semId, dryRun);
            }
        }
    }

    /// <summary>
    /// Finds an existing AcademicYear by name or creates a new one.
    /// Returns the AcademicYear's Id.
    /// </summary>
    /// <param name="name">Year name, e.g. "2025-2026".</param>
    /// <param name="dryRun">When true, build the cache but do not write to DB.</param>
    private string EnsureAcademicYear(string name, bool dryRun)
    {
        if (_ayByName.TryGetValue(name, out var existing)) return existing;

        var ay = new AcademicYear { Name = name };
        if (!dryRun)
        {
            _ayRepo.Insert(ay);
            // Seed the institution's default legal start-time matrix for this year,
            // just as the UI does when a new academic year is created manually.
            SeedData.SeedDefaultLegalStartTimes(_db.Connection, ay.Id);
        }

        _ayByName[name] = ay.Id;
        _ayInserted++;
        return ay.Id;
    }

    /// <summary>
    /// Finds an existing Semester by academic year + name, or creates a new one.
    /// Returns the Semester's Id.
    /// </summary>
    /// <param name="ayId">Parent AcademicYear.Id.</param>
    /// <param name="semesterName">Semester name, e.g. "Fall".</param>
    /// <param name="dryRun">When true, build the cache but do not write to DB.</param>
    private string EnsureSemester(string ayId, string semesterName, bool dryRun)
    {
        var key = $"{ayId}|{semesterName}";
        if (_semesterByKey.TryGetValue(key, out var existing)) return existing;

        // Assign sort order based on the canonical order; unknown names sort to end.
        var sortOrder = Array.IndexOf(Semester.DefaultNames, semesterName);
        if (sortOrder < 0)
            sortOrder = _semesterByKey.Count(k => k.Key.StartsWith(ayId + "|"));

        var semester = new Semester
        {
            AcademicYearId = ayId,
            Name           = semesterName,
            SortOrder      = sortOrder
        };

        if (!dryRun)
            _semesterRepo.Insert(semester);

        _semesterByKey[key] = semester.Id;
        _semestersInserted++;
        return semester.Id;
    }

    /// <summary>
    /// Translates and optionally imports a single section.
    /// Skips sections where <c>SectionIsValid</c> is false or where the
    /// subject abbreviation / course number is missing.
    /// </summary>
    /// <param name="sec">The section JToken to translate.</param>
    /// <param name="refMap">$id/$ref resolution map.</param>
    /// <param name="semesterId">Target semester Id.</param>
    /// <param name="dryRun">When true, report only — do not write to DB.</param>
    private void ImportSection(JToken sec, Dictionary<string, JToken> refMap, string semesterId, bool dryRun)
    {
        // ── Validity guard ────────────────────────────────────────────────────
        if (sec["SectionIsValid"]?.Value<bool>() == false) { _sectionsSkipped++; return; }

        var subjectToken = Resolve(sec["SectionSubject"], refMap);
        var crsCodeToken = Resolve(sec["SectionCrsCode"], refMap);

        var abbr        = subjectToken?["Abbreviation"]?.Value<string>();
        var courseNum   = crsCodeToken?["Name"]?.Value<string>();
        var sectionCode = sec["SectionCode"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(abbr) || string.IsNullOrWhiteSpace(courseNum))
        { _sectionsSkipped++; return; }

        if (string.IsNullOrWhiteSpace(sectionCode))
        {
            _sectionsSkipped++;
            _warnings.Add($"Section with no SectionCode found in {abbr}{courseNum} — skipped.");
            return;
        }

        var courseKey = $"{abbr}|{courseNum}";
        if (!_courseByKey.TryGetValue(courseKey, out var courseId))
        {
            _sectionsSkipped++;
            _warnings.Add($"Course '{abbr} {courseNum}' not in cache — section '{sectionCode}' skipped. " +
                          "Run units and years in the same import run.");
            return;
        }

        // ── Campus ────────────────────────────────────────────────────────────
        string? campusId = null;
        var campusToken  = Resolve(sec["Campus"], refMap);
        var campusName   = campusToken?["Name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(campusName))
        {
            EnsurePropertyValue(SectionPropertyTypes.Campus, campusName, _campusByName, dryRun);
            campusId = _campusByName.GetValueOrDefault(campusName);
        }

        // ── Section type ──────────────────────────────────────────────────────
        string? sectionTypeId = null;
        var stToken = Resolve(sec["SectionType"], refMap);
        var stName  = stToken?["Name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(stName))
            _sectionTypeByName.TryGetValue(stName, out sectionTypeId);

        // ── Tags ──────────────────────────────────────────────────────────────
        var tagIds = new List<string>();
        foreach (var tagToken in GetValues(sec["SectionTags"], refMap))
        {
            var tagName = Resolve(tagToken, refMap)?["Name"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(tagName) && _tagByName.TryGetValue(tagName, out var tid))
                tagIds.Add(tid);
        }

        // ── Resource (section-level, single) ──────────────────────────────────
        var resourceIds  = new List<string>();
        var resourceName = Resolve(sec["Resource"], refMap)?["Name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(resourceName) && _resourceByName.TryGetValue(resourceName, out var rid))
            resourceIds.Add(rid);

        // ── Reserve weights ───────────────────────────────────────────────────
        // ReservationWeightsForSection is a plain object keyed by reserve TTId/Name → int weight.
        var reserveWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sec["ReservationWeightsForSection"] is JObject weightsObj)
            foreach (var prop in weightsObj.Properties())
                if (!prop.Name.StartsWith("$") && prop.Value.Type == JTokenType.Integer)
                    reserveWeights[prop.Name] = prop.Value.Value<int>();

        // ── Reserves ──────────────────────────────────────────────────────────
        var reserves = new List<SectionReserve>();
        foreach (var resToken in GetValues(sec["ReservesForSection"], refMap))
        {
            var resObj   = Resolve(resToken, refMap);
            var resName  = resObj?["Name"]?.Value<string>();
            var resTTId  = resObj?["TTId"]?.Value<string>() ?? resName;
            if (!string.IsNullOrWhiteSpace(resName) && _reserveByName.TryGetValue(resName, out var resId))
            {
                var weight = !string.IsNullOrWhiteSpace(resTTId) && reserveWeights.TryGetValue(resTTId, out var w) ? w : 0;
                reserves.Add(new SectionReserve { ReserveId = resId, Code = weight });
            }
        }

        // ── Instructor assignments ─────────────────────────────────────────────
        // The section's SectionKey matches the WorkLoadObjectKey stored in each
        // instructor's WorkLoadRecord.  Copied sections may lack a TTId, so
        // SectionKey is the reliable field to use here.
        var sectionKey = sec["SectionKey"]?.Value<string>()
                      ?? sec["TTId"]?.Value<string>()
                      ?? sec["Key"]?.Value<string>();

        var assignments = new List<InstructorAssignment>();
        foreach (var staffItem in GetValues(sec["StaffMemberIdentifiers"], refMap))
        {
            // Item2 is the old instructor Key / TTId
            var oldKey = Resolve(staffItem, refMap)?["Item2"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(oldKey) || !_instructorByOldKey.TryGetValue(oldKey, out var newId))
                continue;

            decimal? workload = null;
            if (!string.IsNullOrWhiteSpace(sectionKey)
                && _workloadByInstructorKey.TryGetValue(oldKey, out var wlLookup)
                && wlLookup.TryGetValue(sectionKey, out var wlVal))
                workload = (decimal)wlVal;

            assignments.Add(new InstructorAssignment { InstructorId = newId, Workload = workload });
        }

        // ── Schedule (bookings) ───────────────────────────────────────────────
        var schedule = new List<SectionDaySchedule>();
        foreach (var bookingToken in GetValues(sec["Bookings"], refMap))
        {
            var booking     = Resolve(bookingToken, refMap);
            var dayToken    = Resolve(booking["Day"], refMap);
            var dayPosition = dayToken?["Position"]?.Value<int>() ?? -1;
            if (dayPosition < 0 || dayPosition > 4) continue; // Mon=0 … Fri=4

            var beginRaw = booking["BeginTime"]?.Value<int>() ?? 0;
            var endRaw   = booking["EndTime"]?.Value<int>() ?? 0;
            if (beginRaw == 0 || endRaw == 0) continue;

            var startMin = HhmmToMinutes(beginRaw);
            // The old app stored end times 10 minutes early (a built-in break buffer).
            // Add those 10 minutes back so the imported duration reflects the actual class length.
            var endMin      = HhmmToMinutes(endRaw) + 10;
            var durationMin = endMin - startMin;
            if (durationMin <= 0) continue;

            // Room for this booking
            string? roomId   = null;
            var roomName     = Resolve(booking["Room"], refMap)?["Name"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(roomName))
                _roomByName.TryGetValue(roomName, out roomId);

            // Meeting type
            string? meetingTypeId = null;
            var mtName = Resolve(booking["MeetingType"], refMap)?["Name"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(mtName))
                _meetingTypeByName.TryGetValue(mtName, out meetingTypeId);

            // Week suffix → frequency ("(odd)" → "odd", "(even)" → "even")
            var weekSuffix = booking["WeekSuffix"]?.Value<string>();
            var frequency  = string.IsNullOrWhiteSpace(weekSuffix)
                ? null
                : weekSuffix.Trim('(', ')').ToLowerInvariant();

            schedule.Add(new SectionDaySchedule
            {
                Day             = dayPosition + 1, // old 0-based → new 1=Mon…5=Fri
                StartMinutes    = startMin,
                DurationMinutes = durationMin,
                RoomId          = roomId,
                MeetingTypeId   = meetingTypeId,
                Frequency       = string.IsNullOrEmpty(frequency) ? null : frequency
            });
        }

        // Inherit the level from the course so level-based filtering works without
        // a course look-up at query time.
        var level = courseId is not null && _levelByCourseId.TryGetValue(courseId, out var lv)
            ? lv : null;

        // CommentPresent/CommentText — map the old system's section comment to Notes.
        var commentText = sec["CommentPresent"]?.Value<bool>() == true
            ? sec["CommentText"]?.Value<string>()?.Trim()
            : null;

        var section = new Section
        {
            SemesterId            = semesterId,
            CourseId              = courseId,
            SectionCode           = sectionCode,
            SectionTypeId         = sectionTypeId,
            CampusId              = campusId,
            TagIds                = tagIds,
            ResourceIds           = resourceIds,
            Reserves              = reserves,
            InstructorAssignments = assignments,
            Schedule              = schedule,
            Level                 = level,
            Notes                 = commentText ?? string.Empty
        };

        if (!dryRun)
            _sectionRepo.Insert(section);

        _sectionsInserted++;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an HHMM integer (e.g. 830 = 8:30, 1120 = 11:20) to
    /// minutes from midnight (e.g. 510, 680).
    /// </summary>
    /// <param name="hhmm">Time in HHMM integer format.</param>
    /// <returns>Minutes from midnight.</returns>
    private static int HhmmToMinutes(int hhmm) => (hhmm / 100) * 60 + (hhmm % 100);

    /// <summary>
    /// Extracts the season portion from a full semester name.
    /// For example: "Fall 2025-2026" → "Fall", "Winter2025-2026" → "Winter".
    /// Falls back to returning the full name unchanged if no pattern matches.
    /// </summary>
    /// <param name="semName">Full semester name from the old system.</param>
    /// <param name="yearName">Academic year name, e.g. "2025-2026".</param>
    private static string ExtractSeasonName(string semName, string yearName)
    {
        // Strip the year part if it appears at the end (with optional separator)
        if (semName.EndsWith(yearName, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = semName[..^yearName.Length].TrimEnd(' ', '-', '_');
            if (!string.IsNullOrWhiteSpace(prefix)) return prefix;
        }

        // Fallback: strip trailing YYYY-YYYY via regex
        var m = Regex.Match(semName, @"^(.*?)\s*\d{4}-\d{4}\s*$");
        if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            return m.Groups[1].Value.Trim();

        return semName;
    }

    /// <summary>Appends a standard "N inserted" summary line to the report.</summary>
    /// <param name="sb">Target StringBuilder.</param>
    /// <param name="label">Entity label (e.g. "Rooms").</param>
    /// <param name="inserted">Number inserted this run.</param>
    private static void AppendLine(StringBuilder sb, string label, int inserted)
        => sb.AppendLine($"  {label,-28}  {inserted,4} inserted");

    /// <summary>Resets all counters and lookup caches before a new run.</summary>
    private void ResetState()
    {
        _propInserted = _roomsInserted = _subjectsInserted = _coursesInserted = 0;
        _instructorsInserted = _ayInserted = _semestersInserted = 0;
        _sectionsInserted = _sectionsSkipped = 0;
        _warnings.Clear();

        _tagByName.Clear();         _staffTypeByName.Clear();  _sectionTypeByName.Clear();
        _meetingTypeByName.Clear(); _resourceByName.Clear();   _reserveByName.Clear();
        _campusByName.Clear();      _roomByName.Clear();
        _subjectByAbbr.Clear();     _subjectIdToAbbr.Clear();   _courseByKey.Clear();
        _instructorByOldKey.Clear();
        _ayByName.Clear();          _semesterByKey.Clear();
    }
}
#endif
