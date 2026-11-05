using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// One "Show MWF" line of the day-header menu: the text and the command that applies the pattern.
/// </summary>
public sealed class DayPatternItemViewModel
{
    /// <summary>Creates a menu line.</summary>
    /// <param name="text">The line's text, e.g. "Show MWF".</param>
    /// <param name="days">The pattern's day numbers (1 = Monday ...).</param>
    /// <param name="choose">Called with <paramref name="days"/> when the line is clicked.</param>
    public DayPatternItemViewModel(string text, IReadOnlyList<int> days, Action<IReadOnlyList<int>> choose)
    {
        Text = text;
        Days = days;
        ChooseCommand = new RelayCommand(() => choose(days));
    }

    /// <summary>The line's text, e.g. "Show MWF".</summary>
    public string Text { get; }

    /// <summary>The pattern's day numbers (1 = Monday ...).</summary>
    public IReadOnlyList<int> Days { get; }

    /// <summary>Applies the pattern (<see cref="ScheduleGridViewModel.ShowPattern"/>) and closes the menu.</summary>
    public IRelayCommand ChooseCommand { get; }
}

/// <summary>
/// State of the right-click menu on a day header or a folded strip (collapse days, <c>designs/collapse-days.md</c>
/// Phase 3): "Show only Tuesday", one "Show &lt;pattern&gt;" per block pattern, and "Show all days". The view prepares
/// it with <see cref="Prepare"/> and then sets <see cref="IsOpen"/>, exactly as it does for the tile menu.
/// <para>
/// The block patterns are read from the repository each time the menu opens, so a pattern edited in Settings shows
/// next time with no event.
/// </para>
/// </summary>
public partial class DayHeaderMenuViewModel : ObservableObject
{
    private readonly ScheduleGridViewModel _grid;
    private readonly IBlockPatternRepository _patterns;

    /// <summary>The day the menu was opened on (1 = Monday ...).</summary>
    private int _day;

    /// <summary>True while the popup is open; two-way bound to the popup's <c>IsOpen</c>.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>Text of the "Show only" line, e.g. "Show only Tuesday".</summary>
    [ObservableProperty] private string _showOnlyText = string.Empty;

    /// <summary>False when the day is the only open day (there is nothing to do), so the line is absent (P2-5).</summary>
    [ObservableProperty] private bool _showOnlyVisible;

    /// <summary>True only while something is folded, so "Show all days" has something to do.</summary>
    [ObservableProperty] private bool _showAllVisible;

    /// <summary>The "Show &lt;pattern&gt;" lines: patterns with at least one shown day, in the repository's order.</summary>
    public ObservableCollection<DayPatternItemViewModel> Patterns { get; } = [];

    /// <summary>Creates the menu.</summary>
    /// <param name="grid">The Schedule view model whose fold commands the lines call.</param>
    /// <param name="patterns">Where the block patterns come from.</param>
    public DayHeaderMenuViewModel(ScheduleGridViewModel grid, IBlockPatternRepository patterns)
    {
        _grid = grid;
        _patterns = patterns;
    }

    /// <summary>
    /// Fills the menu for a right-click on a header or strip. Call before setting <see cref="IsOpen"/>.
    /// </summary>
    /// <param name="day">The clicked day, 1 = Monday ... 7 = Sunday.</param>
    /// <param name="dayName">The day's full name for the text, e.g. "Tuesday".</param>
    /// <returns>False when the menu would have no lines (the caller then does not open it).</returns>
    public bool Prepare(int day, string dayName)
    {
        _day = day;
        var shown = _grid.ShownDayNumbers;
        var open = shown.Where(d => !_grid.FoldedDays.Contains(d)).ToList();

        ShowOnlyText = $"Show only {dayName}";
        ShowOnlyVisible = shown.Contains(day) && !(open.Count == 1 && open[0] == day);
        ShowAllVisible = _grid.FoldedDays.Count > 0;

        Patterns.Clear();
        foreach (var item in BuildPatternItems(ReadPatterns(), shown, _grid.ShowPattern, () => IsOpen = false))
            Patterns.Add(item);

        return ShowOnlyVisible || ShowAllVisible || Patterns.Count > 0;
    }

    /// <summary>
    /// Builds the pattern lines: a pattern is listed only when it has a name and at least one of its days is shown
    /// (a Saturday-only pattern with Saturday off would do nothing). Pure, for the tests.
    /// </summary>
    /// <param name="patterns">The block patterns, in the order to list them.</param>
    /// <param name="shownDays">The day numbers the grid currently shows.</param>
    /// <param name="apply">Applies a pattern's days to the grid.</param>
    /// <param name="afterChoose">Runs after a line is chosen (closes the menu).</param>
    /// <returns>One line per listed pattern.</returns>
    public static List<DayPatternItemViewModel> BuildPatternItems(
        IEnumerable<BlockPattern> patterns, IReadOnlyCollection<int> shownDays,
        Action<IReadOnlyList<int>> apply, Action afterChoose)
    {
        var items = new List<DayPatternItemViewModel>();
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || !p.Days.Any(shownDays.Contains)) continue;
            items.Add(new DayPatternItemViewModel($"Show {p.Name.Trim()}", p.Days.ToList(), days =>
            {
                apply(days);
                afterChoose();
            }));
        }
        return items;
    }

    /// <summary>
    /// Reads the block patterns. A failed read leaves the menu without pattern lines rather than losing the
    /// "Show only" and "Show all days" lines.
    /// </summary>
    /// <returns>The patterns, or an empty list when the read fails.</returns>
    private List<BlockPattern> ReadPatterns()
    {
        try
        {
            return _patterns.GetAll();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Day header menu could not read the block patterns");
            return [];
        }
    }

    /// <summary>"Show only Tuesday": that day open, every other shown day folded; then closes the menu.</summary>
    [RelayCommand]
    private void ShowOnly()
    {
        _grid.ShowOnlyDay(_day);
        IsOpen = false;
    }

    /// <summary>"Show all days": opens every folded day; then closes the menu.</summary>
    [RelayCommand]
    private void ShowAll()
    {
        _grid.ShowAllDays();
        IsOpen = false;
    }
}
