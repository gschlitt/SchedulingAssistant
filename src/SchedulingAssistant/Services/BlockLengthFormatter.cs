using SchedulingAssistant.Models;
using System.Globalization;

namespace SchedulingAssistant.Services;

/// <summary>
/// Stateless helpers for formatting and parsing block lengths under the current
/// <see cref="BlockLengthUnit"/> preference.  All storage is in hours; these methods
/// handle the conversion to/from the display unit.
/// </summary>
public static class BlockLengthFormatter
{
    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats a block length (in hours) for display in the given unit.
    /// Hours: compact decimal ("1.5", "2"). Minutes: whole integer ("90", "120").
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    /// <param name="unit">Display unit.</param>
    public static string FormatBlockLength(double hours, BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes
            ? ((int)Math.Round(hours * 60)).ToString(CultureInfo.InvariantCulture)
            : hours.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds a human-readable label for a block length card or option.
    /// Hours: "2 hours", "1.5 hours". Minutes: "120 min", "90 min".
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    /// <param name="unit">Display unit.</param>
    public static string LabelFor(double hours, BlockLengthUnit unit)
    {
        if (unit == BlockLengthUnit.Minutes)
            return $"{(int)Math.Round(hours * 60)} min";

        return hours == Math.Floor(hours) ? $"{(int)hours} hours" : $"{hours} hours";
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a block-length string entered by the user and returns the value in hours.
    /// <para>
    /// Hours mode: accepts decimal ("1.5") and H:MM ("1:30").
    /// Minutes mode: accepts whole integers only ("90", "120").
    /// </para>
    /// Returns null if the string cannot be parsed or is non-positive.
    /// </summary>
    /// <param name="text">Raw user input.</param>
    /// <param name="unit">Display unit the user was entering in.</param>
    /// <returns>Block length in hours, or null if invalid.</returns>
    public static double? ParseBlockLength(string text, BlockLengthUnit unit)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (unit == BlockLengthUnit.Minutes)
        {
            // Integer minutes only (e.g. "90")
            if (!int.TryParse(text, out int mins) || mins <= 0)
                return null;
            return mins / 60.0;
        }

        // Hours mode — decimal or H:MM
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int h)
                && int.TryParse(parts[1].Trim(), out int m)
                && h >= 0 && m >= 0 && m < 60)
                return h + m / 60.0;
            return null;
        }

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val)
            && val > 0)
            return val;

        return null;
    }

    // ── NumericUpDown parameters ───────────────────────────────────────────────

    /// <summary>Step increment for the block-length NumericUpDown control.</summary>
    public static decimal NumericIncrement(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? 30m : 0.5m;

    /// <summary>Minimum value for the block-length NumericUpDown control.</summary>
    public static decimal NumericMinimum(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? 30m : 0.5m;

    /// <summary>Maximum value for the block-length NumericUpDown control.</summary>
    public static decimal NumericMaximum(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? 480m : 8m;

    /// <summary>Format string for the block-length NumericUpDown control.</summary>
    public static string NumericFormatString(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? "0" : "0.0";

    // ── UI strings ────────────────────────────────────────────────────────────

    /// <summary>DataGrid column header for the block-length column.</summary>
    public static string BlockLengthColumnHeader(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? "Block Length (min)" : "Block Length (hrs)";

    /// <summary>Watermark / placeholder for the block-length input field.</summary>
    public static string BlockLengthWatermark(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? "min" : "hours";

    /// <summary>Label for the block-length input field.</summary>
    public static string BlockLengthInputLabel(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? "Block Length (minutes)" : "Block Length (hours)";

    /// <summary>
    /// Converts a display-unit value entered in the NumericUpDown back to hours.
    /// </summary>
    /// <param name="displayValue">Value as shown in the NumericUpDown.</param>
    /// <param name="unit">Display unit.</param>
    public static double DisplayToHours(double displayValue, BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? displayValue / 60.0 : displayValue;

    /// <summary>
    /// Converts a stored hours value to the display unit for the NumericUpDown.
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    /// <param name="unit">Display unit.</param>
    public static double HoursToDisplay(double hours, BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? Math.Round(hours * 60) : hours;

    /// <summary>
    /// Builds the error hint for an invalid block-length entry.
    /// </summary>
    public static string ParseErrorHint(BlockLengthUnit unit) =>
        unit == BlockLengthUnit.Minutes ? "Enter whole minutes like 90" : "Enter a duration like 1.5 or 1:30";
}
