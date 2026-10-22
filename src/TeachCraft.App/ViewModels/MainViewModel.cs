using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TeachCraft.Core.Tools;

namespace TeachCraft.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly IBrush Moss = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush Seal = new SolidColorBrush(Color.Parse("#C6613F"));

    private readonly List<ToolItemViewModel> _imageTools;
    private readonly List<ToolItemViewModel> _fileTools;

    public ObservableCollection<ToolItemViewModel> VisibleImageTools { get; } = new();
    public ObservableCollection<ToolItemViewModel> VisibleFileTools { get; } = new();

    public MainViewModel()
    {
        _imageTools = new List<ToolItemViewModel>
        {
            new(ToolKind.ImageConvert, "01", "image-convert", "图片格式转换",
                "PNG、JPG、WebP 格式之间自由互转。", "image", "图",
                "PNG / JPG / WebP", "指定目标格式", new[] { "PNG", "JPG", "WebP" }),
            new(ToolKind.ImageResize, "02", "image-resize", "图片尺寸调整",
                "自定义像素、分辨率或等比例缩放。", "image", "图",
                "任意图片", "指定宽高或缩放比例"),
            new(ToolKind.ImageCompress, "03", "image-compress", "图片压缩",
                "调节画质与体积，减小图片占用空间。", "image", "图",
                "PNG / JPG / WebP", "压缩后的同格式图片"),
            new(ToolKind.ImageWatermark, "04", "image-watermark", "图片加水印",
                "添加自定义文字水印，保护版式版权。", "image", "图",
                "任意图片", "带文字水印的图片"),
        };

        _fileTools = new List<ToolItemViewModel>
        {
            new(ToolKind.PdfToWord, "05", "pdf-word", "PDF 转 Word",
                "提取可编辑文字，或保留页面版式转换为 DOCX。", "file", "档",
                "PDF 文档", "DOCX 文档"),
            new(ToolKind.WordToPdf, "06", "word-pdf", "Word 转 PDF",
                "将 DOCX 文档转换为 PDF 文件，版式不跑偏。", "file", "档",
                "DOCX 文档", "PDF 文件"),
            new(ToolKind.ImagesToPdf, "07", "images-pdf", "图片合成 PDF",
                "多张图片或照片按顺序合并生成一份 PDF。", "file", "档",
                "多张图片", "单份 PDF 文档"),
            new(ToolKind.PdfToImages, "08", "pdf-images", "PDF 转图片",
                "逐页导出高清 PNG 图片并打包下载。", "file", "档",
                "PDF 文档", "PNG 图片文件夹"),
            new(ToolKind.PdfMerge, "09", "pdf-merge", "PDF 合并",
                "多份 PDF 文档按顺序拼接合为一份。", "file", "档",
                "多份 PDF", "合并后的 PDF"),
            new(ToolKind.PdfSplit, "10", "pdf-split", "PDF 拆分提取",
                "选取指定页码范围，生成新的 PDF 文件。", "file", "档",
                "PDF 文档", "指定页范围的新 PDF"),
        };

        ApplyFilter();
    }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ToolItemViewModel? _selectedTool;
    [ObservableProperty] private bool _isToolPanelOpen;
    [ObservableProperty] private bool _isProjectPanelOpen;
    [ObservableProperty] private string _statusText = "本机运行 · 文件不上传";
    [ObservableProperty] private IBrush _statusBrush = Moss;
    [ObservableProperty] private bool _isAllActive = true;
    [ObservableProperty] private bool _isImageActive;
    [ObservableProperty] private bool _isFileActive;
    [ObservableProperty] private bool _isProjectActive;

    public bool IsPanelOpen => IsToolPanelOpen || IsProjectPanelOpen;

    public string ImageCountLabel => VisibleImageTools.Count.ToString("00");
    public string FileCountLabel => VisibleFileTools.Count.ToString("00");

    public string SearchSummary =>
        string.IsNullOrWhiteSpace(SearchText)
            ? "ALL TOOLS · 10"
            : $"MATCH · {VisibleImageTools.Count + VisibleFileTools.Count}";

    public bool IsEmptyVisible => VisibleImageTools.Count + VisibleFileTools.Count == 0;
    public bool HasImageResults => VisibleImageTools.Count > 0;
    public bool HasFileResults => VisibleFileTools.Count > 0;

    public void OpenTool(ToolItemViewModel tool)
    {
        SelectedTool = tool;
        tool.HasResult = false;
        tool.ResultText = string.Empty;
        IsProjectPanelOpen = false;
        IsProjectActive = false;
        IsToolPanelOpen = true;
        SetStatus("正在查看 " + tool.Name);
        NotifyPanelChanged();
    }

    public void OpenProject()
    {
        IsToolPanelOpen = false;
        IsProjectPanelOpen = true;
        IsProjectActive = true;
        SetStatus("正在查看 分晓 · GradeStrip");
        NotifyPanelChanged();
    }

    public void ClosePanels()
    {
        IsToolPanelOpen = false;
        IsProjectPanelOpen = false;
        IsProjectActive = false;
        SetStatus("本机运行 · 文件不上传");
        NotifyPanelChanged();
    }

    public void SetStatus(string text, bool error = false)
    {
        StatusText = text;
        StatusBrush = error ? Seal : Moss;
    }

    public void SetSection(string section)
    {
        IsAllActive = section == "all";
        IsImageActive = section == "image";
        IsFileActive = section == "file";
        IsProjectActive = false;
    }

    public ToolItemViewModel? FindTool(string id) =>
        _imageTools.Concat(_fileTools).FirstOrDefault(tool => tool.Id == id);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsToolPanelOpenChanged(bool value) => OnPropertyChanged(nameof(IsPanelOpen));
    partial void OnIsProjectPanelOpenChanged(bool value) => OnPropertyChanged(nameof(IsPanelOpen));

    private void ApplyFilter()
    {
        var query = (SearchText ?? string.Empty).Trim().ToLowerInvariant();

        VisibleImageTools.Clear();
        VisibleFileTools.Clear();

        foreach (var tool in _imageTools)
            if (Matches(tool, query)) VisibleImageTools.Add(tool);

        foreach (var tool in _fileTools)
            if (Matches(tool, query)) VisibleFileTools.Add(tool);

        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(FileCountLabel));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(HasImageResults));
        OnPropertyChanged(nameof(HasFileResults));
    }

    private static bool Matches(ToolItemViewModel tool, string query)
    {
        if (query.Length == 0) return true;
        var haystack = string.Join(' ', tool.Name, tool.Id, tool.Description, tool.InputText, tool.OutputText)
            .ToLowerInvariant();
        return haystack.Contains(query, StringComparison.Ordinal);
    }

    private void NotifyPanelChanged()
    {
        OnPropertyChanged(nameof(IsPanelOpen));
    }
}
