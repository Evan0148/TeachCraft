using System.Globalization;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;
using SkiaSharp;

namespace GradeWeave.Core.Export;

/// <summary>纸墨版式的课表位图：界面预览、PNG 导出与 PDF 共用同一份绘制逻辑。</summary>
public sealed class WeaveImageRenderer
{
    private const float PageWidth = 1000f;
    private const float Pad = 36f;
    private const float TitleHeight = 86f;
    private const float LabelWidth = 100f;
    private const float HeaderHeight = 54f;
    private const float RowHeight = 74f;

    private static readonly SKColor Paper = Hex("#FAF9F5");
    private static readonly SKColor Ink = Hex("#141413");
    private static readonly SKColor Clay = Hex("#D97757");
    private static readonly SKColor Muted = Hex("#5F5A4E");
    private static readonly SKColor Grid = Hex("#2B2A27");
    private static readonly SKColor HeaderFill = Hex("#F1EFE9");

    private static readonly Lazy<SKTypeface> RegularTypeface = new(LoadRegular);
    private static readonly Lazy<SKTypeface> BoldTypeface = new(LoadBold);

    public byte[] RenderClassPng(WeaveProject project, string classId, int scale = 2)
    {
        try
        {
            EnsureProject(project);
            var schoolClass = project.FindClass(classId)
                              ?? throw new GradeWeaveException("未找到要导出的班级，请重新选择后再试。");
            var periods = WeaveExportSupport.TeachingPeriodsOf(project);
            var height = Pad + TitleHeight + HeaderHeight + periods.Count * RowHeight + Pad;

            return Render(height, scale, canvas =>
            {
                var subtitle = string.Join(" · ", new[] { project.GradeName, project.TermName }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                DrawTitle(canvas, $"{schoolClass.Name} 课程表", subtitle);
                DrawTable(canvas, periods,
                    (day, period) => ClassCellLines(project, schoolClass.Id, day, period));
            });
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出班级课表图片失败，请重试。", ex);
        }
    }

    public byte[] RenderTeacherPng(WeaveProject project, string teacherId, int scale = 2)
    {
        try
        {
            EnsureProject(project);
            var teacher = project.FindTeacher(teacherId)
                          ?? throw new GradeWeaveException("未找到要导出的教师，请重新选择后再试。");
            var periods = WeaveExportSupport.TeachingPeriodsOf(project);
            var height = Pad + TitleHeight + HeaderHeight + periods.Count * RowHeight + Pad;
            var (placed, required) = WeaveExportSupport.TeacherLoad(project, teacher);

            return Render(height, scale, canvas =>
            {
                var subtitle =
                    $"{WeaveExportSupport.SubjectText(project, teacher)} · 周课时 已排 {placed} / 应排 {required}";
                DrawTitle(canvas, $"{teacher.Name} 老师 课表", subtitle);
                DrawTable(canvas, periods,
                    (day, period) => TeacherCellLines(project, teacher.Id, day, period));
            });
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出教师课表图片失败，请重试。", ex);
        }
    }

    // ----------------------------- 绘制 -----------------------------

    private static void EnsureProject(WeaveProject? project)
    {
        if (project is null || (project.Classes.Count == 0 && project.Teachers.Count == 0))
            throw new GradeWeaveException("工程为空，请先导入总表或打开工程后再导出图片。");
    }

    private static byte[] Render(float logicalHeight, int scale, Action<SKCanvas> draw)
    {
        var factor = Math.Clamp(scale, 1, 4);
        var pixelWidth = (int)Math.Ceiling((double)PageWidth * factor);
        var pixelHeight = (int)Math.Ceiling((double)logicalHeight * factor);

        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) throw new GradeWeaveException("图片画布创建失败，请重试。");

        var canvas = surface.Canvas;
        canvas.Clear(Paper);
        canvas.Scale(factor);
        draw(canvas);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) throw new GradeWeaveException("PNG 编码失败，请重试。");
        return data.ToArray();
    }

    private static void DrawTitle(SKCanvas canvas, string title, string subtitle)
    {
        using (var titleFont = new SKFont(BoldTypeface.Value, 32f))
        using (var titlePaint = new SKPaint { Color = Ink, IsAntialias = true })
        {
            DrawCentered(canvas, title, new SKRect(0, Pad, PageWidth, Pad + 44f), titleFont, titlePaint);
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            using var subtitleFont = new SKFont(RegularTypeface.Value, 13.5f);
            using var subtitlePaint = new SKPaint { Color = Muted, IsAntialias = true };
            DrawCentered(canvas, subtitle, new SKRect(0, Pad + 44f, PageWidth, Pad + 66f), subtitleFont, subtitlePaint);
        }

        using var accent = new SKPaint { Color = Clay, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(PageWidth / 2f - 28f, Pad + 72f, PageWidth / 2f + 28f, Pad + 75f), accent);
    }

    private static void DrawTable(
        SKCanvas canvas,
        IReadOnlyList<PeriodSlot> periods,
        Func<int, int, IReadOnlyList<CellLine>> cellLines)
    {
        var left = Pad;
        var tableWidth = PageWidth - Pad * 2f;
        var dayWidth = (tableWidth - LabelWidth) / WeaveDefaults.Days.Length;
        var xLabel = left + LabelWidth;
        var top = Pad + TitleHeight;
        var bottom = top + HeaderHeight;

        using (var headerFill = new SKPaint { Color = HeaderFill, IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(new SKRect(left, top, left + tableWidth, bottom), headerFill);
        }

        using (var headerFont = new SKFont(BoldTypeface.Value, 18f))
        using (var headerPaint = new SKPaint { Color = Ink, IsAntialias = true })
        using (var cornerFont = new SKFont(RegularTypeface.Value, 12f))
        using (var cornerPaint = new SKPaint { Color = Muted, IsAntialias = true })
        {
            DrawCentered(canvas, "节次/星期", new SKRect(left, top, xLabel, bottom), cornerFont, cornerPaint);
            for (var day = 1; day <= WeaveDefaults.Days.Length; day++)
            {
                var rect = new SKRect(xLabel + (day - 1) * dayWidth, top, xLabel + day * dayWidth, bottom);
                DrawCentered(canvas, WeaveDefaults.DayName(day), rect, headerFont, headerPaint);
            }
        }

        DrawLine(canvas, left, bottom, left + tableWidth, bottom, 2f, Clay);

        var y = bottom;
        foreach (var slot in periods)
        {
            var rowBottom = y + RowHeight;
            var labelLines = new List<CellLine>
            {
                new(slot.Label, 16f, true, Ink),
            };
            if (!string.IsNullOrWhiteSpace(slot.TimeText))
                labelLines.Add(new CellLine(slot.TimeText, 10.5f, false, Muted));

            DrawStacked(canvas, new SKRect(left, y, xLabel, rowBottom), labelLines);

            for (var day = 1; day <= WeaveDefaults.Days.Length; day++)
            {
                var lines = cellLines(day, slot.Index);
                if (lines.Count > 0)
                {
                    var rect = new SKRect(xLabel + (day - 1) * dayWidth, y, xLabel + day * dayWidth, rowBottom);
                    DrawStacked(canvas, rect, lines);
                }
            }

            DrawLine(canvas, left, rowBottom, left + tableWidth, rowBottom, 1f, Grid);
            y = rowBottom;
        }

        DrawLine(canvas, xLabel, top, xLabel, y, 1f, Grid);
        for (var day = 1; day < WeaveDefaults.Days.Length; day++)
            DrawLine(canvas, xLabel + day * dayWidth, top, xLabel + day * dayWidth, y, 1f, Grid);

        using var outer = new SKPaint
        {
            Color = Grid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
        };
        canvas.DrawRect(new SKRect(left, top, left + tableWidth, y), outer);
    }

    private static IReadOnlyList<CellLine> ClassCellLines(
        WeaveProject project, string classId, int day, int period)
    {
        var lesson = project.LessonAt(classId, day, period);
        var lines = new List<CellLine>();

        if (lesson is not null && lesson.IsActivity)
        {
            var name = string.IsNullOrWhiteSpace(lesson.ActivityName) ? "活动" : lesson.ActivityName;
            lines.Add(new CellLine(name, 19f, true, Clay));

            var second = new List<string>();
            if (!string.IsNullOrWhiteSpace(lesson.ActivityTime)) second.Add(lesson.ActivityTime);
            var activityTeacher = project.FindTeacher(lesson.TeacherId)?.Name;
            if (!string.IsNullOrWhiteSpace(activityTeacher)) second.Add(activityTeacher);
            if (second.Count > 0)
                lines.Add(new CellLine(string.Join(" · ", second), 11.5f, false, Muted));
        }
        else if (lesson is not null)
        {
            var subject = project.FindSubject(lesson.SubjectId)?.Name ?? string.Empty;
            var teacher = project.FindTeacher(lesson.TeacherId)?.Name ?? string.Empty;

            if (WeaveExportSupport.IsLayered(lesson))
            {
                lines.Add(new CellLine(subject.Length > 0 ? subject : "走班", 19f, true, Clay));
                var layer = WeaveExportSupport.LayerText(project, lesson, withSuffix: true);
                if (layer.Length > 0) lines.Add(new CellLine(layer, 13f, false, Muted));
            }
            else
            {
                lines.Add(new CellLine(subject.Length > 0 ? subject : teacher, 20f, true, Ink));
                if (subject.Length > 0 && teacher.Length > 0)
                    lines.Add(new CellLine(teacher, 13f, false, Muted));
            }
        }

        // 叠加式活动不改课程，在格内补一行陶土色提醒。
        foreach (var activity in ActivityResolver.ActivitiesAt(project, classId, day, period, ActivityMode.Overlay))
            lines.Add(new CellLine($"{activity.Name} {ActivityResolver.TimeText(activity)}", 11.5f, false, Clay));

        return lines;
    }

    private static IReadOnlyList<CellLine> TeacherCellLines(WeaveProject project, string teacherId, int day, int period)
    {
        var lessons = project.Lessons
            .Where(l => l.TeacherId == teacherId && l.DayIndex == day && l.Period == period)
            .ToList();
        if (lessons.Count == 0) return [];

        var text = string.Join("/", lessons
            .Select(l => WeaveExportSupport.TeacherCell(project, l))
            .Distinct(StringComparer.Ordinal));
        var color = lessons.Any(WeaveExportSupport.IsLayered) ? Clay : Ink;
        return [new CellLine(text, 17f, true, color)];
    }

    private static void DrawStacked(SKCanvas canvas, SKRect rect, IReadOnlyList<CellLine> lines)
    {
        if (lines.Count == 0) return;

        var totalHeight = lines.Sum(l => l.Size * 1.35f);
        var y = rect.MidY - totalHeight / 2f;

        foreach (var line in lines)
        {
            var lineHeight = line.Size * 1.35f;
            using var font = new SKFont(line.Bold ? BoldTypeface.Value : RegularTypeface.Value, line.Size);
            using var paint = new SKPaint { Color = line.Color, IsAntialias = true };
            DrawCentered(canvas, line.Text, new SKRect(rect.Left, y, rect.Right, y + lineHeight), font, paint);
            y += lineHeight;
        }
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKRect rect, SKFont font, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return;

        var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
        canvas.DrawText(text, rect.MidX, baseline, SKTextAlign.Center, font, paint);
    }

    private static void DrawLine(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    // ----------------------------- 字体 -----------------------------

    private static SKTypeface LoadRegular()
    {
        foreach (var family in new[] { "Microsoft YaHei", "SimSun" })
        {
            var typeface = SKFontManager.Default.MatchFamily(family, SKFontStyle.Normal);
            if (typeface is not null) return typeface;
        }

        return SKFontManager.Default.MatchCharacter('李') ?? SKTypeface.Default;
    }

    private static SKTypeface LoadBold()
    {
        foreach (var family in new[] { "Microsoft YaHei", "SimSun" })
        {
            var typeface = SKFontManager.Default.MatchFamily(family, SKFontStyle.Bold);
            if (typeface is not null) return typeface;
        }

        return SKFontManager.Default.MatchCharacter(null, SKFontStyle.Bold, null, '李')
               ?? RegularTypeface.Value;
    }

    private static SKColor Hex(string hex)
    {
        var value = uint.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SKColor(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private readonly record struct CellLine(string Text, float Size, bool Bold, SKColor Color);
}
