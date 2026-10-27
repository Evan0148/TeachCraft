using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using TeachCraft.Core.Tools;

namespace TeachCraft.App.ViewModels;

public partial class ToolItemViewModel : ObservableObject
{
    public ToolKind Kind { get; }
    public string No { get; }
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string CategoryLabel { get; }
    public string CategoryEn { get; }
    public string Glyph { get; }
    public string InputText { get; }
    public string OutputText { get; }

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<FileEntryViewModel> FileEntries { get; } = new();
    public IReadOnlyList<string> Formats { get; }
    public IReadOnlyList<string> WatermarkPositions { get; } = new[] { "右下角", "左下角", "右上角", "左上角", "居中" };

    [ObservableProperty] private string _selectedFormat = "PNG";
    [ObservableProperty] private double _quality = 85;
    [ObservableProperty] private decimal? _widthValue = 1600;
    [ObservableProperty] private decimal? _heightValue = 1000;
    [ObservableProperty] private decimal? _percentValue = 50;
    [ObservableProperty] private bool _keepAspect = true;
    [ObservableProperty] private decimal? _maxEdgeValue = 1600;
    [ObservableProperty] private string _watermarkText = "仅供教学使用";
    [ObservableProperty] private string _selectedWatermarkPosition = "右下角";
    [ObservableProperty] private double _watermarkScale = 1.0;
    [ObservableProperty] private double _watermarkOpacity = 0.8;
    [ObservableProperty] private string _pageRange = string.Empty;
    [ObservableProperty] private bool _keepLayout;
    [ObservableProperty] private decimal? _dpiValue = 150;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _resultIsError;
    [ObservableProperty] private string? _lastOutputPath;

    public ToolItemViewModel(
        ToolKind kind,
        string no,
        string id,
        string name,
        string description,
        string category,
        string glyph,
        string inputText,
        string outputText,
        IEnumerable<string>? formats = null)
    {
        Kind = kind;
        No = no;
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        CategoryLabel = category == "image" ? "图片处理" : "格式工厂";
        CategoryEn = category == "image" ? "IMAGE" : "FILE / PDF";
        Glyph = glyph;
        InputText = inputText;
        OutputText = outputText;
        Formats = formats?.ToArray() ?? Array.Empty<string>();

        if (Formats.Count > 0) SelectedFormat = Formats[0];
        if (kind == ToolKind.ImageResize)
        {
            WidthValue = null;
            HeightValue = null;
            PercentValue = 50;
        }
        if (kind == ToolKind.PdfToImages) DpiValue = 150;

        Files.CollectionChanged += OnFilesChanged;
    }

    public bool HasFiles => Files.Count > 0;
    public bool CanRun => HasFiles && !IsBusy;
    public bool HasOutput => !string.IsNullOrEmpty(LastOutputPath);

