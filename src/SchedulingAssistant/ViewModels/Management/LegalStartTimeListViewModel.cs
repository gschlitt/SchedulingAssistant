using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents one item in the "Preferred block length" ComboBox.</summary>
public record NullableBlockLengthOption(double? Value, string Label);

public partial class LegalStartTimeListViewModel : ViewModelBase
{
    private readonly LegalStartTimeRepository _repo;

    [ObservableProperty] private ObservableCollection<LegalStartTime> _entries = new();
    [ObservableProperty] private LegalStartTime? _selectedEntry;
    [ObservableProperty] private LegalStartTimeEditViewModel? _editVm;

    // ── Preferred block length ────────────────────────────────────────────────

    /// <summary>Options list for the preferred-block-length ComboBox, rebuilt when entries change.</summary>
    public ObservableCollection<NullableBlockLengthOption> PreferredBlockLengthOptions { get; } = new();

    private NullableBlockLengthOption? _selectedPreferredOption;

    /// <summary>
    /// The currently selected preferred-block-length option.
    /// Writes through immediately to AppSettings on change.
    /// </summary>
    public NullableBlockLengthOption? SelectedPreferredOption
    {
        get => _selectedPreferredOption;
        set
        {
            if (_selectedPreferredOption == value) return;
            _selectedPreferredOption = value;
            OnPropertyChanged();

            var settings = AppSettings.Load();
            settings.PreferredBlockLength = value?.Value;
            settings.Save();
        }
    }

    public LegalStartTimeListViewModel(LegalStartTimeRepository repo)
    {
        _repo = repo;
        Load();
    }

    private void Load()
    {
        Entries = new ObservableCollection<LegalStartTime>(_repo.GetAll());
        RebuildPreferredOptions();
    }

    private void RebuildPreferredOptions()
    {
        var saved = AppSettings.Load().PreferredBlockLength;

        PreferredBlockLengthOptions.Clear();

        var none = new NullableBlockLengthOption(null, "(none)");
        PreferredBlockLengthOptions.Add(none);

        foreach (var entry in Entries)
            PreferredBlockLengthOptions.Add(new NullableBlockLengthOption(entry.BlockLength, $"{entry.BlockLength:0.#} hrs"));

        // Restore selection — match by value, fall back to "(none)"
        _selectedPreferredOption = saved.HasValue
            ? PreferredBlockLengthOptions.FirstOrDefault(o => o.Value.HasValue && Math.Abs(o.Value.Value - saved.Value) < 0.01)
              ?? none
            : none;
        OnPropertyChanged(nameof(SelectedPreferredOption));
    }

    [RelayCommand]
    private void Add()
    {
        var entry = new LegalStartTime();
        EditVm = new LegalStartTimeEditViewModel(entry, isNew: true,
            onSave: e => { _repo.Insert(e); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedEntry is null) return;
        var copy = new LegalStartTime { BlockLength = SelectedEntry.BlockLength, StartTimes = new List<int>(SelectedEntry.StartTimes) };
        EditVm = new LegalStartTimeEditViewModel(copy, isNew: false,
            onSave: e => { _repo.Update(e); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry is null) return;
        _repo.Delete(SelectedEntry.BlockLength);
        Load();
    }
}
