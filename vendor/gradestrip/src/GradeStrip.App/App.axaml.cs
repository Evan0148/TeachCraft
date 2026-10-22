using System;
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

            // 支持把多个 xlsx 拖到 exe 上或作为多参数启动
            if (desktop.Args is not null)
            {
                foreach (var arg in desktop.Args)
                {
                    if (arg.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                        arg.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) ||
                        arg.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!viewModel.HasData)
                            viewModel.Load(arg);
                        else
                            viewModel.AddSession(arg);
                    }
                }
            }

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
                    await Task.Delay(1200);
                    viewModel.Section = section;

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
                    mw.CaptureTo(target);
                    desktop.Shutdown();
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