    public Avalonia.Media.IBrush ResultBrush => ResultIsError
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C6613F"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3B36"));

    public string FileSummary => Files.Count switch
    {
        0 => "尚未选择文件",
        1 => Path.GetFileName(Files[0]),
        _ => $"{Files.Count} 个文件 · " + string.Join("、", Files.Take(2).Select(Path.GetFileName)) +
             (Files.Count > 2 ? " 等" : string.Empty),
    };

    public bool IsMultiFile => Kind is ToolKind.ImagesToPdf or ToolKind.PdfMerge;
    public bool ShowFormat => Kind == ToolKind.ImageConvert;

    /// <summary>画质滑块只在有损编码下有意义：PNG 无损，转换到 JPG/WebP 或压缩时才显示。</summary>
    public bool ShowQuality => Kind switch
    {
        ToolKind.ImageCompress => true,
        ToolKind.ImageConvert => SelectedFormat is "JPG" or "WebP",
        _ => false,
    };
    public bool ShowResize => Kind == ToolKind.ImageResize;
    public bool ShowCompress => Kind == ToolKind.ImageCompress;
    public bool ShowWatermark => Kind == ToolKind.ImageWatermark;
    public bool ShowPageRange => Kind == ToolKind.PdfSplit;
    public bool ShowDpi => Kind is ToolKind.PdfToImages or ToolKind.PdfToWord;
    public bool ShowKeepLayout => Kind == ToolKind.PdfToWord;
    public bool ShowPlainNote => Kind is ToolKind.WordToPdf or ToolKind.ImagesToPdf or ToolKind.PdfMerge;

    public string PlainNote => Kind switch
    {
        ToolKind.WordToPdf => "保留标题、段落与表格；图片与页眉页脚暂不参与转换。",
        ToolKind.ImagesToPdf => "按选择顺序每页放一张图，A4 居中，长边自动适配。",
        ToolKind.PdfMerge => "按选择顺序拼接，页码范围可在合并后用“PDF 拆分提取”处理。",
        _ => string.Empty,
    };

    public string InputHint => IsMultiFile ? "可一次选择多个文件" : "支持 " + InputText;

    public ToolRequest ToRequest(string output) => new()
    {
        Kind = Kind,
        Inputs = Files.ToArray(),
        Output = output,
        Format = SelectedFormat,
        Width = ToInt(WidthValue),
        Height = ToInt(HeightValue),
        Percent = PercentValue is > 0 ? (double)PercentValue.Value : null,
        KeepAspect = KeepAspect,
        Quality = (int)Math.Round(Quality),
        MaxEdge = ToInt(MaxEdgeValue),
        WatermarkText = WatermarkText,
        WatermarkPosition = SelectedWatermarkPosition switch
        {
            "左下角" => WatermarkPosition.BottomLeft,
            "右上角" => WatermarkPosition.TopRight,
            "左上角" => WatermarkPosition.TopLeft,
            "居中" => WatermarkPosition.Center,
            _ => WatermarkPosition.BottomRight,
        },
        WatermarkOpacity = WatermarkOpacity,
        WatermarkScale = WatermarkScale,
        PageRange = PageRange,
        KeepLayout = KeepLayout,
        Dpi = ToInt(DpiValue) ?? 150,
    };

    public string SuggestedFileName()
    {
        var source = Files.Count > 0 ? Path.GetFileNameWithoutExtension(Files[0]) : "文件";
        var extension = Files.Count > 0 ? Path.GetExtension(Files[0]) : ".png";
        var safeExtension = extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp"
            ? extension
            : ".png";

        return Kind switch
        {
            ToolKind.ImageConvert => $"{source}-转换.{SelectedFormat.ToLowerInvariant()}",
            ToolKind.ImageResize => $"{source}-缩放{safeExtension}",
            ToolKind.ImageCompress => $"{source}-压缩{safeExtension}",
            ToolKind.ImageWatermark => $"{source}-水印{safeExtension}",
            ToolKind.PdfToWord => $"{source}-转换.docx",
            ToolKind.WordToPdf => $"{source}.pdf",
            ToolKind.ImagesToPdf => "图片合成.pdf",
            ToolKind.PdfMerge => "合并结果.pdf",
            ToolKind.PdfSplit => $"{source}-提取.pdf",
            _ => $"{source}-输出",
        };
    }

    public void ReplaceFiles(IEnumerable<string> paths)
    {
        Files.Clear();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) Files.Add(path);
        LastOutputPath = null;
        ResultText = string.Empty;
        HasResult = false;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Files.Contains(path, StringComparer.OrdinalIgnoreCase)) Files.Add(path);
        }
        LastOutputPath = null;
        ResultText = string.Empty;
        HasResult = false;
    }

    public void MoveFile(FileEntryViewModel entry, int delta)
    {
        var index = Files.IndexOf(entry.Path);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Files.Count) return;
        Files.Move(index, target);
    }

    public void RemoveFile(FileEntryViewModel entry) => Files.Remove(entry.Path);

    private void RefreshEntries()
    {
        FileEntries.Clear();
        for (var i = 0; i < Files.Count; i++)
        {
            FileEntries.Add(new FileEntryViewModel(i + 1, Files[i]));
        }
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshEntries();
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(FileSummary));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
    }

    partial void OnSelectedFormatChanged(string value)
    {
        OnPropertyChanged(nameof(ShowQuality));
    }

    partial void OnLastOutputPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasOutput));
    }

    partial void OnResultIsErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(ResultBrush));
    }

    partial void OnWidthValueChanged(decimal? value)
    {
        if (value is > 0 && PercentValue is not null) PercentValue = null;
    }

    partial void OnHeightValueChanged(decimal? value)
    {
        if (value is > 0 && PercentValue is not null) PercentValue = null;
    }

    partial void OnPercentValueChanged(decimal? value)
    {
        if (value is > 0)
        {
            WidthValue = null;
            HeightValue = null;
        }
    }

    private static int? ToInt(decimal? value) => value is > 0 ? (int)Math.Round(value.Value) : null;
}
