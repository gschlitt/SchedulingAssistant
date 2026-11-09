using System.Collections;
using System.Globalization;
using System.Text;

namespace TermPoint.ViewModels;

/// <summary>
/// Implemented by every inline or flyout editor view model that has an Apply / Save button (spec item 3.1).
/// Before the window closes or the database is switched, the lifecycle asks the open editor, through this contract,
/// whether it holds changes the user has typed but not applied, and carries out the user's answer (apply them,
/// drop them, or stop).
/// </summary>
public interface IUnappliedChanges
{
    /// <summary>
    /// True when the editor's current values differ from the values it had when it was opened. Compared against a
    /// snapshot, never a "something changed" flag: Avalonia re-fires property setters after a list's items load, and
    /// a flag would report a change the user never made. A new ("Add") editor reports a change only when the user
    /// has entered something beyond the defaults.
    /// </summary>
    bool HasUnappliedChanges { get; }

    /// <summary>
    /// Runs exactly what the editor's Apply / Save button runs, validation included.
    /// </summary>
    /// <returns>
    /// True only when the changes were applied (the editor's save completed). False when the editor refused (a
    /// validation error, which the editor shows on its own error line) or the save threw.
    /// </returns>
    Task<bool> TryApplyAsync();

    /// <summary>
    /// Drops the editor's changes by doing what the editor's Cancel button does (closes the editor).
    /// </summary>
    void DiscardUnapplied();
}

/// <summary>
/// Implemented by view models that host editors (the section list, the meeting list, the flyout pages and the
/// containers around them), so the main window can find the one open editor that has unapplied changes without
/// knowing which lists exist. Detached panels share the same view models, so they are covered too.
/// </summary>
public interface IUnappliedChangesHost
{
    /// <summary>
    /// Finds an open editor with unapplied changes among this view model's own editors and the hosts it contains.
    /// </summary>
    /// <returns>The editor, or null when none has unapplied changes.</returns>
    IUnappliedChanges? FindUnappliedChanges();
}

/// <summary>
/// Search helper shared by every <see cref="IUnappliedChangesHost"/>: walks a list of candidate objects (editors,
/// nested hosts, or null) and returns the first editor with unapplied changes.
/// </summary>
public static class UnappliedChangesFinder
{
    /// <summary>
    /// Returns the first candidate that is an editor with unapplied changes, looking inside any candidate that is
    /// itself a host.
    /// </summary>
    /// <param name="candidates">Editors (<see cref="IUnappliedChanges"/>), nested hosts (<see cref="IUnappliedChangesHost"/>) or null entries; null and other types are skipped.</param>
    /// <returns>The first editor reporting <see cref="IUnappliedChanges.HasUnappliedChanges"/>, or null.</returns>
    public static IUnappliedChanges? First(params object?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            switch (candidate)
            {
                case IUnappliedChangesHost host:
                    if (host.FindUnappliedChanges() is { } nested) return nested;
                    break;
                case IUnappliedChanges { HasUnappliedChanges: true } editor:
                    return editor;
            }
        }
        return null;
    }
}

/// <summary>
/// Builds the comparable text an editor uses as its snapshot: the values the editor would save, joined so that two
/// equal sets of values give equal text and any difference gives different text.
/// </summary>
public static class UnappliedChangesKey
{
    /// <summary>
    /// Joins <paramref name="parts"/> into one string. Null becomes a fixed marker, strings stay as they are,
    /// sequences are written element by element (so a list of ids compares by content), and everything else uses its
    /// culture-invariant text.
    /// </summary>
    /// <param name="parts">The values to compare: scalars, strings, or sequences of those.</param>
    /// <returns>The comparable text.</returns>
    public static string Of(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            Append(sb, part);
            sb.Append('\u001F');
        }
        return sb.ToString();
    }

    /// <summary>Appends one value to <paramref name="sb"/>; see <see cref="Of"/> for the rules.</summary>
    /// <param name="sb">The builder being filled.</param>
    /// <param name="value">The value to append.</param>
    private static void Append(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append('\u0000');
                break;
            case string s:
                sb.Append(s);
                break;
            case IEnumerable sequence:
                sb.Append('[');
                foreach (var item in sequence)
                {
                    Append(sb, item);
                    sb.Append('\u001E');
                }
                sb.Append(']');
                break;
            case IFormattable f:
                sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append(value);
                break;
        }
    }
}
