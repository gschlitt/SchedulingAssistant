namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Diagnostic ViewModel — the ViewLocator renders its Message property
/// as red text when navigation or resolution fails.
/// </summary>
public class ErrorViewModel(string message) : ViewModelBase
{
    public string Message { get; } = message;
}
