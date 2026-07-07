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

    public override string StepTitle => "License Agreement";

    /// <summary>
    /// The EULA body, split into paragraphs. Loaded from the embedded Assets/Legal/EULA.md
    /// so the same source text can also be published to the website/payment site as-is.
    /// </summary>
    public IReadOnlyList<string> Paragraphs { get; } = LoadParagraphs();

    private static IReadOnlyList<string> LoadParagraphs()
    {
        using var stream = AssetLoader.Open(EulaAssetUri);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        return text.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
