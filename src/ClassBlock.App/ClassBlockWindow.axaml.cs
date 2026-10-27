using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClassBlock.Core;
using ClassBlock.Core.Import;
using ClassBlock.Core.Rendering;
using ClassBlock.Core.Timetable;
using TeachCraft.Shared;

namespace ClassBlock.App;

public partial class ClassBlockWindow : Window
{
    private string? _masterPath;

    public ClassBlockWindow() : this(new ClassBlockViewModel())
    {
    }

    public ClassBlockWindow(ClassBlockViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private ClassBlockViewModel ViewModel => (ClassBlockViewModel)DataContext!;

    protected override void OnClosed(EventArgs e)
    {
        // 先取消仍在跑的渲染，再释放当前位图，避免后台任务回写一张无人引用的图。
        ViewModel.CancelPendingPreview();
        ViewModel.Preview = null;
        base.OnClosed(e);
    }

    /// <summary>设计走查用：直接导入总表并等待课表渲染完成。</summary>
    public async Task LoadMasterAsync(string path)
    {
        var master = await Task.Run(() => MasterScheduleImporter.Import(path));
        ViewModel.LoadMaster(master, path);
        await ViewModel.RefreshPreviewAsync();
        _masterPath = path;
    }

    /// <summary>设计走查用：选中指定班级并等待课表刷新。</summary>
    public async Task SelectClassAsync(string key)
    {
        var item = ViewModel.FilteredClasses.FirstOrDefault(c => c.Key == key || c.Name == key);
        if (item is null) return;

        ViewModel.SelectedClass = item;
        await ViewModel.RefreshPreviewAsync();
    }

    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    // ----------------------------- 作息模板 -----------------------------

    private async void OnEditTemplate(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTemplate is null) return;

        var editor = new TimetableTemplateEditorWindow(ViewModel.SelectedTemplate);
        await editor.ShowDialog(this);

        if (editor.SavedTemplateId is not null)
            ViewModel.AdoptSavedTemplate(editor.SavedTemplateId);
        else
            ViewModel.LoadTemplates();
    }

    private async void OnNewTemplate(object? sender, RoutedEventArgs e)
    {
        var source = ViewModel.SelectedTemplate ?? TimetableTemplates.DefaultForGrade(null);
        var editor = new TimetableTemplateEditorWindow(source, startAsCopy: true);
        await editor.ShowDialog(this);

        if (editor.SavedTemplateId is not null)
            ViewModel.AdoptSavedTemplate(editor.SavedTemplateId);
    }

    // ----------------------------- 导入 -----------------------------

    private async void OnImportMaster(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync("导入课程总表", new FilePickerFileType("Excel 总表")
        {
            Patterns = new[] { "*.xlsx" },
        });
        if (path is null) return;

        ViewModel.IsBusy = true;
        try
        {
            var master = await Task.Run(() => MasterScheduleImporter.Import(path));
            ViewModel.LoadMaster(master, path);
            _masterPath = path;
        }
        catch (Exception ex)
        {
            Report(ex, "导入总表失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----------------------------- 打印 / 导出 -----------------------------

    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.BuildDocument();
            var directory = Path.Combine(Path.GetTempPath(), "TeachCraft", "ClassBlock");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, SafeFileName("-课程表.pdf"));

            await Task.Run(() => TimetableRenderer.RenderPdf(document, path));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ViewModel.SetStatus("已生成打印 PDF，请在打开的阅读器里打印。");
        }
        catch (Exception ex)
        {
            Report(ex, "生成打印文件失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnExportImage(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("导出课程表图片", SafeFileName(".png"), ".png",
            new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } });
        if (path is null) return;

        if (IsSamePath(path, _masterPath))
        {
            ViewModel.SetStatus("导出路径不能覆盖已导入的总表文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.BuildDocument();
            await Task.Run(() => TimetableRenderer.RenderPng(document, path, 2.5f));
            ViewModel.SetStatus($"已导出图片：{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Report(ex, "导出图片失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----------------------------- 文件选择 -----------------------------

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { fileType },
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveAsync(
        string title,
        string suggestedName,
        string extension,
        FilePickerFileType fileType)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = new[] { fileType },
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    private string SafeFileName(string suffix)
    {
        var title = string.IsNullOrWhiteSpace(ViewModel.Title) ? "课程表" : ViewModel.Title.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            title = title.Replace(invalid, '-');

        return title + suffix;
    }

    private static bool IsSamePath(string path, string? other)
    {
        if (string.IsNullOrWhiteSpace(other)) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(other),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void Report(Exception ex, string prefix)
    {
        ViewModel.SetStatus(ex is ClassBlockException ? ex.Message : $"{prefix}：{ex.Message}", true);
    }
}
