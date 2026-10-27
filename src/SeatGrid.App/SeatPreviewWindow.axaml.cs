using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace SeatGrid.App;

public partial class SeatPreviewWindow : Window
{
    /// <summary>预览区宽 1040，留出 150% 缩放的余量即可，不必解出原图像素。</summary>
    private const int PreviewDecodeWidth = 1600;

    private readonly string _imagePath;
    private bool _closed;

    public SeatPreviewWindow() : this(string.Empty, "座位表", string.Empty)
    {
    }

    public SeatPreviewWindow(string imagePath, string title, string subtitle)
    {
        InitializeComponent();
        _imagePath = imagePath;
        Title = $"打印预览 · {title}";
        Subtitle.Text = subtitle;
        Opened += (_, _) => _ = LoadPreviewAsync();
    }

    /// <summary>
    /// 按预览区宽度解码，不把整张座位表的原始像素搬进内存。
    /// 最大布局（8 组 × 10 排）在 2 倍渲染下是 8000×2624，原样解码要 84MB；
    /// 按 1600 宽解码约 8MB。解码放在后台线程，窗口先出来再补图。
    /// </summary>
    private async Task LoadPreviewAsync()
    {
        if (_closed || !File.Exists(_imagePath)) return;

        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(_imagePath);
                return Bitmap.DecodeToWidth(stream, PreviewDecodeWidth, BitmapInterpolationMode.HighQuality);
            });

            if (_closed)
            {
                bitmap.Dispose();
                return;
            }

            (PreviewImage.Source as IDisposable)?.Dispose();
            PreviewImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            Subtitle.Text = string.IsNullOrEmpty(Subtitle.Text)
                ? $"预览图加载失败：{ex.Message}"
                : $"{Subtitle.Text} · 预览图加载失败";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 预览图是整张座位表解码后的像素缓冲，关窗即释放，避免反复预览累积内存。
        _closed = true;
        (PreviewImage.Source as IDisposable)?.Dispose();
        PreviewImage.Source = null;

        // 预览图是每次渲染生成的临时文件，关窗即删，避免 %TEMP% 无限累积。
        TryDeleteTempPreview();

        base.OnClosed(e);
    }

    private void TryDeleteTempPreview()
    {
        if (string.IsNullOrWhiteSpace(_imagePath)) return;

        try
        {
            // 只删本程序在临时目录里生成的预览图，绝不碰用户自己选的文件。
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TeachCraft"));
            var full = Path.GetFullPath(_imagePath);
            if (full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // 删不掉不影响预览本身，留给系统临时目录清理。
        }
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
