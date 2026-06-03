using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Views;

/// <summary>
/// A borderless, floating "sticky note" that hosts a detached workflow card
/// (a <see cref="SchedulingAssistant.Controls.WorkflowCardChrome"/> clone). The card
/// draws its own colored body; this window adds only the pin (topmost) + close controls,
/// drag-to-move, transparency, and always-on-top behavior. Each note is an independent
/// copy — closing it simply disposes the window.
/// </summary>
public partial class StickyNoteWindow : Window
{
    public StickyNoteWindow()
    {
        InitializeComponent();
    }

    /// <summary>Hosts the detached card control in the note.</summary>
    /// <param name="card">The cloned card to display.</param>
    public void SetCard(Control card)
    {
        Host.Content = card;
    }

    /// <summary>Drag the note by clicking any empty area of its surface.</summary>
    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    /// <summary>Close (dismiss) the note.</summary>
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Host.Content = null;
        base.OnClosed(e);
    }
}
