using System.Globalization;
using System.Text.RegularExpressions;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Scheduling;

/// <summary>
/// 把「10:50—11:10」这样的钟点范围映射到作息节次，并把「替换式活动」落成固定占位课。
/// 叠加式活动不写课程，只在界面与导出里多显示一行提醒。
/// </summary>
public static class ActivityResolver
{
    private static readonly Regex ClockPattern =
        new(@"(\d{1,2})\s*[:：.]\s*(\d{2})", RegexOptions.Compiled);

    /// <summary>解析单个钟点，接受 10:50、10：50、10.50、1050 等写法。</summary>
    public static bool TryParseTime(string? raw, out TimeSpan time)
    {
        time = default;
        var text = Normalize(raw);
        if (text.Length == 0) return false;

        var parts = text.Split(':');
        int hour;
        int minute;
        if (parts.Length is 2 or 3)
        {
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute) ||
                (parts.Length == 3 &&
                 !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return false;
        }
        else if (parts.Length == 1 && text.Length is 3 or 4 && int.TryParse(text, out var digits))
        {
            hour = digits / 100;
            minute = digits % 100;
        }
        else
        {
            return false;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;
        time = new TimeSpan(hour, minute, 0);
        return true;
    }

    /// <summary>解析「10:50—11:10」这类范围；也接受「至」「到」「~」等分隔。</summary>
    public static bool TryParseRange(string? raw, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;
        var text = Normalize(raw);
        if (text.Length == 0) return false;

        var parts = text.Split('-');
        if (parts.Length == 2 && TryParseTime(parts[0], out start) && TryParseTime(parts[1], out end))
            return true;

        // 节次时间可能带「上午」等前缀，退一步只抓两个钟点。
        var matches = ClockPattern.Matches(text);
        if (matches.Count < 2) return false;
        if (!TryParseTime(matches[0].Value, out start) || !TryParseTime(matches[1].Value, out end))
            return false;
        return true;
    }

    /// <summary>活动落在哪些正课节次；手动指定节次时优先用该节。</summary>
    public static List<int> ResolvePeriods(WeaveProject project, ActivityBlock activity)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(activity);

        var teaching = project.Periods.Where(p => !p.IsExtension).Select(p => p.Index).ToHashSet();
        if (activity.Period > 0 && teaching.Contains(activity.Period)) return [activity.Period];
        return OverlappingPeriods(project, activity.StartTime, activity.EndTime);
    }

    /// <summary>钟点范围与哪些正课节次有实际重叠（端点相接不算）。</summary>
    public static List<int> OverlappingPeriods(WeaveProject project, string? startTime, string? endTime)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!TryParseTime(startTime, out var start) || !TryParseTime(endTime, out var end) || end <= start)
            return [];

        var result = new List<int>();
        foreach (var slot in project.Periods.Where(p => !p.IsExtension).OrderBy(p => p.Index))
        {
            if (!TryParseRange(slot.TimeText, out var slotStart, out var slotEnd)) continue;
            if (Math.Min(end.Ticks, slotEnd.Ticks) <= Math.Max(start.Ticks, slotStart.Ticks)) continue;
            result.Add(slot.Index);
        }

        return result;
    }

    /// <summary>界面上「对应第4节（10:40—11:20）」的实时提示。</summary>
    public static string DescribeRange(WeaveProject project, string? startTime, string? endTime)
    {
        if (!TryParseTime(startTime, out var start) || !TryParseTime(endTime, out var end))
            return "时间格式应为 10:50 和 11:10";

        if (end <= start) return "结束时间要晚于开始时间";

        var periods = OverlappingPeriods(project, startTime, endTime);
        if (periods.Count == 0) return "未落在正课节次内，可手动指定归属节次";

        var labels = periods.Select(period => WeaveDefaults.PeriodText(project, period));
        return $"对应 {string.Join("、", labels)}（{Format(start)}—{Format(end)}）";
    }

    /// <summary>活动的钟点文案；解析失败时原样返回，避免界面出现空白。</summary>
    public static string TimeText(ActivityBlock activity)
    {
        if (!TryParseTime(activity.StartTime, out var start) || !TryParseTime(activity.EndTime, out var end))
            return $"{activity.StartTime}—{activity.EndTime}";
        return $"{Format(start)}—{Format(end)}";
    }

    public static List<int> PeriodsOf(WeaveProject project, ActivityBlock activity) =>
        ResolvePeriods(project, activity);

    /// <summary>该班是否属于活动的适用对象；活动未指定班级时视为全年级。</summary>
    public static bool Targets(WeaveProject project, ActivityBlock activity, string classId) =>
        activity.ClassIds.Count == 0 || activity.ClassIds.Contains(classId, StringComparer.Ordinal);

    /// <summary>某班某天某节上的活动；走班等扩展时段不会命中。</summary>
    public static IReadOnlyList<ActivityBlock> ActivitiesAt(
        WeaveProject project, string classId, int dayIndex, int period, ActivityMode mode)
    {
        var result = new List<ActivityBlock>();
        foreach (var activity in project.Activities)
        {
            if (activity.Mode != mode || activity.DayIndex != dayIndex) continue;
            if (!Targets(project, activity, classId)) continue;
            if (!ResolvePeriods(project, activity).Contains(period)) continue;
            result.Add(activity);
        }

        return result;
    }

    /// <summary>
    /// 把替换式活动写进课表：移除重叠节次的原课，插入固定占位课。
    /// 叠加式活动不动任何课程。编排前调用本方法，保证重排后活动仍然占位。
    /// </summary>
    public static void Apply(WeaveProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        project.Lessons.RemoveAll(lesson => lesson.IsActivity);

        foreach (var activity in project.Activities.Where(item => item.Mode == ActivityMode.Replace))
        {
            var periods = ResolvePeriods(project, activity).Distinct().ToList();
            if (periods.Count == 0) continue;

            var classIds = activity.ClassIds.Count == 0
                ? project.Classes.Select(item => item.Id).ToList()
                : activity.ClassIds
                    .Where(id => project.FindClass(id) is not null)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            var timeText = TimeText(activity);
            foreach (var classId in classIds)
            {
                foreach (var period in periods)
                {
                    project.Lessons.RemoveAll(lesson =>
                        lesson.ClassId == classId &&
                        lesson.DayIndex == activity.DayIndex &&
                        lesson.Period == period);

                    project.Lessons.Add(new LessonPlacement
                    {
                        ClassId = classId,
                        DayIndex = activity.DayIndex,
                        Period = period,
                        TeacherId = activity.TeacherId,
                        IsActivity = true,
                        IsFixed = true,
                        ActivityId = activity.Id,
                        ActivityName = activity.Name,
                        ActivityTime = timeText,
                        Note = activity.Note,
                    });
                }
            }
        }
    }

    public static string Format(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}";

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var builder = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            var normalized = ch switch
            {
                >= '０' and <= '９' => (char)(ch - '０' + '0'),
                '：' or '．' => ':',
                '.' => ':',
                '—' or '–' or '－' or '~' or '～' or '〜' => '-',
                ' ' or '\u3000' or '\t' => '\0',
                _ => ch,
            };

            if (normalized == '\0') continue;
            if (normalized == '至' || normalized == '到') builder.Append('-');
            else builder.Append(normalized);
        }

        return builder.ToString();
    }
}
