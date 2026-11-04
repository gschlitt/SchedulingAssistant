using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// One row of the Subjects grid: a <see cref="Models.Subject"/> plus its colour from the
/// <see cref="SubjectColorStore"/>. The colour is a personal setting (never part of the database), so nothing here
/// depends on the write lock.
/// <para>
/// <see cref="ColorHex"/> is bound two-way to the chooser's <c>ColorView</c> so the swatch follows the drag live,
/// but nothing is saved while dragging: the chooser flyout's close runs <see cref="CommitCommand"/>, which writes the
/// final colour once (spec P3-2).
/// </para>
/// </summary>
public partial class SubjectRowViewModel : ViewModelBase
{
    /// <summary>The store that holds the colour.</summary>
    private readonly SubjectColorStore _store;

    /// <summary>The wrapped subject (name and abbreviation come from here).</summary>
    public Subject Subject { get; }

    /// <summary>
    /// The colour being shown, <c>#RRGGBB</c>, or empty for none. Bound two-way to the ColorView (through
    /// <c>HexToColorConverter</c>, which drops alpha, so the stored value is always opaque).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColor))]
    [NotifyPropertyChangedFor(nameof(IsTooDark))]
    private string _colorHex = string.Empty;

    /// <summary>True when a colour is set (the swatch shows it; otherwise it shows a dashed outline).</summary>
    public bool HasColor => ColorHex.Length > 0;

    /// <summary>True when black text would be hard to read on the colour (contrast under 7:1). False when empty.</summary>
    public bool IsTooDark => HasColor && ColorMath.IsTooDark(ColorHex);

    /// <summary>
    /// Sample section code shown on the chooser's preview chip, e.g. "BIOL 105 AB1" (just "105 AB1" when the subject
    /// has no abbreviation), so the user can judge how legible text is on the chosen colour.
    /// </summary>
    public string PreviewText => $"{Subject.CalendarAbbreviation} 105 AB1".Trim();

    /// <summary>Wraps a subject and reads its current colour from the store.</summary>
    /// <param name="subject">The subject this row shows.</param>
    /// <param name="store">The colour store.</param>
    public SubjectRowViewModel(Subject subject, SubjectColorStore store)
    {
        Subject = subject;
        _store = store;
        _colorHex = store.GetColor(subject.CalendarAbbreviation) ?? string.Empty;
    }

    /// <summary>
    /// Re-reads the colour from the store. Used when the store changed from elsewhere (Suggest colours, Clear
    /// colours), so an open swatch never shows a stale value.
    /// </summary>
    public void RefreshFromStore() =>
        ColorHex = _store.GetColor(Subject.CalendarAbbreviation) ?? string.Empty;

    /// <summary>
    /// Saves <see cref="ColorHex"/> to the store if it differs from what is stored. Run when the chooser flyout
    /// closes. Does nothing for a subject with no abbreviation (the store keys colours by abbreviation).
    /// </summary>
    [RelayCommand]
    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(Subject.CalendarAbbreviation)) return;
        var stored = _store.GetColor(Subject.CalendarAbbreviation) ?? string.Empty;
        if (string.Equals(stored, ColorHex, StringComparison.OrdinalIgnoreCase)) return;
        _store.SetColor(Subject.CalendarAbbreviation, ColorHex.Length > 0 ? ColorHex : null);
    }

    /// <summary>"No colour": empties <see cref="ColorHex"/> and removes the stored colour straight away.</summary>
    [RelayCommand]
    private void ClearColor()
    {
        ColorHex = string.Empty;
        if (!string.IsNullOrWhiteSpace(Subject.CalendarAbbreviation))
            _store.SetColor(Subject.CalendarAbbreviation, null);
    }
}
