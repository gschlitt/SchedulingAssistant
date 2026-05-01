using System;
using System.Reflection;
using Avalonia;
using HarmonyLib;

namespace SchedulingAssistant;

/// <summary>
/// Runtime patches for Avalonia bugs applied via Harmony.
/// Each patch targets a specific upstream issue and should be removed when fixed.
/// See WORKAROUNDS.md for full context on each patch.
/// </summary>
static class AvaloniaPatches
{
    private static Harmony? _harmony;

    /// <summary>
    /// Applies all active Avalonia patches. Call once at startup before the
    /// Avalonia app builder runs. Logs a warning if any patch fails to bind
    /// (e.g. Avalonia changed the target method signature in a newer version).
    /// </summary>
    public static void Apply()
    {
        _harmony = new Harmony("com.termpoint.avalonia-patches");

        // Avalonia #19892 — AutoCompleteBox dropdown click crash.
        //
        // When a native popup window's PlatformImpl is destroyed while still
        // logically open, VisualExtensions.PointToScreen throws
        // ArgumentException("Control does not belong to visual tree").
        // Multiple independent callers in the PointerReleased event chain
        // hit this same throw site. Rather than patching each caller, we
        // patch PointToScreen itself with a prefix that returns default(PixelPoint)
        // when the visual is detached. Callers interpret (0,0) as "not within
        // bounds," which is correct — the popup is already gone and the
        // selection has already committed.

        var target = typeof(VisualExtensions).GetMethod(
            "PointToScreen",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(Visual), typeof(Point) },
            null);

        if (target == null)
        {
            App.Logger.LogWarning(
                "AvaloniaPatches: VisualExtensions.PointToScreen not found — skipped. "
                + "Avalonia may have changed its API. See WORKAROUNDS.md entry #1.");
            return;
        }

        var prefix = typeof(AvaloniaPatches).GetMethod(
            nameof(PointToScreenPrefix),
            BindingFlags.NonPublic | BindingFlags.Static);

        // Pre-resolve VisualRoot property. If this fails, skip the patch entirely
        // rather than risk the prefix returning default for ALL PointToScreen calls.
        _visualRootProp = typeof(Visual).GetProperty(
            "VisualRoot",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        if (_visualRootProp == null)
        {
            App.Logger.LogWarning(
                "AvaloniaPatches: Visual.VisualRoot property not found — skipped. "
                + "Avalonia may have changed its API. See WORKAROUNDS.md entry #1.");
            return;
        }

        _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

        App.Logger.LogInfo(
            "AvaloniaPatches: Patched VisualExtensions.PointToScreen (Avalonia #19892)");
    }

    /// <summary>
    /// Harmony prefix for #19892.
    /// When the visual is not attached to a visual tree (GetVisualRoot() == null),
    /// sets the return value to default(PixelPoint) and skips the original method,
    /// preventing the ArgumentException from ever being thrown.
    /// </summary>
    private static PropertyInfo? _visualRootProp;

    static bool PointToScreenPrefix(Visual visual, ref PixelPoint __result)
    {
        if (_visualRootProp!.GetValue(visual) == null)
        {
            __result = default;
            return false; // skip original — visual is detached
        }

        return true; // run original normally
    }
}
