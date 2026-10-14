using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TermPoint.Services;

/// <summary>
/// The shared "how many times does this injected fault fire" machinery behind the DEBUG-ONLY fault
/// plans (<see cref="CheckoutFaultPlan"/>, <see cref="RestoreFaultPlan"/>): the
/// <c>value[:count[:skip]]</c> text syntax, the thread-safe skip/failure budget, and the one-line
/// description written to the log when a plan is created or fires.
///
/// <para>It knows nothing about WHICH faults exist. A plan parses its own <c>value</c> with
/// <see cref="TryParseSpec"/> (which hands back the value text and a fresh budget), maps the text to its
/// own fault enum, and asks <see cref="TryConsume"/> each time a call reaches the stage the fault
/// belongs to.</para>
///
/// <para><b>Syntax:</b> <c>value[:count[:skip]]</c>, case-insensitive.</para>
/// <list type="bullet">
/// <item><description><c>count</c> — a whole number &gt;= 1, or <c>*</c> for "every call" (only useful to
/// reach <c>skip</c>). Only the next <c>count</c> matching calls fail. Absent means every call.</description></item>
/// <item><description><c>skip</c> — a whole number &gt;= 0: let that many matching calls through FIRST.
/// Absent means 0.</description></item>
/// </list>
/// <para>Anything else — a zero, negative, signed or non-numeric count or skip, extra parts, an empty
/// value — is rejected, so a typo can never break a build that is being used for real work.</para>
///
/// <para>Pure and thread-safe; reads no environment. The environment read stays in each plan's
/// <c>FromEnvironment</c>, which is compiled out of Release builds.</para>
/// </summary>
internal sealed class DebugFaultBudget
{
    /// <summary>Guards <see cref="_remaining"/> and <see cref="_skip"/>; matching calls can overlap on thread-pool continuations.</summary>
    private readonly object _gate = new();

    /// <summary>How many more matching calls will fail; null = unlimited.</summary>
    private int? _remaining;

    /// <summary>How many more matching calls are let through before failures begin.</summary>
    private int _skip;

    /// <summary>Creates a budget. Use <see cref="TryParseSpec"/> for text; the constructor does not validate.</summary>
    /// <param name="count">How many matching calls fail; null for every call.</param>
    /// <param name="skip">How many matching calls to let through first.</param>
    internal DebugFaultBudget(int? count, int skip)
    {
        _remaining = count;
        _skip = skip;
    }

    /// <summary>How many more matching calls will fail (after the skipped ones), or null when the budget is unlimited.</summary>
    public int? Remaining
    {
        get { lock (_gate) return _remaining; }
    }

    /// <summary>How many more matching calls will be let through before failures begin.</summary>
    public int Skip
    {
        get { lock (_gate) return _skip; }
    }

    /// <summary>
    /// A short, human-readable budget for the log line written when the plan is created or fires:
    /// "fails every call…" or "N more call(s) will fail", plus any calls still to be let through. Read
    /// after a call has been consumed, so a counted plan's last firing reads "0 more call(s) will fail".
    /// </summary>
    public string Description
    {
        get
        {
            var (remaining, skip) = (Remaining, Skip);
            var budget = remaining is null
                ? "fails every call until the variable is cleared and TermPoint restarted"
                : $"{remaining} more call(s) will fail";
            return skip > 0 ? $"{skip} call(s) will be let through first, then it {budget}" : budget;
        }
    }

    /// <summary>
    /// Counts one matching call: lets it through while the skip allowance lasts, then spends one unit of
    /// the failure budget.
    /// </summary>
    /// <returns>True when this call must fail; false when it is skipped or a counted budget is used up.</returns>
    public bool TryConsume()
    {
        lock (_gate)
        {
            if (_skip > 0)
            {
                _skip--;
                return false;
            }

            if (_remaining is null)
                return true;
            if (_remaining <= 0)
                return false;

            _remaining--;
            return true;
        }
    }

    /// <summary>
    /// Parses <c>value[:count[:skip]]</c> (see the class remarks). Pure: no environment access.
    /// </summary>
    /// <param name="spec">The raw text, e.g. <c>unreachable</c>, <c>snapshot:1</c> or <c>unreachable:1:1</c>; may be null or empty.</param>
    /// <param name="name">
    /// The trimmed, lower-cased <c>value</c> part, for the caller to map to its own fault enum (empty on
    /// failure). The caller must still reject a value it does not know.
    /// </param>
    /// <param name="budget">The parsed count/skip budget, or null on failure.</param>
    /// <returns>True when the syntax is understood; false for empty text or anything malformed.</returns>
    internal static bool TryParseSpec(string? spec, out string name, [NotNullWhen(true)] out DebugFaultBudget? budget)
    {
        name   = string.Empty;
        budget = null;

        if (string.IsNullOrWhiteSpace(spec))
            return false;

        var parts = spec.Split(':');
        if (parts.Length > 3)
            return false;

        // count: absent or "*" = every call; otherwise a whole number >= 1.
        int? count = null;
        if (parts.Length > 1 && parts[1].Trim() != "*")
        {
            if (!TryParseWholeNumber(parts[1], out var n) || n < 1)
                return false;
            count = n;
        }

        // skip: absent = 0; otherwise a whole number >= 0.
        var skip = 0;
        if (parts.Length > 2 && !TryParseWholeNumber(parts[2], out skip))
            return false;

        name   = parts[0].Trim().ToLowerInvariant();
        budget = new DebugFaultBudget(count, skip);
        return true;
    }

    /// <summary>
    /// Parses digits only — so "+1", "-1", "1.5", "1e3" and an empty string are all rejected — after
    /// trimming surrounding whitespace. Overflow is a failure, not an exception.
    /// </summary>
    /// <param name="text">The token to parse.</param>
    /// <param name="value">The parsed number (0 on failure).</param>
    /// <returns>True when <paramref name="text"/> is a non-negative whole number that fits an int.</returns>
    private static bool TryParseWholeNumber(string text, out int value)
        => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
