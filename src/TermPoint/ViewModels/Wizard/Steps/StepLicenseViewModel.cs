using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;

namespace TermPoint.ViewModels.Wizard.Steps;

/// <summary>
/// Wizard step 1 — License Agreement.
/// Read-only; no user input required. The user clicks Next to accept and continue.
/// </summary>
public class StepLicenseViewModel : WizardStepViewModel
{
    private static readonly Uri EulaAssetUri = new("avares://TermPoint/Assets/Legal/EULA.md");

    /// <summary>
    /// Backing field for <see cref="Paragraphs"/>. Null until the EULA is first requested.
    /// </summary>
    private IReadOnlyList<string>? _paragraphs;

    public override string StepTitle => "License Agreement";

    /// <summary>
    /// The EULA body, split into paragraphs. Loaded from the embedded Assets/Legal/EULA.md
    /// so the same source text can also be published to the website/payment site as-is.
    /// <para>
    /// The load is deferred to the first access of this property rather than done in the
    /// constructor. <see cref="AssetLoader"/> requires the Avalonia platform to be running, and
    /// constructing this view model must not depend on that — otherwise the wizard cannot be
    /// built (and therefore cannot be unit-tested) in a headless test host. In the running app
    /// the license view binds to this property when the step is shown, so the EULA is read then.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown on first access if the Avalonia platform (and so the asset loader) is unavailable.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">
    /// Thrown on first access if the embedded EULA asset is missing from the assembly.
    /// </exception>
    public IReadOnlyList<string> Paragraphs => _paragraphs ??= LoadParagraphs();

    /// <summary>
    /// Reads the embedded EULA asset and splits it into trimmed, non-empty paragraphs
    /// (blank-line separated). Called at most once per instance, on first access of
    /// <see cref="Paragraphs"/>.
    /// </summary>
    /// <returns>The EULA text as a list of paragraphs.</returns>
    /// <exception cref="InvalidOperationException">The Avalonia asset loader is unavailable.</exception>
    /// <exception cref="System.IO.FileNotFoundException">The embedded EULA asset does not exist.</exception>
    private static IReadOnlyList<string> LoadParagraphs()
    {
        using var stream = AssetLoader.Open(EulaAssetUri);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        return text.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
