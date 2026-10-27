using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SeatGrid.App;
using ClassBlock.App;
using TeachCraft.App.ViewModels;
using TeachCraft.Core.Tools;
using TeachCraft.Shared;

namespace TeachCraft.App.Views;

public partial class MainWindow : Window
{
    /// <summary>没有打开工具抽屉时拖入文件的提示，避免静默拒绝。</summary>
    private const string NoPanelDragHint = "请先打开对应工具面板，再拖入文件";

    private SeatGridWindow? _seatGridWindow;
    private ClassBlockWindow? _classBlockWindow;
    private GradeWeave.App.GradeWeaveWindow? _gradeWeaveWindow;
    private MeetRoster.App.MeetRosterWindow? _meetRosterWindow;
    private GradeStrip.App.Views.MainWindow? _gradeStripWindow;
    private bool _runDialogOpen;   // 输出对话框 / 任务运行期间的重入守卫
    public MainWindow()
    {
        InitializeComponent();
        SearchBox.KeyDown += OnSearchKeyDown;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    /// <summary>把窗口渲染成 PNG，供设计走查使用（--shot 参数触发）。</summary>
    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);

        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>设计走查：打开某个抽屉后截图。</summary>
    public async Task ShowForShotAsync(string target)
    {
        var wasOpen = ViewModel.IsPanelOpen;
        if (target == "project")
        {
            ViewModel.OpenProject();
        }
        else
        {
            var tool = ViewModel.FindTool(target);
            if (tool is null) return;
            ViewModel.OpenTool(tool);
        }

        await ShowPanelAsync(wasOpen);
    }

    // ----------------------------- 抽屉动效 -----------------------------

    private Task ShowPanelAsync(bool wasOpen)
    {
        PanelHost.IsVisible = true;
        Backdrop.IsVisible = true;

        if (PanelHost.RenderTransform is TranslateTransform transform)
        {
            if (!wasOpen && transform.X >= PanelWidth - 0.5)
            {
                transform.X = PanelWidth;
                PanelHost.Opacity = 0;
                Backdrop.Opacity = 0;
            }
        }

        // 上一次关闭还在跑：先放行它的等待方，再把目标改回 0。
        // 面板会从当前位置直接反向，不回跳到全开位。
        var pending = _panelTcs;
        _panelTcs = null;
        pending?.TrySetResult();

        StartPanelAnimation(0);
        return Task.CompletedTask;
    }

    private Task HidePanelAsync()
    {
        if (!ViewModel.IsPanelOpen) return Task.CompletedTask;

        if (_panelTcs is null || _panelTcs.Task.IsCompleted)
            _panelTcs = new TaskCompletionSource();

        StartPanelAnimation(PanelWidth);
        return _panelTcs.Task;
    }

    // 单一进度模型：只改 TranslateTransform.X 与 Opacity，开关共用一条指数曲线。
    // 旧实现把关闭动画的 0% 关键帧写死成 X=0、Opacity=1，面板滑到一半再按 Esc 会先弹回全开位。
    private const double PanelWidth = 452;
    private double _panelTargetX = PanelWidth;
    private double _panelLastTime;
    private DispatcherTimer? _panelTimer;
    private Stopwatch? _panelClock;
    private TaskCompletionSource? _panelTcs;

    private void StartPanelAnimation(double targetX)
    {
        _panelTargetX = targetX;

        // 系统开了「减弱动效」：不做横移，打开改为 120ms 纯 Opacity 淡入（X 直接落位），
        // 关闭保持当前位置淡出。依旧可打断，每次都从当前透明度续接。
        if (TeachCraft.Shared.MotionPreference.ReduceMotion)
        {
            var opening = targetX < PanelWidth - 0.5;
            if (opening && PanelHost.RenderTransform is TranslateTransform snapTransform)
                snapTransform.X = targetX;

            _reducedFadeActive = true;
            _reducedFadeClosing = !opening;
            _reducedFadeFrom = PanelHost.Opacity;
            _reducedFadeTarget = opening ? 1 : 0;

            if (_panelTimer is null)
            {
                _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _panelTimer.Tick += OnPanelTick;
                _panelClock = Stopwatch.StartNew();
            }

            _reducedFadeStart = _panelClock!.Elapsed.TotalSeconds;
            _panelTimer.Start();
            return;
        }

        if (_panelTimer is null)
        {
            _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _panelTimer.Tick += OnPanelTick;
            _panelClock = Stopwatch.StartNew();
        }

        if (!_panelTimer.IsEnabled)
        {
            _panelLastTime = _panelClock!.Elapsed.TotalSeconds;
            _panelTimer.Start();
        }
    }

    private void StopPanelAnimation() => _panelTimer?.Stop();

