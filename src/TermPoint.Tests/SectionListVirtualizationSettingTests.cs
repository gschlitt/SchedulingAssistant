using System.Text.Json;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the opt-in switch that makes the Section List virtualize (spec_version_1.2.3.md,
/// item 23): the persisted setting and its default, the rule that combines it with the platform
/// capability, and the Preferences view model that edits it. The live panel swap itself needs a
/// visual tree and is covered by the scroll-anchor probe (tools/ScrollAnchorProbe, scenario E7).
/// </summary>
public class SectionListVirtualizationSettingTests
{
    // ---- AppSettings ------------------------------------------------------------------------

    /// <summary>Virtualization is opt-in: a fresh settings object has it off.</summary>
    [Fact]
    public void AppSettings_Default_IsOff()
    {
        Assert.False(new AppSettings().VirtualizeSectionList);
    }

    /// <summary>
    /// A settings file written before the property existed (an upgrade from an older TermPoint) must
    /// load with the default, not fail; unknown keys written by a newer version are ignored the same way.
    /// </summary>
    [Fact]
    public void AppSettings_FileWithoutTheProperty_LoadsAsOff()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"IncludeSaturday\":true}");

        Assert.NotNull(settings);
        Assert.True(settings!.IncludeSaturday);
        Assert.False(settings.VirtualizeSectionList);
    }

    /// <summary>The setting survives a serialize / deserialize round trip, which is how it is saved.</summary>
    [Fact]
    public void AppSettings_RoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(new AppSettings { VirtualizeSectionList = true });

        Assert.True(JsonSerializer.Deserialize<AppSettings>(json)!.VirtualizeSectionList);
    }

    // ---- Effective value: setting AND capability --------------------------------------------

    /// <summary>The list virtualizes only when the user asked for it AND the platform can do it.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]   // the browser demo: capability off, so always off
    [InlineData(false, true, false)]   // desktop, user has not opted in
    [InlineData(true, true, true)]
    public void ResolveIsListVirtualized_IsSettingAndCapability(bool setting, bool capability, bool expected)
    {
        Assert.Equal(expected, SectionListViewModel.ResolveIsListVirtualized(setting, capability));
    }

    /// <summary>The desktop build advertises the capability (the browser build compiles it to false).</summary>
    [Fact]
    public void DesktopBuild_SupportsSectionListVirtualization()
    {
        Assert.True(PlatformCapabilities.SupportsSectionListVirtualization);
    }

    // ---- PreferencesViewModel ---------------------------------------------------------------

    /// <summary>The Preferences view model shows the stored value (load).</summary>
    [Fact]
    public void Preferences_LoadsTheStoredValue()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.VirtualizeSectionList = true;

        Assert.True(new PreferencesViewModel().VirtualizeSectionList);

        AppSettings.Current.VirtualizeSectionList = false;

        Assert.False(new PreferencesViewModel().VirtualizeSectionList);
    }

    /// <summary>
    /// Changing the checkbox stores the value, writes it to disk, tells the UI (PropertyChanged) and
    /// tells live views (<see cref="AppSettings.SettingChanged"/>); setting the same value again does none of that.
    /// </summary>
    [Fact]
    public void Preferences_Set_SavesAndNotifies()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.VirtualizeSectionList = false;
        var vm = new PreferencesViewModel();

        var settingChanges = new List<string>();
        var propertyChanges = new List<string?>();
        void OnSetting(string name) => settingChanges.Add(name);
        AppSettings.SettingChanged += OnSetting;
        vm.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        try
        {
            vm.VirtualizeSectionList = true;
            vm.VirtualizeSectionList = true; // same value: no second notification
        }
        finally
        {
            AppSettings.SettingChanged -= OnSetting;
        }

        Assert.True(AppSettings.Current.VirtualizeSectionList);
        Assert.Equal(new[] { nameof(AppSettings.VirtualizeSectionList) }, settingChanges);
        Assert.Equal(new[] { nameof(PreferencesViewModel.VirtualizeSectionList) }, propertyChanges);
        Assert.True(AppSettings.Load().VirtualizeSectionList); // really on disk, not just in memory
    }

    /// <summary>The checkbox's visibility flag mirrors the platform capability.</summary>
    [Fact]
    public void Preferences_Availability_MirrorsPlatformCapability()
    {
        Assert.Equal(PlatformCapabilities.SupportsSectionListVirtualization,
            new PreferencesViewModel().IsSectionListVirtualizationAvailable);
    }
}
