using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TeachCraft.App.ViewModels;
using TeachCraft.App.Views;

namespace TeachCraft.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            if (!CaptureGradeStripIfRequested(desktop) && !CaptureSeatGridIfRequested(desktop) &&
                !CaptureClassBlockIfRequested(desktop) && !ShowSeatGridDemoIfRequested(desktop))
                CaptureIfRequested(desktop, window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>设计走查：打开「时序」并导入指定总表后截图。</summary>
    private static bool CaptureClassBlockIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--classblock-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var master = index + 2 < args.Length ? args[index + 2] : null;
        var className = index + 3 < args.Length ? args[index + 3] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new ClassBlock.App.ClassBlockWindow();
                window.Show();

                if (master is not null)
                {
                    await window.LoadMasterAsync(master);
                    if (className is not null) await window.SelectClassAsync(className);
                    await Task.Delay(400);
                }

                await Task.Delay(900);
                window.CaptureTo(target);
                window.Close();
            }
            catch (Exception ex)
            {
                File.WriteAllText(target + ".error.txt", ex.ToString());
            }
            finally
            {
                desktop.Shutdown();
            }
        });

        return true;
    }

    /// <summary>本地走查：打开一个带演示名单的归位窗口，便于人工检查交互。</summary>
    private static bool ShowSeatGridDemoIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        if (!args.Contains("--seatgrid-demo")) return false;

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(450);
            var window = new SeatGrid.App.SeatGridWindow();
            window.LoadDemoData();
            window.Show();
        });

        return true;
    }

    /// <summary>设计走查：打开「归位」并装载演示名单后截图。</summary>
    private static bool CaptureSeatGridIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--seatgrid-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var empty = index + 2 < args.Length && args[index + 2] == "empty";

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new SeatGrid.App.SeatGridWindow();
                if (!empty) window.LoadDemoData();
                window.Show();
                await Task.Delay(1600);
                window.CaptureTo(target);
                window.Close();
            }
            catch (Exception ex)
            {
                File.WriteAllText(target + ".error.txt", ex.ToString());
            }
            finally
            {
                desktop.Shutdown();
            }
        });

        return true;
    }

    /// <summary>设计走查：只打开「分晓」窗口并截图，确认它能在工具箱里正常渲染。</summary>
    private static bool CaptureGradeStripIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradestrip-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];

        // 截图参数之后的路径按顺序收集，遇到下一个 -- 参数即停止。
        var paths = new System.Collections.Generic.List<string>();
        for (var i = index + 2; i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal); i++)
        {
            if (File.Exists(args[i])) paths.Add(args[i]);
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var viewModel = new GradeStrip.App.ViewModels.MainViewModel();
                var window = new GradeStrip.App.Views.MainWindow { DataContext = viewModel };
                if (paths.Count > 0) window.Height = 860;
                window.Show();

                if (paths.Count > 0)
                {
                    // 与拖拽一致：成绩表建立会话，分析报表挂到当前会话
                    await viewModel.ImportDroppedFilesAsync(paths);

                    viewModel.Section = 0;
                    window.UpdateLayout();
                    if (window.FindControl<ScrollViewer>("OverviewScrollViewer") is { } overview)
                    {
                        overview.UpdateLayout();
                        overview.Offset = new Vector(0, overview.Extent.Height);
                    }

                    await Task.Delay(600);
                }

                await Task.Delay(1200);

                // 截图走查：--busy-preview 强制显示分晓的忙碌遮罩，检查加载动画
                if (args.Contains("--busy-preview"))
                {
                    viewModel.BusyText = "正在解析成绩总表…";
                    viewModel.IsBusy = true;
                    await Task.Delay(500);
                }

                window.CaptureTo(target);
                window.Close();
            }
            catch (Exception ex)
            {
                File.WriteAllText(target + ".error.txt", ex.ToString());
            }
            finally
            {
                desktop.Shutdown();
            }
        });

        return true;
    }

    private static void CaptureIfRequested(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        var args = desktop.Args ?? Array.Empty<string>();

        var selfTestIndex = Array.IndexOf(args, "--selftest");
        if (selfTestIndex >= 0 && selfTestIndex + 4 < args.Length)
        {
            var toolId = args[selfTestIndex + 1];
            var input = args[selfTestIndex + 2];
            var output = args[selfTestIndex + 3];
            var shot = args[selfTestIndex + 4];

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Task.Delay(900);
                    await window.RunForShotAsync(toolId, input, output);
                    await Task.Delay(700);
                    window.CaptureTo(shot);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(shot + ".error.txt", ex.ToString());
                }
                finally
                {
                    desktop.Shutdown();
                }
            });
            return;
        }

        var shotIndex = Array.IndexOf(args, "--shot");
        if (shotIndex < 0 || shotIndex + 1 >= args.Length) return;

        var target = args[shotIndex + 1];
        var panelIndex = Array.IndexOf(args, "--panel");
        var panel = panelIndex >= 0 && panelIndex + 1 < args.Length ? args[panelIndex + 1] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(1100);
                if (panel is not null)
                {
                    await window.ShowForShotAsync(panel);
                    await Task.Delay(600);
                }

                var searchIndex = Array.IndexOf(args, "--search");
                if (searchIndex >= 0 && searchIndex + 1 < args.Length &&
                    window.DataContext is MainViewModel viewModel)
                {
                    viewModel.SearchText = args[searchIndex + 1];
                    await Task.Delay(250);
                }

                window.CaptureTo(target);
            }
            catch (Exception ex)
            {
                File.WriteAllText(target + ".error.txt", ex.ToString());
            }
            finally
            {
                desktop.Shutdown();
            }
        });
    }
}
