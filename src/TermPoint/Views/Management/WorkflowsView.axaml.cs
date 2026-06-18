using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TermPoint.Controls;
using TermPoint.Views;

namespace TermPoint.Views.Management;

public partial class WorkflowsView : UserControl
{
    /// <summary>
    /// Currently-open sticky notes, kept so new notes cascade rather than stack
    /// directly on top of one another. Static because notes outlive the flyout view.
    /// </summary>
    private static readonly List<StickyNoteWindow> _openNotes = new();

    /// <summary>
    /// Closes every open sticky note. Called during app shutdown so no floating note keeps
    /// the process alive after the main window closes. Iterates a snapshot because each
    /// note's <c>Closed</c> handler removes itself from <see cref="_openNotes"/>.
    /// </summary>
    public static void CloseAllNotes()
    {
        foreach (var note in _openNotes.ToArray())
            note.Close();
        _openNotes.Clear();
    }

    public WorkflowsView()
    {
        InitializeComponent();

        // Cards are authored in AXAML; each WorkflowCardChrome bubbles DetachRequested
        // when its pop-out button is clicked.
        AddHandler(WorkflowCardChrome.DetachRequestedEvent, OnDetachRequested);
    }

    /// <summary>
    /// Spawns a floating sticky-note copy of the card whose detach button was clicked.
    /// The card content is plain strings, so we clone via a property copy (no per-card types).
    /// </summary>
    private void OnDetachRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not WorkflowCardChrome card) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var note = new StickyNoteWindow();
        note.SetCard(card.Clone());
        PositionCascade(note, owner);

        note.Closed += (_, _) => _openNotes.Remove(note);
        _openNotes.Add(note);
        note.Show(owner);
    }

    /// <summary>
    /// Offsets each new note diagonally from the owner window so successive notes
    /// don't land exactly on top of each other. Wraps after a handful of steps.
    /// </summary>
    private static void PositionCascade(Window note, Window owner)
    {
        const int step = 28;
        var i = _openNotes.Count % 8;
        var basePos = owner.Position;
        note.WindowStartupLocation = WindowStartupLocation.Manual;
        note.Position = new PixelPoint(basePos.X + 80 + step * i, basePos.Y + 80 + step * i);
    }
}
