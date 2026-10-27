using GradeWeave.Core.Models;

namespace GradeWeave.Core.Scheduling;

/// <summary>编排引擎与冲突检查共用同一套判定，避免自动排课结果与冲突报告互相矛盾。</summary>
public static class ConstraintRules
{
    public static string? TeacherUnavailable(WeaveProject project, string teacherId, int dayIndex, int period) =>
        project.UnavailableReason(teacherId, dayIndex, period);

    public static bool TryGetTeacherLesson(
        WeaveProject project, string teacherId, int dayIndex, int period, out LessonPlacement? lesson)
    {
        lesson = project.Lessons.FirstOrDefault(l =>
            l.TeacherId == teacherId && l.DayIndex == dayIndex && l.Period == period);
        return lesson is not null;
    }

    public static bool TryGetClassLesson(
        WeaveProject project, string classId, int dayIndex, int period, out LessonPlacement? lesson)
    {
        lesson = project.Lessons.FirstOrDefault(l =>
            l.ClassId == classId && l.DayIndex == dayIndex && l.Period == period);
        return lesson is not null;
    }

    public static bool TryGetLayeredGroupAt(
        WeaveProject project, string classId, int dayIndex, int period, out LayeredClassGroup? group)
    {
        group = project.LayeredGroups.FirstOrDefault(g =>
            g.ClassIds.Contains(classId, StringComparer.Ordinal) && IsGroupAt(g, dayIndex, period));
        return group is not null;
    }

    /// <summary>走班组是否占用指定时段：固定组看配置区间，自动安排组看生成后的落位。</summary>
    public static bool IsGroupAt(LayeredClassGroup group, int dayIndex, int period) =>
        group.IsFlexible
            ? group.ResolvedSlots.Contains(LayeredClassGroup.EncodeSlot(dayIndex, period))
            : group.DayIndex == dayIndex && period >= group.StartPeriod && period <= group.EndPeriod;

    public static IEnumerable<int> PeriodsOf(LayeredClassGroup group) => group.Periods;

    /// <summary>走班时段已经消耗的课时数（每个参与班每节算 1 课时）。自动安排组按已落位槽位计。</summary>
    public static int LayeredHours(WeaveProject project, string classId, string subjectId)
    {
        var total = 0;
        foreach (var group in project.LayeredGroups)
        {
            if (group.SubjectId != subjectId ||
                !group.ClassIds.Contains(classId, StringComparer.Ordinal))
                continue;

            total += group.IsFlexible
                ? group.ResolvedSlots.Count
                : Math.Max(0, group.EndPeriod - group.StartPeriod + 1);
        }

        return total;
    }

    /// <summary>某班某学科还需要自动编排的课时。</summary>
    public static int RemainingDemand(WeaveProject project, TeachingAssignment assignment) =>
        Math.Max(0, assignment.WeeklyHours - LayeredHours(project, assignment.ClassId, assignment.SubjectId));

    /// <summary>某班某学科在已排课表中的节数（不含走班占位）。</summary>
    public static int PlacedCount(WeaveProject project, string classId, string subjectId) =>
        project.Lessons.Count(l =>
            l.ClassId == classId && l.SubjectId == subjectId && !l.IsLayered);
}
