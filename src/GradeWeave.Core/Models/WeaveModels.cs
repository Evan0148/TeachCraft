using System.Globalization;

namespace GradeWeave.Core.Models;

/// <summary>全年级课表编排的完整工程。所有 ID 在工程内唯一。</summary>
public sealed class WeaveProject
{
    public string SchoolName { get; set; } = string.Empty;
    public string GradeName { get; set; } = "九年级";
    public string TermName { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.Now;

    /// <summary>每天固定 7 节，另可追加课后服务 / 晚修等扩展时段。</summary>
    public List<PeriodSlot> Periods { get; set; } = WeaveDefaults.CreatePeriods();

    /// <summary>作息时间表；必须确认后才能生成课表，确认时编译进 Periods。</summary>
    public WeaveTimetable? Timetable { get; set; }

    public List<SchoolClass> Classes { get; set; } = [];
    public List<Subject> Subjects { get; set; } = [];
    public List<Teacher> Teachers { get; set; } = [];
    public List<TeachingAssignment> Assignments { get; set; } = [];

    /// <summary>学科教研：该学科所有任课教师在指定时段不可排课。</summary>
    public List<ResearchBlock> ResearchBlocks { get; set; } = [];

    /// <summary>会议：按兼职岗位或指定教师锁定时段。</summary>
    public List<MeetingBlock> MeetingBlocks { get; set; } = [];

    /// <summary>分层走班：同一组班级在固定时段上同一学科，按 A/B 层分开授课。</summary>
    public List<LayeredClassGroup> LayeredGroups { get; set; } = [];

    /// <summary>临时活动：在指定钟点叠加提醒，或替换重叠节次的原课。</summary>
    public List<ActivityBlock> Activities { get; set; } = [];

    public List<LessonPlacement> Lessons { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class PeriodSlot
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public bool IsAfternoon { get; set; }
    public bool IsExtension { get; set; }

    public string Label => string.IsNullOrWhiteSpace(Name) ? $"第{Index}节" : Name;
}

public sealed class SchoolClass
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HeadTeacher { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class Subject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int WeeklyHours { get; set; }
    public bool IsMain { get; set; }
    public bool IsLayered { get; set; }
    public string Color { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class Teacher
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int WeeklyHours { get; set; }
    public List<string> Roles { get; set; } = [];

    /// <summary>领导会议等特殊约束直接按人锁定。</summary>
    public bool IsLeader { get; set; }

    public override string ToString() => Name;
}

public sealed class TeachingAssignment
{
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    public int WeeklyHours { get; set; }
    public bool IsLayered { get; set; }
    public string LayerName { get; set; } = string.Empty;
}

public sealed class ResearchBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> SubjectIds { get; set; } = [];
    public int DayIndex { get; set; } = 1;
    public int StartPeriod { get; set; } = 1;
    public int EndPeriod { get; set; } = 1;
    public string Note { get; set; } = string.Empty;

    public IEnumerable<int> Periods => Enumerable.Range(StartPeriod, Math.Max(0, EndPeriod - StartPeriod + 1));
}

public sealed class MeetingBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int DayIndex { get; set; } = 1;
    public int StartPeriod { get; set; } = 1;
    public int EndPeriod { get; set; } = 1;

    /// <summary>为空且 AppliesToAllTeachers 为 false 时，只锁 TeacherIds。</summary>
    public List<string> Roles { get; set; } = [];
    public List<string> TeacherIds { get; set; } = [];
    public bool AppliesToAllTeachers { get; set; }
    public bool IsLeadership { get; set; }
    public string Note { get; set; } = string.Empty;

    public IEnumerable<int> Periods => Enumerable.Range(StartPeriod, Math.Max(0, EndPeriod - StartPeriod + 1));
}

public sealed class LayeredClassGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public List<string> ClassIds { get; set; } = [];
    public List<LayeredClassLayer> Layers { get; set; } = [];
    public int DayIndex { get; set; } = 1;
    public int StartPeriod { get; set; } = 1;
    public int EndPeriod { get; set; } = 1;
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// 自动安排模式：不固定时段，由编排引擎挑选所有参与班与各层教师都空闲的节次，
    /// 除不在行政班上课外与常规课相同。固定时段模式仍使用 DayIndex / StartPeriod / EndPeriod。
    /// </summary>
    public bool IsFlexible { get; set; }

    /// <summary>自动安排模式下的每周走班课时（1—7）。</summary>
    public int WeeklyHours { get; set; } = 1;

    /// <summary>自动安排的落位结果，编码为 天×100+节；每次重新生成课表都会重算。</summary>
    public List<int> ResolvedSlots { get; set; } = [];

    public IEnumerable<int> Periods => Enumerable.Range(StartPeriod, Math.Max(0, EndPeriod - StartPeriod + 1));

