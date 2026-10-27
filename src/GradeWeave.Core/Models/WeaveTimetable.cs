using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Core.Models;

/// <summary>作息时间表里的行类型；只有正课与课后服务 / 晚修会变成编排节次。</summary>
public enum TimetableRowKind
{
    Lesson,
    MorningReading,
    Break,
    NoonStudy,
    Lunch,
    Rest,
    EyeExercise,
    AfterSchool,
    EveningStudy,
    Routine,
}

/// <summary>作息时间表的一天中的一段时间；名称留空时按类型自动命名。</summary>
public sealed class TimetableRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TimetableRowKind Kind { get; set; } = TimetableRowKind.Lesson;
    public string Name { get; set; } = string.Empty;
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "08:40";

    public TimetableRow Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Name = Name,
        StartTime = StartTime,
        EndTime = EndTime,
    };
}

/// <summary>
/// 一天作息时间表。确认签名与当前内容一致时才算已确认；
/// 老师改动任意一行后签名自动失配，生成课表会重新被拦住。
/// </summary>
public sealed class WeaveTimetable
{
    public string Name { get; set; } = "作息时间表";
    public List<TimetableRow> Rows { get; set; } = [];

    /// <summary>确认时写入的内容签名；为空表示还没确认过。</summary>
    public string ConfirmedSignature { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfirmed =>
        Rows.Count > 0 &&
        ConfirmedSignature.Length > 0 &&
        string.Equals(ConfirmedSignature, WeaveTimetables.Signature(Rows), StringComparison.Ordinal);

    [JsonIgnore]
    public int LessonCount => Rows.Count(row => row.Kind == TimetableRowKind.Lesson);

    [JsonIgnore]
    public int ExtensionCount => Rows.Count(row => WeaveTimetables.IsExtension(row.Kind));

    public WeaveTimetable Clone() => new()
    {
        Name = Name,
        ConfirmedSignature = ConfirmedSignature,
        Rows = Rows.Select(row => row.Clone()).ToList(),
    };
}

/// <summary>作息时间表的内置预设、命名与签名工具；预设只用通用年级名，不含任何真实学校。</summary>
public static class WeaveTimetables
{
    public sealed class Preset
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
    }

    public static IReadOnlyList<Preset> Presets { get; } =
    [
        new() { Id = "grade-9", Name = "九年级标准作息", Description = "早读、7 节正课、课服与两段晚修" },
        new() { Id = "grade-8", Name = "八年级标准作息", Description = "早读 7:20，第四节前有 20 分钟朗读，午餐顺延" },
        new() { Id = "grade-7", Name = "七年级标准作息", Description = "早读 7:10，第四节前有 10 分钟写字，午餐顺延" },
        new() { Id = "lesson-only", Name = "仅正课七节", Description = "只保留 08:00 起的七节正课" },
    ];

    public static bool IsTeaching(TimetableRowKind kind) => kind == TimetableRowKind.Lesson;

    public static bool IsExtension(TimetableRowKind kind) =>
        kind is TimetableRowKind.AfterSchool or TimetableRowKind.EveningStudy;

    public static bool IsPeriod(TimetableRowKind kind) => IsTeaching(kind) || IsExtension(kind);

    public static string KindLabel(TimetableRowKind kind) => kind switch
    {
        TimetableRowKind.Lesson => "正课",
        TimetableRowKind.MorningReading => "早读",
        TimetableRowKind.Break => "课间",
        TimetableRowKind.NoonStudy => "午学",
        TimetableRowKind.Lunch => "午餐",
        TimetableRowKind.Rest => "午休",
        TimetableRowKind.EyeExercise => "眼保健操",
        TimetableRowKind.AfterSchool => "课后服务",
        TimetableRowKind.EveningStudy => "晚修",
        TimetableRowKind.Routine => "作息",
        _ => "时段",
    };

    public static string DisplayName(TimetableRow row)
    {
        var name = (row.Name ?? string.Empty).Trim();
        return name.Length > 0 ? name : KindLabel(row.Kind);
    }

    public static string DefaultName(TimetableRowKind kind, int lessonCount)
    {
        var index = Math.Clamp(lessonCount + 1, 1, 20);
        return kind switch
        {
            TimetableRowKind.Lesson => $"第{index}节",
            TimetableRowKind.MorningReading => "早读",
            TimetableRowKind.Break => "课间",
            TimetableRowKind.NoonStudy => "午学",
            TimetableRowKind.Lunch => "午餐",
            TimetableRowKind.Rest => "午休",
            TimetableRowKind.EyeExercise => "眼保健操",
            TimetableRowKind.AfterSchool => "课后服务",
            TimetableRowKind.EveningStudy => "晚修",
            TimetableRowKind.Routine => "作息",
            _ => "时段",
        };
    }

    /// <summary>把工程现有的 Periods 还原成作息行；旧工程第一次打开时用。</summary>
    public static WeaveTimetable FromPeriods(IEnumerable<PeriodSlot> periods)
    {
        var timetable = new WeaveTimetable();
        foreach (var slot in periods.OrderBy(p => p.IsExtension ? 1 : 0).ThenBy(p => p.Index))
        {
            var (start, end) = SplitTimeText(slot.TimeText);
            timetable.Rows.Add(new TimetableRow
            {
                Kind = slot.IsExtension
                    ? slot.Name.Contains('晚') ? TimetableRowKind.EveningStudy : TimetableRowKind.AfterSchool
                    : TimetableRowKind.Lesson,
                Name = slot.Name,
                StartTime = start,
                EndTime = end,
            });
        }

        return timetable;
    }

