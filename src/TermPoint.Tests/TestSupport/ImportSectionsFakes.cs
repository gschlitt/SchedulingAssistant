using System.Text;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// A stand-in for the main window as the Import Sections flyout sees it: write access, how many semesters are
/// shown, whether a section is open for editing, the target semester, and a reload counter. Changing any of the
/// three facts through <see cref="Change"/> tells the watchers, as the real host does.
/// </summary>
public sealed class FakeImportSectionsHost : IImportSectionsHost
{
    private readonly List<Action> _watchers = new();

    /// <summary>The semester the host reports; null when none or several are shown.</summary>
    public ImportTarget? CurrentTarget { get; set; }

    /// <inheritdoc />
    public bool IsWriteEnabled { get; private set; } = true;

    /// <inheritdoc />
    public int ShownSemesterCount { get; private set; } = 1;

    /// <inheritdoc />
    public bool IsEditingSection { get; private set; }

    /// <inheritdoc />
    public ImportTarget? Target => CurrentTarget;

    /// <summary>How many times <see cref="ReloadSections"/> was called.</summary>
    public int ReloadCount { get; private set; }

    /// <summary>When set, the reload throws it (after the import has been written).</summary>
    public Exception? ReloadThrows { get; set; }

    /// <summary>How many watchers are listening now.</summary>
    public int WatcherCount => _watchers.Count;

    /// <summary>Creates a host showing one semester.</summary>
    /// <param name="target">The shown semester.</param>
    public FakeImportSectionsHost(ImportTarget target) => CurrentTarget = target;

    /// <summary>Changes any of the three facts and tells every watcher once.</summary>
    /// <param name="isWriteEnabled">New write access, or null to keep it.</param>
    /// <param name="shownSemesterCount">New shown count, or null to keep it. The target becomes null unless it is exactly 1.</param>
    /// <param name="isEditingSection">New editing state, or null to keep it.</param>
    public void Change(bool? isWriteEnabled = null, int? shownSemesterCount = null, bool? isEditingSection = null)
    {
        if (isWriteEnabled is not null) IsWriteEnabled = isWriteEnabled.Value;
        if (isEditingSection is not null) IsEditingSection = isEditingSection.Value;
        if (shownSemesterCount is not null)
        {
            ShownSemesterCount = shownSemesterCount.Value;
            if (ShownSemesterCount != 1) CurrentTarget = null;
        }
        foreach (var watcher in _watchers.ToList()) watcher();
    }

    /// <summary>
    /// Changes the facts WITHOUT telling the watchers, to prove the view model re-reads them itself at the
    /// moment it matters (the button's state can be stale).
    /// </summary>
    /// <param name="isWriteEnabled">New write access, or null to keep it.</param>
    /// <param name="shownSemesterCount">New shown count, or null to keep it.</param>
    /// <param name="isEditingSection">New editing state, or null to keep it.</param>
    /// <param name="target">A different shown semester, or null to keep it.</param>
    public void SetStateWithoutNotifying(bool? isWriteEnabled = null, int? shownSemesterCount = null,
        bool? isEditingSection = null, ImportTarget? target = null)
    {
        if (isWriteEnabled is not null) IsWriteEnabled = isWriteEnabled.Value;
        if (shownSemesterCount is not null) ShownSemesterCount = shownSemesterCount.Value;
        if (isEditingSection is not null) IsEditingSection = isEditingSection.Value;
        if (target is not null) CurrentTarget = target;
    }

    /// <summary>Shows a different single semester and tells the watchers.</summary>
    /// <param name="target">The newly shown semester.</param>
    public void ShowOtherSemester(ImportTarget target)
    {
        CurrentTarget = target;
        ShownSemesterCount = 1;
        foreach (var watcher in _watchers.ToList()) watcher();
    }

    /// <inheritdoc />
    public void ReloadSections()
    {
        ReloadCount++;
        if (ReloadThrows is not null) throw ReloadThrows;
    }

    /// <inheritdoc />
    public IDisposable WatchChanges(Action onChanged)
    {
        _watchers.Add(onChanged);
        return new Handle(() => _watchers.Remove(onChanged));
    }

