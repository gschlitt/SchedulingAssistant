using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Test-side helpers for the per-attempt temporary files beside a database (1.2.4 item 4.1). Tests assert by
/// pattern (<see cref="ShareTempFile.IsLeftoverOf"/>) rather than predict a name, because every save picks a fresh one.
/// </summary>
internal static class ShareTemp
{
    /// <summary>
    /// Every temporary file of <paramref name="db"/> that currently exists in its folder: the per-attempt names and
    /// the legacy <c>D.tmp</c>.
    /// </summary>
    /// <param name="db">Full path of the database.</param>
    /// <returns>The files' full paths (empty when the folder has none).</returns>
    internal static string[] Leftovers(string db)
    {
        var folder = Path.GetDirectoryName(db)!;
        return Directory.GetFiles(folder).Where(f => ShareTempFile.IsLeftoverOf(db, f)).ToArray();
    }

    /// <summary>Asserts that no temporary file of <paramref name="db"/> exists.</summary>
    /// <param name="db">Full path of the database.</param>
    /// <param name="because">What the caller expects, for the failure message.</param>
    internal static void AssertNone(string db, string because = "No temporary file of the save may be left on the share.")
    {
        var left = Leftovers(db);
        Assert.True(left.Length == 0, because + " Found: " + string.Join(", ", left.Select(Path.GetFileName)));
    }

    /// <summary>
    /// The single temporary file of <paramref name="db"/> that exists (fails when there is none or several).
    /// </summary>
    /// <param name="db">Full path of the database.</param>
    /// <returns>Its full path.</returns>
    internal static string Single(string db) => Assert.Single(Leftovers(db));
}

/// <summary>Unit tests for <see cref="ShareTempFile"/>: the name shape, its freshness, and the strict leftover match.</summary>
public class ShareTempFileTests
{
    private static readonly string Db = Path.Combine(Path.GetTempPath(), "Dept", "BIOL-TT.db");

    /// <summary>The name is the target, a dot, eight lowercase hex digits and <c>.tmp</c>, in the same folder.</summary>
    [Fact]
    public void NewPath_HasTheShapeTargetDotEightHexDotTmp()
    {
        var path = ShareTempFile.NewPath(Db);

        Assert.Matches("^" + System.Text.RegularExpressions.Regex.Escape(Db) + @"\.[0-9a-f]{8}\.tmp$", path);
    }

    /// <summary>A fresh value on every call: two saves (or two attempts of one save) never share a name.</summary>
    [Fact]
    public void NewPath_IsFreshOnEveryCall()
    {
        var names = new HashSet<string>(Enumerable.Range(0, 200).Select(_ => ShareTempFile.NewPath(Db)));

        Assert.Equal(200, names.Count);
    }

    /// <summary>Whatever <see cref="ShareTempFile.NewPath"/> makes, <see cref="ShareTempFile.IsLeftoverOf"/> recognises.</summary>
    [Fact]
    public void IsLeftoverOf_RecognisesWhatNewPathMakes()
    {
        Assert.True(ShareTempFile.IsLeftoverOf(Db, ShareTempFile.NewPath(Db)));
        Assert.True(ShareTempFile.IsLeftoverOf(Db, Path.GetFileName(ShareTempFile.NewPath(Db))));
    }

    /// <summary>The 1.2.3 name, <c>D.tmp</c>, is still a leftover (1.2.3 may still be writing it).</summary>
    [Fact]
    public void IsLeftoverOf_RecognisesTheLegacyName()
    {
        Assert.True(ShareTempFile.IsLeftoverOf(Db, Db + ".tmp"));
        Assert.True(ShareTempFile.IsLeftoverOf(Db, "BIOL-TT.db.tmp"));
    }

    /// <summary>Matching ignores case.</summary>
    [Fact]
    public void IsLeftoverOf_IsCaseInsensitive()
    {
        Assert.True(ShareTempFile.IsLeftoverOf(Db, "biol-tt.DB.3F2A9C1E.TMP"));
        Assert.True(ShareTempFile.IsLeftoverOf(Db, "BIOL-TT.DB.TMP"));
    }

    /// <summary>Only the file names are compared: the candidate's folder does not matter.</summary>
    [Fact]
    public void IsLeftoverOf_ComparesFileNamesOnly()
    {
        Assert.True(ShareTempFile.IsLeftoverOf(Db, Path.Combine(Path.GetTempPath(), "elsewhere", "BIOL-TT.db.3f2a9c1e.tmp")));
    }

    /// <summary>Look-alikes are never leftovers: other suffixes, wrong hex length, trailing text, other databases.</summary>
    [Theory]
    [InlineData("BIOL-TT.db")]
    [InlineData("BIOL-TT.db.lock")]
    [InlineData("BIOL-TT.db.lock.tmp")]
    [InlineData("BIOL-TT.db.backup.tmp")]
    [InlineData("BIOL-TT.db.3f2a9c1e.tmp.x")]
    [InlineData("BIOL-TT.db.3f2a9c1.tmp")]      // 7 hex
    [InlineData("BIOL-TT.db.3f2a9c1ea.tmp")]    // 9 hex
    [InlineData("BIOL-TT.db.3f2a9c1g.tmp")]     // not hex
    [InlineData("BIOL-TT.db.3f2a9c1e")]         // no .tmp
    [InlineData("BIOL-TT.db..tmp")]
    [InlineData("BIOL-TT.db.x.3f2a9c1e.tmp")]
    [InlineData("BIOL-TT.db2.3f2a9c1e.tmp")]
    [InlineData("OTHER.db.3f2a9c1e.tmp")]
    [InlineData("BIOL-TT.tmp")]
    [InlineData("")]
    public void IsLeftoverOf_RejectsLookAlikes(string name)
    {
        Assert.False(ShareTempFile.IsLeftoverOf(Db, name));
    }
}
