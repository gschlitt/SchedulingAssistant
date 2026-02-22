using Avalonia.Controls;
using System;

namespace SchedulingAssistant.Views;

public partial class DetachedPanelWindow : Window
{
    public Action? OnReattach { get; init; }

    public DetachedPanelWindow()
    {
        InitializeComponent();
    }

    public void SetContent(string title, Control content)
    {
        Title = title;
        this.FindControl<ContentControl>("ContentArea")!.Content = content;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        OnReattach?.Invoke();
    }
}
