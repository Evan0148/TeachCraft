using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

/// <summary>
/// 把总表里的一个班级组装成参考图样式的固定课程表：
/// 早读、1–7 节、大课间、午餐、午学、课后服务、晚餐、新闻联播、晚修。
/// </summary>
public static class TimetableBuilder
{
    private static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    private static readonly (string Time, string Label)[] PeriodSlots =
    {
        ("8:00—8:40", "1"),
        ("8:50—9:30", "2"),
        ("10:00—10:40", "3"),
        ("10:50—11:30", "4"),
        ("14:20—15:00", "5"),
        ("15:10—15:50", "6"),
        ("16:00—16:40", "7"),
    };

    public static ClassTimetable Build(MasterSchedule master, string classKeyOrName, TimetableSettings? settings = null)
    {
        settings ??= new TimetableSettings();

        var cls = master.FindClass(classKeyOrName)
                  ?? throw new ClassBlockException("总表里没有这个班级，请重新选择。");

        var headTeacher = master.HeadTeachers.TryGetValue(cls.Key, out var found) ? found : string.Empty;
        var days = cls.Days;
        var rows = new List<TimetableRow>();

        AddEarlyReading(rows, master, settings);
        AddPeriods(rows, master, cls, settings);
        AddAfterSchool(rows, master, cls, settings);
        AddEveningStudy(rows, days, settings);

        var title = string.IsNullOrWhiteSpace(settings.Title) ? $"{cls.Name}课程表" : settings.Title.Trim();

        return new ClassTimetable
        {
            Title = title,
            Subtitle = BuildSubtitle(master, headTeacher),
            ClassName = cls.Name,
            HeadTeacher = headTeacher,
            ScheduleSheet = master.Info.ScheduleSheet,
            DayHeaders = DayHeaders,
            Rows = rows,
            Footnotes = BuildFootnotes(master, settings),
        };
    }

