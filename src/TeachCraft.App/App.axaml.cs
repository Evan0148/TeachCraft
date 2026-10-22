using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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

            if (!CaptureGradeStripIfRequested(desktop)) CaptureIfRequested(desktop, window);        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>设计走查：只打开「分晓」窗口并截图，确认它能在工具箱里正常渲染。</summary>
    private static bool CaptureGradeStripIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        var index = Array.IndexOf(args, "--gradestrip-shot");
        if (index < 0 || index + 1 >= args.Length) return false;

        var target = args[index + 1];
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(500);
                var window = new GradeStrip.App.Views.MainWindow
                {
                    DataContext = new GradeStrip.App.ViewModels.MainViewModel(),
                };
                window.Show();
                await Task.Delay(1200);
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
