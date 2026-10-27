using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

/// <summary>
/// 按「作息模板」把总表里的一个班级组装成课程表：
/// 模板决定行序与时间，Slot 决定每行从总表绑定什么数据。
/// </summary>
public static class TimetableBuilder
{
    private static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    public static ClassTimetable Build(MasterSchedule master, string classKeyOrName, TimetableSettings? settings = null)
        => Build(master, classKeyOrName, TimetableTemplates.DefaultForGrade(master.Info.Grade), settings);

    public static ClassTimetable Build(
        MasterSchedule master,
        string classKeyOrName,
        TimetableTemplate template,
        TimetableSettings? settings = null)
    {
        settings ??= new TimetableSettings();

        var cls = master.FindClass(classKeyOrName)
                  ?? throw new ClassBlockException("总表里没有这个班级，请重新选择。");

        var headTeacher = master.HeadTeachers.TryGetValue(cls.Key, out var found) ? found : string.Empty;
        var rows = new List<TimetableRow>();

        foreach (var spec in template.Rows)
        {
            if (!IsEnabled(settings, spec.Slot)) continue;

            var row = BuildRow(master, cls, spec, settings);
            if (row is not null) rows.Add(row);
        }

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

    /// <summary>显示开关只过滤绑定行；静态行与结构行（大课间/午餐/晚餐/新闻联播）始终保留。</summary>
    private static bool IsEnabled(TimetableSettings settings, TemplateSlot slot) => slot switch
    {
        TemplateSlot.EarlyReading => settings.ShowEarlyReading,
        TemplateSlot.LunchStudy => settings.ShowLunchStudy,
        TemplateSlot.AfterSchool1 or TemplateSlot.AfterSchool2 => settings.ShowAfterSchool,
        TemplateSlot.Evening => settings.ShowEveningStudy,
        _ => true,
    };

    private static TimetableRow? BuildRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec, TimetableSettings settings)
    {
        return spec.Slot switch
        {
            TemplateSlot.Period1 => PeriodRow(cls, spec, 0, settings.ShowTeachers),
            TemplateSlot.Period2 => PeriodRow(cls, spec, 1, settings.ShowTeachers),
            TemplateSlot.Period3 => PeriodRow(cls, spec, 2, settings.ShowTeachers),
            TemplateSlot.Period4 => PeriodRow(cls, spec, 3, settings.ShowTeachers),
            TemplateSlot.Period5 => PeriodRow(cls, spec, 4, settings.ShowTeachers),
            TemplateSlot.Period6 => PeriodRow(cls, spec, 5, settings.ShowTeachers),
            TemplateSlot.Period7 => PeriodRow(cls, spec, 6, settings.ShowTeachers),
            TemplateSlot.EarlyReading => EarlyReadingRow(master, spec),
            TemplateSlot.LunchStudy => LunchStudyRow(master, cls, spec),
            TemplateSlot.AfterSchool1 => ServiceRow(master, cls, spec, first: true, settings.ShowTeachers),
            TemplateSlot.AfterSchool2 => ServiceRow(master, cls, spec, first: false, settings.ShowTeachers),
            TemplateSlot.Evening => EveningRow(cls.Days, spec),
            _ => StaticRow(spec),
        };
    }

    private static TimetableRow PeriodRow(ClassSchedule cls, TemplateRowSpec spec, int periodIndex, bool showTeachers) => new()
    {
        Times = spec.Times,
        Label = spec.Label,
        Kind = TimetableRowKind.Normal,
        Cells = Enumerable.Range(0, 5)
            .Select(day => CourseText(cls.Days, day, periodIndex, showTeachers))
            .ToArray(),
    };

    private static TimetableRow? EarlyReadingRow(MasterSchedule master, TemplateRowSpec spec)
    {
        var cells = Enumerable.Range(0, 5)
            .Select(i => i < master.EarlyReading.Count ? Normalize(master.EarlyReading[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return null;

        return new TimetableRow { Times = spec.Times, Label = spec.Label, Kind = spec.Kind, Cells = cells };
    }

    private static TimetableRow? LunchStudyRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec)
    {
        if (!master.LunchStudy.TryGetValue(cls.Key, out var lunch) || lunch.Count == 0) return null;

        var cells = Enumerable.Range(0, 5)
            .Select(i => i < lunch.Count ? FormatTeacher(lunch[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return null;

        return new TimetableRow { Times = spec.Times, Label = spec.Label, Kind = spec.Kind, Cells = cells };
    }

    private static TimetableRow? ServiceRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec, bool first, bool showTeachers)
    {
        var cells = Enumerable.Range(0, 5)
            .Select(day => ServiceText(master, cls, day, first, showTeachers))
            .ToArray();

        if (cells.All(t => t.Length == 0)) return null;

        return new TimetableRow
        {
            Times = spec.Times,
            Label = spec.Label,
            Kind = spec.Kind,
            Cells = cells,
            MergeDays = Mergeable(cells),
        };
    }

    private static TimetableRow EveningRow(IReadOnlyList<DaySchedule> days, TemplateRowSpec spec) => new()
    {
        Times = spec.Times,
        Label = spec.Label,
        Kind = TimetableRowKind.Evening,
        Cells = Enumerable.Range(0, 5)
            .Select(day => day < days.Count ? FormatTeacher(days[day].EveningStudy) : string.Empty)
            .ToArray(),
    };

    private static TimetableRow StaticRow(TemplateRowSpec spec)
    {
        if (spec.Kind is TimetableRowKind.Break or TimetableRowKind.News or TimetableRowKind.Lunch or TimetableRowKind.Dinner)
        {
            return new TimetableRow
            {
                Times = spec.Times,
                Label = spec.Label,
                Kind = spec.Kind,
                MergedText = spec.StaticText,
            };
        }

        return new TimetableRow
        {
            Times = spec.Times,
            Label = spec.Label,
            Kind = spec.Kind,
            Cells = Enumerable.Repeat(spec.StaticText, 5).ToArray(),
            MergeDays = spec.MergeDays,
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
