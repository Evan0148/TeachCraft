using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClassBlock.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace ClassBlock.Core.Rendering;

/// <summary>
/// 课表输出：界面预览、导出 PNG 与打印 PDF 共用同一份 <see cref="ClassTimetable"/>。
/// PDF 使用「高分辨率 PNG 嵌入 A4」，避免把整套中文字体内嵌进 PDF。
/// </summary>
public static class TimetableRenderer
{
    private static readonly Lazy<SKTypeface> RegularTypeface =
        new(() => SKFontManager.Default.MatchCharacter('李') ?? SKTypeface.Default);

    private static readonly Lazy<SKTypeface> BoldTypeface =
        new(() => SKFontManager.Default.MatchCharacter(null, SKFontStyle.Bold, null, '李') ?? RegularTypeface.Value);

    private const float PageWidth = 1000f;
    private const float Pad = 36f;
    private const float TimeWidth = 150f;
    private const float LabelWidth = 92f;
    private const float HeaderHeight = 52f;
    private const float NormalRowHeight = 72f;
    private const float BreakHeight = 40f;
    private const float MealHeight = 40f;
    private const float NewsHeight = 46f;
    private const float EveningHeight = 118f;

    private static readonly SKColor Ink = Hex("#141413");
    private static readonly SKColor Red = Hex("#D0342C");
    private static readonly SKColor Muted = Hex("#5F5A4E");
    private static readonly SKColor Grid = Hex("#2B2A27");
    private static readonly SKColor BreakFill = Hex("#F4F3EF");

