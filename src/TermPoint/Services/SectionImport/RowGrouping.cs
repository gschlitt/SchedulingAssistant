namespace TermPoint.Services.SectionImport;

/// <summary>
/// Collects the rows (and how many times) something happened under a key, keeping the keys in order
/// of first appearance, so a warning can be written once per key rather than once per row. The rows
/// of each key are distinct and ascending.
/// </summary>
/// <typeparam name="TKey">What the warning is grouped by.</typeparam>
internal sealed class RowGrouping<TKey> where TKey : notnull
{
    /// <summary>One key with its rows and its occurrence count.</summary>
    public sealed class Entry
    {
        private readonly SortedSet<int> _rows = new();

        /// <summary>Creates an entry for a key.</summary>
        /// <param name="key">The key.</param>
        public Entry(TKey key) => Key = key;

        /// <summary>The key.</summary>
        public TKey Key { get; }

        /// <summary>How many times the key was added (a row may count twice, e.g. two meetings on one row).</summary>
        public int Count { get; private set; }

        /// <summary>The distinct rows, ascending.</summary>
        public IReadOnlyList<int> Rows => _rows.ToList();

        /// <summary>Records one occurrence.</summary>
        /// <param name="row">The row it happened on.</param>
        internal void Record(int row)
        {
            _rows.Add(row);
            Count++;
        }
    }

    private readonly Dictionary<TKey, Entry> _byKey;
    private readonly List<Entry> _ordered = new();

    /// <summary>Creates an empty grouping.</summary>
    /// <param name="comparer">How keys are compared; null for the default.</param>
    public RowGrouping(IEqualityComparer<TKey>? comparer = null) => _byKey = new(comparer);

    /// <summary>The entries in order of first appearance of their keys.</summary>
    public IReadOnlyList<Entry> Entries => _ordered;

    /// <summary>Records that something happened under a key on a row.</summary>
    /// <param name="key">The key.</param>
    /// <param name="row">The row number.</param>
    /// <returns>The key's entry.</returns>
    public Entry Add(TKey key, int row)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            entry = new Entry(key);
            _byKey[key] = entry;
            _ordered.Add(entry);
        }
        entry.Record(row);
        return entry;
    }

    /// <summary>Finds the entry of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The entry, or null when nothing was recorded under the key.</returns>
    public Entry? Find(TKey key) => _byKey.TryGetValue(key, out var entry) ? entry : null;
}
