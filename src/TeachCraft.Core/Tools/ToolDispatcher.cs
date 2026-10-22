namespace TeachCraft.Core.Tools;

public enum ToolKind
{
    ImageConvert,
    ImageResize,
    ImageCompress,
    ImageWatermark,
    PdfToWord,
    WordToPdf,
    ImagesToPdf,
    PdfToImages,
    PdfMerge,
    PdfSplit,
}

/// <summary>一次工具运行所需的全部输入与选项。</summary>
public sealed class ToolRequest
{
    public ToolKind Kind { get; init; }
    public IReadOnlyList<string> Inputs { get; init; } = Array.Empty<string>();
    public string Output { get; init; } = string.Empty;

    public string? Format { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Percent { get; init; }
    public bool KeepAspect { get; init; } = true;

    public int Quality { get; init; } = 85;
    public int? MaxEdge { get; init; }

    public string? WatermarkText { get; init; }
    public WatermarkPosition WatermarkPosition { get; init; } = WatermarkPosition.BottomRight;
    public double WatermarkOpacity { get; init; } = 0.8;
    public double WatermarkScale { get; init; } = 1.0;

    public string? PageRange { get; init; }
    public bool KeepLayout { get; init; }
    public int Dpi { get; init; } = 150;
}

public static class ToolDispatcher
{
    public static string Run(ToolRequest request, IProgress<int>? progress = null)
    {
        if (request.Inputs.Count == 0)
            throw new ToolboxException("请先选择要处理的文件。");
        if (string.IsNullOrWhiteSpace(request.Output))
            throw new ToolboxException("请先选择输出位置。");

        foreach (var input in request.Inputs)
        {
            if (!File.Exists(input))
                throw new ToolboxException($"找不到文件：{Path.GetFileName(input)}");
        }

        var first = request.Inputs[0];
        return request.Kind switch
        {
            ToolKind.ImageConvert => ImageToolbox.Convert(first, request.Output, request.Format ?? "png", request.Quality),
            ToolKind.ImageResize => ImageToolbox.Resize(
                first, request.Output, request.Width, request.Height, request.Percent, request.KeepAspect),
            ToolKind.ImageCompress => RunCompress(request),
            ToolKind.ImageWatermark => ImageToolbox.Watermark(
                first,
                request.Output,
                request.WatermarkText ?? string.Empty,
                request.WatermarkPosition,
                request.WatermarkOpacity,
                request.WatermarkScale),
            ToolKind.PdfToWord => PdfToolbox.PdfToWord(first, request.Output, request.KeepLayout, request.Dpi),
            ToolKind.WordToPdf => PdfToolbox.WordToPdf(first, request.Output),
            ToolKind.ImagesToPdf => PdfToolbox.ImagesToPdf(request.Inputs, request.Output),
            ToolKind.PdfToImages => PdfToolbox.PdfToImages(first, request.Output, request.Dpi, progress),
            ToolKind.PdfMerge => PdfToolbox.Merge(request.Inputs, request.Output),
            ToolKind.PdfSplit => PdfToolbox.Split(first, request.Output, request.PageRange ?? string.Empty),
            _ => throw new ToolboxException("未知的工具类型。"),
        };
    }

    private static string RunCompress(ToolRequest request)
    {
        var summary = ImageToolbox.Compress(
            request.Inputs[0], request.Output, request.Quality, request.MaxEdge, out var before);
        _ = before;
        return summary;
    }
}
