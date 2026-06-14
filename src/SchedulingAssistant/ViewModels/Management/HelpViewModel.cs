using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using SchedulingAssistant.Services;
using Avalonia.Controls;

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
        PlatformProcess.OpenLocalFile(path);
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
    /// </summary>
    /// <param name="url">The URL to open.</param>
    private static void OpenInBrowser(string url) => PlatformProcess.OpenUri(url);

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
            Title        = "Navigating TermPoint and Section Editing",
            Description  = "The basics of getting around TermPoint and editing sections",
            HtmlFileName =  "navigating.html",
            VideoUrl  = "https://youtu.be/SZsPk6b7laA",
        },

        

        
        

        new()
        {
            Title        = "Using the Rooming Tool",
            Description  = "Use the room tool to find a room for a section",
            HtmlFileName = "rooming-tool.html",
             VideoUrl  = "https://youtu.be/WhcNJaS62d4",
        },

        new()
        {
            Title  =  "Handling Multiple Users",
            Description = "How TermPoint handles multiple users",
            HtmlFileName = "multi-user.html"
        },

         new()
         {
             Title="Workflows",
             Description="Check out Workflow Guides under the Workflows Tab"
         }



    ];
}