    private bool _reducedFadeActive;
    private bool _reducedFadeClosing;
    private double _reducedFadeStart;
    private double _reducedFadeFrom;
    private double _reducedFadeTarget;

    private void OnReducedPanelFadeTick()
    {
        var progress = Math.Clamp((_panelClock!.Elapsed.TotalSeconds - _reducedFadeStart) / 0.12, 0, 1);
        var opacity = _reducedFadeFrom + (_reducedFadeTarget - _reducedFadeFrom) * progress;
        PanelHost.Opacity = opacity;
        Backdrop.Opacity = opacity;

        if (progress < 1) return;

        StopPanelAnimation();
        _reducedFadeActive = false;
        PanelHost.Opacity = _reducedFadeTarget;
        Backdrop.Opacity = _reducedFadeTarget;

        if (!_reducedFadeClosing) return; // 停在打开态

        ViewModel.ClosePanels();
        PanelHost.IsVisible = false;
        Backdrop.IsVisible = false;
        var tcs = _panelTcs;
        _panelTcs = null;
        tcs?.TrySetResult();
    }

    private void OnPanelTick(object? sender, EventArgs e)
    {
        if (_reducedFadeActive)
        {
            OnReducedPanelFadeTick();
            return;
        }

        if (PanelHost.RenderTransform is not TranslateTransform transform)
        {
            StopPanelAnimation();
            return;
        }

        var now = _panelClock!.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _panelLastTime, 0.001, 0.05);
        _panelLastTime = now;

        // 指数趋近（约等于临界阻尼，response ≈ 0.09s）：每帧按比例靠近目标。
        // 目标随时可改，运动始终从当前屏幕位置续接，不会出现「砖墙」。
        var k = 1 - Math.Exp(-dt / 0.09);
        transform.X += (_panelTargetX - transform.X) * k;

        var progress = Math.Clamp(1 - transform.X / PanelWidth, 0, 1);
        PanelHost.Opacity = progress;
        Backdrop.Opacity = progress;

        if (Math.Abs(transform.X - _panelTargetX) > 0.7) return;

        transform.X = _panelTargetX;
        var settled = _panelTargetX <= 0.5 ? 1d : 0d;
        PanelHost.Opacity = settled;
        Backdrop.Opacity = settled;
        StopPanelAnimation();

        if (_panelTargetX < PanelWidth) return; // 停在打开态，无需收尾

