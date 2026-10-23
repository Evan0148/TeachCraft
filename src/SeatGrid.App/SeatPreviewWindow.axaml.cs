using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace SeatGrid.App;

public partial class SeatPreviewWindow : Window
{
    private readonly string _imagePath;

    public SeatPreviewWindow() : this(string.Empty, "座位表", string.Empty)
    {
    }

    public SeatPreviewWindow(string imagePath, string title, string subtitle)
    {
        InitializeComponent();
        _imagePath = imagePath;
        Title = $"打印预览 · {title}";
        Subtitle.Text = subtitle;
        if (File.Exists(imagePath))
            PreviewImage.Source = new Bitmap(imagePath);
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(_imagePath)) return;

        try
        {
            var folder = Path.GetDirectoryName(_imagePath);
            if (!string.IsNullOrEmpty(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // 打开文件夹失败不影响预览本身
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
