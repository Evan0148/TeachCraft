using System;
using System.Collections.Generic;
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
using SeatGrid.App;
using TeachCraft.App.ViewModels;
using TeachCraft.Core.Tools;

namespace TeachCraft.App.Views;

public partial class MainWindow : Window
{
    private SeatGridWindow? _seatGridWindow;
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

    private async Task ShowPanelAsync(bool wasOpen)
    {
        if (wasOpen) return;

        if (PanelHost.RenderTransform is TranslateTransform transform) transform.X = 452;
        PanelHost.Opacity = 0;
        Backdrop.Opacity = 0;
        await Task.Yield();
        await AnimateOpenAsync();
    }

    private async Task HidePanelAsync()
    {
        if (!ViewModel.IsPanelOpen) return;

        await AnimateCloseAsync();
        ViewModel.ClosePanels();

        if (PanelHost.RenderTransform is TranslateTransform transform) transform.X = 452;
        PanelHost.Opacity = 1;
        Backdrop.Opacity = 1;
    }

    private async Task AnimateOpenAsync()
    {
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 452d),
                        new Setter(Visual.OpacityProperty, 0d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 0d),
                        new Setter(Visual.OpacityProperty, 1d),
                    },
                },
            },
        };

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };

        await Task.WhenAll(slide.RunAsync(PanelHost), fade.RunAsync(Backdrop));
    }

    private async Task AnimateCloseAsync()
    {
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 0d),
                        new Setter(Visual.OpacityProperty, 1d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 452d),
                        new Setter(Visual.OpacityProperty, 0d),
                    },
                },
            },
        };

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
            },
        };

        await Task.WhenAll(slide.RunAsync(PanelHost), fade.RunAsync(Backdrop));
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
        if (tool is null || tool.IsBusy) return;

        if (!tool.HasFiles)
        {
            ViewModel.SetStatus("请先选择要处理的文件。", true);
            return;
        }

        var output = await PickOutputAsync(tool);
        if (string.IsNullOrEmpty(output)) return;

        await RunToolAsync(tool, output);
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
            var message = "处理失败：" + ex.Message;
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

    // ----------------------------- 分晓 -----------------------------

    private void OnLaunchGradeStrip(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = new GradeStrip.App.Views.MainWindow
            {
                DataContext = new GradeStrip.App.ViewModels.MainViewModel(),
            };
            window.Show();
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
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && ViewModel.IsToolPanelOpen
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!ViewModel.IsToolPanelOpen || ViewModel.SelectedTool is null) return;

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
