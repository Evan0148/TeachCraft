namespace GradeWeave.Core.Models;

public sealed class ScheduleOptions
{
    public int RandomSeed { get; set; } = 20261026;
    public int MaxSameSubjectPerDay { get; set; } = 2;
    public int MaxSameSubjectPerAfternoon { get; set; } = 1;
    public bool PreferMainMorning { get; set; } = true;
    public int MaxRestarts { get; set; } = 24;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromSeconds(8);
}

public sealed class ScheduleResult
{
    public bool Success => Unplaced.Count == 0;
    public int PlacedCount { get; set; }
    public int TotalCount { get; set; }
    public List<UnplacedLesson> Unplaced { get; set; } = [];
    public List<string> Messages { get; set; } = [];
    public int Score { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public sealed class WeaveImportResult
{
    public WeaveProject Project { get; set; } = new();
    public List<SheetSummary> Sheets { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ResearchBlock> SuggestedResearchBlocks { get; set; } = [];
    public List<MeetingBlock> SuggestedMeetingBlocks { get; set; } = [];
    public List<string> UnparsedNotes { get; set; } = [];
}

public sealed class SheetSummary
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Columns { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class NoteParseResult
{
    public List<ResearchBlock> ResearchBlocks { get; set; } = [];
    public List<MeetingBlock> MeetingBlocks { get; set; } = [];
    public List<string> Unparsed { get; set; } = [];
}

public enum ConflictKind
{
    TeacherClash,
    ClassClash,
    TeacherUnavailable,
    LayeredClash,
    MissingTeacher,
    HourMismatch,
    Unplaced,
}

public sealed class WeaveConflict
{
    public ConflictKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ClassId { get; set; }
    public string? TeacherId { get; set; }
    public int DayIndex { get; set; }
    public int Period { get; set; }

    public string Location => DayIndex >= 1
        ? WeaveDefaults.SlotText(DayIndex, Period, Period)
        : string.Empty;
}

public sealed class ExportOptions
{
    public bool IncludeMasterSheet { get; set; } = true;
    public bool IncludeClassSheets { get; set; } = true;
    public bool IncludeTeacherSheets { get; set; } = true;
    public bool IncludeAssignmentSheet { get; set; } = true;

    /// <summary>教师职位安排表：姓名、学科、兼职岗位、任教班级与学科周课时。</summary>
    public bool IncludeTeacherPositionSheet { get; set; } = true;

    public bool IncludeConflictSheet { get; set; } = true;
    public string? OnlyClassId { get; set; }
    public string? OnlyTeacherId { get; set; }

    /// <summary>工作簿版式：软件自带纸墨样式，或按参考工作簿复刻的「参考校样式」。</summary>
    public WeaveExportStyle Style { get; set; } = WeaveExportStyle.Software;
}

public enum WeaveExportStyle
{
    /// <summary>知微纸墨色板、雅黑字体、浅色细边。</summary>
    Software,

    /// <summary>参考校参考工作簿：宋体 / 楷体、黑色细框、横向课程总表与教师职位表。</summary>
    Reference,
}
