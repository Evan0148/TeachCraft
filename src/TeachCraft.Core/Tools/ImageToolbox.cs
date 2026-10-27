using SkiaSharp;

namespace TeachCraft.Core.Tools;

public enum WatermarkPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
    Center,
}

/// <summary>图片处理：转换、缩放、压缩、水印。全部在本机用 SkiaSharp 完成。</summary>
public static class ImageToolbox
{
    public static readonly string[] InputExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    public static string Convert(string input, string output, string targetFormat, int quality = 92)
    {
        var format = targetFormat.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "png" => SKEncodedImageFormat.Png,
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            _ => throw new ToolboxException($"暂不支持输出为 {targetFormat}。"),
        };

        using var bitmap = Load(input);
        Encode(bitmap, output, format, quality);
        return $"{Path.GetFileName(output)} · {bitmap.Width}×{bitmap.Height}";
    }

    public static string Resize(
        string input,
        string output,
        int? width,
        int? height,
        double? percent,
        bool keepAspect)
    {
        using var source = Load(input);
        var (targetWidth, targetHeight) = CalculateSize(source.Width, source.Height, width, height, percent, keepAspect);

        using var resized = source.Resize(
            new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new ToolboxException("缩放失败，请换一张图片试试。");

        Encode(resized, output, FormatFromPath(output), 92);
        return $"{source.Width}×{source.Height} → {targetWidth}×{targetHeight} · {Path.GetFileName(output)}";
    }

    public static string Compress(string input, string output, int quality, int? maxEdge, out long beforeBytes)
    {
        beforeBytes = new FileInfo(input).Length;
        using var source = Load(input);

        SKBitmap? scaled = null;
        try
        {
            var bitmap = source;
            if (maxEdge is > 0 && (source.Width > maxEdge || source.Height > maxEdge))
            {
                var ratio = maxEdge.Value / (double)Math.Max(source.Width, source.Height);
                var w = Math.Max(1, (int)Math.Round(source.Width * ratio));
                var h = Math.Max(1, (int)Math.Round(source.Height * ratio));
                scaled = source.Resize(
                    new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (scaled is not null) bitmap = scaled;
            }

            var format = FormatFromPath(output);
            Encode(bitmap, output, format, Math.Clamp(quality, 1, 100));
        }
        finally
        {
            scaled?.Dispose();
        }

        var after = new FileInfo(output).Length;
        var note = FormatFromPath(output) == SKEncodedImageFormat.Png
            ? " PNG 为无损格式，画质滑块主要影响体积上限；可先缩小尺寸。"
            : string.Empty;
        return $"{(beforeBytes / 1024.0):0.#} KB → {(after / 1024.0):0.#} KB · {Path.GetFileName(output)}{note}";
    }

    public static string Watermark(
        string input,
        string output,
        string text,
        WatermarkPosition position,
        double opacity,
        double scale)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ToolboxException("请先填写水印文字。");

        using var source = Load(input);
        using var bitmap = new SKBitmap(source.Info);
        float textWidth;
        float textHeight;
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);

            using var typeface = ResolveCjkTypeface(SKFontStyle.Bold);
            var fontSize = Math.Max(13f, (float)(source.Width * 0.032 * Math.Clamp(scale, 0.4, 4.0)));
            using var font = new SKFont(typeface, fontSize);
            textWidth = font.MeasureText(text);
            var metrics = font.Metrics;
            textHeight = metrics.Descent - metrics.Ascent;

            var padding = Math.Max(12f, source.Width * 0.028f);
            var (x, y, align) = position switch
            {
                WatermarkPosition.TopLeft => (padding, padding - metrics.Ascent, SKTextAlign.Left),
                WatermarkPosition.TopRight => (source.Width - padding, padding - metrics.Ascent, SKTextAlign.Right),
                WatermarkPosition.Center => (source.Width / 2f, source.Height / 2f + textHeight / 2f, SKTextAlign.Center),
                WatermarkPosition.BottomLeft => (padding, source.Height - padding - metrics.Descent, SKTextAlign.Left),
                _ => (source.Width - padding, source.Height - padding - metrics.Descent, SKTextAlign.Right),
            };

            var alpha = (byte)Math.Clamp(opacity * 255, 8, 255);
            var lightImage = IsLightImage(source);
            var textColor = lightImage ? new SKColor(20, 20, 19, alpha) : new SKColor(255, 255, 255, alpha);
            var shadowColor = lightImage
                ? new SKColor(255, 255, 255, (byte)(alpha * 0.55))
                : new SKColor(0, 0, 0, (byte)(alpha * 0.42));

            using var shadow = new SKPaint
            {
                Color = shadowColor,
                IsAntialias = true,
            };
            canvas.DrawText(text, x + fontSize * 0.04f, y + fontSize * 0.04f, align, font, shadow);

            using var paint = new SKPaint
            {
                Color = textColor,
                IsAntialias = true,
            };
            canvas.DrawText(text, x, y, align, font, paint);
        }

        Encode(bitmap, output, FormatFromPath(output), 94);
        return $"{Path.GetFileName(output)} · {textWidth:0}×{textHeight:0} 水印已写入";
    }

    private static (int Width, int Height) CalculateSize(
        int sourceWidth,
        int sourceHeight,
        int? width,
        int? height,
        double? percent,
        bool keepAspect)
    {
        int w, h;

        if (percent is > 0)
        {
            w = (int)Math.Round(sourceWidth * percent.Value / 100.0);
            h = (int)Math.Round(sourceHeight * percent.Value / 100.0);
        }
        else if (width is > 0 && height is > 0)
        {
            if (!keepAspect)
            {
                w = width.Value;
                h = height.Value;
            }
            else
            {
                var ratio = Math.Min(width.Value / (double)sourceWidth, height.Value / (double)sourceHeight);
                w = (int)Math.Round(sourceWidth * ratio);
                h = (int)Math.Round(sourceHeight * ratio);
            }
        }
        else if (width is > 0)
        {
            w = width.Value;
            h = (int)Math.Round(sourceHeight * (width.Value / (double)sourceWidth));
        }
        else if (height is > 0)
        {
            h = height.Value;
            w = (int)Math.Round(sourceWidth * (height.Value / (double)sourceHeight));
        }
        else
        {
            throw new ToolboxException("请填写目标宽度、高度，或缩放百分比。");
        }

        if (w < 1 || h < 1 || w > 40000 || h > 40000)
            throw new ToolboxException("目标尺寸不合理，请检查输入的数值。");

        return (w, h);
    }

    private static SKBitmap Load(string path)
    {
        if (!File.Exists(path))
            throw new ToolboxException($"找不到文件：{Path.GetFileName(path)}");

        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ToolboxException($"无法识别 {Path.GetFileName(path)} 的图片格式。");

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                bitmap.Dispose();
                throw new ToolboxException($"读取 {Path.GetFileName(path)} 失败（{result}）。");
            }

            return ApplyOrientation(bitmap, codec.EncodedOrigin);
        }
        catch (ToolboxException) { throw; }
        catch (Exception ex)
        {
            throw new ToolboxException($"读取图片失败：{ex.Message}");
        }
    }

    /// <summary>按照 EXIF 方向把手机拍的照片摆正。</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source;

        var rotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = rotated ? source.Height : source.Width;
        var height = rotated ? source.Width : source.Height;

        var target = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(target))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(270);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(270);
                    canvas.Scale(1, -1);
                    break;
            }

            canvas.DrawBitmap(source, 0, 0);
        }

        source.Dispose();
        return target;
    }

    private static void Encode(SKBitmap bitmap, string output, SKEncodedImageFormat format, int quality)
    {
        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // 直接编码进文件流。走 image.Encode(...) → SKData 会把整张编码结果再缓冲一份，
        // 高像素照片（4000 万像素以上）仅这份副本就上百 MB，峰值会白白抬高一大截。
        using var stream = File.Create(output);
        if (!bitmap.Encode(stream, format, Math.Clamp(quality, 1, 100)))
            throw new ToolboxException("编码失败，请检查输出格式。");
    }

    private static SKEncodedImageFormat FormatFromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".png" => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Png,
        };

    private static bool IsLightImage(SKBitmap bitmap)
    {
        long total = 0;
        var count = 0;
        var stepX = Math.Max(1, bitmap.Width / 24);
        var stepY = Math.Max(1, bitmap.Height / 24);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                total += (color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000;
                count++;
            }
        }

        return count == 0 || total / count > 145;
    }

    internal static SKTypeface ResolveCjkTypeface(SKFontStyle style)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "Microsoft YaHei", "DengXian", "SimHei", "SimSun" }
            : OperatingSystem.IsMacOS()
                ? new[] { "PingFang SC", "Hiragino Sans GB", "Songti SC" }
                : new[] { "Noto Sans CJK SC", "WenQuanYi Micro Hei", "DejaVu Sans" };

        foreach (var name in candidates)
        {
            var face = SKTypeface.FromFamilyName(name, style);
            if (face is not null) return face;
        }

        return SKFontManager.Default.MatchCharacter('微')
            ?? SKTypeface.Default;
    }
}
