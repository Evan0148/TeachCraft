using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TeachCraft.App.Views;

public partial class ToolPanelView : UserControl
{
    public event EventHandler? FilePickRequested;
    public event EventHandler? RunRequested;
    public event EventHandler? CloseRequested;

    public ToolPanelView()
    {
        InitializeComponent();
    }

    private void OnPick(object? sender, RoutedEventArgs e) => FilePickRequested?.Invoke(this, EventArgs.Empty);
    private void OnRun(object? sender, RoutedEventArgs e) => RunRequested?.Invoke(this, EventArgs.Empty);
    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ToolItemViewModel tool) tool.ReplaceFiles(Array.Empty<string>());
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ViewModels.FileEntryViewModel entry &&
            DataContext is ViewModels.ToolItemViewModel tool)
        {
            tool.MoveFile(entry, -1);
        }
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ViewModels.FileEntryViewModel entry &&
            DataContext is ViewModels.ToolItemViewModel tool)
        {
            tool.MoveFile(entry, 1);
        }
    }

    private void OnRemoveFile(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ViewModels.FileEntryViewModel entry &&
            DataContext is ViewModels.ToolItemViewModel tool)
        {
            tool.RemoveFile(entry);
        }
    }

    private void OnOpenOutput(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.ToolItemViewModel tool || string.IsNullOrEmpty(tool.LastOutputPath))
            return;

        try
        {
            var directory = System.IO.Directory.Exists(tool.LastOutputPath)
                ? tool.LastOutputPath
                : System.IO.Path.GetDirectoryName(tool.LastOutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // 打不开资源管理器时保持安静，结果文件仍然在磁盘上。
        }
    }
}
