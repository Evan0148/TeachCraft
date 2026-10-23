using System;
using System.Collections.Generic;

namespace ClassBlock.Core.Models;

public enum TimetableRowKind
{
    Normal,
    Break,
    Lunch,
    Dinner,
    News,
    Evening,
}

public sealed class TimetableRow
{
    /// <summary>时间列文字；晚修用两条时间。</summary>
    public IReadOnlyList<string> Times { get; init; } = Array.Empty<string>();

    public string Label { get; init; } = string.Empty;

    public TimetableRowKind Kind { get; init; } = TimetableRowKind.Normal;

    /// <summary>星期一到星期五的格子内容，可用 \n 换行。</summary>
    public IReadOnlyList<string> Cells { get; init; } = Array.Empty<string>();

    /// <summary>大课间 / 新闻联播 / 午餐 / 晚餐这类整行文字。</summary>
    public string MergedText { get; init; } = string.Empty;

    /// <summary>五天内容相同，合并为一格。</summary>
    public bool MergeDays { get; init; }

    public bool IsFullWidth => Kind is TimetableRowKind.Lunch or TimetableRowKind.Dinner;

    public bool MergesLabelAndDays => Kind is TimetableRowKind.Break or TimetableRowKind.News or TimetableRowKind.Lunch or TimetableRowKind.Dinner;
}

public sealed class ClassTimetable
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string HeadTeacher { get; init; } = string.Empty;
    public string ScheduleSheet { get; init; } = string.Empty;

    public IReadOnlyList<string> DayHeaders { get; init; } = new[] { "星期一", "星期二", "星期三", "星期四", "星期五" };
    public IReadOnlyList<TimetableRow> Rows { get; init; } = Array.Empty<TimetableRow>();
    public IReadOnlyList<string> Footnotes { get; init; } = Array.Empty<string>();
}

public sealed class TimetableSettings
{
    /// <summary>留空则使用「班级名 + 课程表」。</summary>
    public string Title { get; init; } = string.Empty;

    public bool ShowTeachers { get; init; }
    public bool ShowEarlyReading { get; init; } = true;
    public bool ShowLunchStudy { get; init; } = true;
    public bool ShowAfterSchool { get; init; } = true;
    public bool ShowEveningStudy { get; init; } = true;
}
