using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.App;

/// <summary>01 步左侧的学科条目：学科名、周课时与任课教师数。</summary>
public sealed class ResearchSubjectItemViewModel
{
    public ResearchSubjectItemViewModel(Subject subject, int teacherCount)
    {
        Subject = subject;
        TeacherCount = teacherCount;
    }

    public Subject Subject { get; }
    public int TeacherCount { get; }

    public string Name => Subject.Name;
    public string Meta => $"每周 {Subject.WeeklyHours} 课时 · {TeacherCount} 位教师";
    public bool IsMain => Subject.IsMain;
    public string AutomationName => $"{Name}，{Meta}";
}

/// <summary>班级列表条目。</summary>
public sealed class ClassItemViewModel
{
    public ClassItemViewModel(SchoolClass schoolClass)
    {
        Class = schoolClass;
        var digits = new string(schoolClass.Name.Where(char.IsDigit).ToArray());
        Badge = digits.Length > 0 ? digits : "班";
    }

    public SchoolClass Class { get; }
    public string Name => Class.Name;
    public string Badge { get; }

    public string Meta => string.IsNullOrWhiteSpace(Class.HeadTeacher)
        ? "未设置班主任"
        : $"班主任 {Class.HeadTeacher}";

    public string AutomationName => $"{Name}，{Meta}";
}

/// <summary>教师列表条目。</summary>
public sealed class TeacherItemViewModel
{
    public TeacherItemViewModel(Teacher teacher) => Teacher = teacher;

    public Teacher Teacher { get; }
    public string Name => Teacher.Name;

    public string Meta => string.IsNullOrWhiteSpace(Teacher.Subject)
        ? $"每周 {Teacher.WeeklyHours} 课时"
        : $"{Teacher.Subject} · 每周 {Teacher.WeeklyHours} 课时";

    public string Badge => Teacher.IsLeader ? "领" : "师";
    public string AutomationName => $"{Name}，{Meta}";
}

/// <summary>走班组列表条目。</summary>
public sealed class LayeredGroupItemViewModel
{
    private readonly string _meta;
    private readonly string _detail;

    public LayeredGroupItemViewModel(LayeredClassGroup group, WeaveProject project)
    {
        Group = group;
        SubjectName = project.FindSubject(group.SubjectId)?.Name ?? "未设置学科";

        if (group.IsFlexible)
        {
            var hours = Math.Max(1, group.WeeklyHours);
            _meta = group.ResolvedSlots.Count > 0
                ? $"自动安排 · {hours} 课时 · 已落位 {DescribeSlots(group.ResolvedSlots)}"
                : $"自动安排 · {hours} 课时 · 待生成";
        }
        else
        {
            _meta = $"{WeaveDefaults.SlotText(group.DayIndex, group.StartPeriod, group.EndPeriod)} · {group.ClassIds.Count} 个班";
        }

        var layers = group.Layers.Select(layer =>
        {
            var teacherName = project.FindTeacher(layer.TeacherId)?.Name;
            return string.IsNullOrWhiteSpace(teacherName) ? layer.Name : $"{layer.Name} {teacherName}";
        });
        var scope = group.ClassIds.Count > 0 ? $" · {group.ClassIds.Count} 个班同节" : string.Empty;
        _detail = $"{SubjectName} · {string.Join(" / ", layers)}{scope}";
    }

    private static string DescribeSlots(IReadOnlyList<int> slots)
    {
        var parts = slots.Take(3).Select(slot =>
        {
            var (day, period) = LayeredClassGroup.DecodeSlot(slot);
            return WeaveDefaults.SlotText(day, period, period);
        });
        return string.Join("、", parts) + (slots.Count > 3 ? " 等" : string.Empty);
    }

    public LayeredClassGroup Group { get; }
    public string SubjectName { get; }

    public string Name => string.IsNullOrWhiteSpace(Group.Name) ? "未命名走班组" : Group.Name;
    public string Badge => Group.IsFlexible ? "灵活" : "走班";
    public string Meta => _meta;

    public string Detail => _detail;
}