    private static string BuildSubtitle(MasterSchedule master, string headTeacher)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(master.Info.Term)) parts.Add(master.Info.Term);
        if (!string.IsNullOrWhiteSpace(master.Info.Month)) parts.Add(master.Info.Month);
        if (!string.IsNullOrWhiteSpace(headTeacher)) parts.Add($"班主任 {headTeacher}");
        return string.Join(" · ", parts);
    }

    private static void AddEarlyReading(List<TimetableRow> rows, MasterSchedule master, TimetableSettings settings)
    {
        if (!settings.ShowEarlyReading) return;

        var cells = Enumerable.Range(0, 5)
            .Select(i => i < master.EarlyReading.Count ? Normalize(master.EarlyReading[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return;

        rows.Add(new TimetableRow
        {
            Times = new[] { "7:20—7:50" },
            Label = "早读",
            Kind = TimetableRowKind.Normal,
            Cells = cells,
        });
    }

    private static void AddPeriods(List<TimetableRow> rows, MasterSchedule master, ClassSchedule cls, TimetableSettings settings)
    {
        var days = cls.Days;
        for (var index = 0; index < PeriodSlots.Length; index++)
        {
            var (time, label) = PeriodSlots[index];
            rows.Add(new TimetableRow
            {
                Times = new[] { time },
                Label = label,
                Kind = TimetableRowKind.Normal,
                Cells = Enumerable.Range(0, 5)
                    .Select(day => CourseText(days, day, index, settings.ShowTeachers))
                    .ToArray(),
            });

            if (index == 1)
            {
                rows.Add(new TimetableRow
                {
                    Times = new[] { "9:30—10:00" },
                    Kind = TimetableRowKind.Break,
                    MergedText = "大课间",
                });
            }
            else if (index == 3)
            {
                rows.Add(new TimetableRow
                {
                    Kind = TimetableRowKind.Lunch,
                    MergedText = "11:30—12:00　午餐",
                });

                AddLunchStudy(rows, master, cls.Key, settings);
            }
        }
    }

    private static void AddLunchStudy(List<TimetableRow> rows, MasterSchedule master, string key, TimetableSettings settings)
    {
        if (!settings.ShowLunchStudy) return;
        if (!master.LunchStudy.TryGetValue(key, out var lunch) || lunch.Count == 0) return;

        var cells = Enumerable.Range(0, 5)
            .Select(i => i < lunch.Count ? FormatTeacher(lunch[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return;

        rows.Add(new TimetableRow
        {
            Times = new[] { "12:00—12:30" },
            Label = "午学",
            Kind = TimetableRowKind.Normal,
            Cells = cells,
        });
    }

    private static void AddAfterSchool(List<TimetableRow> rows, MasterSchedule master, ClassSchedule cls, TimetableSettings settings)
    {
        if (settings.ShowAfterSchool)
        {
            var first = Enumerable.Range(0, 5)
                .Select(day => ServiceText(master, cls, day, first: true, settings.ShowTeachers))
                .ToArray();
            var second = Enumerable.Range(0, 5)
                .Select(day => ServiceText(master, cls, day, first: false, settings.ShowTeachers))
                .ToArray();

            if (first.Any(t => t.Length > 0))
            {
                rows.Add(new TimetableRow
                {
                    Times = new[] { "16:50—17:20" },
                    Label = "课服1",
                    Kind = TimetableRowKind.Normal,
                    Cells = first,
                    MergeDays = Mergeable(first),
                });
            }

            if (second.Any(t => t.Length > 0))
            {
                rows.Add(new TimetableRow
                {
                    Times = new[] { "17:20—17:50" },
                    Label = "课服2",
                    Kind = TimetableRowKind.Normal,
                    Cells = second,
                    MergeDays = Mergeable(second),
                });
            }
        }

        rows.Add(new TimetableRow
        {
            Kind = TimetableRowKind.Dinner,
            MergedText = "17:50—18:50　晚餐",
        });
    }

    private static void AddEveningStudy(List<TimetableRow> rows, IReadOnlyList<DaySchedule> days, TimetableSettings settings)
    {
        if (!settings.ShowEveningStudy) return;

        rows.Add(new TimetableRow
        {
            Times = new[] { "19:00—19:20" },
            Kind = TimetableRowKind.News,
            MergedText = "新闻联播",
        });

        rows.Add(new TimetableRow
        {
            Times = new[] { "19:20—20:20", "20:30—21:30" },
            Label = "晚修",
            Kind = TimetableRowKind.Evening,
            Cells = Enumerable.Range(0, 5)
                .Select(day => day < days.Count ? FormatTeacher(days[day].EveningStudy) : string.Empty)
                .ToArray(),
        });
    }

    private static string CourseText(IReadOnlyList<DaySchedule> days, int day, int period, bool showTeachers)
    {
        if (day >= days.Count || period >= days[day].Periods.Count) return string.Empty;

        var cell = days[day].Periods[period];
        if (cell.IsEmpty) return string.Empty;

        if (!showTeachers || string.IsNullOrWhiteSpace(cell.Teacher)) return cell.Subject;
        return $"{cell.Subject}\n{cell.Teacher}";
    }

    private static string ServiceText(MasterSchedule master, ClassSchedule cls, int day, bool first, bool showTeachers)
    {
        if (day >= cls.Days.Count) return string.Empty;

        var cell = first ? cls.Days[day].AfterSchool1 : cls.Days[day].AfterSchool2;
        if (cell.IsEmpty) return string.Empty;

        var teacher = cell.Teacher;
        if (string.IsNullOrWhiteSpace(teacher) &&
            master.AfterSchool.TryGetValue(cls.Key, out var service) &&
            day < service.Count)
        {
            teacher = first ? service[day].Service1 : service[day].Service2;
        }

        if (!showTeachers || string.IsNullOrWhiteSpace(teacher)) return cell.Subject;
        return $"{cell.Subject}\n{teacher}";
    }

    /// <summary>午学、晚修的教师姓名补上「老师」，并保留「详见附件」这类说明。</summary>
    private static string FormatTeacher(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        var name = Regex.Replace(text, @"\s+", string.Empty);
        if (text.EndsWith("老师", StringComparison.Ordinal) ||
            name.Contains("详见") ||
            name.Contains("附件") ||
            name.Contains('（') ||
            name.Contains('('))
        {
            return text;
        }

        return IsChineseName(name) ? $"{name}老师" : text;
    }

    private static bool IsChineseName(string value) =>
        value.Length is >= 2 and <= 4 && value.All(c => c >= '\u4e00' && c <= '\u9fff');

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim(), @"\s*/\s*", "/");

    private static bool Mergeable(IReadOnlyList<string> cells)
    {
        if (cells.Count == 0 || cells.Any(string.IsNullOrWhiteSpace)) return false;
        return cells.All(c => string.Equals(c, cells[0], StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BuildFootnotes(MasterSchedule master, TimetableSettings settings)
    {
        var notes = new List<string>();
        if (settings.ShowEarlyReading && !string.IsNullOrWhiteSpace(master.Info.EarlyReadingRule))
            notes.Add($"早读：{Normalize(master.Info.EarlyReadingRule)}");

        return notes;
    }
}
