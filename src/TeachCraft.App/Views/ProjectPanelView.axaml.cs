using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TeachCraft.App.Views;

public partial class ProjectPanelView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? LaunchRequested;

    public ProjectPanelView()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnLaunch(object? sender, RoutedEventArgs e) => LaunchRequested?.Invoke(this, EventArgs.Empty);
}
