using Avalonia;
using Avalonia.Controls;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior for Grid controls that toggles a column's width between two values
/// based on a boolean condition. When the condition is true, the column uses TrueWidth;
/// when false, it uses FalseWidth.
///
/// This replaces the UpdateLeftColumnWidth code-behind in MainWindow.axaml.cs, which
/// toggled ThreePanelGrid's column 0 between 370px and 650px based on IsEditing.
///
/// Usage in AXAML:
///   <Grid b:ConditionalColumnWidthBehavior.ColumnIndex="0"
///         b:ConditionalColumnWidthBehavior.Condition="{Binding SectionListVm.IsEditing}"
///         b:ConditionalColumnWidthBehavior.TrueWidth="650"
///         b:ConditionalColumnWidthBehavior.FalseWidth="370" />
///
/// Note: All four properties must be set for the behavior to apply. The width is
/// re-evaluated whenever any of the four properties change, so the order of AXAML
/// attribute declaration does not matter.
/// </summary>
public static class ConditionalColumnWidthBehavior
{
    /// <summary>
    /// The zero-based index of the column whose width should be toggled.
    /// Defaults to -1 (disabled). The behavior does nothing until this is set to 0 or higher.
    /// </summary>
    public static readonly AttachedProperty<int> ColumnIndexProperty =
        AvaloniaProperty.RegisterAttached<Grid, int>(
            "ColumnIndex", typeof(ConditionalColumnWidthBehavior), -1);

    /// <summary>
    /// The boolean condition that drives the width toggle.
    /// When true, TrueWidth is applied; when false, FalseWidth is applied.
    /// </summary>
    public static readonly AttachedProperty<bool> ConditionProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>(
            "Condition", typeof(ConditionalColumnWidthBehavior));

    /// <summary>
    /// The column width (in pixels) to apply when Condition is true.
    /// Must be greater than zero for the behavior to activate.
    /// </summary>
    public static readonly AttachedProperty<double> TrueWidthProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>(
            "TrueWidth", typeof(ConditionalColumnWidthBehavior), 0d);

    /// <summary>
    /// The column width (in pixels) to apply when Condition is false.
    /// Must be greater than zero for the behavior to activate.
    /// </summary>
    public static readonly AttachedProperty<double> FalseWidthProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>(
            "FalseWidth", typeof(ConditionalColumnWidthBehavior), 0d);

    // ── Getters and setters ─────────────────────────────────────────────────

    /// <summary>Gets the column index.</summary>
    public static int GetColumnIndex(Grid g) => g.GetValue(ColumnIndexProperty);

    /// <summary>Sets the column index.</summary>
    public static void SetColumnIndex(Grid g, int value) => g.SetValue(ColumnIndexProperty, value);

    /// <summary>Gets the condition value.</summary>
    public static bool GetCondition(Grid g) => g.GetValue(ConditionProperty);

    /// <summary>Sets the condition value.</summary>
    public static void SetCondition(Grid g, bool value) => g.SetValue(ConditionProperty, value);

    /// <summary>Gets the width when condition is true.</summary>
    public static double GetTrueWidth(Grid g) => g.GetValue(TrueWidthProperty);

    /// <summary>Sets the width when condition is true.</summary>
    public static void SetTrueWidth(Grid g, double value) => g.SetValue(TrueWidthProperty, value);

    /// <summary>Gets the width when condition is false.</summary>
    public static double GetFalseWidth(Grid g) => g.GetValue(FalseWidthProperty);

    /// <summary>Sets the width when condition is false.</summary>
    public static void SetFalseWidth(Grid g, double value) => g.SetValue(FalseWidthProperty, value);

    // ── Static constructor ───────────────────────────────────────────────────

    static ConditionalColumnWidthBehavior()
    {
        // Re-evaluate whenever any of the four controlling properties change.
        // This ensures the correct width is applied regardless of AXAML attribute order.
        ConditionProperty.Changed.AddClassHandler<Grid>(OnAnyPropertyChanged);
        TrueWidthProperty.Changed.AddClassHandler<Grid>(OnAnyPropertyChanged);
        FalseWidthProperty.Changed.AddClassHandler<Grid>(OnAnyPropertyChanged);
        ColumnIndexProperty.Changed.AddClassHandler<Grid>(OnAnyPropertyChanged);
    }

    /// <summary>
    /// Called whenever any controlling property changes. Applies the width only when all
    /// four properties are set (ColumnIndex >= 0, and the relevant width > 0).
    /// </summary>
    private static void OnAnyPropertyChanged(Grid grid, AvaloniaPropertyChangedEventArgs e)
    {
        var colIndex = GetColumnIndex(grid);
        if (colIndex < 0 || colIndex >= grid.ColumnDefinitions.Count)
            return;

        var condition = GetCondition(grid);
        var width = condition ? GetTrueWidth(grid) : GetFalseWidth(grid);

        // Don't apply if the target width hasn't been set yet (still at default 0).
        // This avoids a transient zero-width column during AXAML property initialization.
        if (width <= 0)
            return;

        grid.ColumnDefinitions[colIndex].Width = new GridLength(width, GridUnitType.Pixel);
    }
}