    /// <summary>按年级生成内置作息；无法识别年级时回落到九年级。</summary>
    public static WeaveTimetable FromPreset(string? presetId)
    {
        var id = (presetId ?? string.Empty).Trim();
        return id switch
        {
            "grade-7" => GradeSeven(),
            "grade-8" => GradeEight(),
            "grade-9" => GradeNine(),
            "lesson-only" => CreateDefault(),
            _ => GradeNine(),
        };
    }

    public static WeaveTimetable GradeNine()
    {
        var timetable = new WeaveTimetable { Name = "九年级作息" };
        timetable.Rows.AddRange(
        [
            Row(TimetableRowKind.MorningReading, "早读", "07:20", "07:50"),
            Lesson("第1节", "08:00", "08:40"),
            Lesson("第2节", "08:50", "09:30"),
            Row(TimetableRowKind.Break, "大课间", "09:30", "10:00"),
            Lesson("第3节", "10:00", "10:40"),
            Lesson("第4节", "10:50", "11:30"),
            Row(TimetableRowKind.Lunch, "午餐", "11:30", "12:20"),
            Row(TimetableRowKind.Rest, "午休", "12:40", "14:00"),
            Lesson("第5节", "14:20", "15:00"),
            Row(TimetableRowKind.EyeExercise, "眼保健操", "15:00", "15:05"),
            Lesson("第6节", "15:10", "15:50"),
            Lesson("第7节", "16:00", "16:40"),
            Row(TimetableRowKind.AfterSchool, "课服1", "16:50", "17:20"),
            Row(TimetableRowKind.AfterSchool, "课服2", "17:20", "17:50"),
            Row(TimetableRowKind.Routine, "晚餐", "18:00", "18:50"),
            Row(TimetableRowKind.EveningStudy, "晚修1", "19:20", "20:20"),
            Row(TimetableRowKind.EveningStudy, "晚修2", "20:30", "21:30"),
        ]);
        return timetable;
    }

    public static WeaveTimetable GradeEight()
    {
        var timetable = GradeNine();
        timetable.Name = "八年级作息";
        var fourth = timetable.Rows.First(row => row.Kind == TimetableRowKind.Lesson && row.Name == "第4节");
        fourth.StartTime = "11:10";
        fourth.EndTime = "11:50";
        var lunch = timetable.Rows.First(row => row.Kind == TimetableRowKind.Lunch);
        lunch.StartTime = "11:50";
        lunch.EndTime = "12:20";

        var reading = Row(TimetableRowKind.Routine, "朗读", "10:50", "11:10");
        timetable.Rows.Insert(timetable.Rows.IndexOf(fourth), reading);
        return timetable;
    }

    public static WeaveTimetable GradeSeven()
    {
        var timetable = GradeNine();
        timetable.Name = "七年级作息";
        var earlyReading = timetable.Rows.First(row => row.Kind == TimetableRowKind.MorningReading);
        earlyReading.StartTime = "07:10";
        earlyReading.EndTime = "07:30";
        var fourth = timetable.Rows.First(row => row.Kind == TimetableRowKind.Lesson && row.Name == "第4节");
        fourth.StartTime = "11:00";
        fourth.EndTime = "11:40";
        var lunch = timetable.Rows.First(row => row.Kind == TimetableRowKind.Lunch);
        lunch.StartTime = "11:40";
        lunch.EndTime = "12:20";

        var writing = Row(TimetableRowKind.Routine, "写字", "10:50", "11:00");
        timetable.Rows.Insert(timetable.Rows.IndexOf(fourth), writing);
        return timetable;
    }

    /// <summary>只含七节正课的干净默认表，保持旧工程的行为。</summary>
    public static WeaveTimetable CreateDefault() => FromPeriods(WeaveDefaults.CreatePeriods());

    /// <summary>内容签名：按起止时间排序后的「类型 | 名称 | 开始 | 结束」的 SHA-256。</summary>
    public static string Signature(IEnumerable<TimetableRow> rows)
    {
        var canonical = string.Join('\n', OrderRows(rows).Select(row =>
            $"{row.Kind}|{(row.Name ?? string.Empty).Trim()}|{NormalizeTime(row.StartTime)}|{NormalizeTime(row.EndTime)}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    /// <summary>按开始时间、结束时间稳定排序；时间无法解析的行排在最后。</summary>
    public static List<TimetableRow> OrderRows(IEnumerable<TimetableRow> rows) =>
        rows.OrderBy(row => ActivityResolver.TryParseTime(row.StartTime, out var start) ? start : TimeSpan.MaxValue)
            .ThenBy(row => ActivityResolver.TryParseTime(row.EndTime, out var end) ? end : TimeSpan.MaxValue)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToList();

    public static string NormalizeTime(string? raw) =>
        ActivityResolver.TryParseTime(raw, out var time) ? ActivityResolver.Format(time) : (raw ?? string.Empty).Trim();

    private static (string Start, string End) SplitTimeText(string? text) =>
        ActivityResolver.TryParseRange(text, out var start, out var end)
            ? (ActivityResolver.Format(start), ActivityResolver.Format(end))
            : (string.Empty, string.Empty);

    private static TimetableRow Row(TimetableRowKind kind, string name, string start, string end) => new()
    {
        Kind = kind,
        Name = name,
        StartTime = start,
        EndTime = end,
    };

    private static TimetableRow Lesson(string name, string start, string end) =>
        Row(TimetableRowKind.Lesson, name, start, end);
}
