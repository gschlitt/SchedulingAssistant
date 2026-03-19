using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SchedulingAssistant.ViewModels.Management;

// ─────────────────────────────────────────────────────────────────────────────
// HelpTopic
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single node in the help documentation tree.
/// A node may be a navigable article (when <see cref="HtmlFileName"/> is set) or
/// a grouping header whose only purpose is to hold <see cref="Children"/>.
/// </summary>
public sealed class HelpTopic
{
    /// <summary>Display title shown in the topic tree and as the article heading.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// One- or two-sentence summary shown in the content pane when the topic is selected.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// File name (not full path) of the HTML file in the <c>Help/</c> output folder,
    /// e.g. <c>"getting-started.html"</c>. Null for category-only nodes.
    /// </summary>
    public string? HtmlFileName { get; init; }

    /// <summary>
    /// Full YouTube URL (e.g. <c>"https://www.youtube.com/watch?v=VIDEO_ID"</c>) shown as a
    /// "Watch Video" button. Null when no video has been recorded yet.
    /// </summary>
    public string? VideoUrl { get; init; }

    /// <summary>
    /// Child topics. Null or empty means this is a leaf node; the tree will not
    /// render an expand/collapse chevron for it.
    /// </summary>
    public IReadOnlyList<HelpTopic>? Children { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// HelpViewModel
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Help flyout. Owns the topic tree, tracks the selected topic,
/// and exposes commands that open the article HTML or a YouTube video in the
/// system's default browser — no embedded WebView required.
/// </summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>Currently selected topic node (may be a category or a leaf).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArticle))]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    [NotifyCanExecuteChangedFor(nameof(OpenArticleCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenVideoCommand))]
    private HelpTopic? _selectedTopic;

    // ── Computed properties ────────────────────────────────────────────────

    /// <summary>True when the selected topic has an associated HTML file on disk.</summary>
    public bool HasArticle =>
        _selectedTopic?.HtmlFileName is string fileName &&
        File.Exists(Path.Combine(HelpDirectory, fileName));

    /// <summary>True when the selected topic has a YouTube video URL.</summary>
    public bool HasVideo =>
        _selectedTopic?.VideoUrl is { Length: > 0 };

    // ── Topic tree ─────────────────────────────────────────────────────────

    /// <summary>Root-level topic nodes for the tree view.</summary>
    public IReadOnlyList<HelpTopic> Topics { get; } = BuildTopicTree();

    // ── Constructor ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the ViewModel and pre-selects the Welcome topic so the
    /// content pane is not blank when the flyout first opens.
    /// </summary>
    public HelpViewModel()
    {
        SelectedTopic = Topics[0];
    }

    // ── Commands ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the selected topic's HTML file in the system default browser.
    /// The file lives at <c>&lt;OutputDir&gt;/Help/&lt;HtmlFileName&gt;</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no topic is selected or the topic has no HTML file.
    /// </exception>
    [RelayCommand(CanExecute = nameof(HasArticle))]
    private void OpenArticle()
    {
        if (_selectedTopic?.HtmlFileName is not string fileName) return;
        var path = Path.Combine(HelpDirectory, fileName);
        OpenInBrowser(new Uri(path).AbsoluteUri);
    }

    /// <summary>
    /// Opens the selected topic's YouTube video URL in the system default browser.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasVideo))]
    private void OpenVideo()
    {
        if (_selectedTopic?.VideoUrl is not string url) return;
        OpenInBrowser(url);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute path to the Help output folder that contains the HTML files.
    /// </summary>
    private static string HelpDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Help");

    /// <summary>
    /// Launches a URL in the platform's default browser.
    /// Works on Windows, macOS, and Linux.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    private static void OpenInBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    // ── Topic tree definition ──────────────────────────────────────────────

    /// <summary>
    /// Builds the complete topic tree. Add, remove, or re-order entries here
    /// as the help documentation grows. To attach a video, set <see cref="HelpTopic.VideoUrl"/>
    /// to the full <c>https://www.youtube.com/watch?v=…</c> URL.
    /// </summary>
    private static IReadOnlyList<HelpTopic> BuildTopicTree() =>
    [
        new()
        {
            Title       = "Welcome",
            Description = "An overview of TermPoint and what it can do.",
            HtmlFileName = "welcome.html",
            // VideoUrl = "https://www.youtube.com/watch?v=VIDEO_ID",
        },

        new()
        {
            Title        = "Getting Started",
            Description  = "How to launch TermPoint, open or create a database, and navigate the three-panel layout.",
            HtmlFileName = "getting-started.html",
            // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
        },

        new()
        {
            Title        = "The Schedule Grid",
            Description  = "How sections are displayed on the weekly grid, including co-scheduled sections, overlaps, and multi-semester colouring.",
            HtmlFileName = "schedule-grid.html",
            Children     =
            [
                new()
                {
                    Title        = "Reading the Grid",
                    Description  = "Anatomy of a tile, selecting sections, and the right-click context menu.",
                    HtmlFileName = "schedule-grid.html",
                    // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
                },
                new()
                {
                    Title        = "Using Filters",
                    Description  = "Filter the grid by instructor, room, subject, campus, section type, tag, meeting type, or level — with AND/OR logic.",
                    HtmlFileName = "schedule-grid-filters.html",
                    // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
                },
            ]
        },

        new()
        {
            Title        = "Managing Sections",
            Description  = "Adding, editing, and copying sections using the inline step-gate editor.",
            HtmlFileName = "managing-sections.html",
            // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
        },

        new()
        {
            Title        = "Managing Instructors",
            Description  = "Instructor records, standing commitments, the workload panel, and the Workload Mailer.",
            HtmlFileName = "managing-instructors.html",
            // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
        },

        new()
        {
            Title        = "Exporting",
            Description  = "Exporting the schedule as a PNG image and generating workload reports and emails.",
            HtmlFileName = "exporting.html",
            // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
        },

        new()
        {
            Title       = "Workflows",
            Description = "Step-by-step guides for common scheduling tasks.",
            Children    =
            [
                new()
                {
                    Title        = "Make Workload Easy",
                    Description  = "Use the Unstaffed filter and instructor overlays together to assign sections to instructors quickly and without conflicts.",
                    HtmlFileName = "workload-assignment.html",
                    // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
                },
                new()
                {
                    Title        = "Section Editing Tips",
                    Description  = "How copied sections get auto-incremented codes, and how block patterns propagate meeting times across all days in one step.",
                    HtmlFileName = "section-editing-tips.html",
                    // VideoUrl  = "https://www.youtube.com/watch?v=VIDEO_ID",
                },
            ]
        },
    ];
}
