using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>The outcome of counting slates.</summary>
/// <param name="Count">How many slates were found (at most the cap). One for an empty set of groups (the empty slate).</param>
/// <param name="Capped">Whether the count stopped at the cap, so the real number may be larger.</param>
/// <param name="UsedSectionIds">
/// Ids of sections that are in at least one slate. Complete even when capped: sections the capped search never
/// reached are each tested by an existence search with the section fixed.
/// </param>
/// <param name="SlatesPerSection">
/// Slates each section was seen in during the counting. Exact when not capped; when capped, a partial tally (and
/// zero for a section found only by the completion step).
/// </param>
public sealed record SlateCountResult(
    int Count,
    bool Capped,
    IReadOnlySet<string> UsedSectionIds,
    IReadOnlyDictionary<string, int> SlatesPerSection);

/// <summary>
/// Searches for slates (rules 16-23): choices of exactly one section from each component group with no two
/// sections that cannot be together. Compatibility is an index-based matrix built once per instance: two sections
/// of different groups are compatible when they have no time conflict (rule 13) and, when the one-campus rule
/// applies, are not in person at different campuses (rule 15). The matrix holds time and campus separately, so
/// <see cref="WithCampusRule"/> switches the rule without rebuilding it. An instance holds no mutable state
/// (each search keeps its scratch data to itself), so it may be shared.
/// </summary>
public sealed class SlateSearch
{
    private readonly IReadOnlyList<ComponentGroup> _groups;
    private readonly Section[] _sections;
    private readonly int[][] _members;
    private readonly int[] _groupOf;
    private readonly bool[][] _timeOk;
    private readonly bool[][] _campusOk;
    private readonly bool _oneCampusRule;
    private readonly Dictionary<string, int> _indexById = new();

    /// <summary>Number of groups searched.</summary>
    public int GroupCount => _groups.Count;

