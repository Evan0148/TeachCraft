using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GradeStrip.App.ViewModels;
using GradeStrip.App.Views;

namespace GradeStrip.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // 支持把多个 xlsx 拖到 exe 上或作为多参数启动；成绩表与分析报表自动分流
            var startupArgs = desktop.Args ?? Array.Empty<string>();
            var startupFiles = new List<string>();
            for (var i = 0; i < startupArgs.Length; i++)
            {
                var arg = startupArgs[i];

                // 跳过开关自带的参数值（截图路径、报表路径等），避免把报表当成绩表加载
                if (arg is "--shot" or "--student" or "--subject" or "--height" or "--report" or "--report-sheet" or "--report-class" or "--strip-class" or "--as-grade")
                {
                    i++;
                    continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
                if (IsExcelFile(arg) || arg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    startupFiles.Add(arg);
            }

            Task? startupImport = null;
            if (startupFiles.Count > 0)
                startupImport = viewModel.ImportDroppedFilesAsync(startupFiles);

            // 设计走查：--shot <png> 渲染窗口后退出
            var shotIndex = Array.IndexOf(desktop.Args ?? Array.Empty<string>(), "--shot");
            if (shotIndex >= 0 && shotIndex + 1 < desktop.Args!.Length)
            {
                var target = desktop.Args[shotIndex + 1];
                var section = desktop.Args.Contains("--strips") ? 1 : 0;
                var studentIndex = Array.IndexOf(desktop.Args, "--student");
                var studentNo = studentIndex >= 0 && studentIndex + 1 < desktop.Args.Length
                    ? int.TryParse(desktop.Args[studentIndex + 1], out var parsed) ? parsed : 0
                    : 0;

                var subjectIndex = Array.IndexOf(desktop.Args, "--subject");
                var subjectName = subjectIndex >= 0 && subjectIndex + 1 < desktop.Args.Length
                    ? desktop.Args[subjectIndex + 1]
                    : null;

                Dispatcher.UIThread.Post(async () =>
                {
                    // 截图前先等待启动导入完成，避免异步化后截到空状态
                    if (startupImport is not null) await startupImport;
                    await Task.Delay(1200);
                    viewModel.Section = section;

                    // 截图走查：--report <xlsx> 可重复，把报表挂到当前成绩会话后渲染
                    var reportPaths = new List<string>();
                    for (var i = 0; i < desktop.Args!.Length - 1; i++)
                    {
                        if (desktop.Args[i] == "--report") reportPaths.Add(desktop.Args[i + 1]);
                    }
                    if (reportPaths.Count > 0) await viewModel.ImportReportsAsync(reportPaths);

                    // 截图走查：--as-grade <xlsx> 强制走成绩总表入口，验证纯报表拦截
                    var asGradeIndex = Array.IndexOf(desktop.Args!, "--as-grade");
                    if (asGradeIndex >= 0 && asGradeIndex + 1 < desktop.Args!.Length)
                        await viewModel.LoadAsync(desktop.Args[asGradeIndex + 1]);

                    // 截图走查：--report-sheet N 选择报表内的第 N 张工作表
                    var reportSheetIndex = Array.IndexOf(desktop.Args!, "--report-sheet");
                    if (reportSheetIndex >= 0 && reportSheetIndex + 1 < desktop.Args!.Length &&
                        int.TryParse(desktop.Args[reportSheetIndex + 1], out var sheetIndex))
                        viewModel.ActivateReportSheet(sheetIndex);

                    // 截图走查：--report-class <班级> 选择报表班级；缺省为全年级
                    var reportClassIndex = Array.IndexOf(desktop.Args!, "--report-class");
                    if (reportClassIndex >= 0 && reportClassIndex + 1 < desktop.Args!.Length)
                        viewModel.SelectedReportClass = desktop.Args[reportClassIndex + 1];

                    // 截图走查：--strip-class <班级> 选择成绩条班级；缺省为全年级
                    var stripClassIndex = Array.IndexOf(desktop.Args!, "--strip-class");
                    if (stripClassIndex >= 0 && stripClassIndex + 1 < desktop.Args!.Length)
                        viewModel.SelectedStripClass = desktop.Args[stripClassIndex + 1];

                    var heightIndex = Array.IndexOf(desktop.Args!, "--height");
                    if (heightIndex >= 0 && heightIndex + 1 < desktop.Args!.Length &&
                        double.TryParse(desktop.Args[heightIndex + 1], out var height) && height > 0)
                        desktop.MainWindow!.Height = height;

                    if (!string.IsNullOrEmpty(subjectName))
                    {
                        viewModel.OpenSubject(subjectName);
                    }
                    else if (studentNo > 0)
                    {
                        // 概览可能在切页签时释放，截图前先回到概览重建学生列表
                        viewModel.Section = 0;
                        var row = viewModel.Students.FirstOrDefault(s => s.No == studentNo);
                        if (row is not null) viewModel.OpenStudent(row);
                    }
                    else if (desktop.Args.Contains("--include-absent"))
                    {
                        viewModel.ExcludeAbsent = false;
                    }
                    else if (desktop.Args.Contains("--exam-manager"))
                    {
                        viewModel.OpenExamManager();
                    }

                    await Task.Delay(600);
                    var mw = (MainWindow)desktop.MainWindow!;
                    if (desktop.Args.Contains("--scroll-bottom"))
                    {
                        mw.StudentDetailScrollViewer.Offset = new Avalonia.Vector(0, 700);
                        await Task.Delay(400);
                    }
                    else if (desktop.Args.Contains("--scroll-overview-bottom"))
                    {
                        mw.OverviewScrollViewer.Offset = new Avalonia.Vector(0, mw.OverviewScrollViewer.Extent.Height);
                        await Task.Delay(400);
                    }
                    else if (desktop.Args.Contains("--scroll-subject-bottom"))
                    {
                        mw.SubjectDetailScrollViewer.UpdateLayout();
                        mw.SubjectDetailScrollViewer.Offset = new Avalonia.Vector(0, mw.SubjectDetailScrollViewer.Extent.Height);
                        await Task.Delay(400);
                    }

                    // 截图走查：把报表表格横向滚到最右侧，确认最后一列可达
                    if (desktop.Args.Contains("--report-scroll-right"))
                    {
                        await Task.Delay(200);
                        mw.ReportTableScrollViewer.UpdateLayout();
                        mw.ReportTableScrollViewer.Offset = new Avalonia.Vector(
                            mw.ReportTableScrollViewer.Extent.Width,
                            mw.ReportTableScrollViewer.Offset.Y);
                        await Task.Delay(300);
                    }
                    // 截图走查：--busy-preview 强制显示忙碌遮罩，检查加载动画与遮罩排版
                    if (desktop.Args.Contains("--busy-preview"))
                    {
                        viewModel.BusyText = "正在解析成绩总表…";
                        viewModel.IsBusy = true;
                        await Task.Delay(500);
                    }

                    mw.CaptureTo(target);
                    desktop.Shutdown();
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsExcelFile(string path) =>
        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
}
