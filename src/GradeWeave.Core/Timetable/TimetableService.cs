using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Core.Timetable;

public sealed class TimetableValidationResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public int LessonCount { get; set; }
    public int ExtensionCount { get; set; }

    public bool IsValid => Errors.Count == 0;

    public string Summary => LessonCount == 0
        ? "还没有正课节次"
        : $"每天 {LessonCount} 节正课" +
          (ExtensionCount > 0 ? $" · {ExtensionCount} 个扩展时段" : string.Empty);
}

public sealed class TimetableConfirmResult
{
    public bool Confirmed { get; set; }
    public int RemovedLessons { get; set; }
    public List<string> Messages { get; } = [];
    public List<PeriodSlot> Periods { get; } = [];
    public TimetableValidationResult Validation { get; set; } = new();
}

/// <summary>
/// 作息时间表的校验、编译与确认。确认后把作息里的正课 / 课服 / 晚修编译成
/// <see cref="WeaveProject.Periods"/>，编排器只认这份节次；内容再改动即自动失效。
/// </summary>
public static class TimetableService
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    /// <summary>工程没有时间表时，从现有节次还原一份未确认的时间表。</summary>
    public static WeaveTimetable Ensure(WeaveProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Timetable is { Rows.Count: > 0 }) return project.Timetable;

        var periods = project.Periods.Count > 0 ? project.Periods : WeaveDefaults.CreatePeriods();
        project.Timetable = WeaveTimetables.FromPeriods(periods);
        return project.Timetable;
    }

    public static bool IsConfirmed(WeaveProject project) =>
        project.Timetable is { } timetable && timetable.IsConfirmed;

    public static TimetableValidationResult Validate(WeaveTimetable? timetable)
    {
        var result = new TimetableValidationResult();
        if (timetable is null)
        {
            result.Errors.Add("还没有作息时间表，请先添加节次。");
            return result;
        }

        var rows = timetable.Rows.Where(row => row is not null).ToList();
        if (rows.Count == 0)
        {
            result.Errors.Add("作息时间表还是空的，请先添加至少一节正课。");
            return result;
        }

        var parsed = new List<ParsedRow>();
        foreach (var row in rows)
        {
            var name = WeaveTimetables.DisplayName(row);
            if (!ActivityResolver.TryParseTime(row.StartTime, out var start))
            {
                result.Errors.Add($"「{name}」的开始时间「{row.StartTime}」不是有效时间，请按 08:00 填写。");
                continue;
            }

            if (!ActivityResolver.TryParseTime(row.EndTime, out var end))
            {
                result.Errors.Add($"「{name}」的结束时间「{row.EndTime}」不是有效时间，请按 08:40 填写。");
                continue;
            }

            if (end <= start)
            {
                result.Errors.Add($"「{name}」的结束时间 {ActivityResolver.Format(end)} 要晚于开始时间 {ActivityResolver.Format(start)}。");
                continue;
            }

            if (end - start < TimeSpan.FromMinutes(1))
            {
                result.Errors.Add($"「{name}」不足 1 分钟，请把时间拉长。");
                continue;
            }

            parsed.Add(new ParsedRow(row, start, end));
        }

        result.LessonCount = rows.Count(row => row.Kind == TimetableRowKind.Lesson);
        result.ExtensionCount = rows.Count(row => WeaveTimetables.IsExtension(row.Kind));

        if (result.LessonCount == 0)
            result.Errors.Add("至少需要一节正课；早读、课间、午餐等不能替代正课。");
        else if (result.LessonCount > 15)
            result.Errors.Add($"每天正课 {result.LessonCount} 节过多，请控制在 15 节以内。");

        if (result.ExtensionCount > 8)
            result.Errors.Add($"扩展时段 {result.ExtensionCount} 个过多，请控制在 8 个以内。");

        var ordered = parsed
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .ThenBy(item => item.Row.Id, StringComparer.Ordinal)
            .ToList();

        var originalOrder = parsed.Select(item => item.Row.Id).ToList();
        if (!originalOrder.SequenceEqual(ordered.Select(item => item.Row.Id)))
            result.Warnings.Add("保存时会按开始时间从早到晚重新排列时间表。");

        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (current.Start >= previous.End) continue;

            result.Errors.Add(
                $"「{WeaveTimetables.DisplayName(previous.Row)}」与" +
                $"「{WeaveTimetables.DisplayName(current.Row)}」时间重叠：" +
                $"{ActivityResolver.Format(previous.End)} 早于对方开始 {ActivityResolver.Format(current.Start)}。");
        }

        if (result.IsValid && result.ExtensionCount > 0)
        {
            var lastLesson = ordered.LastOrDefault(item => WeaveTimetables.IsTeaching(item.Row.Kind));
            var firstExtension = ordered.FirstOrDefault(item => WeaveTimetables.IsExtension(item.Row.Kind));
            if (lastLesson is not null && firstExtension is not null && firstExtension.Start < lastLesson.Start)
                result.Warnings.Add("扩展时段早于正课开始；课服 / 晚修通常排在正课之后。");
        }

        return result;
    }

    /// <summary>把作息编译成正课在前、扩展时段在后的节次表；无效时间会拒绝。</summary>
    public static List<PeriodSlot> BuildPeriods(WeaveTimetable timetable)
    {
        var validation = Validate(timetable);
        if (!validation.IsValid)
            throw new GradeWeaveException($"作息时间表还不能使用：{string.Join("；", validation.Errors.Take(3))}");

        var ordered = WeaveTimetables.OrderRows(timetable.Rows);
        var lessons = ordered.Where(row => row.Kind == TimetableRowKind.Lesson).ToList();
        var extensions = ordered.Where(row => WeaveTimetables.IsExtension(row.Kind)).ToList();

        var periods = new List<PeriodSlot>();
        for (var index = 0; index < lessons.Count; index++)
        {
            var row = lessons[index];
            ActivityResolver.TryParseTime(row.StartTime, out var start);
            ActivityResolver.TryParseTime(row.EndTime, out var end);
            periods.Add(new PeriodSlot
            {
                Index = index + 1,
                Name = string.IsNullOrWhiteSpace(row.Name) ? $"第{index + 1}节" : row.Name.Trim(),
                TimeText = $"{ActivityResolver.Format(start)}—{ActivityResolver.Format(end)}",
                IsAfternoon = start >= Noon,
                IsExtension = false,
            });
        }

        var afterSchoolIndex = 0;
        var eveningIndex = 0;
        for (var index = 0; index < extensions.Count; index++)
        {
            var row = extensions[index];
            ActivityResolver.TryParseTime(row.StartTime, out var start);
            ActivityResolver.TryParseTime(row.EndTime, out var end);
            var isEvening = row.Kind == TimetableRowKind.EveningStudy;
            var ordinate = isEvening ? ++eveningIndex : ++afterSchoolIndex;
            periods.Add(new PeriodSlot
            {
                Index = lessons.Count + index + 1,
                Name = string.IsNullOrWhiteSpace(row.Name)
                    ? isEvening ? $"晚修{ordinate}" : $"课服{ordinate}"
                    : row.Name.Trim(),
                TimeText = $"{ActivityResolver.Format(start)}—{ActivityResolver.Format(end)}",
                IsAfternoon = start >= Noon,
                IsExtension = true,
            });
        }

        return periods;
    }

    /// <summary>
    /// 确认作息并把节次应用到工程。超出新节次范围的课会被移除并如实报告；
    /// 教研、会议、走班、活动等引用越界时只提示，不静默改动。
    /// </summary>
    public static TimetableConfirmResult Confirm(WeaveProject project, WeaveTimetable? timetable = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var target = timetable ?? Ensure(project);
        var validation = Validate(target);
        var result = new TimetableConfirmResult { Validation = validation };
        if (!validation.IsValid)
            throw new GradeWeaveException($"作息时间表还不能确认：{string.Join("；", validation.Errors.Take(3))}");

        target.Rows = WeaveTimetables.OrderRows(target.Rows);
        var periods = BuildPeriods(target);
        var teaching = periods.Where(period => !period.IsExtension).Select(period => period.Index).ToHashSet();
        var allPeriods = periods.Select(period => period.Index).ToHashSet();

        var removed = project.Lessons.RemoveAll(lesson => !teaching.Contains(lesson.Period));
        result.RemovedLessons = removed;
        if (removed > 0)
            result.Messages.Add($"新作息只有 {teaching.Count} 节正课，已移除 {removed} 节超出范围的课。");

        foreach (var block in project.ResearchBlocks)
        {
            var outside = block.Periods.Where(period => !teaching.Contains(period)).ToList();
            if (outside.Count == 0) continue;
            result.Messages.Add(
                $"教研「{DescribeResearch(project, block)}」包含第 {string.Join("、", outside)} 节，已超出新正课节次。");
        }

        foreach (var block in project.MeetingBlocks)
        {
            var outside = block.Periods.Where(period => !allPeriods.Contains(period)).ToList();
            if (outside.Count == 0) continue;
            result.Messages.Add(
                $"会议「{block.Name}」包含第 {string.Join("、", outside)} 节，已超出新时间表的节次范围。");
        }

        foreach (var group in project.LayeredGroups.Where(group => !group.IsFlexible))
        {
            if (group.Periods.All(teaching.Contains)) continue;
            result.Messages.Add($"走班组「{group.Name}」的固定时段不在新正课节次内，请重新选择时段。");
        }

        foreach (var activity in project.Activities.Where(activity => activity.Period > 0 && !teaching.Contains(activity.Period)))
        {
            var oldPeriod = activity.Period;
            activity.Period = 0;
            result.Messages.Add($"活动「{activity.Name}」原归属第 {oldPeriod} 节已不存在，改为按时间自动匹配。");
        }

        project.Periods = periods;
        target.ConfirmedSignature = WeaveTimetables.Signature(target.Rows);
        project.Timetable = target;

        foreach (var activity in project.Activities)
        {
            if (ActivityResolver.ResolvePeriods(project, activity).Count > 0) continue;
            result.Messages.Add($"活动「{activity.Name}」的 {ActivityResolver.TimeText(activity)} 没有落在新正课节次内，请手动指定归属节次。");
        }

        project.Warnings.AddRange(result.Messages);
        result.Confirmed = true;
        result.Periods.AddRange(periods);
        result.Messages.Add(
            $"作息时间表已确认：每天 {teaching.Count} 节正课" +
            (periods.Count > teaching.Count ? $"、{periods.Count - teaching.Count} 个扩展时段" : string.Empty) +
            "。");
        return result;
    }

    private static string DescribeResearch(WeaveProject project, ResearchBlock block)
    {
        var names = block.SubjectIds
            .Select(id => project.FindSubject(id)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        return names.Count > 0 ? string.Join(" / ", names!) : "学科教研";
    }

    private sealed record ParsedRow(TimetableRow Row, TimeSpan Start, TimeSpan End);
}
