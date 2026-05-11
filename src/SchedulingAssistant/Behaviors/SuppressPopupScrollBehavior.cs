using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that unconditionally suppresses <see cref="Control.RequestBringIntoViewEvent"/>
/// to prevent ComboBox popup mispositioning in WASM.
///
/// In WASM, when a <see cref="ComboBox"/> inside a <see cref="ListBox"/> is clicked,
/// two <c>RequestBringIntoView</c> events fire in quick succession — one from the
/// <see cref="ListBoxItem"/> and one from the <see cref="ComboBox"/> itself. Each
/// causes the parent <see cref="ScrollViewer"/>'s <c>ScrollContentPresenter</c> to
/// scroll, but the popup position was already calculated from pre-scroll coordinates.
/// The popup then appears at the wrong location ("floating above").
///
/// Because suppression is unconditional, any code that needs programmatic scroll-to-item
/// (e.g. cross-view selection sync) must manipulate the <see cref="ScrollViewer.Offset"/>
/// directly instead of calling <see cref="Control.BringIntoView()"/>.
///
/// The behavior must be placed on a control INSIDE the ScrollViewer (e.g. the content
/// StackPanel), not on the ScrollViewer itself. The <c>ScrollContentPresenter</c> sits
/// between the content and the ScrollViewer in the visual tree; a handler on the
/// ScrollViewer fires too late.
///
/// Known Avalonia bug: GitHub #18203, #19356, #16762.
/// See also WORKAROUNDS.md entry #2.
///
/// Usage in AXAML:
///   <![CDATA[<StackPanel b:SuppressPopupScrollBehavior.IsEnabled="True">]]>
/// </summary>
public static class SuppressPopupScrollBehavior
{
    /// <summary>
    /// Set to <c>True</c> on a control inside a ScrollViewer to suppress all
    /// <c>RequestBringIntoView</c> events before they reach the ScrollContentPresenter.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled", typeof(SuppressPopupScrollBehavior));

    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

    static SuppressPopupScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RemoveHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);

        if (e.NewValue is true)
            c.AddHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
    }

    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}
