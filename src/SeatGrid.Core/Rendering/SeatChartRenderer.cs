using System;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SeatGrid.Core.Models;

namespace SeatGrid.Core.Rendering;

public static class SeatChartRenderer
{
    private static readonly Lazy<SKTypeface> RegularTypeface =
        new(() => SKFontManager.Default.MatchCharacter('李') ?? SKTypeface.Default);

    private static readonly Lazy<SKTypeface> BoldTypeface =
        new(() => SKFontManager.Default.MatchCharacter(null, SKFontStyle.Bold, null, '李') ?? RegularTypeface.Value);

    public static void RenderPng(SeatChartDocument document, string path, double scale = 2.0)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, RenderPngBytes(document, scale));
    }

    public static byte[] RenderPngBytes(SeatChartDocument document, double scale)
    {
        var metrics = Measure(document);
        var width = (int)Math.Ceiling(metrics.Width * scale);
        var height = (int)Math.Ceiling(metrics.Height * scale);

        const int maxDimension = 8000;
        var longest = Math.Max(width, height);
        if (longest > maxDimension)
        {
            var ratio = maxDimension / (double)longest;
            width = Math.Max(1, (int)(width * ratio));
            height = Math.Max(1, (int)(height * ratio));
            scale *= ratio;
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            throw new SeatGridException("导出画布创建失败，请把格子尺寸调小后再试。");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale((float)scale);
        Draw(canvas, document, metrics);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string RenderPngToTemp(SeatChartDocument document, double scale = 2.0)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeachCraft", "SeatGrid");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"seatgrid-{Guid.NewGuid():N}.png");
        RenderPng(document, path, scale);
        return path;
    }

    /// <summary>
    /// 打印用 PDF：把座位表以高分辨率 PNG 铺进 A4，避免 PDF 后端把整个中文字体
    /// 内嵌进去（一张 80px 格子的座位表曾达到 23MB）。
    /// </summary>
    public static void RenderPdf(SeatChartDocument document, string path)
    {
        var metrics = Measure(document);
        var target = Math.Max(metrics.Width, metrics.Height);
        var scale = Math.Clamp(3200f / target, 1.5f, 4f);
        var png = RenderPngBytes(document, scale);
        var landscape = metrics.Width >= metrics.Height;

        QuestPDF.Settings.License = LicenseType.Community;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(10, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.Content().Image(png).FitArea();
            });
        }).GeneratePdf(path);
    }

    private static ChartMetrics Measure(SeatChartDocument document)
    {
        var settings = document.Settings;
        var cell = settings.CellSizePx;
        var gap = Math.Max(3f, cell * 0.07f);
        var groupGap = Math.Max(16f, cell * 0.28f);
        var groupWidth = document.Layout.Columns * cell + (document.Layout.Columns - 1) * gap;
        var gridWidth = document.Layout.Groups * groupWidth + (document.Layout.Groups - 1) * groupGap;
        var gridHeight = document.Layout.Rows * cell + (document.Layout.Rows - 1) * gap;

        var hints = settings.ShowGroupRowHints;
        var rowLabelWidth = hints ? 44f : 0f;
        var groupLabelHeight = hints ? 24f : 0f;
        var contentWidth = rowLabelWidth + gridWidth;
        var podiumHeight = 42f;

        var width = 28f * 2 + contentWidth;
        var height = 28f * 2
                     + 40f
                     + 24f
                     + groupLabelHeight
                     + gridHeight
                     + settings.PodiumGap
                     + podiumHeight;

        return new ChartMetrics
        {
            Width = width,
            Height = height,
            Cell = cell,
            Gap = gap,
            GroupGap = groupGap,
            GroupWidth = groupWidth,
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            RowLabelWidth = rowLabelWidth,
            GroupLabelHeight = groupLabelHeight,
            PodiumWidth = Math.Min(contentWidth * 0.55f, 360f),
            PodiumHeight = podiumHeight,
        };
    }

    private static void Draw(SKCanvas canvas, SeatChartDocument document, ChartMetrics metrics)
    {
        var settings = document.Settings;
        var pad = 28f;
        var centerX = metrics.Width / 2f;

        var ink = Color("#141413");
        var muted = Color("#85806F");
        var titleColor = Color("#141413");
        var groupColor = Color(settings.GroupLabel);
        var rowColor = Color(settings.RowLabel);

        var y = pad;

        using (var titleFont = new SKFont(document.Settings.BoldTitle ? BoldTypeface.Value : RegularTypeface.Value, 24f))
        using (var titlePaint = new SKPaint { Color = titleColor, IsAntialias = true })
        {
            DrawCentered(canvas, document.Title, centerX, y + 26f, titleFont, titlePaint);
        }

        y += 40f;

        using (var infoFont = new SKFont(RegularTypeface.Value, 11.5f))
        using (var infoPaint = new SKPaint { Color = muted, IsAntialias = true })
        {
            DrawCentered(canvas, document.InfoLine, centerX, y + 15f, infoFont, infoPaint);
        }

        y += 24f;

        if (document.PodiumOnTop)
        {
            DrawPodium(canvas, document, metrics, centerX, y, settings);
            y += metrics.PodiumHeight + settings.PodiumGap;
        }

        if (settings.ShowGroupRowHints)
        {
            using var groupFont = new SKFont(RegularTypeface.Value, 10.5f);
            using var groupPaint = new SKPaint { Color = groupColor, IsAntialias = true };

            for (var groupIndex = 0; groupIndex < document.Groups.Count; groupIndex++)
            {
                var group = document.Groups[groupIndex];
                var groupX = pad + metrics.RowLabelWidth + groupIndex * (metrics.GroupWidth + metrics.GroupGap);
                DrawCentered(canvas, group.Label, groupX + metrics.GroupWidth / 2f, y + 15f, groupFont, groupPaint);
            }
        }

        var gridTop = y + metrics.GroupLabelHeight;
        var gridLeft = pad + metrics.RowLabelWidth;
        var seatFontSize = (float)settings.NameFontSize;

        using var rowFont = new SKFont(RegularTypeface.Value, 10f);
        using var rowPaint = new SKPaint { Color = rowColor, IsAntialias = true };

        for (var groupIndex = 0; groupIndex < document.Groups.Count; groupIndex++)
        {
            var group = document.Groups[groupIndex];
            var groupX = gridLeft + groupIndex * (metrics.GroupWidth + metrics.GroupGap);

            for (var rowIndex = 0; rowIndex < group.Rows.Count; rowIndex++)
            {
                var row = group.Rows[rowIndex];
                var rowY = gridTop + rowIndex * (metrics.Cell + metrics.Gap);

                if (settings.ShowGroupRowHints)
                {
                    using var rowFontLocal = new SKFont(RegularTypeface.Value, 10f);
                    using var rowPaintLocal = new SKPaint { Color = rowColor, IsAntialias = true };
                    canvas.DrawText(row.Label, pad + metrics.RowLabelWidth - 9f, rowY + metrics.Cell / 2f - rowFontLocal.Metrics.Ascent / 2f - rowFontLocal.Metrics.Descent / 2f + 1.5f, SKTextAlign.Right, rowFontLocal, rowPaintLocal);
                }

                for (var columnIndex = 0; columnIndex < row.Seats.Count; columnIndex++)
                {
                    var seat = row.Seats[columnIndex];
                    var rect = new SKRect(
                        groupX + columnIndex * (metrics.Cell + metrics.Gap),
                        rowY,
                        groupX + columnIndex * (metrics.Cell + metrics.Gap) + metrics.Cell,
                        rowY + metrics.Cell);

                    DrawSeat(canvas, document, seat, rect, seatFontSize);
                }
            }
        }

        if (!document.PodiumOnTop)
        {
            var podiumY = gridTop + metrics.GridHeight + settings.PodiumGap;
            DrawPodium(canvas, document, metrics, centerX, podiumY, settings);
        }
    }

    private static void DrawSeat(SKCanvas canvas, SeatChartDocument document, DisplaySeat seat, SKRect rect, float fontSize)
    {
        var settings = document.Settings;
        var empty = seat.IsEmpty;

        using var fill = new SKPaint
        {
            Color = Color(empty ? settings.EmptySeatBackground : settings.SeatBackground),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(rect, 6f, 6f, fill);

        if (!empty || !settings.HideEmptySeats)
        {
            using var border = new SKPaint
            {
                Color = Color(settings.SeatBorder),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                PathEffect = empty ? SKPathEffect.CreateDash(new[] { 4f, 3f }, 0f) : null,
            };
            canvas.DrawRoundRect(rect, 6f, 6f, border);
        }

        if (empty) return;

        var name = seat.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        var size = fontSize;
        var typeface = settings.BoldNames ? BoldTypeface.Value : RegularTypeface.Value;
        using var measureFont = new SKFont(typeface, size);
        var available = rect.Width - 10f;
        var measured = measureFont.MeasureText(name);

        if (measured > available && measured > 0)
        {
            size = Math.Max(8f, size * available / measured);
        }

        using var nameFont = new SKFont(typeface, size);
        using var namePaint = new SKPaint
        {
            Color = settings.MarkFemales && seat.IsFemale ? Color(settings.FemaleMark) : Color(settings.SeatText),
            IsAntialias = true,
        };

        var centerY = rect.MidY - (nameFont.Metrics.Ascent + nameFont.Metrics.Descent) / 2f;
        canvas.DrawText(name, rect.MidX, centerY, SKTextAlign.Center, nameFont, namePaint);
    }

    private static void DrawPodium(
        SKCanvas canvas,
        SeatChartDocument document,
        ChartMetrics metrics,
        float centerX,
        float top,
        SeatChartSettings settings)
    {
        var rect = new SKRect(
            centerX - metrics.PodiumWidth / 2f,
            top,
            centerX + metrics.PodiumWidth / 2f,
            top + metrics.PodiumHeight);

        using (var fill = new SKPaint { Color = Color(settings.PodiumBackground), IsAntialias = true })
            canvas.DrawRoundRect(rect, 8f, 8f, fill);

        using (var border = new SKPaint
               {
                   Color = Color(settings.SeatBorder),
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 1f,
               })
            canvas.DrawRoundRect(rect, 8f, 8f, border);

        using var font = new SKFont(RegularTypeface.Value, 12f);
        using var paint = new SKPaint { Color = Color(settings.PodiumText), IsAntialias = true };
        canvas.DrawText("讲　台", rect.MidX, rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2f, SKTextAlign.Center, font, paint);
    }

    private static void DrawCentered(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint)
    {
        canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, paint);
    }

    private static SKColor Color(string hex)
    {
        var normalized = SeatChartSettings.NormalizeColor(hex, "#000000");
        var value = uint.Parse(normalized[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SKColor(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private sealed class ChartMetrics
    {
        public float Width { get; init; }
        public float Height { get; init; }
        public float Cell { get; init; }
        public float Gap { get; init; }
        public float GroupGap { get; init; }
        public float GroupWidth { get; init; }
        public float GridWidth { get; init; }
        public float GridHeight { get; init; }
        public float RowLabelWidth { get; init; }
        public float GroupLabelHeight { get; init; }
        public float PodiumWidth { get; init; }
        public float PodiumHeight { get; init; }
    }
}
