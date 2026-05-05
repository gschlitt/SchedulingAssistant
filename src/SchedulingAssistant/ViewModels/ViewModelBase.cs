using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Inline error message shown in the view when an operation fails or is blocked by a
    /// business rule. Set to a descriptive string on failure; clear to <c>null</c> at the
    /// start of each new operation. Replaces modal error dialogs, which are unavailable in
    /// the WASM browser demo and are heavier than necessary for routine validation feedback.
    /// Views bind a <c>TextBlock</c> to this property, hidden when null via
    /// <c>ObjectConverters.IsNotNull</c>.
    /// </summary>
    [ObservableProperty] private string? _lastErrorMessage;
}
