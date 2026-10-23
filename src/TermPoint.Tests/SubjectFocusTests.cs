using System.Linq;
using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the <see cref="SubjectFocus"/> value, its persisted shape <see cref="SavedFocus"/>, and the
/// <see cref="AppSettings.FocusByDatabase"/> settings property (spec item 20, step 20.1). Pure, no database.
/// </summary>
public sealed class SubjectFocusTests
{
    /// <summary>All and None are distinct values with no subjects.</summary>
    [Fact]
    public void AllAndNone_HaveNoSubjects()
    {
        Assert.Equal(FocusKind.All, SubjectFocus.All.Kind);
        Assert.Equal(FocusKind.None, SubjectFocus.None.Kind);
        Assert.Empty(SubjectFocus.All.SubjectIds);
        Assert.Empty(SubjectFocus.None.SubjectIds);
        Assert.NotEqual(SubjectFocus.All, SubjectFocus.None);
    }

    /// <summary>A set of no subjects (empty, null, or only blank ids) normalises to None.</summary>
    [Fact]
    public void ForSubjects_EmptyNormalisesToNone()
    {
        Assert.Same(SubjectFocus.None, SubjectFocus.ForSubjects([]));
        Assert.Same(SubjectFocus.None, SubjectFocus.ForSubjects(null));
        Assert.Same(SubjectFocus.None, SubjectFocus.ForSubjects(["", "  "]));
    }

    /// <summary>A real set keeps its ids, ignoring duplicates.</summary>
    [Fact]
    public void ForSubjects_KeepsDistinctIds()
    {
        var focus = SubjectFocus.ForSubjects(["a", "b", "a"]);
        Assert.Equal(FocusKind.Subjects, focus.Kind);
        Assert.Equal(2, focus.SubjectIds.Count);
        Assert.True(focus.ContainsSubject("a"));
        Assert.False(focus.ContainsSubject("c"));
    }

    /// <summary>Equality is by kind and set, whatever the order, and the hash agrees.</summary>
    [Fact]
    public void Equality_IsByKindAndSet()
    {
        var x = SubjectFocus.ForSubjects(["a", "b"]);
        var y = SubjectFocus.ForSubjects(["b", "a"]);
        Assert.Equal(x, y);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
        Assert.NotEqual(x, SubjectFocus.ForSubjects(["a"]));
        Assert.NotEqual(x, SubjectFocus.All);
    }

    /// <summary>The persisted shape converts both ways, including through System.Text.Json inside AppSettings.</summary>
    [Fact]
    public void SavedFocus_RoundTripsThroughJson()
    {
        var settings = new AppSettings();
        settings.FocusByDatabase["db1"] = SubjectFocus.ForSubjects(["s2", "s1"]).ToSaved();
        settings.FocusByDatabase["db2"] = SubjectFocus.None.ToSaved();
        settings.FocusByDatabase["db3"] = SubjectFocus.All.ToSaved();

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal(SubjectFocus.ForSubjects(["s1", "s2"]), SubjectFocus.FromSaved(back.FocusByDatabase["db1"]));
        Assert.Equal(SubjectFocus.None, SubjectFocus.FromSaved(back.FocusByDatabase["db2"]));
        Assert.Equal(SubjectFocus.All, SubjectFocus.FromSaved(back.FocusByDatabase["db3"]));
    }

    /// <summary>Saved ids are sorted, so the settings file does not churn.</summary>
    [Fact]
    public void ToSaved_SortsIds()
    {
        var saved = SubjectFocus.ForSubjects(["c", "a", "b"]).ToSaved();
        Assert.Equal(["a", "b", "c"], saved.SubjectIds);
    }

    /// <summary>A Subjects record with no ids is None; an unrecognised kind falls back to All (hides nothing).</summary>
    [Fact]
    public void FromSaved_EdgeCases()
    {
        Assert.Equal(SubjectFocus.None, SubjectFocus.FromSaved(new SavedFocus { Kind = FocusKind.Subjects }));
        Assert.Equal(SubjectFocus.All, SubjectFocus.FromSaved(new SavedFocus { Kind = (FocusKind)99 }));
    }

    /// <summary>A settings file written before the property existed has no key: it loads with an empty dictionary.</summary>
    [Fact]
    public void SettingsFileWithoutTheKey_LoadsWithEmptyDictionary()
    {
        var back = JsonSerializer.Deserialize<AppSettings>("{\"InstallId\":\"x\",\"IncludeSaturday\":true}")!;
        Assert.NotNull(back.FocusByDatabase);
        Assert.Empty(back.FocusByDatabase);
    }
}
