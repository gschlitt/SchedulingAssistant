using System.Text.RegularExpressions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="PrivacyScrubber"/> (spec item 6, step 6a): the free-text scrubber every error report will go
/// through. Each kind of private detail is pushed through it, and the tests check that nothing of it survives, that it
/// becomes the right tag, and that the words around it are kept. The names registered here are made up (prefix "zqx")
/// so they can't collide with real words; registrations are global for the test run, which is harmless for such names.
/// </summary>
public class PrivacyScrubberTests
{
    // ── Paths ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// .NET's own file-error message with a user folder that contains a space: the whole path goes (no half of
    /// "Jo Smith" is left), the drive letter is kept, and the message around it is untouched.
    /// </summary>
    [Fact]
    public void UserFolderWithSpaces_IsRemovedWhole_KeepingTheDrive()
    {
        var clean = PrivacyScrubber.ScrubText(
            @"Access to the path 'C:\Users\Jo Smith\AppData\Roaming\TermPoint\working\1a2b3c4d_BIOL-TT.db' is denied.");

        Assert.Equal("Access to the path '<drive C: path>' is denied.", clean);
    }

    /// <summary>A UNC path, a mapped drive, forward slashes, a Unix path and a home path each become their shape tag.</summary>
    [Theory]
    [InlineData(@"open \\fileserver\Dept Share\BIOL\BIOL-TT.db failed", "open <unc path>")]
    [InlineData(@"H:\Schedules\BIOL-TT.db", "<drive H: path>")]
    [InlineData(@"h:\Schedules\BIOL-TT.db", "<drive H: path>")]
    [InlineData("C:/Users/jsmith/Documents/x.db", "<drive C: path>")]
    [InlineData("file /Users/jsmith/Library/TermPoint/x.db", "file <unix path>")]
    [InlineData("~/Documents/TermPoint/x.db", "<home path>")]
    public void EachKindOfPath_BecomesItsShapeTag(string text, string expected)
        => Assert.Equal(expected, PrivacyScrubber.ScrubText(text));

    /// <summary>A path ends at a line break or a quote, so the next line and the closing words are kept.</summary>
    [Fact]
    public void APath_EndsAtALineBreakOrAQuote()
    {
        var clean = PrivacyScrubber.ScrubText("first C:\\a b\\c.db\nsecond \"\\\\srv\\s\\d.db\" done");

        Assert.Equal("first <drive C: path>\nsecond \"<unc path>\" done", clean);
    }

    // ── Emails and file names ─────────────────────────────────────────────────

    /// <summary>An email address, alone or in a mailto: link, becomes &lt;email&gt;.</summary>
    [Theory]
    [InlineData("from jane.doe@ufv.ca today", "from <email> today")]
    [InlineData("mailto:jane.doe+tp@dept.example.ca?subject=Workload", "mailto:<email>?subject=Workload")]
    public void EmailAddresses_BecomeTheEmailTag(string text, string expected)
        => Assert.Equal(expected, PrivacyScrubber.ScrubText(text));

    /// <summary>A bare database file name (often the department's name) becomes &lt;file&gt;.</summary>
    [Theory]
    [InlineData("BIOL-TT.db could not be opened", "<file> could not be opened")]
    [InlineData("left behind: MATH_2026-10-14_09-00-00.db-wal", "left behind: <file>")]
    public void BareDatabaseFileNames_BecomeTheFileTag(string text, string expected)
        => Assert.Equal(expected, PrivacyScrubber.ScrubText(text));

    // ── Registered names ──────────────────────────────────────────────────────

    /// <summary>
    /// Registered names are replaced by their labels in any letter case, but only as whole words: a longer word that
    /// merely contains one is left alone.
    /// </summary>
    [Fact]
    public void RegisteredNames_AreReplacedAsWholeWords_InAnyCase()
    {
        PrivacyScrubber.RegisterSensitive("zqxholder", PrivacyScrubber.UserLabel);
        PrivacyScrubber.RegisterSensitive("ZQX-LAB-07", PrivacyScrubber.MachineLabel);

        Assert.Equal("Locked by <user> on <machine>.", PrivacyScrubber.ScrubText("Locked by ZQXHOLDER on zqx-lab-07."));
        Assert.Equal("zqxholderish stays", PrivacyScrubber.ScrubText("zqxholderish stays"));
    }

    /// <summary>A name shorter than three characters is ignored (it would match ordinary words); so is a blank one.</summary>
    [Fact]
    public void NamesUnderThreeCharacters_AreIgnored()
    {
        PrivacyScrubber.RegisterSensitive("zq", PrivacyScrubber.UserLabel);
        PrivacyScrubber.RegisterSensitive("   ", PrivacyScrubber.UserLabel);
        PrivacyScrubber.RegisterSensitive(null, PrivacyScrubber.UserLabel);

        Assert.Equal("zq said zq", PrivacyScrubber.ScrubText("zq said zq"));
    }

    /// <summary>This computer's own user name and machine name are registered from the start.</summary>
    [Fact]
    public void ThisComputersUserAndMachineNames_AreRegisteredFromTheStart()
    {
        if (Environment.UserName.Trim().Length >= PrivacyScrubber.MinimumNameLength)
            Assert.Equal("held by <user>", PrivacyScrubber.ScrubText($"held by {Environment.UserName}"));
        if (Environment.MachineName.Trim().Length >= PrivacyScrubber.MinimumNameLength)
            Assert.Equal("on <machine>", PrivacyScrubber.ScrubText($"on {Environment.MachineName.ToLowerInvariant()}"));
    }

    /// <summary>A name inside a path goes with the path (paths are scrubbed first), leaving no fragment behind.</summary>
    [Fact]
    public void ANameInsideAPath_GoesWithThePath()
    {
        PrivacyScrubber.RegisterSensitive("zqxinpath", PrivacyScrubber.UserLabel);

        Assert.Equal("at <drive C: path>", PrivacyScrubber.ScrubText(@"at C:\Users\zqxinpath\TermPoint\a.db"));
    }

    // ── Everything else ───────────────────────────────────────────────────────

    /// <summary>Ordinary text with nothing private in it comes back unchanged, and null stays null.</summary>
    [Theory]
    [InlineData("SQLite error 11: database disk image is malformed.")]
    [InlineData("offset 3/4 and / or 1/2 are fine")]
    [InlineData("Save failed: CopyToLocation")]
    [InlineData("")]
    public void TextWithNothingPrivate_IsUnchanged(string text)
        => Assert.Equal(text, PrivacyScrubber.ScrubText(text));

    /// <summary>Null in, null out.</summary>
    [Fact]
    public void Null_StaysNull() => Assert.Null(PrivacyScrubber.ScrubText(null));

    /// <summary>A scrub that runs out of time throws, so the caller can refuse to send the text (it never comes back unchecked).</summary>
    [Fact]
    public void ATimeout_Throws_SoNothingUncheckedIsReturned()
    {
        PrivacyScrubber.BeforeScrubForTests = _ => throw new RegexMatchTimeoutException();
        try
        {
            Assert.Throws<RegexMatchTimeoutException>(() => PrivacyScrubber.ScrubText(@"C:\Users\alice\a.db"));
        }
        finally
        {
            PrivacyScrubber.BeforeScrubForTests = null;
        }
    }
}
