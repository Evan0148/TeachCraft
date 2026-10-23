using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassBlock.Core.Models;

public sealed class MasterInfo
{
    public string Title { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public string Term { get; init; } = string.Empty;
    public string Month { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string ScheduleSheet { get; init; } = string.Empty;
    public string EarlyReadingRule { get; init; } = string.Empty;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Grade)) parts.Add(Grade);
            if (!string.IsNullOrWhiteSpace(Term)) parts.Add(Term);
            if (!string.IsNullOrWhiteSpace(Month)) parts.Add(Month);
            return string.Join(" · ", parts);
        }
    }
}

public sealed class CourseCell
{
    public static readonly CourseCell Empty = new();

    public string Raw { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Teacher { get; init; } = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Subject) && string.IsNullOrWhiteSpace(Teacher);

    public string Display => string.IsNullOrWhiteSpace(Teacher) ? Subject : $"{Subject}\n{Teacher}";
}

public sealed class DaySchedule
{
    public string DayName { get; init; } = string.Empty;
    public IReadOnlyList<CourseCell> Periods { get; init; } = Array.Empty<CourseCell>();
    public CourseCell AfterSchool1 { get; init; } = CourseCell.Empty;
    public CourseCell AfterSchool2 { get; init; } = CourseCell.Empty;
    public string EveningStudy { get; init; } = string.Empty;
}

public sealed class ClassSchedule
{
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public int Number { get; init; }
    public int Row { get; init; }
    public IReadOnlyList<DaySchedule> Days { get; init; } = Array.Empty<DaySchedule>();
}

public sealed class DayService
{
    public string Service1 { get; init; } = string.Empty;
    public string Service2 { get; init; } = string.Empty;
    public string Evening { get; init; } = string.Empty;
}

public sealed class MasterSchedule
{
    public required MasterInfo Info { get; init; }
    public required IReadOnlyList<ClassSchedule> Classes { get; init; }
    public required IReadOnlyDictionary<string, string> HeadTeachers { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> LunchStudy { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<DayService>> AfterSchool { get; init; }
    public required IReadOnlyList<string> EarlyReading { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public ClassSchedule? FindClass(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey)) return null;

        return Classes.FirstOrDefault(c =>
            c.Key == nameOrKey ||
            c.Name == nameOrKey ||
            c.Name.Replace(" ", string.Empty) == nameOrKey.Replace(" ", string.Empty));
    }
}

public static class CourseParser
{
    private static readonly string[] Subjects = new[]
    {
        "主题班会", "劳动实践", "培优辅差", "阳光体育", "道德与法治", "综合实践",
        "心理健康", "地方课程", "信息技术", "班会", "自习", "阅读", "写字",
        "语文", "数学", "英语", "物理", "化学", "道法", "历史", "体育",
        "音乐", "美术", "信息", "劳动", "科学", "综合", "早读", "午学",
    }.OrderByDescending(s => s.Length).ToArray();

    public static CourseCell Parse(string? raw)
    {
        var text = (raw ?? string.Empty).Trim().Replace("\r\n", "\n");
        if (text.Length == 0) return CourseCell.Empty;

        foreach (var subject in Subjects)
        {
            if (!text.StartsWith(subject, StringComparison.Ordinal)) continue;

            var teacher = text[subject.Length..].Trim().TrimStart('：', ':', '\n', ' ', '\t');
            return new CourseCell
            {
                Raw = text,
                Subject = subject,
                Teacher = teacher,
            };
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length >= 2)
        {
            return new CourseCell
            {
                Raw = text,
                Subject = lines[0],
                Teacher = string.Join(" ", lines.Skip(1)),
            };
        }

        return new CourseCell { Raw = text, Subject = text };
    }
}

public static class ClassKey
{
    public static bool TryParse(string? value, string defaultGrade, out string name, out string key, out string grade, out int number)
    {
        name = string.Empty;
        key = string.Empty;
        grade = string.Empty;
        number = 0;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^\s*(?<grade>[七八九])?\s*(?:年级)?\s*[（(]?\s*(?<num>\d{1,2})\s*[）)]?\s*班\s*$");

        if (!match.Success) return false;

        var g = match.Groups["grade"].Success ? match.Groups["grade"].Value : defaultGrade;
        if (g.Length == 0) return false;

        var num = int.Parse(match.Groups["num"].Value);
        grade = g + "年级";
        number = num;
        name = $"{g}（{num}）班";
        key = $"{g}{num}";
        return true;
    }
}
