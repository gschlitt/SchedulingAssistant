using System.Threading.Tasks;

namespace TermPoint.Services;

public interface IDialogService
{
    Task<bool> Confirm(string message, string confirmLabel = "Delete");
    Task ShowError(string message);
}
