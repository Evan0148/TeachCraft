namespace MeetRoster.Core.Models;

/// <summary>运动会项目分类，决定赛程表的标签与配色。</summary>
public enum MeetEventCategory
{
    Track,
    Field,
    Team,
    Fun,
    Ceremony,
    Other,
}

/// <summary>一份运动会工程：报名班级、项目目录与赛程。</summary>
public sealed class MeetProject
{
    public string Title { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public List<MeetEvent> Events { get; } = [];
    public List<MeetClass> Classes { get; } = [];
    public List<MeetDay> Days { get; } = [];
}

/// <summary>项目目录中的一项比赛。</summary>
public sealed class MeetEvent
{
    public required string Name { get; init; }
    public string Gender { get; init; } = "不限";
    public MeetEventCategory Category { get; init; } = MeetEventCategory.Other;
}

/// <summary>一个参赛班级的报名信息。</summary>
public sealed class MeetClass
{
    public required string Name { get; init; }
    public string Group { get; init; } = string.Empty;
    public string Code { get; set; } = "（不填）";
    public string LeaderContact { get; set; } = string.Empty;
    public string Coach { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    /// <summary>导入来源文件；用于按班回写时保留原模板样式。</summary>
    public string? SourcePath { get; set; }
    public List<Athlete> Athletes { get; } = [];
}

/// <summary>一名运动员，字段与报名表模板一一对应。</summary>
public sealed class Athlete
{
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = "男子";
    public string TeamEvent1 { get; set; } = string.Empty;
    public string TeamEvent2 { get; set; } = string.Empty;
    public string TeamEvent3 { get; set; } = string.Empty;
    public string IndividualEvent1 { get; set; } = string.Empty;
    public string IndividualEvent2 { get; set; } = string.Empty;
}

/// <summary>一个比赛日。</summary>
public sealed class MeetDay
{
    public required string Name { get; init; }
    public required string DateText { get; init; }
    /// <summary>从赛程解析出的比赛日期；示例数据可能为空。用于分组表按日期对齐时段。</summary>
    public DateOnly? Date { get; init; }
    public List<MeetSlot> Slots { get; } = [];
}

/// <summary>赛程中的一个时段。</summary>
public sealed class MeetSlot
{
    public required string Time { get; init; }
    public required string Period { get; init; }
    public required string EventName { get; init; }
    public string Grade { get; init; } = string.Empty;
    public string Gender { get; init; } = "不限";
    public MeetEventCategory Category { get; init; } = MeetEventCategory.Other;
    public string Venue { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string GroupCount { get; init; } = string.Empty;
    public string AdvanceCount { get; init; } = string.Empty;
    public string EntryCountText { get; init; } = string.Empty;
    public List<MeetEntry> Entries { get; } = [];
}

/// <summary>赛程中的一条参赛记录。</summary>
public sealed class MeetEntry
{
    public required string ClassName { get; init; }
    public required string AthleteName { get; init; }
    public string Lane { get; init; } = string.Empty;
    public string Bib { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
}
