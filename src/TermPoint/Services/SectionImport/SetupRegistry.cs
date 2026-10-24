namespace TermPoint.Services.SectionImport;

/// <summary>
/// The outcome of looking a typed value up in a <see cref="SetupRegistry{T, TRef}"/>: either a
/// reference (to an existing record or a new one), or, when the value matches more than one existing
/// record, the list of those records.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <typeparam name="TRef">The reference type.</typeparam>
/// <param name="Ref">The resolved reference, or null when the value is ambiguous.</param>
/// <param name="Ambiguous">The two or more existing records the value matched; empty when resolved.</param>
internal readonly record struct SetupMatch<T, TRef>(TRef? Ref, IReadOnlyList<T> Ambiguous)
    where T : class
    where TRef : SetupRef<T>;

/// <summary>
/// Matches typed values against one kind of existing record and keeps the new records the import
/// plans for the values that match nothing. It guarantees the "one record per normalised value" rule:
/// the first time an unmatched value is met a new record is created, and every later mention gets the
/// same reference instance. Existing records are likewise wrapped once, so every draft that names
/// one shares a single reference. Internal to the planner.
/// </summary>
/// <typeparam name="T">The record type (for example <c>Room</c>).</typeparam>
/// <typeparam name="TRef">The reference type that wraps a record (<see cref="SetupRef{T}"/> or a subclass).</typeparam>
internal sealed class SetupRegistry<T, TRef>
    where T : class
    where TRef : SetupRef<T>
{
    /// <summary>The existing records, grouped by their normalised key.</summary>
    private readonly Dictionary<string, List<T>> _existingByKey;

    /// <summary>One shared reference per existing record that has been named so far.</summary>
    private readonly Dictionary<T, TRef> _existingRefs = new(ReferenceEqualityComparer.Instance);

    /// <summary>The new references by normalised key.</summary>
    private readonly Dictionary<string, TRef> _newByKey;

    /// <summary>The new references in order of first appearance.</summary>
    private readonly List<TRef> _new = new();

    /// <summary>Keys whose ambiguity has already been reported, so it is reported once.</summary>
    private readonly HashSet<string> _reportedAmbiguities;

    /// <summary>Wraps an existing record in a reference.</summary>
    private readonly Func<T, TRef> _wrapExisting;

    /// <summary>The sort order the next new record receives.</summary>
    private int _nextSortOrder;

    /// <summary>
    /// Creates a registry over the existing records.
    /// </summary>
    /// <param name="existing">The records already in the database.</param>
    /// <param name="keyOf">Gives a record's key (compared with <paramref name="comparer"/>).</param>
    /// <param name="comparer">Compares keys; use <see cref="NameMatch.Comparer"/> or <see cref="NameMatch.CodeComparer"/>.</param>
    /// <param name="wrapExisting">Wraps an existing record in a reference.</param>
    /// <param name="sortOrderOf">
    /// Gives a record's sort order, so new records can be appended after the highest one
    /// (<c>Max + 1</c>, or 0 when there are none); null for kinds with no sort order.
    /// </param>
    public SetupRegistry(
        IEnumerable<T> existing,
        Func<T, string> keyOf,
        StringComparer comparer,
        Func<T, TRef> wrapExisting,
        Func<T, int>? sortOrderOf = null)
    {
        _wrapExisting = wrapExisting;
        _existingByKey = new Dictionary<string, List<T>>(comparer);
        _newByKey = new Dictionary<string, TRef>(comparer);
        _reportedAmbiguities = new HashSet<string>(comparer);

        var highest = -1;
        foreach (var record in existing)
        {
            var key = keyOf(record);
            if (!_existingByKey.TryGetValue(key, out var list))
                _existingByKey[key] = list = new List<T>();
            list.Add(record);

            if (sortOrderOf is not null)
                highest = Math.Max(highest, sortOrderOf(record));
        }
        _nextSortOrder = highest + 1;
    }

    /// <summary>The new references the import will create, in order of first appearance.</summary>
    public IReadOnlyList<TRef> NewRefs => _new;

    /// <summary>The existing records whose key equals <paramref name="key"/>.</summary>
    /// <param name="key">The typed key.</param>
    /// <returns>Zero, one, or several records.</returns>
    public IReadOnlyList<T> ExistingMatches(string key) =>
        _existingByKey.TryGetValue(key, out var list) ? list : Array.Empty<T>();

    /// <summary>The shared reference to an existing record, created on first use.</summary>
    /// <param name="record">The existing record.</param>
    /// <returns>The one reference every draft naming this record shares.</returns>
    public TRef ExistingRef(T record)
    {
        if (!_existingRefs.TryGetValue(record, out var reference))
            _existingRefs[record] = reference = _wrapExisting(record);
        return reference;
    }

    /// <summary>Looks for a new reference already planned for this key.</summary>
    /// <param name="key">The typed key.</param>
    /// <param name="reference">The planned reference when found.</param>
    /// <returns>True when a new record is already planned for the key.</returns>
    public bool TryGetNew(string key, out TRef? reference) => _newByKey.TryGetValue(key, out reference);

    /// <summary>
    /// Plans a new record for a key that matched nothing. The sort order handed to the factory
    /// increments in order of first appearance.
    /// </summary>
    /// <param name="key">The typed key (the first spelling met in the file names the record).</param>
    /// <param name="create">Builds the new reference given the sort order to assign.</param>
    /// <returns>The new reference, also listed in <see cref="NewRefs"/>.</returns>
    public TRef AddNew(string key, Func<int, TRef> create)
    {
        var reference = create(_nextSortOrder++);
        _newByKey[key] = reference;
        _new.Add(reference);
        return reference;
    }

    /// <summary>
    /// Resolves a typed key: one existing match is used; none gives the (shared) new record, planning
    /// it first if this is the first mention; two or more is ambiguous.
    /// </summary>
    /// <param name="key">The typed key.</param>
    /// <param name="create">Builds a new reference given the sort order to assign.</param>
    /// <returns>The reference, or the ambiguous matches.</returns>
    public SetupMatch<T, TRef> Resolve(string key, Func<int, TRef> create)
    {
        var matches = ExistingMatches(key);
        if (matches.Count > 1)
            return new SetupMatch<T, TRef>(null, matches);
        if (matches.Count == 1)
            return new SetupMatch<T, TRef>(ExistingRef(matches[0]), Array.Empty<T>());

        return new SetupMatch<T, TRef>(
            TryGetNew(key, out var planned) ? planned : AddNew(key, create),
            Array.Empty<T>());
    }

    /// <summary>
    /// Records that the ambiguity of a key is being reported. Two records in the database that share a
    /// key is a fault in TermPoint's own data, not in one row of the file, so it is reported once.
    /// </summary>
    /// <param name="key">The ambiguous key.</param>
    /// <returns>True the first time for this key; false afterwards.</returns>
    public bool MarkAmbiguityReported(string key) => _reportedAmbiguities.Add(key);
}
