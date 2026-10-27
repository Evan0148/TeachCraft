using GradeWeave.Core.Models;
using GradeWeave.Core.Timetable;

namespace GradeWeave.Core.Scheduling;

/// <summary>手动放课候选：某班某学科的任课教师与本周课时进度。</summary>
public sealed class LessonCandidate
{
    public string SubjectId { get; init; } = string.Empty;
    public string TeacherId { get; init; } = string.Empty;
    public string SubjectName { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public int WeeklyHours { get; init; }
    public int PlacedHours { get; init; }
    public bool IsMain { get; init; }

    public bool HasTeacher => !string.IsNullOrWhiteSpace(TeacherId);
    public int RemainingHours => Math.Max(0, WeeklyHours - PlacedHours);
}

public sealed class LessonPlacementOutcome
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// 在 04 步课表格里手动放入 / 替换一节课。写入的课标记为固定课，
/// 重新生成课表时会保留并按已排课时扣除，不会被自动编排覆盖。
/// </summary>
public static class LessonPlacementService
{
    /// <summary>列出本班可放入的「学科 + 任课教师」。未建任课关系的学科也会列出，便于老师看到缺口。</summary>
    public static IReadOnlyList<LessonCandidate> GetCandidates(WeaveProject project, string classId)
    {
        ArgumentNullException.ThrowIfNull(project);

        var teachingPeriods = project.Periods
            .Where(period => !period.IsExtension)
            .Select(period => period.Index)
            .ToHashSet();
        var placed = project.Lessons
            .Where(lesson => lesson.ClassId == classId && teachingPeriods.Contains(lesson.Period))
            .GroupBy(lesson => lesson.SubjectId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(lesson => (lesson.DayIndex, lesson.Period)).Distinct().Count(),
                StringComparer.Ordinal);

        var candidates = new List<LessonCandidate>();
        var seen = new HashSet<(string Subject, string Teacher)>();
        foreach (var assignment in project.Assignments.Where(item => item.ClassId == classId))
        {
            var subject = project.FindSubject(assignment.SubjectId);
            if (subject is null) continue;
            if (!seen.Add((subject.Id, assignment.TeacherId))) continue;

            var teacher = project.FindTeacher(assignment.TeacherId);
            candidates.Add(new LessonCandidate
            {
                SubjectId = subject.Id,
                TeacherId = teacher?.Id ?? string.Empty,
                SubjectName = subject.Name,
                TeacherName = teacher?.Name
                               ?? (string.IsNullOrWhiteSpace(assignment.TeacherId)
                                   ? "未设置任课教师"
                                   : $"教师编号 {assignment.TeacherId} 不存在"),
                WeeklyHours = assignment.WeeklyHours,
                PlacedHours = placed.GetValueOrDefault(subject.Id),
                IsMain = subject.IsMain,
            });
        }

        // 还没有任课关系的学科：显示为「未设置任课教师」，放课后冲突检查会提示缺少教师。
        foreach (var subject in project.Subjects.Where(subject =>
                     candidates.All(candidate => candidate.SubjectId != subject.Id)))
        {
            candidates.Add(new LessonCandidate
            {
                SubjectId = subject.Id,
                SubjectName = subject.Name,
                TeacherName = "未设置任课教师",
                WeeklyHours = subject.WeeklyHours,
                PlacedHours = placed.GetValueOrDefault(subject.Id),
                IsMain = subject.IsMain,
            });
        }

        var order = project.Subjects
            .Select((subject, index) => (subject.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        return candidates
            .OrderBy(candidate => order.GetValueOrDefault(candidate.SubjectId, int.MaxValue))
            .ThenBy(candidate => candidate.TeacherName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>把学科与教师写入指定班级格；目标格原有普通课会被替换，活动与走班格不允许手动放课。</summary>
    public static LessonPlacementOutcome Place(
        WeaveProject project,
        string classId,
        int dayIndex,
        int period,
        string subjectId,
        string teacherId)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!TimetableService.IsConfirmed(project))
            return Fail("请先在 01 步「作息时间」里确认时间表，再手动编排课表。");

        var schoolClass = project.FindClass(classId);
        if (schoolClass is null) return Fail("找不到要放课的班级。");

        var subject = project.FindSubject(subjectId);
        if (subject is null) return Fail("找不到要放入的学科。");

        if (dayIndex is < 1 or > 5) return Fail("星期超出范围。");
        if (project.Periods.All(slot => slot.Index != period || slot.IsExtension))
            return Fail("节次不在正课编排范围内。");

        var slotText = WeaveDefaults.SlotText(dayIndex, period, period);
        var existing = project.Lessons
            .Where(lesson => lesson.ClassId == classId &&
                             lesson.DayIndex == dayIndex &&
                             lesson.Period == period)
            .ToList();

        if (existing.Any(lesson => lesson.IsActivity))
            return Fail($"{schoolClass.Name} {slotText} 是活动占位，请先在「活动」列表里处理。");

        var layeredLesson = existing.Any(lesson => lesson.IsLayered);
        LayeredClassGroup? layeredGroup = null;
        if (!layeredLesson)
            ConstraintRules.TryGetLayeredGroupAt(project, classId, dayIndex, period, out layeredGroup);

        if (layeredLesson || layeredGroup is not null)
        {
            var groupText = layeredGroup is null ? string.Empty : $"「{layeredGroup.Name}」";
            return Fail($"{schoolClass.Name} {slotText} 属于走班组{groupText}，由走班编排管理，不能手动放课。");
        }

        var previousSubject = existing
            .Select(lesson => project.FindSubject(lesson.SubjectId)?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        project.RemoveLessonAt(classId, dayIndex, period);

        var teacher = project.FindTeacher(teacherId);
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = classId,
            DayIndex = dayIndex,
            Period = period,
            SubjectId = subject.Id,
            TeacherId = teacher?.Id ?? string.Empty,
            IsFixed = true,
            Note = "手动放课",
        });

        var warnings = new List<string>();
        if (teacher is null)
        {
            warnings.Add("该学科还没有任课教师，冲突检查会提示缺少教师。");
        }
        else
        {
            var clashes = project.Lessons
                .Where(lesson => lesson.TeacherId == teacher.Id &&
                                 lesson.DayIndex == dayIndex &&
                                 lesson.Period == period &&
                                 lesson.ClassId != classId)
                .Select(lesson => project.FindClass(lesson.ClassId)?.Name ?? lesson.ClassId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (clashes.Count > 0)
                warnings.Add($"{teacher.Name} 在 {slotText} 同时还要上 {string.Join("、", clashes)} 的课。");

            var unavailable = ConstraintRules.TeacherUnavailable(project, teacher.Id, dayIndex, period);
            if (!string.IsNullOrWhiteSpace(unavailable))
                warnings.Add($"{teacher.Name} 在 {slotText} 有「{unavailable}」。");
        }

        var expected = project.Assignments
            .Where(assignment => assignment.ClassId == classId && assignment.SubjectId == subject.Id)
            .Sum(assignment => assignment.WeeklyHours);
        var actual = CountPlaced(project, classId, subject.Id);
        if (expected > 0 && actual > expected)
            warnings.Add($"{subject.Name} 本周已排 {actual} 节，超出应排 {expected} 节。");

        var head = previousSubject is null
            ? $"已把 {SubjectTeacherText(subject.Name, teacher?.Name)} 放入 {schoolClass.Name} {slotText}。"
            : $"已把 {schoolClass.Name} {slotText} 的 {previousSubject} 替换为 {SubjectTeacherText(subject.Name, teacher?.Name)}。";
        var tail = warnings.Count > 0 ? " " + string.Join(" ", warnings) : string.Empty;

        return new LessonPlacementOutcome
        {
            Success = true,
            Message = head + tail,
            Warnings = warnings,
        };
    }

    /// <summary>某班某学科本周已排节数（按天 × 节去重，走班 A/B 层只算一节）。</summary>
    public static int CountPlaced(WeaveProject project, string classId, string subjectId)
    {
        var teachingPeriods = project.Periods
            .Where(period => !period.IsExtension)
            .Select(period => period.Index)
            .ToHashSet();
        return project.Lessons
            .Where(lesson => lesson.ClassId == classId &&
                             lesson.SubjectId == subjectId &&
                             teachingPeriods.Contains(lesson.Period))
            .Select(lesson => (lesson.DayIndex, lesson.Period))
            .Distinct()
            .Count();
    }

    private static string SubjectTeacherText(string subjectName, string? teacherName) =>
        string.IsNullOrWhiteSpace(teacherName) ? subjectName : $"{subjectName} · {teacherName}";

    private static LessonPlacementOutcome Fail(string message) =>
        new() { Success = false, Message = message };
}