    /// <summary>走班组实际占用的（天，节）：固定时段用配置区间，自动安排用生成后的落位。</summary>
    public IEnumerable<(int Day, int Period)> Slots() =>
        IsFlexible
            ? ResolvedSlots.Select(DecodeSlot)
            : Periods.Select(period => (DayIndex, period));

    /// <summary>把编码槽位还原为（天，节）。</summary>
    public static (int Day, int Period) DecodeSlot(int slot) => (slot / 100, slot % 100);

    public static int EncodeSlot(int day, int period) => day * 100 + period;
}

public sealed class LayeredClassLayer
{
    public string Name { get; set; } = "A层";
    public string TeacherId { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
}

public enum ActivityMode
{
    /// <summary>只在该节格子里叠加活动提醒，原课照常。</summary>
    Overlay,

    /// <summary>用活动替换该节原课，被替换的课时会成为缺口。</summary>
    Replace,
}

/// <summary>临时活动：例如九年级在 10:50—11:10 插入书法课。</summary>
public sealed class ActivityBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "临时活动";
    public int DayIndex { get; set; } = 1;
    public string StartTime { get; set; } = "10:50";
    public string EndTime { get; set; } = "11:10";

    /// <summary>0 表示按时间自动匹配节次；大于 0 时作为手动指定的节次。</summary>
    public int Period { get; set; }

    /// <summary>参与班级；为空表示全年级。</summary>
    public List<string> ClassIds { get; set; } = [];

    public string TeacherId { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public ActivityMode Mode { get; set; } = ActivityMode.Overlay;
    public string Note { get; set; } = string.Empty;
}

/// <summary>一个班在一天的一节课。扩展时段（课服/晚修）同样使用该结构。</summary>
public sealed class LessonPlacement
{
    public string ClassId { get; set; } = string.Empty;
    public int DayIndex { get; set; } = 1;
    public int Period { get; set; } = 1;
    public string SubjectId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public bool IsLayered { get; set; }
    public bool IsFixed { get; set; }
    public string Note { get; set; } = string.Empty;

    /// <summary>由「替换式活动」生成的固定占位课；SubjectId 为空。</summary>
    public bool IsActivity { get; set; }
    public string ActivityId { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
    public string ActivityTime { get; set; } = string.Empty;
}

public sealed class UnplacedLesson
{
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public string Reason { get; set; } = string.Empty;
}

public static class WeaveDefaults
{
    public static readonly string[] Days = ["星期一", "星期二", "星期三", "星期四", "星期五"];

    public static List<PeriodSlot> CreatePeriods() =>
    [
        new() { Index = 1, Name = "第1节", TimeText = "08:00—08:40" },
        new() { Index = 2, Name = "第2节", TimeText = "08:50—09:30" },
        new() { Index = 3, Name = "第3节", TimeText = "09:50—10:30" },
        new() { Index = 4, Name = "第4节", TimeText = "10:40—11:20" },
        new() { Index = 5, Name = "第5节", TimeText = "14:20—15:00", IsAfternoon = true },
        new() { Index = 6, Name = "第6节", TimeText = "15:10—15:50", IsAfternoon = true },
        new() { Index = 7, Name = "第7节", TimeText = "16:00—16:40", IsAfternoon = true },
    ];

    public static string DayName(int dayIndex) =>
        dayIndex >= 1 && dayIndex <= Days.Length ? Days[dayIndex - 1] : $"第{dayIndex}天";

    public static string PeriodText(WeaveProject project, int period)
    {
        var slot = project.Periods.FirstOrDefault(p => p.Index == period);
        return slot?.Label ?? $"第{period}节";
    }

    public static string SlotText(int dayIndex, int startPeriod, int endPeriod) =>
        startPeriod == endPeriod
            ? $"{DayName(dayIndex)}第{startPeriod}节"
            : $"{DayName(dayIndex)}第{startPeriod}-{endPeriod}节";

    public static string SubjectAlias(string raw)
    {
        var text = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal);
        return text switch
        {
            "道法" or "政治" or "道德与法治" => "道法",
            "物理" => "物理",
            "化学" => "化学",
            "语文" => "语文",
            "数学" => "数学",
            "英语" => "英语",
            "历史" => "历史",
            "体育" => "体育",
            "劳动" or "劳动课" => "劳动",
            "班会" => "班会",
            "综合" => "综合",
            "地理" => "地理",
            "生物" => "生物",
            _ => text,
        };
    }

    public static string NormalizeTeacherName(string raw) =>
        raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal);

    public static string ClassKey(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? "C" + int.Parse(digits, CultureInfo.InvariantCulture) : raw.Trim();
    }

    public static int ParsePeriodNumber(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out var n) ? n : 0;
    }
}
