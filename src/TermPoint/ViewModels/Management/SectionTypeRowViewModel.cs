using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// One row of Scheduling Environment, Section Type, with its Component box (Access Analyzer revision 8f). The box is
/// two-way on <see cref="IsComponent"/>; the list view model does the saving and, when unticking would change
/// courses, shows the in-row ask (<see cref="IsAsking"/>, <see cref="AskText"/>, <see cref="ConfirmUnmarkCommand"/>,
/// <see cref="CancelUnmarkCommand"/>). While the ask shows, the box reads unticked; Cancel ticks it again.
/// </summary>
public sealed partial class SectionTypeRowViewModel : ObservableObject
{
    private readonly SchedulingEnvironmentListViewModel _owner;
    private bool _isComponent;

    /// <summary>The section type this row shows.</summary>
    public SchedulingEnvironmentValue Value { get; }

    /// <summary>The section type's name.</summary>
    public string Name => Value.Name;

    /// <summary>True while the in-row "Stop counting it?" ask is showing.</summary>
    [ObservableProperty] private bool _isAsking;

    /// <summary>The ask's question ("Stop counting Lab as a component? It comes off A, B and C.").</summary>
    [ObservableProperty] private string _askText = string.Empty;

    /// <summary>"Stop counting it": unmarks the type and takes it off every course that lists it.</summary>
    public IRelayCommand ConfirmUnmarkCommand { get; }

    /// <summary>Cancel: the box goes back to ticked and nothing is saved.</summary>
    public IRelayCommand CancelUnmarkCommand { get; }

    /// <summary>Whether the box can be changed now: this computer can save.</summary>
    public bool IsEnabled => _owner.CanChangeComponents;

    /// <summary>Whether the type is a component. Setting it (the view's click) saves through the list view model.</summary>
    public bool IsComponent
    {
        get => _isComponent;
        set
        {
            if (_isComponent == value) return;
            var previous = _isComponent;
            _isComponent = value;
            OnPropertyChanged();
            _owner.OnComponentBoxChanged(this, previous);
        }
    }

    /// <summary>Creates a row.</summary>
    /// <param name="owner">The list view model that saves the change.</param>
    /// <param name="value">The section type.</param>
    /// <param name="isComponent">The box's state: the stored flag, or before the setup the guess.</param>
    internal SectionTypeRowViewModel(SchedulingEnvironmentListViewModel owner, SchedulingEnvironmentValue value, bool isComponent)
    {
        _owner = owner;
        Value = value;
        _isComponent = isComponent;
        ConfirmUnmarkCommand = new RelayCommand(() => _owner.ConfirmUnmark(this));
        CancelUnmarkCommand = new RelayCommand(() => _owner.CancelUnmark(this));
    }

    /// <summary>Sets the box without saving (reverting a refused or cancelled change).</summary>
    /// <param name="value">The state to show.</param>
    internal void SetWithoutSaving(bool value)
    {
        _isComponent = value;
        OnPropertyChanged(nameof(IsComponent));
    }

    /// <summary>Tells the view to re-read <see cref="IsEnabled"/> (the lock changed).</summary>
    internal void RefreshEnabled() => OnPropertyChanged(nameof(IsEnabled));
}
