using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
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
                !CaptureClassBlockIfRequested(desktop) && !CaptureGradeWeaveIfRequested(desktop) &&
                !CaptureGradeWeaveTimetableIfRequested(desktop) &&
                !CaptureGradeWeaveAssignmentIfRequested(desktop) &&
                !CaptureGradeWeaveConflictIfRequested(desktop) &&
                !CaptureMeetRosterIfRequested(desktop) &&
                !RunSliderBenchIfRequested(desktop) &&
                !ShowGradeWeaveDemoIfRequested(desktop) &&
                !ShowSeatGridDemoIfRequested(desktop))
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

    /// <summary>设计走查：打开「织序」窗口并截图。</summary>
    private static bool CaptureGradeWeaveIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradeweave-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var source = index + 2 < args.Length && args[index + 2] != "-" ? args[index + 2] : null;
        var stepText = index + 3 < args.Length ? args[index + 3] : null;
        var lockText = index + 4 < args.Length ? args[index + 4] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new GradeWeave.App.GradeWeaveWindow
                {
                    Width = 1440,
                    Height = 920,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };

                window.Show();
                await Task.Delay(400);

                if (!string.IsNullOrWhiteSpace(source))
                {
                    if (!string.Equals(source, "empty", StringComparison.OrdinalIgnoreCase))
                        await window.LoadProjectFromFileAsync(source);
                }
                else
                {
                    window.LoadDemoProject();
                }

                if (int.TryParse(stepText, out var step))
                {
                    window.SetStepForCapture(step);
                    if (step == 5)
                    {
                        await window.GenerateForCaptureAsync();

                        // 走查锁课角标：给当前班级的所有普通课加锁。
                        if (string.Equals(lockText, "lock", StringComparison.OrdinalIgnoreCase) &&
                            window.DataContext is GradeWeave.App.GradeWeaveViewModel viewModel &&
                            viewModel.FilteredClasses.FirstOrDefault() is { } classItem)
                        {
                            viewModel.SetClassLessonsLocked(classItem.Class.Id, locked: true);
                        }
                    }
                }

                await Task.Delay(900);
                window.UpdateLayout();

                var pixelSize = new PixelSize(
                    (int)Math.Max(1, window.Bounds.Width),
                    (int)Math.Max(1, window.Bounds.Height));
                using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                bitmap.Render(window);

                using var stream = File.Create(target);
                bitmap.Save(stream, new PngBitmapEncoderOptions());
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

    /// <summary>设计走查：打开织序「作息时间表」编辑窗口并截图。</summary>
    private static bool CaptureGradeWeaveTimetableIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradeweave-timetable-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var source = index + 2 < args.Length && args[index + 2] != "-" ? args[index + 2] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);

                GradeWeave.Core.Models.WeaveProject project;
                if (source is not null)
                {
                    project = new GradeWeave.Core.Import.WeaveWorkbookImporter().Import(source).Project;
                    GradeWeave.Core.Timetable.TimetableService.Ensure(project);
                }
                else
                {
                    project = GradeWeave.Core.Models.WeaveSamples.CreateDemo();
                }

                var window = new GradeWeave.App.TimetableEditorWindow(project)
                {
                    Width = 1040,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                window.Show();
                await Task.Delay(900);
                window.UpdateLayout();

                var pixelSize = new PixelSize(
                    (int)Math.Max(1, window.Bounds.Width),
                    (int)Math.Max(1, window.Bounds.Height));
                using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                bitmap.Render(window);

                using var stream = File.Create(target);
                bitmap.Save(stream, new PngBitmapEncoderOptions());
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

    /// <summary>设计走查：打开织序「教师分工」窗口并截图。</summary>
    private static bool CaptureGradeWeaveAssignmentIfRequested(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradeweave-assignment-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var source = index + 2 < args.Length && args[index + 2] != "-" ? args[index + 2] : null;
        var viewText = index + 3 < args.Length ? args[index + 3] : null;
        var teacherName = index + 4 < args.Length ? args[index + 4] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);

                GradeWeave.Core.Models.WeaveProject project;
                if (source is null)
                {
                    project = GradeWeave.Core.Models.WeaveSamples.CreateDemo();
                }
                else if (string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    project = GradeWeave.Core.Project.WeaveProjectIO.Load(source);
                }
                else
                {
                    project = new GradeWeave.Core.Import.WeaveWorkbookImporter().Import(source).Project;
                }

                var window = new GradeWeave.App.AssignmentEditorWindow(project)
                {
                    Width = 1120,
                    Height = 760,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                window.Show();
                await Task.Delay(300);
                if (int.TryParse(viewText, out var viewMode))
                    window.SetViewForCapture(viewMode, teacherName);

                await Task.Delay(900);
                window.UpdateLayout();

                var pixelSize = new PixelSize(
                    (int)Math.Max(1, window.Bounds.Width),
                    (int)Math.Max(1, window.Bounds.Height));
                using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                bitmap.Render(window);

                using var stream = File.Create(target);
                bitmap.Save(stream, new PngBitmapEncoderOptions());
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

    /// <summary>设计走查：打开织序冲突聚合台并截图。</summary>
    private static bool CaptureGradeWeaveConflictIfRequested(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradeweave-conflicts-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var source = index + 2 < args.Length && args[index + 2] != "-" ? args[index + 2] : null;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new GradeWeave.App.GradeWeaveWindow
                {
                    Width = 1440,
                    Height = 920,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                window.Show();
                await Task.Delay(300);

                if (source is not null) await window.LoadProjectFromFileAsync(source);
                else window.LoadDemoProject();

                window.SetStepForCapture(5);
                if (window.DataContext is not GradeWeave.App.GradeWeaveViewModel viewModel)
                    throw new InvalidOperationException("织序窗口没有可用的视图模型。");

                if (viewModel.Project.Lessons.Count == 0) await window.GenerateForCaptureAsync();
                await viewModel.RefreshConflictsAsync(quiet: true, announce: false);

                var center = new GradeWeave.App.ConflictCenterWindow(viewModel, _ => { })
                {
                    Width = 880,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                center.Show(window);
                await Task.Delay(900);
                center.UpdateLayout();

                var pixelSize = new PixelSize(
                    (int)Math.Max(1, center.Bounds.Width),
                    (int)Math.Max(1, center.Bounds.Height));
                using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                bitmap.Render(center);

                using var stream = File.Create(target);
                bitmap.Save(stream, new PngBitmapEncoderOptions());
                center.Close();
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

    /// <summary>设计走查：打开「竞名」并截取报名界面。</summary>
    private static bool CaptureMeetRosterIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--meetroster-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        var view = index + 2 < args.Length ? args[index + 2] : null;
        var dayText = index + 3 < args.Length ? args[index + 3] : null;
        var category = index + 4 < args.Length ? args[index + 4] : null;
        var widthIndex = Array.IndexOf(args, "--meetroster-width");
        var heightIndex = Array.IndexOf(args, "--meetroster-height");
        var width = widthIndex >= 0 && widthIndex + 1 < args.Length &&
                    int.TryParse(args[widthIndex + 1], out var parsedWidth)
            ? parsedWidth
            : 1400;
        var height = heightIndex >= 0 && heightIndex + 1 < args.Length &&
                     int.TryParse(args[heightIndex + 1], out var parsedHeight)
            ? parsedHeight
            : 900;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new MeetRoster.App.MeetRosterWindow
                {
                    Width = width,
                    Height = height,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                window.Show();
                await Task.Delay(400);

                if (string.Equals(view, "schedule", StringComparison.OrdinalIgnoreCase))
                {
                    var dayIndex = int.TryParse(dayText, out var parsedDay) ? parsedDay : 1;
                    window.ShowScheduleForCapture(dayIndex, category);
                }

                await Task.Delay(1000);
                window.UpdateLayout();
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

    /// <summary>本地走查：打开一个已生成课表的织序窗口，便于人工检查拖拽与冲突（P1-B 取证）。</summary>
    private static bool ShowGradeWeaveDemoIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        if (!args.Contains("--gradeweave-demo")) return false;

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(450);
            var window = new GradeWeave.App.GradeWeaveWindow
            {
                Width = 1440,
                Height = 920,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            window.LoadDemoProject();
            window.SetStepForCapture(5);
            window.Show();
            await window.GenerateForCaptureAsync();
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

    /// <summary>
    /// 滑块手感取证（P1-C）：--slider-bench &lt;md路径&gt;。在真实 Avalonia 运行时里
    /// 连续拖动布局滑块 60 帧，把每次实时刷新的 p50/p95 写入给定文件后退出。
    /// </summary>
    private static bool RunSliderBenchIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--slider-bench");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                File.WriteAllText(target, await SeatGrid.App.SeatGridDiagnostics.RunLayoutBenchAsync());
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

                if (args.Contains("--shot-bottom") &&
                    window.FindControl<ScrollViewer>("MainScroll") is { } scroll)
                {
                    scroll.UpdateLayout();
                    scroll.Offset = new Vector(0, scroll.Extent.Height);
                    await Task.Delay(300);
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
