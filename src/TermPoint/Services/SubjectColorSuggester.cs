namespace TermPoint.Services;

/// <summary>
/// The "Suggest colours" rule (spec subject-colours, Phase 4): a pure, deterministic function that gives each
/// colourless subject a palette colour, keeping new colours visibly apart from the ones already in use. Same input,
/// same output, on any computer. No Avalonia dependency.
/// </summary>
public static class SubjectColorSuggester
{
    /// <summary>
    /// Smallest OKLab distance a suggested colour must keep from every colour already in use. PROVISIONAL: it is just
    /// below the smallest pairwise distance among the 11 draft palette colours (about 0.0417), chosen so that with
    /// nothing in use the whole palette is taken in order. Retuned on real tiles in task 5;
    /// <c>SubjectPaletteTests</c> fails if a palette change makes this larger than that smallest distance.
    /// </summary>
    public const double DefaultMinimumDistance = 0.04;

    /// <summary>
    /// Suggests a colour for each abbreviation.
    /// <para>
    /// Abbreviations are trimmed, upper-cased, de-duplicated (blank ones dropped) and processed in ordinal
    /// alphabetical order. For each: (1) the first palette colour whose OKLab distance to EVERY colour in use is at
    /// least <paramref name="minimumDistance"/>; it then counts as in use. (2) If none qualifies, the fallback: take
    /// the first palette colour, in palette order, that is not used at all (not equal to any colour in use, or to one
    /// already handed out in this call); if every palette colour is used, cycle: repeat the palette in order, one
    /// step per such fallback (the n-th cycled subject gets palette[n mod palette size], n from 0). A fallback colour
    /// also counts as in use.
    /// </para>
    /// </summary>
    /// <param name="abbreviations">Abbreviations that have no colour (any case or spacing, any order).</param>
    /// <param name="colorsInUse">Colours already used by this database's subjects; unparseable entries are ignored.</param>
    /// <param name="palette">The ordered palette; unparseable entries are ignored.</param>
    /// <param name="minimumDistance">Smallest OKLab distance to keep from every colour in use.</param>
    /// <returns>
    /// Normalised abbreviation to <c>"#RRGGBB"</c>, in alphabetical order. Empty if the palette has no valid colour.
    /// </returns>
    public static IReadOnlyList<KeyValuePair<string, string>> Suggest(
        IEnumerable<string> abbreviations,
        IEnumerable<string> colorsInUse,
        IReadOnlyList<string> palette,
        double minimumDistance = DefaultMinimumDistance)
    {
        var result = new List<KeyValuePair<string, string>>();

        var pal = palette.Select(ColorMath.NormalizeHex).Where(h => h is not null).Select(h => h!).ToList();
        if (pal.Count == 0) return result;

        var names = abbreviations
            .Select(a => a?.Trim().ToUpperInvariant() ?? "")
            .Where(a => a.Length > 0)
            .Distinct()
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        // Normalised hexes in use (for the "not used at all" test) and their parsed form (for distances).
        var inUse = colorsInUse.Select(ColorMath.NormalizeHex).Where(h => h is not null).Select(h => h!).ToList();
        var used = new HashSet<string>(inUse);
        var inUseRgb = inUse.Select(Parse).ToList();
        var paletteRgb = pal.Select(Parse).ToList();

        var cycleIndex = 0; // position in the repeat cycle, advanced only once the palette has no unused colour left

        foreach (var name in names)
        {
            string? chosen = null;

            // Rule step 1: first palette colour far enough from every colour in use.
            for (var i = 0; i < pal.Count && chosen is null; i++)
            {
                var farEnough = true;
                foreach (var other in inUseRgb)
                {
                    if (ColorMath.OkLabDistance(paletteRgb[i], other) < minimumDistance) { farEnough = false; break; }
                }
                if (farEnough) chosen = pal[i];
            }

            // Rule step 2 (fallback): first unused palette colour, else cycle in palette order.
            if (chosen is null)
            {
                chosen = pal.FirstOrDefault(h => !used.Contains(h));
                if (chosen is null)
                {
                    chosen = pal[cycleIndex % pal.Count];
                    cycleIndex++;
                }
            }

            result.Add(new KeyValuePair<string, string>(name, chosen));
            inUseRgb.Add(Parse(chosen));
            used.Add(chosen);
        }

        return result;
    }

    /// <summary>Parses an already-normalised hex string.</summary>
    private static (byte R, byte G, byte B) Parse(string hex)
    {
        ColorMath.TryParseHex(hex, out var rgb);
        return rgb;
    }
}
