using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that closes the parent Menu when a child MenuItem is clicked.
///
/// Avalonia 11.x does not auto-close menus for ItemsSource-generated items.
/// Attach this to a MenuItem whose children are generated via ItemsSource so
/// that clicking a child item dismisses the entire menu tree.
///
/// Usage in AXAML:
///   <MenuItem b:CloseMenuOnClickBehavior.IsEnabled="True"
///             ItemsSource="{Binding Items}" />
/// </summary>
public static class CloseMenuOnClickBehavior
{
    /// <summary>
    /// When true, listens for bubbled MenuItem.Click events and closes the root Menu.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, bool>(
            "IsEnabled", typeof(CloseMenuOnClickBehavior));

    /// <summary>Gets the IsEnabled value.</summary>
    public static bool GetIsEnabled(MenuItem m) => m.GetValue(IsEnabledProperty);

    /// <summary>Sets the IsEnabled value.</summary>
    public static void SetIsEnabled(MenuItem m, bool value) => m.SetValue(IsEnabledProperty, value);

    static CloseMenuOnClickBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<MenuItem>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(MenuItem menuItem, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            menuItem.AddHandler(MenuItem.ClickEvent, OnMenuItemClick, RoutingStrategies.Bubble, handledEventsToo: true);
        else
            menuItem.RemoveHandler(MenuItem.ClickEvent, OnMenuItemClick);
    }

    /// <summary>
    /// Walks up the visual/logical tree to find the root Menu and closes it.
    /// </summary>
    private static void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        var current = sender as Control;
        while (current != null)
        {
            if (current is Menu menu)
            {
                menu.Close();
                return;
            }
            current = current.Parent as Control;
        }
    }
}
