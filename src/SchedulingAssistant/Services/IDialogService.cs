using System.Threading.Tasks;

namespace SchedulingAssistant.Services;

public interface IDialogService
{
    Task<bool> Confirm(string message, string confirmLabel = "Delete");
    Task ShowError(string message);
}