        ViewModel.ClosePanels();
        PanelHost.IsVisible = false;
        Backdrop.IsVisible = false;
        var tcs = _panelTcs;
        _panelTcs = null;
        tcs?.TrySetResult();
    }

    // ----------------------------- 导航 -----------------------------

    private void OnNavAll(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetSection("all");
        MainScroll.Offset = new Vector(0, 0);
    }

    private void OnNavImage(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetSection("image");
        ImageSectionHead.BringIntoView();
    }

    private void OnNavFile(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetSection("file");
        FileSectionHead.BringIntoView();
    }

    private async void OnNavProject(object? sender, RoutedEventArgs e)
    {
        MainScroll.Offset = new Vector(0, 0);
        var wasOpen = ViewModel.IsPanelOpen;
        ViewModel.OpenProject();
        await ShowPanelAsync(wasOpen);
    }

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var wasOpen = ViewModel.IsPanelOpen;
        ViewModel.OpenProject();
        await ShowPanelAsync(wasOpen);
    }

    private async void OnOpenTool(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ToolItemViewModel tool) return;

        var wasOpen = ViewModel.IsPanelOpen;
        ViewModel.OpenTool(tool);
        await ShowPanelAsync(wasOpen);
    }

    private async void OnClosePanel(object? sender, EventArgs e) => await HidePanelAsync();

    private async void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => await HidePanelAsync();

    // ----------------------------- 文件选择 -----------------------------

    private async void OnPickFiles(object? sender, EventArgs e)
    {
        var tool = ViewModel.SelectedTool;
        if (tool is null) return;

        var (label, patterns) = InputFilter(tool);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = tool.IsMultiFile ? "选择文件（可多选）" : "选择文件",
            AllowMultiple = tool.IsMultiFile,
            FileTypeFilter = new[] { new FilePickerFileType(label) { Patterns = patterns } },
        });

        if (files.Count == 0) return;

        var paths = files.Select(GetLocalPath).Where(path => !string.IsNullOrEmpty(path)).Cast<string>().ToList();
        if (paths.Count == 0) return;

        if (tool.IsMultiFile) tool.AddFiles(paths);
        else tool.ReplaceFiles(paths.Take(1));

        ViewModel.SetStatus($"{tool.Name} · 已选择 {tool.Files.Count} 个文件");
    }

    private async void OnRunTool(object? sender, EventArgs e)
    {
        var tool = ViewModel.SelectedTool;
        if (tool is null || tool.IsBusy || _runDialogOpen) return;

        if (!tool.HasFiles)
        {
            ViewModel.SetStatus("请先选择要处理的文件。", true);
            return;
        }

        // 输出对话框弹出期间按钮仍是启用态，快速双击会叠出两个保存框、同任务跑两遍。
        // 用一个独立的对话标志兜住入口，而不是等到 RunToolAsync 才置 IsBusy。
        _runDialogOpen = true;
        try
        {
            var output = await PickOutputAsync(tool);
            if (string.IsNullOrEmpty(output)) return;

            await RunToolAsync(tool, output);
        }
        finally
        {
            _runDialogOpen = false;
        }
    }

    /// <summary>设计走查 / 自测：跳过系统文件对话框，直接跑一遍完整 UI 流程。</summary>
    public async Task RunForShotAsync(string toolId, string input, string output)
    {
        var tool = ViewModel.FindTool(toolId);
        if (tool is null) return;

        tool.ReplaceFiles(input.Split(';', StringSplitOptions.RemoveEmptyEntries));
        var wasOpen = ViewModel.IsPanelOpen;
        ViewModel.OpenTool(tool);
        await ShowPanelAsync(wasOpen);
        await RunToolAsync(tool, output);
    }

    private async Task RunToolAsync(ToolItemViewModel tool, string output)
    {
        tool.IsBusy = true;
        tool.Progress = 0;
        tool.ResultText = string.Empty;
        tool.HasResult = false;
        ViewModel.SetStatus($"正在处理 {tool.Name}…");

        try
        {
            var progress = new Progress<int>(value => tool.Progress = value);
            var request = tool.ToRequest(output);
            var summary = await Task.Run(() => ToolDispatcher.Run(request, progress));

            tool.Progress = 100;
            tool.ResultText = summary;
            tool.HasResult = true;
            tool.ResultIsError = false;
            tool.LastOutputPath = output;
            ViewModel.SetStatus("处理完成 · " + summary);
        }
        catch (ToolboxException ex)
        {
            tool.ResultText = ex.Message;
            tool.HasResult = true;
            tool.ResultIsError = true;
            ViewModel.SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            var message = ErrorText.Describe(ex, "处理");
            tool.ResultText = message;
            tool.HasResult = true;
            tool.ResultIsError = true;
            ViewModel.SetStatus(message, true);
        }
        finally
        {
            tool.IsBusy = false;
        }
    }

    private async Task<string?> PickOutputAsync(ToolItemViewModel tool)
    {
        if (tool.Kind == ToolKind.PdfToImages)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 PNG 输出文件夹",
                AllowMultiple = false,
            });
            return folders.Count > 0 ? GetLocalPath(folders[0]) : null;
        }

        var suggested = tool.SuggestedFileName();
        var extension = Path.GetExtension(suggested).TrimStart('.');
        var (label, patterns) = SaveFilter(tool, extension);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存处理结果",
            SuggestedFileName = suggested,
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(label) { Patterns = patterns } },
            ShowOverwritePrompt = true,
        });

        return file is null ? null : GetLocalPath(file);
    }

    private static (string Label, string[] Patterns) InputFilter(ToolItemViewModel tool) => tool.Kind switch
    {
        ToolKind.PdfToWord or ToolKind.PdfToImages or ToolKind.PdfMerge or ToolKind.PdfSplit =>
            ("PDF 文档", new[] { "*.pdf" }),
        ToolKind.WordToPdf =>
            ("Word 文档", new[] { "*.docx" }),
        _ =>
            ("图片", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" }),
    };

    private static (string Label, string[] Patterns) SaveFilter(ToolItemViewModel tool, string extension) =>
        tool.Kind switch
        {
            ToolKind.PdfToWord => ("Word 文档", new[] { "*.docx" }),
            ToolKind.WordToPdf or ToolKind.PdfMerge or ToolKind.PdfSplit or ToolKind.ImagesToPdf =>
                ("PDF 文档", new[] { "*.pdf" }),
            ToolKind.ImageConvert => (tool.SelectedFormat + " 图片", new[] { "*." + extension }),
            _ => ("图片", new[] { "*." + extension }),
        };

    private void OnProjectLaunch(object? sender, EventArgs e) => OnLaunchGradeStrip(sender, new RoutedEventArgs());

    // ----------------------------- 归位 · SeatGrid -----------------------------

    private void OnLaunchSeatGrid(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_seatGridWindow is { IsVisible: true })
            {
                _seatGridWindow.Activate();
                return;
            }

            _seatGridWindow = new SeatGridWindow();
            _seatGridWindow.Closed += (_, _) => _seatGridWindow = null;
            _seatGridWindow.Show();
            ViewModel.SetStatus("已打开 归位 · SeatGrid");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("打开 归位 失败：" + ex.Message, true);
        }
    }

    // ----------------------------- 时序 · ClassBlock -----------------------------

    private void OnLaunchClassBlock(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_classBlockWindow is { IsVisible: true })
            {
                _classBlockWindow.Activate();
                return;
            }

            _classBlockWindow = new ClassBlockWindow();
            _classBlockWindow.Closed += (_, _) => _classBlockWindow = null;
            _classBlockWindow.Show();
            ViewModel.SetStatus("已打开 时序 · ClassBlock");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("打开 时序 失败：" + ex.Message, true);
        }
    }

    // ----------------------------- 织序 · GradeWeave -----------------------------

    private void OnLaunchGradeWeave(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_gradeWeaveWindow is { IsVisible: true })
            {
                _gradeWeaveWindow.Activate();
                return;
            }

            _gradeWeaveWindow = new GradeWeave.App.GradeWeaveWindow();
            _gradeWeaveWindow.Closed += (_, _) => _gradeWeaveWindow = null;
            _gradeWeaveWindow.Show();
            ViewModel.SetStatus("已打开 织序 · GradeWeave");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("打开 织序 失败：" + ex.Message, true);
        }
    }

    // ----------------------------- 竞名 · MeetRoster -----------------------------

    private void OnLaunchMeetRoster(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_meetRosterWindow is { IsVisible: true })
            {
                _meetRosterWindow.Activate();
                return;
            }

            _meetRosterWindow = new MeetRoster.App.MeetRosterWindow();
            _meetRosterWindow.Closed += (_, _) => _meetRosterWindow = null;
            _meetRosterWindow.Show();
            ViewModel.SetStatus("已打开 竞名 · MeetRoster");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("打开 竞名 失败：" + ex.Message, true);
        }
    }

    // ----------------------------- 分晓 -----------------------------

    private void OnLaunchGradeStrip(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 单实例复用：分晓一次导入可能持有数千名学生的数据与报表视图模型，
            // 重复打开窗口等于把这份内存翻倍。
            if (_gradeStripWindow is { IsVisible: true })
            {
                _gradeStripWindow.Activate();
                return;
            }

            _gradeStripWindow = new GradeStrip.App.Views.MainWindow
            {
                DataContext = new GradeStrip.App.ViewModels.MainViewModel(),
            };
            _gradeStripWindow.Closed += (_, _) => _gradeStripWindow = null;
            _gradeStripWindow.Show();
            ViewModel.SetStatus("已打开 分晓 · GradeStrip");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("打开分晓失败：" + ex.Message, true);
        }
    }

    // ----------------------------- 键盘 / 拖放 -----------------------------

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // 搜索框回车 = 打开第一个命中工具，而不是把按键吞掉什么也不做。
        var first = ViewModel.VisibleImageTools.FirstOrDefault()
                    ?? ViewModel.VisibleFileTools.FirstOrDefault();
        if (first is not null)
        {
            var wasOpen = ViewModel.IsPanelOpen;
            ViewModel.OpenTool(first);
            _ = ShowPanelAsync(wasOpen);
        }

        e.Handled = true;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var control = (e.KeyModifiers & KeyModifiers.Control) != 0 || (e.KeyModifiers & KeyModifiers.Meta) != 0;
        if (control && e.Key == Key.K)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (ViewModel.IsPanelOpen) await HidePanelAsync();
            else if (SearchBox.IsFocused) SearchBox.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles && ViewModel.IsToolPanelOpen
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (hasFiles && !ViewModel.IsToolPanelOpen && ViewModel.StatusText != NoPanelDragHint)
            ViewModel.SetStatus(NoPanelDragHint);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!ViewModel.IsToolPanelOpen || ViewModel.SelectedTool is null)
        {
            if (e.DataTransfer.Contains(DataFormat.File)) ViewModel.SetStatus(NoPanelDragHint);
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var paths = files.Select(GetLocalPath).Where(path => !string.IsNullOrEmpty(path)).Cast<string>().ToList();
        if (paths.Count == 0) return;

        var tool = ViewModel.SelectedTool;
        if (tool.IsMultiFile) tool.AddFiles(paths);
        else tool.ReplaceFiles(paths.Take(1));

        ViewModel.SetStatus($"{tool.Name} · 已选择 {tool.Files.Count} 个文件");
    }

    private static string? GetLocalPath(IStorageItem? item)
    {
        if (item is null) return null;

        var path = item.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            if (item.Path.IsAbsoluteUri && item.Path.IsFile)
                path = item.Path.LocalPath;
            else if (item.Path.IsAbsoluteUri)
                path = Uri.UnescapeDataString(item.Path.AbsolutePath);
            else
                path = item.Path.ToString();
        }

        return path?.Normalize(System.Text.NormalizationForm.FormC);
    }
}