    /// <summary>Runs an action when disposed.</summary>
    private sealed class Handle : IDisposable
    {
        private readonly Action _onDispose;

        /// <summary>Creates the handle.</summary>
        /// <param name="onDispose">What to run.</param>
        public Handle(Action onDispose) => _onDispose = onDispose;

        /// <summary>Runs the action.</summary>
        public void Dispose() => _onDispose();
    }
}

/// <summary>
/// A stand-in for the pickers, the clipboard and the bundled template: the paths the "user" will choose,
/// whether the clipboard works, and what was put on it.
/// </summary>
public sealed class FakeImportSectionsPlatform : IImportSectionsPlatform
{
    /// <summary>The template workbook's file name in the test output folder (copied there by the test project).</summary>
    public const string TemplateFileName = "TermPointSectionsTemplate.xlsx";

    /// <summary>What "Choose file…" returns; null means the user cancelled.</summary>
    public string? PathToOpen { get; set; }

    /// <summary>What "Save template…" returns; null means the user cancelled.</summary>
    public string? PathToSave { get; set; }

    /// <summary>The file name the Save dialog was offered, or null if it has not been opened.</summary>
    public string? SuggestedFileName { get; private set; }

    /// <summary>Whether the clipboard accepts text.</summary>
    public bool ClipboardWorks { get; set; } = true;

    /// <summary>The last text put on the clipboard, or null.</summary>
    public string? ClipboardText { get; private set; }

    /// <summary>When set, reading the template throws it.</summary>
    public Exception? TemplateReadThrows { get; set; }

    /// <summary>The template's bytes, read from the test output folder (the copy of the shipped asset).</summary>
    public static byte[] ShippedTemplateBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", TemplateFileName));

    /// <inheritdoc />
    public Task<string?> PickFileToOpenAsync() => Task.FromResult(PathToOpen);

    /// <inheritdoc />
    public Task<string?> PickFileToSaveAsync(string suggestedFileName)
    {
        SuggestedFileName = suggestedFileName;
        return Task.FromResult(PathToSave);
    }

    /// <inheritdoc />
    public Task<bool> SetClipboardTextAsync(string text)
    {
        if (!ClipboardWorks) return Task.FromResult(false);
        ClipboardText = text;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public byte[] ReadTemplate() => TemplateReadThrows is null ? ShippedTemplateBytes() : throw TemplateReadThrows;
}

/// <summary>An <see cref="IAppLogger"/> that remembers what was logged, so tests can see errors without a banner or a re-throw.</summary>
public sealed class RecordingImportLogger : IAppLogger
{
    /// <summary>The exception of every <see cref="LogError"/> call.</summary>
    public List<Exception?> Errors { get; } = new();

    /// <summary>The text of every info or warning line.</summary>
    public List<string> Lines { get; } = new();

    /// <inheritdoc />
    public bool ThrowOnError { get; set; }

    /// <inheritdoc />
    public event EventHandler<string>? ErrorLogged { add { } remove { } }

    /// <inheritdoc />
    public void LogError(Exception? ex, string? context = null, bool unhandled = false) => Errors.Add(ex);

    /// <inheritdoc />
    public void LogWarning(string message, string? context = null) => Lines.Add(message);

    /// <inheritdoc />
    public void LogInfo(string message, string? context = null) => Lines.Add(message);

    /// <inheritdoc />
    public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
}

/// <summary>Writes import sheets as CSV files for the tests that choose a real file.</summary>
public static class ImportCsv
{
    /// <summary>Turns a sheet into CSV text: the template header, then one line per row, with quoting.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The CSV text.</returns>
    public static string ToCsv(ImportSheet sheet)
    {
        static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", C.All.Select(c => Field(c.Name))));
        foreach (var row in sheet.Rows)
            sb.AppendLine(string.Join(",", C.All.Select(c => Field(row.Cells[c.Name]))));
        return sb.ToString();
    }

    /// <summary>Writes the sheet as a CSV file.</summary>
    /// <param name="folder">The folder to write in.</param>
    /// <param name="name">The file name.</param>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The file's full path.</returns>
    public static string Write(string folder, string name, ImportSheet sheet)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, ToCsv(sheet), new UTF8Encoding(false));
        return path;
    }
}
