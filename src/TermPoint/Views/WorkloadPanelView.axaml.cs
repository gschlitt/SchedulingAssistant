using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TermPoint.ViewModels;

namespace TermPoint.Views;

public partial class WorkloadPanelView : UserControl
{
    public WorkloadPanelView()
    {
        InitializeComponent();

        // Intercept Ctrl+Click on chips before the Button's command fires.
        // The tunnel phase runs outer → inner, so handling here suppresses
        // the Button's PointerReleased → Command execution.
        AddHandler(
            InputElement.PointerPressedEvent,
            OnChipCtrlClick,
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Tunnel-phase PointerPressed handler. When Ctrl is held on a left-click over a
    /// chip Button, calls <see cref="WorkloadPanelViewModel.HandleItemCtrlClickCommand"/>
    /// and marks the event handled so the Button's normal command does not also fire.
    /// </summary>
    private void OnChipCtrlClick(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;

        // Walk up the visual tree to find the chip Button.
        Visual? visual = e.Source as Visual;
        while (visual is not null && visual is not Button)
            visual = visual.GetVisualParent();

        if (visual is Button btn
            && btn.Classes.Contains("chip")
            && btn.CommandParameter is WorkloadItemViewModel item
            && DataContext is WorkloadPanelViewModel vm)
        {
            vm.HandleItemCtrlClickCommand.Execute(item);
            e.Handled = true;
        }
    }
}
