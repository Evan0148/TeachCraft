using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GradeStrip.App.ViewModels;
using GradeStrip.Core.Comparison;

namespace GradeStrip.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>把窗口内容渲染成 PNG，用于设计走查（--shot 参数触发）。</summary>
    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);

        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private async void OnPickFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择成绩总表",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel 工作簿")
                {
                    Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm", "*.zip" },
                },
            },
        });

        if (files.Count > 0)
        {
            var path = GetSafeLocalPath(files[0])!;
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.Status = "压缩包不能直接导入。请先解压后选择其中的 .xlsx / .xls 表格导入；若解压后文件名出现乱码，推荐使用 Windows 自带解压或 Bandizip。";
                ViewModel.StatusBrush = Palette.Clay;
                return;
            }

            if (!ViewModel.HasData)
                await ViewModel.LoadAsync(path);
            else
                await ViewModel.AddSessionAsync(path);
        }
    }

    /// <summary>导入智学网分析报表（统计表 / 试卷分析），支持一次多选。</summary>
    private async void OnImportReports(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入分析报表",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("智学网分析报表")
                {
                    Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm", "*.zip" },
                },
            },
        });

        if (files.Count == 0) return;

        var paths = files
            .Select(GetSafeLocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0) return;

        if (paths.Any(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            ViewModel.Status = "压缩包不能直接导入。请先解压后选择其中的 .xlsx / .xls 表格导入；若解压后文件名出现乱码，推荐使用 Windows 自带解压或 Bandizip。";
            ViewModel.StatusBrush = Palette.Clay;
            paths = paths.Where(p => !p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (paths.Count > 0) await ViewModel.ImportReportsAsync(paths);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出成绩条",
            SuggestedFileName = ViewModel.SuggestedFileName(),
            DefaultExtension = "xlsx",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx" } },
            },
        });

        if (file is not null) await ViewModel.ExportAsync(GetSafeLocalPath(file)!);
    }

    private void OnNavOverview(object? sender, RoutedEventArgs e)
    {
        ViewModel.Detail = null;
        ViewModel.SubjectDetail = null;
        ViewModel.Section = 0;
        OverviewScrollViewer.Offset = new Avalonia.Vector(0, 0);
    }

    private void OnNavStrips(object? sender, RoutedEventArgs e) => ViewModel.Section = 1;

    /// <summary>展开 / 收起学生排行与单科学生排名（超过 50 人折叠）。</summary>
    private void OnToggleStudents(object? sender, RoutedEventArgs e) => ViewModel.ToggleStudentsExpanded();

    private void OnOpenStudent(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StudentRow row)
        {
            ViewModel.OpenStudent(row);
            StudentDetailScrollViewer.Offset = new Avalonia.Vector(0, 0);
        }
    }

    private void OnSubjectClick(object? sender, RoutedEventArgs e)
    {
        string? name = null;
        if (sender is Button btn && btn.Tag is string tag)
            name = tag;
        else if ((sender as Control)?.DataContext is SubjectStat stat)
            name = stat.Name;
        else if ((sender as Control)?.DataContext is DetailSubjectRow detailRow)
            name = detailRow.Name;
        else if ((sender as Control)?.DataContext is SubjectTrajectoryRow trajRow)
            name = trajRow.SubjectName;

        if (!string.IsNullOrEmpty(name))
        {
            ViewModel.OpenSubject(name);
            SubjectDetailScrollViewer.Offset = new Avalonia.Vector(0, 0);
        }
    }

    private void OnStudentNameClick(object? sender, RoutedEventArgs e)
    {
        string? name = null;
        if (sender is Button btn && btn.Tag is string tag)
            name = tag;
        else if ((sender as Control)?.DataContext is SubjectStudentRow row)
            name = row.StudentName;

        if (!string.IsNullOrEmpty(name))
        {
            ViewModel.OpenStudentByName(name);
            StudentDetailScrollViewer.Offset = new Avalonia.Vector(0, 0);
        }
    }

    private void OnBackToOverview(object? sender, RoutedEventArgs e)
    {
        ViewModel.Detail = null;
        ViewModel.SubjectDetail = null;
        ViewModel.Section = 0;
        OverviewScrollViewer.Offset = new Avalonia.Vector(0, 0);
    }

    private void OnSelectClassRank(object? sender, RoutedEventArgs e) => ViewModel.UseSchoolRank = false;

    private void OnSelectSchoolRank(object? sender, RoutedEventArgs e) => ViewModel.UseSchoolRank = true;

    private void OnSelectCompPrevious(object? sender, RoutedEventArgs e) =>
        ViewModel.ComparisonMode = ComparisonBaseline.PreviousExam;

    private void OnSelectCompFirst(object? sender, RoutedEventArgs e) =>
        ViewModel.ComparisonMode = ComparisonBaseline.FirstExam;

    private void OnSelectCompOriginal(object? sender, RoutedEventArgs e) =>
        ViewModel.ComparisonMode = ComparisonBaseline.OriginalFile;

    private void OnSelectTrendScore(object? sender, RoutedEventArgs e) =>
        ViewModel.StudentTrendChartMode = 0;

    private void OnSelectTrendClassRank(object? sender, RoutedEventArgs e) =>
        ViewModel.StudentTrendChartMode = 1;

    private void OnSelectTrendSchoolRank(object? sender, RoutedEventArgs e) =>
        ViewModel.StudentTrendChartMode = 2;

    private void OnOpenExamManager(object? sender, RoutedEventArgs e) =>
        ViewModel.OpenExamManager();

    private void OnCloseExamManager(object? sender, RoutedEventArgs e) =>
        ViewModel.CloseExamManager();

    private void OnSelectSessionFromNav(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order.HasValue) ViewModel.SetActiveSession(order.Value);
    }

    private void OnSetActiveSession(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order.HasValue) ViewModel.SetActiveSession(order.Value);
    }

    private void OnMoveSessionUp(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order.HasValue) ViewModel.MoveSessionUp(order.Value);
    }

    private void OnMoveSessionDown(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order.HasValue) ViewModel.MoveSessionDown(order.Value);
    }

    private async void OnAddExamSession(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择考试成绩表",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm" } },
            },
        });

        if (files.Count > 0)
        {
            await ViewModel.AddSessionAsync(GetSafeLocalPath(files[0])!);
        }
    }

    private async void OnReplaceSession(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order is null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"替换【第 {order.Value} 次考试】成绩表",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm" } },
            },
        });

        if (files.Count > 0)
        {
            await ViewModel.ReplaceSessionAsync(order.Value, GetSafeLocalPath(files[0])!);
        }
    }

    private void OnRemoveSession(object? sender, RoutedEventArgs e)
    {
        var order = (sender as Control)?.Tag as int? ?? ((sender as Control)?.DataContext as ExamSessionItem)?.Order;
        if (order.HasValue)
        {
            ViewModel.RemoveSession(order.Value);
        }
    }

    private void OnDismissNotice(object? sender, RoutedEventArgs e) => ViewModel.DismissNotice();

    private void OnToggleAbsent(object? sender, PointerPressedEventArgs e) =>
        ViewModel.ExcludeAbsent = !ViewModel.ExcludeAbsent;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var paths = files?
            .Select(GetSafeLocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths is { Count: > 0 })
            await ViewModel.ImportDroppedFilesAsync(paths);
    }

    private static string? GetSafeLocalPath(Avalonia.Platform.Storage.IStorageItem? item)
    {
        if (item is null) return null;
        var p = item.TryGetLocalPath();
        if (string.IsNullOrEmpty(p))
        {
            if (item.Path.IsAbsoluteUri && item.Path.IsFile)
                p = item.Path.LocalPath;
            else if (item.Path.IsAbsoluteUri)
                p = Uri.UnescapeDataString(item.Path.AbsolutePath);
            else
                p = item.Path.ToString();
        }
        return p?.Normalize(System.Text.NormalizationForm.FormC);
    }
}