    /// <summary>
    /// Builds the compatibility matrix over every candidate of every group.
    /// </summary>
    /// <param name="groups">The component groups, in the stable order slates are listed in.</param>
    /// <param name="oneCampusRule">Whether in-person sections at different campuses are incompatible (Multi-campus off).</param>
    public SlateSearch(IReadOnlyList<ComponentGroup> groups, bool oneCampusRule)
    {
        _groups = groups;
        _oneCampusRule = oneCampusRule;
        var sections = new List<Section>();
        var groupOf = new List<int>();
        _members = new int[groups.Count][];
        for (int g = 0; g < groups.Count; g++)
        {
            _members[g] = new int[groups[g].Candidates.Count];
            for (int k = 0; k < _members[g].Length; k++)
            {
                _members[g][k] = sections.Count;
                _indexById.TryAdd(groups[g].Candidates[k].Id, sections.Count);
                sections.Add(groups[g].Candidates[k]);
                groupOf.Add(g);
            }
        }
        _sections = sections.ToArray();
        _groupOf = groupOf.ToArray();
        int n = _sections.Length;
        _timeOk = new bool[n][];
        _campusOk = new bool[n][];
        for (int i = 0; i < n; i++)
        {
            _timeOk[i] = new bool[n];
            _campusOk[i] = new bool[n];
            Array.Fill(_timeOk[i], true);
            Array.Fill(_campusOk[i], true);
        }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (_groupOf[i] == _groupOf[j]) continue;
                _timeOk[i][j] = _timeOk[j][i] = !SectionOverlap.Conflicts(_sections[i], _sections[j]);
                _campusOk[i][j] = _campusOk[j][i] = !CampusRule.Splits(_sections[i], _sections[j]);
            }
    }

    /// <summary>Shares another search's matrix with a different campus rule.</summary>
    private SlateSearch(SlateSearch other, bool oneCampusRule)
    {
        _groups = other._groups;
        _sections = other._sections;
        _members = other._members;
        _groupOf = other._groupOf;
        _timeOk = other._timeOk;
        _campusOk = other._campusOk;
        _indexById = other._indexById;
        _oneCampusRule = oneCampusRule;
    }

    /// <summary>The same groups and matrix with the one-campus rule switched on or off.</summary>
    /// <param name="oneCampusRule">Whether in-person sections at different campuses are incompatible.</param>
    /// <returns>A search on the same matrix; this instance is unchanged.</returns>
    public SlateSearch WithCampusRule(bool oneCampusRule) =>
        oneCampusRule == _oneCampusRule ? this : new SlateSearch(this, oneCampusRule);

    /// <summary>
    /// Counts slates up to a cap, smallest group first, recording which sections appear in some slate and how
    /// many slates each is in. When the cap is reached the used set is completed with existence searches (a
    /// capped search may never have reached a usable section).
    /// </summary>
    /// <param name="cap">The most slates to count; the search stops there.</param>
    /// <returns>The count, whether it was capped, the used sections and per-section tallies.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is less than 1.</exception>
    public SlateCountResult Count(int cap) => Count(cap, AllGroups());

    /// <summary>Counts slates for a subset of the groups (see <see cref="Count(int)"/>).</summary>
    /// <param name="cap">The most slates to count.</param>
    /// <param name="groupIndexes">The groups taking part, by index.</param>
    /// <returns>The count result for those groups.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is less than 1.</exception>
    internal SlateCountResult Count(int cap, IReadOnlyList<int> groupIndexes)
    {
        if (cap < 1) throw new ArgumentOutOfRangeException(nameof(cap), "The cap must be at least 1.");
        int count = 0;
        var per = new int[_sections.Length];
        Walk(groupIndexes, -1, -1, -1, pick =>
        {
            count++;
            foreach (int s in pick) per[s]++;
            return count < cap;
        });
        bool capped = count >= cap;
        var used = new HashSet<string>();
        var tally = new Dictionary<string, int>();
        foreach (int g in groupIndexes)
            foreach (int s in _members[g])
            {
                if (per[s] > 0) { used.Add(_sections[s].Id); tally[_sections[s].Id] = per[s]; }
                else if (capped && Exists(groupIndexes, s, -1, -1)) used.Add(_sections[s].Id);
            }
        return new SlateCountResult(count, capped, used, tally);
    }

    /// <summary>Whether at least one slate exists.</summary>
    /// <returns><c>true</c> when the groups can be filled (always, for no groups).</returns>
    public bool Exists() => Exists(AllGroups(), -1, -1, -1);

    /// <summary>Whether at least one slate exists with one conflict between two sections set aside (rule 33).</summary>
    /// <param name="ignoreA">One section of the pair.</param>
    /// <param name="ignoreB">The other section of the pair.</param>
    /// <returns><c>true</c> when a slate exists once the pair's time conflict is ignored (campus still applies).</returns>
    public bool Exists(Section ignoreA, Section ignoreB) =>
        Exists(AllGroups(), -1, IndexOf(ignoreA), IndexOf(ignoreB));

    /// <summary>Whether at least one slate contains the given section (it is "usable").</summary>
    /// <param name="fixedSection">The section that must be in the slate.</param>
    /// <returns><c>true</c> when such a slate exists; <c>false</c> when none does or the section is not a candidate.</returns>
    public bool ExistsWith(Section fixedSection)
    {
        int f = IndexOf(fixedSection);
        return f >= 0 && Exists(AllGroups(), f, -1, -1);
    }

    /// <summary>
    /// Whether at least one slate contains <paramref name="fixedSection"/> once one conflict is set aside: the test
    /// of rule 33 for a stranded section (an existence search with the section fixed, not a full recount).
    /// </summary>
    /// <param name="fixedSection">The section that must be in the slate.</param>
    /// <param name="ignoreA">One section of the ignored pair.</param>
    /// <param name="ignoreB">The other section of the ignored pair.</param>
    /// <returns><c>true</c> when such a slate exists.</returns>
    public bool ExistsWith(Section fixedSection, Section ignoreA, Section ignoreB)
    {
        int f = IndexOf(fixedSection);
        return f >= 0 && Exists(AllGroups(), f, IndexOf(ignoreA), IndexOf(ignoreB));
    }

    /// <summary>Whether a slate exists for only the groups of the given courses (a "partial slate", rule 17).</summary>
    /// <param name="courseIds">The courses taking part.</param>
    /// <returns><c>true</c> when those courses' groups can be filled together.</returns>
    public bool ExistsForCourses(IReadOnlyCollection<string> courseIds) =>
        Exists(GroupIndexesOf(courseIds), -1, -1, -1);

    /// <summary>
    /// Whether every group except <paramref name="skipGroups"/> can be filled together: with those groups' sections
    /// moved somewhere free, would a full slate exist? The test behind the fewest sections to move
    /// (<see cref="MoveSuggestions"/>).
    /// </summary>
    /// <param name="skipGroups">The groups left out, by index.</param>
    /// <returns><c>true</c> when the other groups can be filled.</returns>
    internal bool ExistsWithout(IReadOnlyCollection<int> skipGroups) =>
        Exists(Without(skipGroups), -1, -1, -1);

    /// <summary>
    /// The conflicts whose setting aside would let <paramref name="stranded"/> into a filling of every group except
    /// <paramref name="skipGroups"/>: rule 33's test for a stranded section, for every conflict at once. One search
    /// over fillings that hold the section and break exactly one time conflict (campus never), recording that
    /// conflict; a branch whose conflict is already recorded is cut. Much cheaper than one existence search per
    /// conflict, most of which fail only after trying everything (Greg's GEOE-Y3: 13 stranded sections, 62
    /// conflicts).
    /// </summary>
    /// <param name="stranded">The section that must be in the filling (normally a stranded one).</param>
    /// <param name="skipGroups">The groups left out, by index; empty for full slates.</param>
    /// <returns>The conflicts as order-free id pairs (smaller id first); empty when the section is not a candidate or its group is skipped.</returns>
    internal IReadOnlySet<(string, string)> FreeingConflicts(Section stranded, IReadOnlyCollection<int> skipGroups)
    {
        var found = new HashSet<(int, int)>();
        int f = IndexOf(stranded);
        if (f < 0 || skipGroups.Contains(_groupOf[f])) return new HashSet<(string, string)>();
        int fixedGroup = _groupOf[f];
        int[] order = Without(skipGroups).OrderBy(g => g == fixedGroup ? 0 : _members[g].Length).ToArray();
        var pick = new int[order.Length];

        void Recurse(int depth, int va, int vb)
        {
            if (va >= 0 && found.Contains((va, vb))) return; // this conflict is already known to free the section
            if (depth == order.Length)
            {
                if (va >= 0) found.Add((va, vb));
                return;
            }
            int g = order[depth];
            foreach (int s in _members[g])
            {
                if (g == fixedGroup && s != f) continue;
                int na = va, nb = vb;
                bool ok = true;
                for (int d = 0; d < depth && ok; d++)
                {
                    int p = pick[d];
                    if (_oneCampusRule && !_campusOk[p][s]) ok = false;
                    else if (!_timeOk[p][s])
                    {
                        var pair = p < s ? (p, s) : (s, p);
                        if (na < 0 && !found.Contains(pair)) (na, nb) = pair;
                        else if ((na, nb) != pair) ok = false;
                    }
                }
                if (!ok) continue;
                pick[depth] = s;
                Recurse(depth + 1, na, nb);
            }
        }

        Recurse(0, -1, -1);
        var ids = new HashSet<(string, string)>();
        foreach (var (a, b) in found)
        {
            string x = _sections[a].Id, y = _sections[b].Id;
            ids.Add(string.CompareOrdinal(x, y) < 0 ? (x, y) : (y, x));
        }
        return ids;
    }

    /// <summary>
    /// The smallest sets of groups whose leaving out lets the rest be filled: the most complete partial slates (Greg,
    /// 2026-11-27: with no full slate, stranded, depends on and the cautions are judged against these, so every watch
    /// reports them). Sets of one group first; if none works, every working pair. The same test
    /// <see cref="MoveSuggestions"/> runs, without the sections kept in place: the analysis describes the schedule,
    /// not the session.
    /// </summary>
    /// <param name="maxSize">The largest set to try (1 or 2).</param>
    /// <returns>The sets, each ascending; empty when the full set fills (no set is needed), or no set of at most <paramref name="maxSize"/> groups works.</returns>
    internal IReadOnlyList<int[]> FewestSkipSets(int maxSize)
    {
        int n = _groups.Count;
        var sets = new List<int[]>();
        for (int a = 0; a < n; a++)
            if (ExistsWithout([a])) sets.Add([a]);
        if (sets.Count > 0 || maxSize < 2) return sets;
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (ExistsWithout([a, b])) sets.Add([a, b]);
        return sets;
    }

    /// <summary>Every group index except those in <paramref name="skipGroups"/>, ascending.</summary>
    internal int[] Without(IReadOnlyCollection<int> skipGroups) =>
        AllGroups().Where(g => !skipGroups.Contains(g)).ToArray();

    /// <summary>
    /// The fewest time conflicts <paramref name="mover"/> can have with a filling of every group except
    /// <paramref name="skipGroups"/>: the conflicts that moving it to a free time would clear. The filling's sections
    /// must all fit together, and when the one-campus rule applies each must be on a campus the mover can share
    /// (moving changes a time, never a campus). Branch and bound, smallest group first; it stops as soon as it finds
    /// a filling with <paramref name="floor"/> conflicts, which the caller knows to be the least possible.
    /// </summary>
    /// <param name="mover">The section that would move; its own group must be among <paramref name="skipGroups"/>.</param>
    /// <param name="skipGroups">The groups left out of the filling, by index.</param>
    /// <param name="mustUse">A section the filling must use, or null.</param>
    /// <param name="floor">The fewest conflicts any filling can have (1 when there is no full slate).</param>
    /// <returns>The fewest conflicts; -1 when no such filling exists, or the mover is not a candidate of a skipped group.</returns>
    internal int FewestConflictsOf(Section mover, IReadOnlyCollection<int> skipGroups, Section? mustUse, int floor)
    {
        int m = IndexOf(mover);
        if (m < 0 || !skipGroups.Contains(_groupOf[m])) return -1;
        int f = mustUse is null ? -1 : IndexOf(mustUse);
        if (mustUse is not null && (f < 0 || skipGroups.Contains(_groupOf[f]))) return -1;
        int fixedGroup = f >= 0 ? _groupOf[f] : -1;
        int[] order = AllGroups().Where(g => !skipGroups.Contains(g))
            .OrderBy(g => g == fixedGroup ? 0 : _members[g].Length).ToArray();
        var pick = new int[order.Length];
        int best = int.MaxValue;

        // Returns false to stop the whole search (a filling at the floor was found).
        bool Recurse(int depth, int cost)
        {
            if (cost >= best) return true;
            if (depth == order.Length) { best = cost; return cost > floor; }
            int g = order[depth];
            foreach (int s in _members[g])
            {
                if (g == fixedGroup && s != f) continue;
                if (_oneCampusRule && !_campusOk[m][s]) continue;
                bool ok = true;
                for (int d = 0; d < depth; d++)
                    if (!Compatible(pick[d], s, -1, -1)) { ok = false; break; }
                if (!ok) continue;
                pick[depth] = s;
                if (!Recurse(depth + 1, cost + (_timeOk[m][s] ? 0 : 1))) return false;
            }
            return true;
        }

        Recurse(0, 0);
        return best == int.MaxValue ? -1 : best;
    }

    /// <summary>The indexes of the groups belonging to the given courses.</summary>
    /// <param name="courseIds">The courses.</param>
    /// <returns>Group indexes, ascending.</returns>
    internal int[] GroupIndexesOf(IReadOnlyCollection<string> courseIds) =>
        Enumerable.Range(0, _groups.Count).Where(g => courseIds.Contains(_groups[g].CourseId)).ToArray();

    /// <summary>
    /// Lists slates in the stable order (Q34): groups in the order given (course, then component), sections by
    /// code within a group, so the first slate is the lexicographically first.
    /// </summary>
    /// <param name="cap">The most slates to list.</param>
    /// <returns>Slates, each one section per group in group order; at most <paramref name="cap"/> of them.</returns>
    public IReadOnlyList<IReadOnlyList<Section>> List(int cap)
    {
        var result = new List<IReadOnlyList<Section>>();
        if (cap < 1) return result;
        Walk(AllGroups(), -1, -1, -1, pick =>
        {
            result.Add(pick.Select(i => _sections[i]).ToList());
            return result.Count < cap;
        }, smallestFirst: false);
        return result;
    }

    /// <summary>The index of a candidate section, or -1 when it is not one.</summary>
    private int IndexOf(Section section) => _indexById.TryGetValue(section.Id, out int i) ? i : -1;

    /// <summary>All group indexes in order.</summary>
    private int[] AllGroups() => Enumerable.Range(0, _groups.Count).ToArray();

    /// <summary>Existence search (a walk that stops at the first slate).</summary>
    private bool Exists(IReadOnlyList<int> groupIndexes, int fixedSection, int ignoreA, int ignoreB)
    {
        bool found = false;
        Walk(groupIndexes, fixedSection, ignoreA, ignoreB, _ => { found = true; return false; });
        return found;
    }

    /// <summary>Whether two sections of different groups may share a slate.</summary>
    private bool Compatible(int i, int j, int ignoreA, int ignoreB)
    {
        bool timeOk = _timeOk[i][j]
            || (ignoreA >= 0 && ((i == ignoreA && j == ignoreB) || (i == ignoreB && j == ignoreA)));
        return timeOk && (!_oneCampusRule || _campusOk[i][j]);
    }

    /// <summary>
    /// Depth-first walk over the groups, one section per group, pruning incompatible picks.
    /// </summary>
    /// <param name="groupIndexes">The groups taking part.</param>
    /// <param name="fixedSection">A section index that must be picked (its group has only it), or -1.</param>
    /// <param name="ignoreA">First section of a pair whose time conflict is set aside, or -1.</param>
    /// <param name="ignoreB">Second section of that pair, or -1.</param>
    /// <param name="onSlate">Called with the picked section indexes (in walk order) for each slate; return <c>false</c> to stop.</param>
    /// <param name="smallestFirst">Visit the smallest group first (fast pruning); <c>false</c> keeps the given order.</param>
    private void Walk(IReadOnlyList<int> groupIndexes, int fixedSection, int ignoreA, int ignoreB,
        Func<int[], bool> onSlate, bool smallestFirst = true)
    {
        int fixedGroup = fixedSection >= 0 ? _groupOf[fixedSection] : -1;
        int[] order = smallestFirst
            ? groupIndexes.OrderBy(g => g == fixedGroup ? 1 : _members[g].Length).ToArray()
            : groupIndexes.ToArray();
        var pick = new int[order.Length];

        bool Recurse(int depth)
        {
            if (depth == order.Length) return onSlate(pick);
            int g = order[depth];
            foreach (int s in _members[g])
            {
                if (g == fixedGroup && s != fixedSection) continue;
                bool ok = true;
                for (int d = 0; d < depth; d++)
                    if (!Compatible(pick[d], s, ignoreA, ignoreB)) { ok = false; break; }
                if (!ok) continue;
                pick[depth] = s;
                if (!Recurse(depth + 1)) return false;
            }
            return true;
        }

        Recurse(0);
    }
}
