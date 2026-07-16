using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Pre-built lookup index that resolves exported display names from a shared schedule CSV
/// against the importing department's local entities. Constructed once per import via
/// <see cref="Build"/>, then queried by <see cref="ImportResolver"/> for each section.
/// </summary>
public class ImportResolutionIndex
{
    private readonly Dictionary<(string LastName, string FirstName), List<Instructor>> _instructors;
    private readonly Dictionary<(string Building, string RoomNumber), Room> _roomsByComposite;
    private readonly Dictionary<string, List<Room>> _roomsByNumberOnly;
    private readonly Dictionary<string, SchedulingEnvironmentValue> _campuses;
    private readonly Dictionary<string, SchedulingEnvironmentValue> _sectionTypes;
    private readonly Dictionary<string, SchedulingEnvironmentValue> _tags;
    private readonly Dictionary<string, SchedulingEnvironmentValue> _meetingTypes;

    private ImportResolutionIndex(
        Dictionary<(string, string), List<Instructor>> instructors,
        Dictionary<(string, string), Room> roomsByComposite,
        Dictionary<string, List<Room>> roomsByNumberOnly,
        Dictionary<string, SchedulingEnvironmentValue> campuses,
        Dictionary<string, SchedulingEnvironmentValue> sectionTypes,
        Dictionary<string, SchedulingEnvironmentValue> tags,
        Dictionary<string, SchedulingEnvironmentValue> meetingTypes)
    {
        _instructors = instructors;
        _roomsByComposite = roomsByComposite;
        _roomsByNumberOnly = roomsByNumberOnly;
        _campuses = campuses;
        _sectionTypes = sectionTypes;
        _tags = tags;
        _meetingTypes = meetingTypes;
    }

