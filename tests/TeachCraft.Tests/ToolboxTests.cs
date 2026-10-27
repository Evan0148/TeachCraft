using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using TeachCraft.Core.Tools;

namespace TeachCraft.Tests;

public sealed class ToolboxTests : IDisposable
{
    private readonly string _root;

    public ToolboxTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TeachCraft.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 临时目录清理失败不影响测试结论 */ }
    }

    [Fact]
    public void ImageConvert_PngToWebp_KeepsDimensions()
    {
        var source = MakeImage("source.png", 800, 400);
        var output = Path.Combine(_root, "converted.webp");

        ImageToolbox.Convert(source, output, "webp", 90);

        Assert.True(File.Exists(output));
        Assert.Equal((800, 400), ImageSize(output));
    }

    [Fact]
    public void ImageResize_Percent_ScalesBothSides()
    {
        var source = MakeImage("resize.png", 800, 400);
        var output = Path.Combine(_root, "resized.png");

        ImageToolbox.Resize(source, output, null, null, 50, true);

        Assert.Equal((400, 200), ImageSize(output));
    }

    [Fact]
    public void ImageCompress_ReducesJpegSize()
    {
        var source = Path.Combine(_root, "photo.jpg");
        using (var bitmap = MakeBitmap(1200, 800))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 96))
        using (var stream = File.Create(source)) data.SaveTo(stream);

        var before = new FileInfo(source).Length;
        var output = Path.Combine(_root, "compressed.jpg");
        ImageToolbox.Compress(source, output, 45, 900, out _);

        var after = new FileInfo(output).Length;
        Assert.True(after < before, $"压缩后应更小：{before} → {after}");
    }

    [Fact]
    public void ImageWatermark_KeepsSizeAndWritesFile()
    {
        var source = MakeImage("mark.png", 640, 480);
        var output = Path.Combine(_root, "marked.png");

        ImageToolbox.Watermark(source, output, "仅供教学使用", WatermarkPosition.BottomRight, 0.8, 1.0);

        Assert.True(File.Exists(output));
        Assert.Equal((640, 480), ImageSize(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public void PdfMergeAndSplit_ProducesExpectedPages()
    {
        var first = Path.Combine(_root, "first.pdf");
        var second = Path.Combine(_root, "second.pdf");
        PdfToolbox.ImagesToPdf(new[] { MakeImage("a.png", 600, 400) }, first);
        PdfToolbox.ImagesToPdf(new[] { MakeImage("b.png", 400, 600) }, second);

        var merged = Path.Combine(_root, "merged.pdf");
        PdfToolbox.Merge(new[] { first, second }, merged);
        Assert.Equal(2, PageCount(merged));

        var extracted = Path.Combine(_root, "page2.pdf");
        PdfToolbox.Split(merged, extracted, "2");
        Assert.Equal(1, PageCount(extracted));
    }

    [Fact]
    public void PdfToImages_WritesOnePngPerPage()
    {
        var first = MakeImage("p1.png", 600, 400);
        var second = MakeImage("p2.png", 400, 600);
        var pdf = Path.Combine(_root, "two-pages.pdf");
        PdfToolbox.ImagesToPdf(new[] { first, second }, pdf);

        var folder = Path.Combine(_root, "pages");
        PdfToolbox.PdfToImages(pdf, folder, 96, new Progress<int>());

        Assert.Equal(2, Directory.GetFiles(folder, "*.png").Length);
    }

    [Fact]
    public void WordToPdf_And_PdfToWord_RoundTripText()
    {
        if (CjkFont.Family == "Lato")
            return; // CI 环境缺少中文字体时跳过，避免误报

        var docx = MakeDocx("report.docx");
        var pdf = Path.Combine(_root, "report.pdf");
        PdfToolbox.WordToPdf(docx, pdf);

        Assert.True(new FileInfo(pdf).Length > 1024);
        Assert.Equal(1, PageCount(pdf));

        var extracted = Path.Combine(_root, "extracted.docx");
        PdfToolbox.PdfToWord(pdf, extracted, keepLayout: false);
        Assert.Contains("学情", ReadDocxText(extracted));
    }

    [Fact]
    public void PdfToWord_LayoutMode_EmbedsPageImages()
    {
        if (CjkFont.Family == "Lato") return;

        var docx = MakeDocx("layout.docx");
        var pdf = Path.Combine(_root, "layout.pdf");
        PdfToolbox.WordToPdf(docx, pdf);

        var layoutDocx = Path.Combine(_root, "layout-extracted.docx");
        PdfToolbox.PdfToWord(pdf, layoutDocx, keepLayout: true);

        using var word = WordprocessingDocument.Open(layoutDocx, false);
        Assert.True(word.MainDocumentPart!.ImageParts.Count() >= 1);
    }

    [Fact]
    public void PageRange_ParsesAndRejectsOutOfRange()
    {
        var pages = PdfPageRange.Parse("1-2,4", 5);
        Assert.Equal(new[] { 1, 2, 4 }, pages);
        Assert.Equal("1-2,4", PdfPageRange.Describe(pages));

        Assert.Throws<ToolboxException>(() => PdfPageRange.Parse("9", 5));
        Assert.Throws<ToolboxException>(() => PdfPageRange.Parse("3-1", 5));
    }

    private string MakeImage(string name, int width, int height)
    {
        var path = Path.Combine(_root, name);
        using var bitmap = MakeBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static SKBitmap MakeBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0xDD, 0xEB, 0xF7));
        using var paint = new SKPaint { Color = new SKColor(0x70, 0xAD, 0x47) };
        canvas.DrawRect(0, 0, width, height / 3f, paint);
        return bitmap;
    }

    private static (int Width, int Height) ImageSize(string path)
    {
        using var codec = SKCodec.Create(path);
        Assert.NotNull(codec);
        return (codec!.Info.Width, codec.Info.Height);
    }

    private static int PageCount(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    private string MakeDocx(string name)
    {
        var path = Path.Combine(_root, name);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();

        var body = new Body(
            new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("九年级六班 · 学情分析"))),
            new Paragraph(new Run(new Text("本次考试全班 49 人参加，总分均分 514.91 分。"))),
            new Table(
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("科目")))),
                    new TableCell(new Paragraph(new Run(new Text("均分"))))),
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("语文")))),
                    new TableCell(new Paragraph(new Run(new Text("73.29")))))));

        main.Document = new Document(body);
        main.Document.Save();
        return path;
    }

    private static string ReadDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }
}
