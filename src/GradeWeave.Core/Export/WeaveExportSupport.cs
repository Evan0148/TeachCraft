using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Core.Export;

/// <summary>导出层共用的路径保护、单元格文案与页签名工具。</summary>
internal static class WeaveExportSupport
{
    /// <summary>校验导出路径：空路径、覆盖导入源文件都拒绝，并确保目标目录存在。</summary>
    public static void Prepare(WeaveProject? project, string path)
    {
        if (project is null) throw new GradeWeaveException("工程数据为空，无法导出。");
        if (string.IsNullOrWhiteSpace(path)) throw new GradeWeaveException("未指定导出路径，请选择保存位置。");

        if (!string.IsNullOrWhiteSpace(project.SourceFile) && SamePath(project.SourceFile, path))
            throw new GradeWeaveException("导出路径与导入的原始文件相同，请另选保存位置，避免覆盖源文件。");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    public static bool SamePath(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>按「教学节在前、扩展时段在后」的稳定顺序返回节次；工程未配置时回落到默认七节。</summary>
    public static List<PeriodSlot> PeriodsOf(WeaveProject project)
    {
        var periods = project.Periods
            .OrderBy(p => p.IsExtension ? 1 : 0)
            .ThenBy(p => p.Index)
            .ToList();
        return periods.Count > 0 ? periods : WeaveDefaults.CreatePeriods();
    }

    public static List<PeriodSlot> TeachingPeriodsOf(WeaveProject project)
    {
        var teaching = project.Periods.Where(p => !p.IsExtension).OrderBy(p => p.Index).ToList();
        return teaching.Count > 0 ? teaching : WeaveDefaults.CreatePeriods();
    }

    public static bool IsLayered(LessonPlacement lesson) =>
        lesson.IsLayered
        || !string.IsNullOrWhiteSpace(lesson.GroupId)
        || !string.IsNullOrWhiteSpace(lesson.LayerName);

    /// <summary>走班层级文案：Excel 用 A/B，图片用 A/B层；单个层级同理。</summary>
    public static string LayerText(WeaveProject project, LessonPlacement lesson, bool withSuffix)
    {
        if (!IsLayered(lesson)) return string.Empty;

        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(lesson.GroupId))
        {
            var group = project.LayeredGroups.FirstOrDefault(g => g.Id == lesson.GroupId);
            if (group is not null)
                names.AddRange(group.Layers.Select(l => l.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        }

        if (names.Count == 0 && !string.IsNullOrWhiteSpace(lesson.LayerName))
            names.Add(lesson.LayerName);
        if (names.Count == 0) return string.Empty;

        var hasSuffix = names.Any(n => n.EndsWith("层", StringComparison.Ordinal));
        var bare = names.Select(n => n.EndsWith("层", StringComparison.Ordinal) ? n[..^1] : n);
        var joined = string.Join("/", bare);
        return withSuffix && hasSuffix ? joined + "层" : joined;
    }

    /// <summary>班级课表 / 总表的格内文案：主行学科（走班带层级），次行教师。</summary>
    public static string LessonCell(WeaveProject project, LessonPlacement? lesson)
    {
        if (lesson is null) return string.Empty;

        if (lesson.IsActivity)
        {
            var activityHead = string.IsNullOrWhiteSpace(lesson.ActivityName) ? "活动" : lesson.ActivityName;
            var teacher = project.FindTeacher(lesson.TeacherId)?.Name ?? string.Empty;
            var second = string.IsNullOrWhiteSpace(lesson.ActivityTime)
                ? teacher
                : teacher.Length == 0
                    ? lesson.ActivityTime
                    : $"{lesson.ActivityTime} · {teacher}";
            return second.Length == 0 ? activityHead : $"{activityHead}\n{second}";
        }

        var subject = project.FindSubject(lesson.SubjectId)?.Name ?? string.Empty;
        var layer = LayerText(project, lesson, withSuffix: false);
        var teacherName = project.FindTeacher(lesson.TeacherId)?.Name ?? string.Empty;

        var head = subject;
        if (layer.Length > 0) head = head.Length > 0 ? $"{head} {layer}" : layer;
        if (head.Length == 0) head = teacherName;

        if (teacherName.Length == 0 || teacherName == head) return head;
        return $"{head}\n{teacherName}";
    }

    /// <summary>
    /// 课堂格内文案：普通课 + 叠加式活动提醒。
    /// 叠加活动不改课程，只在同一格多一行「书法课 10:50—11:10」。
    /// </summary>
    public static string CellText(WeaveProject project, string classId, int dayIndex, int period, LessonPlacement? lesson)
    {
        var text = LessonCell(project, lesson);
        var overlays = ActivityResolver.ActivitiesAt(project, classId, dayIndex, period, ActivityMode.Overlay)
            .Select(activity => $"{activity.Name} {ActivityResolver.TimeText(activity)}")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (overlays.Count == 0) return text;

        var overlayText = string.Join("\n", overlays);
        return text.Length == 0 ? overlayText : $"{text}\n{overlayText}";
    }

    /// <summary>教师课表格内文案：班级名；走班写「九1 A层」，活动写活动名。</summary>
    public static string TeacherCell(WeaveProject project, LessonPlacement lesson)
    {
        if (lesson.IsActivity)
            return string.IsNullOrWhiteSpace(lesson.ActivityName) ? "活动" : lesson.ActivityName;

        var className = project.FindClass(lesson.ClassId)?.Name ?? "未知班级";
        if (!IsLayered(lesson)) return className;

        var layer = TeacherLayerName(project, lesson);
        if (layer.Length == 0) layer = LayerText(project, lesson, withSuffix: true);
        var shortName = ShortClassName(className);
        return layer.Length > 0 ? $"{shortName} {layer}" : shortName;
    }

    /// <summary>该节课中属于某位教师的具体层级名（保留「层」字）。</summary>
    public static string TeacherLayerName(WeaveProject project, LessonPlacement lesson)
    {
        if (!IsLayered(lesson)) return string.Empty;

        if (!string.IsNullOrWhiteSpace(lesson.GroupId))
        {
            var group = project.LayeredGroups.FirstOrDefault(g => g.Id == lesson.GroupId);
            var layer = group?.Layers.FirstOrDefault(l => l.TeacherId == lesson.TeacherId);
            if (layer is not null && !string.IsNullOrWhiteSpace(layer.Name)) return layer.Name;
        }

        return string.IsNullOrWhiteSpace(lesson.LayerName) ? string.Empty : lesson.LayerName;
    }

    /// <summary>「九（1）班」→「九1」，用于走班时的紧凑班级名。</summary>
    public static string ShortClassName(string name)
    {
        var text = name
            .Replace("（", string.Empty, StringComparison.Ordinal)
            .Replace("）", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (text.EndsWith("班", StringComparison.Ordinal)) text = text[..^1];
        return text.Length > 0 ? text : name;
    }

    /// <summary>周课时统计：已排 = 实际落位节数；应排 = 分工课时，缺失时用教师周课时。</summary>
    public static (int Placed, int Required) TeacherLoad(WeaveProject project, Teacher teacher)
    {
        // 走班的 A/B 记录跨班合层上课，同一组同一节同一层只算一节；活动不计入应排课时。
        var placed = project.Lessons
            .Where(l => l.TeacherId == teacher.Id && !l.IsActivity)
            .Select(l => l.IsLayered && !string.IsNullOrWhiteSpace(l.GroupId)
                ? $"G|{l.GroupId}|{l.DayIndex}|{l.Period}|{l.LayerName}"
                : $"L|{l.ClassId}|{l.DayIndex}|{l.Period}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        var required = project.Assignments.Where(a => a.TeacherId == teacher.Id).Sum(a => a.WeeklyHours);
        if (required <= 0) required = teacher.WeeklyHours;
        return (placed, required);
    }

    public static string SubjectText(WeaveProject project, Teacher teacher)
    {
        var names = project.Assignments
            .Where(a => a.TeacherId == teacher.Id)
            .Select(a => project.FindSubject(a.SubjectId)?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0 && !string.IsNullOrWhiteSpace(teacher.Subject)) names.Add(teacher.Subject);
        return names.Count > 0 ? string.Join("、", names) : "—";
    }

    public static string ConflictKindText(ConflictKind kind) => kind switch
    {
        ConflictKind.TeacherClash => "教师冲突",
        ConflictKind.ClassClash => "班级冲突",
        ConflictKind.TeacherUnavailable => "教研 / 会议占用",
        ConflictKind.LayeredClash => "走班冲突",
        ConflictKind.MissingTeacher => "缺少任课教师",
        ConflictKind.HourMismatch => "课时不符",
        ConflictKind.Unplaced => "未排课",
        _ => "其他",
    };

    public static string Heading(WeaveProject project, string suffix)
    {
        var parts = new[] { project.SchoolName, project.GradeName, project.TermName }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? suffix : $"{text} {suffix}";
    }
}

/// <summary>Excel 页签名：去重、合法化、超 31 字符截断。</summary>
internal sealed class SheetNameAllocator
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Allocate(string raw, string fallback)
    {
        var baseName = Sanitize(raw, fallback);
        if (_used.Add(baseName)) return baseName;

        for (var index = 2; ; index++)
        {
            var suffix = " " + index.ToString();
            var head = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)];
            var candidate = head + suffix;
            if (_used.Add(candidate)) return candidate;
        }
    }

    private static string Sanitize(string raw, string fallback)
    {
        var text = (raw ?? string.Empty).Trim();
        foreach (var invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' })
            text = text.Replace(invalid, ' ');
        text = text.Replace('\t', ' ').Replace('\n', ' ').Trim('\'', ' ');

        if (text.Length == 0) text = fallback;
        if (text.Length > 31) text = text[..31];
        return text;
    }
}