/// <summary>会议时段条目（列表与备注建议共用）。</summary>
public sealed class MeetingBlockItemViewModel
{
    public MeetingBlockItemViewModel(MeetingBlock block) => Block = block;

    public MeetingBlock Block { get; }

    public string Name => string.IsNullOrWhiteSpace(Block.Name) ? "未命名会议" : Block.Name;

    public string Badge => Block.IsLeadership ? "领导" : Block.AppliesToAllTeachers ? "全体" : "会议";

    public string Meta => WeaveDefaults.SlotText(Block.DayIndex, Block.StartPeriod, Block.EndPeriod);

    public string Detail
    {
        get
        {
            var scope = Block.AppliesToAllTeachers
                ? "全体教师"
                : Block.Roles.Count > 0
                    ? string.Join(" / ", Block.Roles)
                    : Block.TeacherIds.Count > 0
                        ? $"指定 {Block.TeacherIds.Count} 位教师"
                        : "未设置适用对象";
            return string.IsNullOrWhiteSpace(Block.Note) ? scope : $"{scope} · {Block.Note}";
        }
    }
}

/// <summary>05 步「活动」列表条目。</summary>
public sealed class ActivityItemViewModel
{
    private readonly string _detail;

    public ActivityItemViewModel(ActivityBlock activity, WeaveProject project)
    {
        Activity = activity;
        Periods = ActivityResolver.ResolvePeriods(project, activity);

        var scope = activity.ClassIds.Count == 0 ? "全年级" : $"{activity.ClassIds.Count} 个班";
        var periodText = Periods.Count == 0
            ? "未匹配节次"
            : string.Join("、", Periods.Select(period => WeaveDefaults.PeriodText(project, period)));
        var parts = new List<string> { scope, periodText };

        var teacher = project.FindTeacher(activity.TeacherId)?.Name;
        if (!string.IsNullOrWhiteSpace(teacher)) parts.Add(teacher);
        if (!string.IsNullOrWhiteSpace(activity.Room)) parts.Add(activity.Room);
        if (!string.IsNullOrWhiteSpace(activity.Note)) parts.Add(activity.Note);

        _detail = string.Join(" · ", parts);
    }

    public ActivityBlock Activity { get; }
    public IReadOnlyList<int> Periods { get; }

    public string Name => string.IsNullOrWhiteSpace(Activity.Name) ? "未命名活动" : Activity.Name;
    public string Badge => Activity.Mode == ActivityMode.Replace ? "替换" : "叠加";
    public string Meta => $"{WeaveDefaults.DayName(Activity.DayIndex)} {ActivityResolver.TimeText(Activity)}";
    public string Detail => _detail;
    public string AutomationName => $"编辑活动 {Name}，{Meta}，{Detail}";
}

/// <summary>冲突面板条目。</summary>
public sealed class ConflictItemViewModel
{
    public ConflictItemViewModel(WeaveConflict conflict) => Conflict = conflict;

    public WeaveConflict Conflict { get; }
    public string Message => Conflict.Message;
    public string Location => Conflict.Location;
    public string Kind => KindLabel(Conflict.Kind);
    public bool HasLocation => Conflict.DayIndex >= 1 && Conflict.Period >= 1;
    public string DisplayLocation => HasLocation ? Location : "位置未定位";
    public bool CanNavigate =>
        HasLocation ||
        !string.IsNullOrWhiteSpace(Conflict.ClassId) ||
        !string.IsNullOrWhiteSpace(Conflict.TeacherId);
    public string ActionText => HasLocation
        ? "定位"
        : string.IsNullOrWhiteSpace(Conflict.ClassId) ? "查看教师" : "查看班级";

    public static string KindLabel(ConflictKind kind) => kind switch
    {
        ConflictKind.TeacherClash => "教师冲突",
        ConflictKind.ClassClash => "班级冲突",
        ConflictKind.TeacherUnavailable => "教研 / 会议",
        ConflictKind.LayeredClash => "走班冲突",
        ConflictKind.MissingTeacher => "缺少教师",
        ConflictKind.HourMismatch => "课时不符",
        ConflictKind.Unplaced => "未排课",
        _ => "冲突",
    };
}

