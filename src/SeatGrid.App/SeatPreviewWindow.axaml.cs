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

    protected override void OnClosed(EventArgs e)
    {
        // 预览图是整张座位表解码后的像素缓冲，关窗即释放，避免反复预览累积内存。
        (PreviewImage.Source as IDisposable)?.Dispose();
        PreviewImage.Source = null;
        base.OnClosed(e);
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
