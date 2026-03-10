using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that monitors an ObservableCollection of INotifyPropertyChanged items
/// and executes a command whenever any item's specified property changes (or when the
/// collection itself changes).
///
/// This replaces the manual SubscribeCollection / OnCollectionChanged / OnItemChanged
/// boilerplate that was in GridFilterView.axaml.cs, where 8 filter dimension collections
/// each needed per-item PropertyChanged subscriptions to trigger header updates.
///
/// Usage in AXAML (attach to any Control):
///   <Panel b:CollectionItemPropertyWatcher.Collection="{Binding Instructors}"
///          b:CollectionItemPropertyWatcher.PropertyName="IsSelected"
///          b:CollectionItemPropertyWatcher.Command="{Binding UpdateHeadersCommand}" />
///
/// To watch multiple collections, attach to multiple controls (e.g. one hidden Panel per
/// collection, or use an ItemsControl).
/// </summary>
public static class CollectionItemPropertyWatcher
{
    /// <summary>
    /// The ObservableCollection to watch. Items must implement INotifyPropertyChanged.
    /// </summary>
    public static readonly AttachedProperty<object?> CollectionProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>(
            "Collection", typeof(CollectionItemPropertyWatcher));

    /// <summary>
    /// The property name to monitor on each item (e.g. "IsSelected").
    /// If null or empty, any property change triggers the command.
    /// </summary>
    public static readonly AttachedProperty<string?> PropertyNameProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "PropertyName", typeof(CollectionItemPropertyWatcher));

    /// <summary>
    /// The command to execute when a change is detected.
    /// Called with no parameter.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(CollectionItemPropertyWatcher));

    // ── Getters and setters ─────────────────────────────────────────────────

    /// <summary>Gets the collection to watch.</summary>
    public static object? GetCollection(Control c) => c.GetValue(CollectionProperty);

    /// <summary>Sets the collection to watch.</summary>
    public static void SetCollection(Control c, object? value) => c.SetValue(CollectionProperty, value);

    /// <summary>Gets the property name filter.</summary>
    public static string? GetPropertyName(Control c) => c.GetValue(PropertyNameProperty);

    /// <summary>Sets the property name filter.</summary>
    public static void SetPropertyName(Control c, string? value) => c.SetValue(PropertyNameProperty, value);

    /// <summary>Gets the command to execute on change.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command to execute on change.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    // ── Static constructor: property change hooks ────────────────────────────

    static CollectionItemPropertyWatcher()
    {
        CollectionProperty.Changed.AddClassHandler<Control>(OnCollectionPropertyChanged);
    }

    /// <summary>
    /// Subscribes/unsubscribes to collection and item changes when the attached
    /// Collection property is set or cleared.
    /// </summary>
    private static void OnCollectionPropertyChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Unsubscribe from old collection.
        if (e.OldValue is INotifyCollectionChanged oldNcc)
        {
            oldNcc.CollectionChanged -= GetCollectionChangedHandler(control);
            if (e.OldValue is System.Collections.IEnumerable oldItems)
                foreach (var item in oldItems)
                    if (item is INotifyPropertyChanged oldNpc)
                        oldNpc.PropertyChanged -= GetItemChangedHandler(control);
        }

        // Subscribe to new collection.
        if (e.NewValue is INotifyCollectionChanged newNcc)
        {
            // Ensure we create and cache handlers per control instance.
            EnsureHandlers(control);

            newNcc.CollectionChanged += GetCollectionChangedHandler(control);
            if (e.NewValue is System.Collections.IEnumerable newItems)
                foreach (var item in newItems)
                    if (item is INotifyPropertyChanged newNpc)
                        newNpc.PropertyChanged += GetItemChangedHandler(control);
        }
    }

    // ── Per-control handler cache (stored as attached properties) ────────────

    // We need stable handler references per control so we can unsubscribe correctly.
    // These are stored as private attached properties.

    private static readonly AttachedProperty<NotifyCollectionChangedEventHandler?> CollectionHandlerProperty =
        AvaloniaProperty.RegisterAttached<Control, NotifyCollectionChangedEventHandler?>(
            "CollectionHandler", typeof(CollectionItemPropertyWatcher));

    private static readonly AttachedProperty<PropertyChangedEventHandler?> ItemHandlerProperty =
        AvaloniaProperty.RegisterAttached<Control, PropertyChangedEventHandler?>(
            "ItemHandler", typeof(CollectionItemPropertyWatcher));

    /// <summary>
    /// Creates and caches the two event handlers for a given control, if not already created.
    /// </summary>
    private static void EnsureHandlers(Control control)
    {
        if (control.GetValue(CollectionHandlerProperty) is not null)
            return;

        // Collection-level handler: subscribe/unsubscribe items, then fire command.
        NotifyCollectionChangedEventHandler collectionHandler = (_, args) =>
        {
            var itemHandler = GetItemChangedHandler(control);
            if (itemHandler is not null)
            {
                if (args.NewItems is not null)
                    foreach (var item in args.NewItems)
                        if (item is INotifyPropertyChanged npc)
                            npc.PropertyChanged += itemHandler;
                if (args.OldItems is not null)
                    foreach (var item in args.OldItems)
                        if (item is INotifyPropertyChanged npc)
                            npc.PropertyChanged -= itemHandler;
            }
            FireCommand(control);
        };

        // Item-level handler: fire command when the watched property changes.
        PropertyChangedEventHandler itemHandler = (_, args) =>
        {
            var filter = GetPropertyName(control);
            if (string.IsNullOrEmpty(filter) || args.PropertyName == filter)
                FireCommand(control);
        };

        control.SetValue(CollectionHandlerProperty, collectionHandler);
        control.SetValue(ItemHandlerProperty, itemHandler);
    }

    private static NotifyCollectionChangedEventHandler? GetCollectionChangedHandler(Control c)
        => c.GetValue(CollectionHandlerProperty);

    private static PropertyChangedEventHandler? GetItemChangedHandler(Control c)
        => c.GetValue(ItemHandlerProperty);

    /// <summary>
    /// Executes the Command attached to the control, if available and executable.
    /// </summary>
    private static void FireCommand(Control control)
    {
        var cmd = GetCommand(control);
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }
}
