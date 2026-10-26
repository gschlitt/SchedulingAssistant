using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs a test only when the environment variable
/// <c>TERMPOINT_IMPORT_STRESS_KIT</c> names an output folder <em>and</em> the private real-data database exists
/// (<see cref="RoomAvailabilityIntegrationTests.SourceDbPath"/>); otherwise the test is reported as skipped,
/// never failed. The kit writer is opt-in because it writes files (including a copy of private data) outside
/// the repository, to the folder the person chose.
/// </summary>
public sealed class FactRequiresImportStressKitAttribute : FactAttribute
{
    /// <summary>Marks the test skipped unless the variable is set and the real-data database is present.</summary>
    public FactRequiresImportStressKitAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(StressKitWriter.EnvironmentVariable)))
        {
            Skip = "Opt-in: set the environment variable TERMPOINT_IMPORT_STRESS_KIT to an output folder to write the Import Sections hand-test kit there.";
        }
        else if (!File.Exists(RoomAvailabilityIntegrationTests.SourceDbPath))
        {
            Skip = $"Real-data integration DB not found at '{RoomAvailabilityIntegrationTests.SourceDbPath}'. The kit is built from a copy of it.";
        }
    }
}

/// <summary>
/// Writes the hand-test kit for the Import Sections flyout (see <see cref="StressKitWriter"/>) into the folder named
/// by the environment variable <c>TERMPOINT_IMPORT_STRESS_KIT</c>: <c>BIOL-stress.db</c> (a copy of the real-data
/// database), a numbered file for every scenario, and <c>CHECKLIST.md</c>. It is skipped unless the variable is
/// set, so it never runs by accident. After writing, it opens every file with the importer's own reader and
/// checks the reader's verdict against the scenario's expected outcome, so a broken file never reaches the person.
///
/// <para>The folder will hold private data: choose it outside the repository and delete it when the checking is done.</para>
/// </summary>
public sealed class WriteImportStressKit
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test.</summary>
    /// <param name="output">The test output the file list is written to.</param>
    public WriteImportStressKit(ITestOutputHelper output) => _output = output;

    /// <summary>Writes the kit and proves its files open as the checklist says.</summary>
    [FactRequiresImportStressKit]
    public void WriteTheKit_IntoTheFolderNamedByTheEnvironmentVariable()
    {
        var folder = Environment.GetEnvironmentVariable(StressKitWriter.EnvironmentVariable)!;

        var files = StressKitWriter.Write(folder);

        var reader = new ImportFileReader();
        var wrong = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.FileName);
            var length = new FileInfo(path).Length;
            var read = reader.Read(File.ReadAllBytes(path), path);
            _output.WriteLine($"{file.FileName}: {length / 1024.0:F0} KB, {file.Built.Outcome}" +
                              (read.IsSuccess ? $", {read.Sheet!.Rows.Count} rows" : $", file problem: {read.Problem}"));
            if (file.Built.Outcome == StressOutcome.FileProblem)
            {
                if (read.IsSuccess || read.Problem != file.Built.FileProblem)
                    wrong.Add($"{file.FileName}: expected the file problem [{file.Built.FileProblem}] but the reader said [{read.Problem}]");
            }
            else if (!read.IsSuccess)
            {
                wrong.Add($"{file.FileName}: the reader refused it: {read.Problem}");
            }
        }
        Assert.Empty(wrong);

        Assert.True(File.Exists(Path.Combine(folder, StressKitWriter.DatabaseFileName)));
        var checklist = File.ReadAllText(Path.Combine(folder, StressKitWriter.ChecklistFileName));
        foreach (var file in files)
            Assert.Contains(file.FileName, checklist);
        _output.WriteLine($"Kit written: {files.Count} files, the database copy and the checklist ({checklist.Length:N0} characters).");
    }
}