    public static void RenderPng(ClassTimetable timetable, string path, float scale = 2.5f)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, RenderPngBytes(timetable, scale));
    }

    public static byte[] RenderPngBytes(ClassTimetable timetable, float scale)
    {
        var height = MeasureHeight(timetable);
        var width = PageWidth;

        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);
        const int maxDimension = 8000;
        var longest = Math.Max(pixelWidth, pixelHeight);
        if (longest > maxDimension)
        {
            var ratio = maxDimension / (double)longest;
            pixelWidth = Math.Max(1, (int)(pixelWidth * ratio));
            pixelHeight = Math.Max(1, (int)(pixelHeight * ratio));
            scale *= (float)ratio;
        }

        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            throw new ClassBlockException("导出画布创建失败，请重试。");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        Draw(canvas, timetable);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string RenderPngToTemp(ClassTimetable timetable, float scale = 2.2f)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeachCraft", "ClassBlock");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"classblock-{Guid.NewGuid():N}.png");
        RenderPng(timetable, path, scale);
        return path;
    }

    public static void RenderPdf(ClassTimetable timetable, string path)
    {
        var png = RenderPngBytes(timetable, 3.0f);

        QuestPDF.Settings.License = LicenseType.Community;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(10, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.Content().Image(png).FitArea();
            });
        }).GeneratePdf(path);
    }

    // ----------------------------- 版面 -----------------------------

    private static float MeasureHeight(ClassTimetable timetable)
    {
        var height = TableTop() + HeaderHeight + timetable.Rows.Sum(RowHeight);

        if (timetable.Footnotes.Count > 0)
            height += 10f + timetable.Footnotes.Count * 17f;

        return height + Pad;
    }

    private static float TableTop() => Pad + 86f;

    private static float RowHeight(TimetableRow row) => row.Kind switch
    {
        TimetableRowKind.Break => BreakHeight,
        TimetableRowKind.Lunch or TimetableRowKind.Dinner => MealHeight,
        TimetableRowKind.News => NewsHeight,
        TimetableRowKind.Evening => EveningHeight,
        _ => NormalRowHeight,
    };

    private static void Draw(SKCanvas canvas, ClassTimetable timetable)
    {
        var left = Pad;
        var tableWidth = PageWidth - Pad * 2;
        var dayWidth = (tableWidth - TimeWidth - LabelWidth) / 5f;

        DrawTitle(canvas, timetable);

        var headerTop = TableTop();
        var headerBottom = headerTop + HeaderHeight;
        var xTime = left + TimeWidth;
        var xLabel = xTime + LabelWidth;

        using (var headerFont = new SKFont(BoldTypeface.Value, 19f))
        using (var headerPaint = new SKPaint { Color = Ink, IsAntialias = true })
        {
            for (var day = 0; day < 5; day++)
            {
                var rect = new SKRect(xLabel + day * dayWidth, headerTop, xLabel + (day + 1) * dayWidth, headerBottom);
                DrawCentered(canvas, timetable.DayHeaders[day], rect, headerFont, headerPaint);
            }
        }

        DrawLine(canvas, left, headerBottom, left + tableWidth, headerBottom, 1f);
        DrawLine(canvas, xTime, headerTop, xTime, headerBottom, 1f);
        DrawLine(canvas, xLabel, headerTop, xLabel, headerBottom, 1f);
        for (var day = 1; day < 5; day++)
            DrawLine(canvas, xLabel + day * dayWidth, headerTop, xLabel + day * dayWidth, headerBottom, 1f);

        var y = headerBottom;

        foreach (var row in timetable.Rows)
        {
            var rowHeight = RowHeight(row);
            var bottom = y + rowHeight;
            DrawRow(canvas, row, left, tableWidth, y, bottom, xTime, xLabel, dayWidth);
            y = bottom;
        }

        var tableBottom = y;
        DrawLine(canvas, left, tableBottom, left + tableWidth, tableBottom, 1f);

        using (var outer = new SKPaint
               {
                   Color = Ink,
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 2.6f,
               })
        {
            canvas.DrawRect(new SKRect(left, headerTop, left + tableWidth, tableBottom), outer);
        }

        DrawFootnotes(canvas, timetable, tableBottom);
    }

    private static void DrawTitle(SKCanvas canvas, ClassTimetable timetable)
    {
        using (var titleFont = new SKFont(BoldTypeface.Value, 33f))
        using (var titlePaint = new SKPaint { Color = Ink, IsAntialias = true })
        {
            var baseline = Pad + 31f;
            canvas.DrawText(timetable.Title, PageWidth / 2f, baseline, SKTextAlign.Center, titleFont, titlePaint);
        }

        if (string.IsNullOrWhiteSpace(timetable.Subtitle)) return;

        using var subtitleFont = new SKFont(RegularTypeface.Value, 13.5f);
        using var subtitlePaint = new SKPaint { Color = Muted, IsAntialias = true };
        canvas.DrawText(timetable.Subtitle, PageWidth / 2f, Pad + 58f, SKTextAlign.Center, subtitleFont, subtitlePaint);
    }

    private static void DrawRow(
        SKCanvas canvas,
        TimetableRow row,
        float left,
        float tableWidth,
        float top,
        float bottom,
        float xTime,
        float xLabel,
        float dayWidth)
    {
        var right = left + tableWidth;

        if (row.IsFullWidth)
        {
            using (var font = new SKFont(BoldTypeface.Value, 19f))
            using (var paint = new SKPaint { Color = Red, IsAntialias = true })
            {
                DrawCentered(canvas, row.MergedText, new SKRect(left, top, right, bottom), font, paint);
            }

            DrawLine(canvas, left, bottom, right, bottom, 1f);
            return;
        }

        var xMerged = row.MergesLabelAndDays ? xTime : xLabel;

        if (row.MergesLabelAndDays)
        {
            if (row.Kind == TimetableRowKind.Break)
            {
                using var fill = new SKPaint { Color = BreakFill, IsAntialias = true, Style = SKPaintStyle.Fill };
                canvas.DrawRect(new SKRect(xMerged, top, right, bottom), fill);
            }

            using (var font = new SKFont(BoldTypeface.Value, 19f))
            using (var paint = new SKPaint { Color = Ink, IsAntialias = true })
            {
                DrawCentered(canvas, row.MergedText, new SKRect(xMerged, top, right, bottom), font, paint);
            }

            DrawTime(canvas, row, left, xTime, top, bottom);
            DrawLine(canvas, xTime, top, xTime, bottom, 1f);
            DrawLine(canvas, left, bottom, right, bottom, 1f);
            return;
        }

        var isEvening = row.Kind == TimetableRowKind.Evening;

        if (isEvening)
        {
            var mid = top + (bottom - top) / 2f;
            using (var font = new SKFont(RegularTypeface.Value, 13.5f))
            using (var paint = new SKPaint { Color = Muted, IsAntialias = true })
            {
                DrawCentered(canvas, row.Times.Count > 0 ? row.Times[0] : string.Empty, new SKRect(left, top, xTime, mid), font, paint);
                DrawCentered(canvas, row.Times.Count > 1 ? row.Times[1] : string.Empty, new SKRect(left, mid, xTime, bottom), font, paint);
            }

            DrawLine(canvas, left, mid, xTime, mid, 1f);
        }
        else
        {
            DrawTime(canvas, row, left, xTime, top, bottom);
        }

        using (var labelFont = new SKFont(BoldTypeface.Value, isEvening ? 21f : 20f))
        using (var labelPaint = new SKPaint { Color = Ink, IsAntialias = true })
        {
            DrawCentered(canvas, row.Label, new SKRect(xTime, top, xLabel, bottom), labelFont, labelPaint);
        }

        DrawDayCells(canvas, row, xLabel, top, bottom, dayWidth);

        DrawLine(canvas, xTime, top, xTime, bottom, 1f);
        DrawLine(canvas, xLabel, top, xLabel, bottom, 1f);

        if (!row.MergeDays)
        {
            for (var day = 1; day < 5; day++)
                DrawLine(canvas, xLabel + day * dayWidth, top, xLabel + day * dayWidth, bottom, 1f);
        }

        DrawLine(canvas, left, bottom, right, bottom, 1f);
    }

    private static void DrawDayCells(SKCanvas canvas, TimetableRow row, float xLabel, float top, float bottom, float dayWidth)
    {
        if (row.MergeDays)
        {
            var text = row.Cells.Count > 0 ? row.Cells[0] : string.Empty;
            DrawCellText(canvas, text, new SKRect(xLabel, top, xLabel + dayWidth * 5, bottom));
            return;
        }

        for (var day = 0; day < 5; day++)
        {
            var text = day < row.Cells.Count ? row.Cells[day] : string.Empty;
            if (text.Length == 0) continue;

            var rect = new SKRect(xLabel + day * dayWidth, top, xLabel + (day + 1) * dayWidth, bottom);
            DrawCellText(canvas, text, rect);
        }
    }

    private static void DrawTime(SKCanvas canvas, TimetableRow row, float left, float xTime, float top, float bottom)
    {
        var text = row.Times.Count > 0 ? row.Times[0] : string.Empty;
        using var font = new SKFont(RegularTypeface.Value, 13.5f);
        using var paint = new SKPaint { Color = Muted, IsAntialias = true };
        DrawCentered(canvas, text, new SKRect(left, top, xTime, bottom), font, paint);
    }

    /// <summary>课程格：第一行学科加粗，任课教师排第二行；姓名类内容整体加粗。</summary>
    private static void DrawCellText(SKCanvas canvas, string text, SKRect rect)
    {
        var bold = !text.Contains('\n');
        var primarySize = bold ? 18f : 20f;
        var secondarySize = 13.5f;

        var lines = new List<(string Text, float Size, bool Bold)>();
        foreach (var segment in text.Split('\n'))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;

            var isPrimary = lines.Count == 0;
            var size = isPrimary ? primarySize : secondarySize;
            var lineBold = isPrimary && bold;

            var typeface = lineBold ? BoldTypeface.Value : RegularTypeface.Value;
            using var measureFont = new SKFont(typeface, size);
            foreach (var line in Wrap(trimmed, measureFont, rect.Width - 10f))
                lines.Add((line, size, lineBold));
        }

        if (lines.Count == 0) return;

        var totalHeight = lines.Sum(l => l.Size * 1.32f);
        var y = rect.MidY - totalHeight / 2f;

        foreach (var (line, size, lineBold) in lines)
        {
            var lineHeight = size * 1.32f;
            var baseline = y + lineHeight / 2f + size * 0.35f;

            using var font = new SKFont(lineBold ? BoldTypeface.Value : RegularTypeface.Value, size);
            using var paint = new SKPaint { Color = Ink, IsAntialias = true };
            canvas.DrawText(line, rect.MidX, baseline, SKTextAlign.Center, font, paint);

            y += lineHeight;
        }
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return new[] { text };

        var lines = new List<string>();
        var current = string.Empty;

        foreach (var ch in text)
        {
            var candidate = current + ch;
            if (current.Length > 0 && font.MeasureText(candidate) > maxWidth)
            {
                lines.Add(current);
                current = ch.ToString();
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static void DrawFootnotes(SKCanvas canvas, ClassTimetable timetable, float tableBottom)
    {
        if (timetable.Footnotes.Count == 0) return;

        using var font = new SKFont(RegularTypeface.Value, 11.5f);
        using var paint = new SKPaint { Color = Muted, IsAntialias = true };

        var y = tableBottom + 24f;
        foreach (var note in timetable.Footnotes)
        {
            canvas.DrawText(note, Pad, y, SKTextAlign.Left, font, paint);
            y += 17f;
        }
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKRect rect, SKFont font, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return;

        var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
        canvas.DrawText(text, rect.MidX, baseline, SKTextAlign.Center, font, paint);
    }

    private static void DrawLine(SKCanvas canvas, float x1, float y1, float x2, float y2, float width)
    {
        using var paint = new SKPaint
        {
            Color = Grid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };

        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static SKColor Hex(string hex)
    {
        var value = uint.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SKColor(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }
}
