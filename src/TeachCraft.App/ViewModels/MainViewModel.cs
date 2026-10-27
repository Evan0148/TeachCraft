using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>界面版本号直接取程序集版本，避免与 Directory.Build.props 重复硬编码。</summary>
    public string VersionText { get; } =
        $"V{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public string VersionLine => $"{VersionText} · 10 TOOLS · 4 APPS";

    public ObservableCollection<ToolItemViewModel> VisibleImageTools { get; } = new();
    public ObservableCollection<ToolItemViewModel> VisibleFileTools { get; } = new();

    /// <summary>案头常备四张卡的检索文本（应用名 + 英文名 + 描述），与工具卡走同一套匹配规则。顺序：分晓 / 归位 / 时序 / 织序。</summary>
    private static readonly string[] AppCards =
    {
        "gradestrip 分晓 把智学网导出的成绩总表，变成一叠可以裁开的成绩条。多考进退、学科诊断、学生画像，都在同一张纸上。",
        "seatgrid 归位 座位表 导入名单，自动识别姓名与性别；排座、预览、打印与导出一次完成。",
        "classblock 时序 课程表 导入课程总表，自动识别年级与班级；选中班级即可得到可直接打印的课程表。",
        "gradeweave 织序 课表编排 全年级课表编排：教研、走班、会议先定，再一次排清冲突。",
    };

    private int _visibleAppCardCount = AppCards.Length;

    public bool IsGradeStripVisible { get; private set; } = true;
    public bool IsSeatGridVisible { get; private set; } = true;
    public bool IsClassBlockVisible { get; private set; } = true;
    public bool IsGradeWeaveVisible { get; private set; } = true;
    public bool HasPinnedResults { get; private set; } = true;

    public MainViewModel()
    {
        _imageTools = new List<ToolItemViewModel>
        {
            new(ToolKind.ImageConvert, "01", "image-convert", "图片格式转换",
                "PNG、JPG、WebP 格式之间自由互转。", "image", "图",
                "PNG / JPG / WebP", "转换后的 PNG / JPG / WebP 文件", new[] { "PNG", "JPG", "WebP" }),
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

    public int MatchCount => VisibleImageTools.Count + VisibleFileTools.Count + _visibleAppCardCount;

    public string PinnedCountLabel => _visibleAppCardCount.ToString("00");

    public string SearchSummary =>
        string.IsNullOrWhiteSpace(SearchText)
            ? "ALL TOOLS · 10"
            : $"MATCH · {MatchCount}";

    public bool IsEmptyVisible => MatchCount == 0;
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

        ApplyCardFilter(query);

        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(FileCountLabel));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(HasImageResults));
        OnPropertyChanged(nameof(HasFileResults));
    }

    private void ApplyCardFilter(string query)
    {
        IsGradeStripVisible = Matches(AppCards[0], query);
        IsSeatGridVisible = Matches(AppCards[1], query);
        IsClassBlockVisible = Matches(AppCards[2], query);
        IsGradeWeaveVisible = Matches(AppCards[3], query);
        HasPinnedResults = IsGradeStripVisible || IsSeatGridVisible || IsClassBlockVisible || IsGradeWeaveVisible;
        _visibleAppCardCount = new[] { IsGradeStripVisible, IsSeatGridVisible, IsClassBlockVisible, IsGradeWeaveVisible }
            .Count(visible => visible);

        OnPropertyChanged(nameof(IsGradeStripVisible));
        OnPropertyChanged(nameof(IsSeatGridVisible));
        OnPropertyChanged(nameof(IsClassBlockVisible));
        OnPropertyChanged(nameof(IsGradeWeaveVisible));
        OnPropertyChanged(nameof(HasPinnedResults));
        OnPropertyChanged(nameof(PinnedCountLabel));
    }

    private static bool Matches(string haystack, string query)
    {
        if (query.Length == 0) return true;
        return haystack.ToLowerInvariant().Contains(query, StringComparison.Ordinal);
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