/// <summary>冲突聚合台的一组：同一冲突类型折叠展示。</summary>
public sealed class ConflictGroupViewModel
{
    public ConflictGroupViewModel(ConflictKind kind, IReadOnlyList<ConflictItemViewModel> items)
    {
        Kind = kind;
        Items = items;
        Count = items.Count;
        LocatedCount = items.Count(item => item.HasLocation);
        IsExpanded = true;
    }

    public ConflictKind Kind { get; }
    public IReadOnlyList<ConflictItemViewModel> Items { get; }
    public int Count { get; }
    public int LocatedCount { get; }
    public bool IsExpanded { get; set; }
    public string KindText => ConflictItemViewModel.KindLabel(Kind);
    public string Meta => LocatedCount == Count
        ? $"{Count} 条 · 均可定位"
        : $"{Count} 条 · 可定位 {LocatedCount} · 位置未知 {Count - LocatedCount}";
}

/// <summary>走班参与班的勾选项。</summary>
public partial class ClassOptionViewModel : ObservableObject
{
    public ClassOptionViewModel(SchoolClass schoolClass) => Class = schoolClass;

    public SchoolClass Class { get; }
    public string Name => Class.Name;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>会议指定教师的勾选项。</summary>
public partial class TeacherOptionViewModel : ObservableObject
{
    public TeacherOptionViewModel(Teacher teacher) => Teacher = teacher;

    public Teacher Teacher { get; }
    public string Name => Teacher.Name;
    public string Subject => Teacher.Subject;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>会议适用角色的勾选项。</summary>
public partial class RoleOptionViewModel : ObservableObject
{
    public RoleOptionViewModel(string name) => Name = name;

    public string Name { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>走班编辑卡里的一层。</summary>
public partial class LayerEditViewModel : ObservableObject
{
    public LayerEditViewModel(IEnumerable<Teacher> teachers, LayeredClassLayer? layer = null)
    {
        SetTeachers(teachers);
        if (layer is null) return;

        Name = layer.Name;
        Room = layer.Room;
        Teacher = Teachers.FirstOrDefault(item => item.Id == layer.TeacherId);
    }

    /// <summary>只包含当前走班学科的任课教师；切换学科时由主 ViewModel 重建。</summary>
    public ObservableCollection<Teacher> Teachers { get; } = new();

    public void SetTeachers(IEnumerable<Teacher> teachers)
    {
        var selectedId = Teacher?.Id;
        Teachers.Clear();
        foreach (var teacher in teachers) Teachers.Add(teacher);
        Teacher = string.IsNullOrWhiteSpace(selectedId)
            ? null
            : Teachers.FirstOrDefault(teacher => teacher.Id == selectedId);
    }

    /// <summary>教室可选项 = 上方勾选的参与班级；只能从这里选。</summary>
    public ObservableCollection<string> RoomOptions { get; } = new();

    [ObservableProperty] private string _name = "A层";
    [ObservableProperty] private Teacher? _teacher;
    [ObservableProperty] private string _room = string.Empty;

    /// <summary>
    /// 刷新教室可选项。旧工程里手填且不在勾选班级中的值保留为附加项，
    /// 不静默丢弃；保存时会提示改选参与班教室。
    /// </summary>
    public void SetRoomOptions(IEnumerable<string> rooms)
    {
        var options = rooms
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(Room) && !options.Contains(Room, StringComparer.Ordinal))
            options.Insert(0, Room);

        RoomOptions.Clear();
        foreach (var option in options) RoomOptions.Add(option);
    }
}

/// <summary>课节下拉项，序号来自已确认作息的节次编译。</summary>
public sealed class PeriodOption
{
    public PeriodOption(int index, string label, string timeText)
    {
        Index = index;
        Label = label;
        TimeText = timeText;
    }

    public int Index { get; }
    public string Label { get; }
    public string TimeText { get; }

    public override string ToString() => Label;
}