    /// <summary>
    /// Normalizes a name for comparison: trims whitespace, collapses internal whitespace
    /// runs to a single space, and lowercases (ordinal). Returns empty string for null input.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var span = value.AsSpan().Trim();
        var chars = new char[span.Length];
        int writePos = 0;
        bool prevWasSpace = false;

        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                if (!prevWasSpace)
                {
                    chars[writePos++] = ' ';
                    prevWasSpace = true;
                }
            }
            else
            {
                chars[writePos++] = char.ToLowerInvariant(span[i]);
                prevWasSpace = false;
            }
        }

        return new string(chars, 0, writePos);
    }

    /// <summary>
    /// Builds the resolution index from the importing department's local entities.
    /// </summary>
    /// <param name="instructors">All local instructors.</param>
    /// <param name="rooms">All local rooms.</param>
    /// <param name="campuses">SchedulingEnvironmentValues of type "campus".</param>
    /// <param name="sectionTypes">SchedulingEnvironmentValues of type "sectionType".</param>
    /// <param name="tags">SchedulingEnvironmentValues of type "tag".</param>
    /// <param name="meetingTypes">SchedulingEnvironmentValues of type "meetingType".</param>
    public static ImportResolutionIndex Build(
        IEnumerable<Instructor> instructors,
        IEnumerable<Room> rooms,
        IEnumerable<Campus> campuses,
        IEnumerable<SchedulingEnvironmentValue> sectionTypes,
        IEnumerable<SchedulingEnvironmentValue> tags,
        IEnumerable<SchedulingEnvironmentValue> meetingTypes)
    {
        var comparer = NormalizedTupleComparer.Instance;

        // Instructors: keyed by normalized (LastName, FirstName), list for same-name collisions
        var instructorIndex = new Dictionary<(string, string), List<Instructor>>(comparer);
        foreach (var inst in instructors)
        {
            var key = (Normalize(inst.LastName), Normalize(inst.FirstName));
            if (!instructorIndex.TryGetValue(key, out var list))
            {
                list = new List<Instructor>();
                instructorIndex[key] = list;
            }
            list.Add(inst);
        }

        // Rooms: composite (Building, RoomNumber) index + RoomNumber-only fallback index
        var roomComposite = new Dictionary<(string, string), Room>(comparer);
        var roomByNumber = new Dictionary<string, List<Room>>(StringComparer.OrdinalIgnoreCase);
        foreach (var room in rooms)
        {
            var compositeKey = (Normalize(room.Building), Normalize(room.RoomNumber));
            // Last-write-wins if duplicate composites exist
            roomComposite[compositeKey] = room;

            var numKey = Normalize(room.RoomNumber);
            if (numKey.Length > 0)
            {
                if (!roomByNumber.TryGetValue(numKey, out var list))
                {
                    list = new List<Room>();
                    roomByNumber[numKey] = list;
                }
                list.Add(room);
            }
        }

        return new ImportResolutionIndex(
            instructorIndex,
            roomComposite,
            roomByNumber,
            BuildSevIndex(campuses),
            BuildSevIndex(sectionTypes),
            BuildSevIndex(tags),
            BuildSevIndex(meetingTypes));
    }

    /// <summary>
    /// Resolves an exported instructor name against local instructors.
    /// Returns the matched instructor, or null if unresolved.
    /// </summary>
    /// <param name="lastName">Exported last name.</param>
    /// <param name="firstName">Exported first name.</param>
    /// <param name="initials">Exported initials for tiebreaking same-name collisions.</param>
    /// <param name="warning">Set when the match is ambiguous (multiple candidates, initials don't disambiguate).</param>
    public Instructor? ResolveInstructor(string? lastName, string? firstName, string? initials, out string? warning)
    {
        warning = null;
        var key = (Normalize(lastName), Normalize(firstName));
        if (key.Item1.Length == 0 && key.Item2.Length == 0)
            return null;

        if (!_instructors.TryGetValue(key, out var candidates))
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        // Multiple candidates — use initials tiebreaker
        var normalizedInitials = Normalize(initials);
        if (normalizedInitials.Length == 0)
        {
            warning = $"Ambiguous instructor: multiple local instructors named \"{lastName}, {firstName}\" and no initials to disambiguate.";
            return null;
        }

        var initialsMatches = candidates
            .Where(c => string.Equals(Normalize(c.Initials), normalizedInitials, StringComparison.Ordinal))
            .ToList();

        if (initialsMatches.Count == 1)
            return initialsMatches[0];

        warning = initialsMatches.Count == 0
            ? $"Ambiguous instructor: multiple local instructors named \"{lastName}, {firstName}\" but none match initials \"{initials}\"."
            : $"Ambiguous instructor: multiple local instructors named \"{lastName}, {firstName}\" with initials \"{initials}\".";
        return null;
    }

    /// <summary>
    /// Resolves an exported room against local rooms using composite (Building, RoomNumber) matching.
    /// Falls back to RoomNumber-only when the exported building is empty.
    /// </summary>
    /// <param name="building">Exported building name (may be null/empty).</param>
    /// <param name="roomNumber">Exported room number.</param>
    /// <param name="warning">Set when the match is ambiguous.</param>
    public Room? ResolveRoom(string? building, string? roomNumber, out string? warning)
    {
        warning = null;
        var normalizedRoom = Normalize(roomNumber);
        if (normalizedRoom.Length == 0)
            return null;

        var normalizedBuilding = Normalize(building);

        // When building is provided, try composite match first
        if (normalizedBuilding.Length > 0)
        {
            if (_roomsByComposite.TryGetValue((normalizedBuilding, normalizedRoom), out var room))
                return room;

            // Composite didn't match — no fallback to number-only when building was specified
            return null;
        }

        // Building is empty — fall back to RoomNumber-only
        if (!_roomsByNumberOnly.TryGetValue(normalizedRoom, out var candidates))
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        warning = $"Ambiguous room: multiple local rooms with number \"{roomNumber}\" in different buildings.";
        return null;
    }

    /// <summary>Resolves an exported campus name against local campuses.</summary>
    public SchedulingEnvironmentValue? ResolveCampus(string? name)
        => ResolveSev(_campuses, name);

    /// <summary>Resolves an exported section type name against local section types.</summary>
    public SchedulingEnvironmentValue? ResolveSectionType(string? name)
        => ResolveSev(_sectionTypes, name);

    /// <summary>Resolves an exported tag name against local tags.</summary>
    public SchedulingEnvironmentValue? ResolveTag(string? name)
        => ResolveSev(_tags, name);

    /// <summary>Resolves an exported meeting type name against local meeting types.</summary>
    public SchedulingEnvironmentValue? ResolveMeetingType(string? name)
        => ResolveSev(_meetingTypes, name);

    /// <summary>
    /// Generic SEV resolution: normalized name lookup in a dictionary.
    /// Returns null if the name is empty or not found.
    /// </summary>
    private static SchedulingEnvironmentValue? ResolveSev(
        Dictionary<string, SchedulingEnvironmentValue> index, string? name)
    {
        var normalized = Normalize(name);
        if (normalized.Length == 0)
            return null;

        return index.TryGetValue(normalized, out var sev) ? sev : null;
    }

    /// <summary>
    /// Builds a normalized-name → SEV dictionary from a collection of SchedulingEnvironmentValues.
    /// Last-write-wins if duplicate normalized names exist.
    /// </summary>
    private static Dictionary<string, SchedulingEnvironmentValue> BuildSevIndex(
        IEnumerable<SchedulingEnvironmentValue> values)
    {
        var dict = new Dictionary<string, SchedulingEnvironmentValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var sev in values)
        {
            var key = Normalize(sev.Name);
            if (key.Length > 0)
                dict[key] = sev;
        }
        return dict;
    }

    /// <summary>
    /// Also accepts Campus entities for the campus index, converting them to SEV-shaped entries.
    /// </summary>
    private static Dictionary<string, SchedulingEnvironmentValue> BuildSevIndex(
        IEnumerable<Campus> campuses)
    {
        var dict = new Dictionary<string, SchedulingEnvironmentValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var campus in campuses)
        {
            var key = Normalize(campus.Name);
            if (key.Length > 0)
                dict[key] = new SchedulingEnvironmentValue { Id = campus.Id, Name = campus.Name };
        }
        return dict;
    }

    /// <summary>
    /// Case-insensitive, whitespace-normalized equality comparer for (string, string) tuple keys.
    /// Uses the pre-normalized keys stored in the dictionary (Normalize is called at build time),
    /// so this comparer uses ordinal comparison on already-lowercased strings.
    /// </summary>
    private sealed class NormalizedTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly NormalizedTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
            && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) obj)
            => HashCode.Combine(
                obj.Item1.GetHashCode(StringComparison.Ordinal),
                obj.Item2.GetHashCode(StringComparison.Ordinal));
    }
}
