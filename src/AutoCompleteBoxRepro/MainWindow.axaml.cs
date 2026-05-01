using Avalonia.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AutoCompleteBoxRepro;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}

/// <summary>
/// Minimal ViewModel reproducing TermPoint's start-time AutoCompleteBox setup.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Dummy time strings matching TermPoint's AllStartTimeStrings format.</summary>
    public ObservableCollection<string> Times { get; } =
    [
        "0800", "0830", "0900", "0930", "1000", "1030",
        "1100", "1130", "1200", "1230", "1300", "1330",
        "1400", "1430", "1500", "1530", "1600"
    ];

    private string _selectedText = "";
    public string SelectedText
    {
        get => _selectedText;
        set
        {
            if (_selectedText == value) return;
            _selectedText = value;
            OnPropertyChanged();

            // Simulate TermPoint's OnStartTimeTextChanged auto-commit:
            // when the text matches a preset, do work synchronously.
            if (Times.Contains(value))
                OnTimeCommitted(value);
        }
    }

    /// <summary>
    /// Simulates CommitStartTime → OnSelectedStartTimeChanged → RefreshBlockLengths
    /// → auto-fill BlockLengthText → CommitBlockLength cascade.
    /// In TermPoint this triggers a chain of property changes across multiple VMs.
    /// </summary>
    private void OnTimeCommitted(string time)
    {
        System.Diagnostics.Debug.WriteLine($"Time committed: {time}");
    }

    /// <summary>Rows for Tests 3/5 (DataTemplate scenario).</summary>
    public ObservableCollection<RowViewModel> Rows { get; } =
    [
        new("Mon"),
        new("Tue"),
        new("Wed"),
    ];

    /// <summary>Row for Tests 6/7 — with both behaviors + sibling cascade.</summary>
    public ObservableCollection<MeetingRowViewModel> MeetingRows { get; } =
    [
        new("Mon"),
        new("Tue"),
        new("Wed"),
    ];

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Row ViewModel for Tests 3/5.</summary>
public class RowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public ObservableCollection<string> Times { get; } =
    [
        "0800", "0830", "0900", "0930", "1000", "1030",
        "1100", "1130", "1200", "1230", "1300"
    ];

    private string _selectedText = "";
    public string SelectedText
    {
        get => _selectedText;
        set
        {
            if (_selectedText == value) return;
            _selectedText = value;
            OnPropertyChanged();
        }
    }

    public RowViewModel(string label) => Label = label;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Simulates TermPoint's SectionMeetingViewModel — two paired AutoCompleteBoxes
/// where selecting a start time rebuilds the block length list and auto-fills it.
/// </summary>
public class MeetingRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public ObservableCollection<string> Times { get; } =
    [
        "0800", "0830", "0900", "0930", "1000", "1030",
        "1100", "1130", "1200", "1230", "1300"
    ];

    /// <summary>Block lengths available for the current start time (rebuilt on commit).</summary>
    public ObservableCollection<string> BlockLengths { get; } = [];

    // ── Start Time ───────────────────────────────────────────────────────────

    private string _startTimeText = "";
    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (_startTimeText == value) return;
            _startTimeText = value;
            OnPropertyChanged();

            // Simulate OnStartTimeTextChanged auto-commit
            if (Times.Contains(value))
                CommitStartTime();
        }
    }

    public ICommand CommitStartTimeCommand { get; }

    /// <summary>
    /// Simulates TermPoint's CommitStartTime → OnSelectedStartTimeChanged →
    /// RefreshBlockLengths (Clear + repopulate sibling's ItemsSource) →
    /// auto-fill BlockLengthText.
    /// </summary>
    public void CommitStartTime()
    {
        System.Diagnostics.Debug.WriteLine($"[{Label}] CommitStartTime: {StartTimeText}");

        // Simulate RefreshBlockLengths — clears and repopulates the
        // AvailableBlockLengthStrings collection (bound to sibling AutoCompleteBox).
        BlockLengths.Clear();
        BlockLengths.Add("1");
        BlockLengths.Add("1.5");
        BlockLengths.Add("2");
        BlockLengths.Add("3");

        // Simulate auto-fill of preferred block length
        BlockLengthText = "1.5";
    }

    // ── Block Length ──────────────────────────────────────────────────────────

    private string _blockLengthText = "";
    public string BlockLengthText
    {
        get => _blockLengthText;
        set
        {
            if (_blockLengthText == value) return;
            _blockLengthText = value;
            OnPropertyChanged();

            if (BlockLengths.Contains(value))
                CommitBlockLength();
        }
    }

    public ICommand CommitBlockLengthCommand { get; }

    public void CommitBlockLength()
    {
        System.Diagnostics.Debug.WriteLine($"[{Label}] CommitBlockLength: {BlockLengthText}");
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public MeetingRowViewModel(string label)
    {
        Label = label;
        CommitStartTimeCommand = new SimpleCommand(() => CommitStartTime());
        CommitBlockLengthCommand = new SimpleCommand(() => CommitBlockLength());
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Minimal ICommand implementation for the repro.</summary>
public class SimpleCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
