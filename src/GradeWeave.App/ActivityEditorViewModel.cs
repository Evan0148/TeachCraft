using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.App;

/// <summary>新增 / 编辑临时活动的表单状态；时间会自动映射到重叠的正课节次。</summary>
public partial class ActivityEditorViewModel : ObservableObject
{
    private readonly WeaveProject _project;
    private readonly ActivityBlock? _existing;
    private readonly string _initialSnapshot;

    public ActivityEditorViewModel(WeaveProject project, ActivityBlock? existing)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _existing = existing;

        foreach (var day in WeaveDefaults.Days) Days.Add(day);

        PeriodChoices.Add(new PeriodOption(0, "按时间自动识别", string.Empty));
        foreach (var period in project.Periods.Where(item => !item.IsExtension).OrderBy(item => item.Index))
            PeriodChoices.Add(new PeriodOption(period.Index, period.Label, period.TimeText));

        TeacherChoices.Add(new TeacherChoice(null));
        foreach (var teacher in project.Teachers)
            TeacherChoices.Add(new TeacherChoice(teacher));

        foreach (var schoolClass in project.Classes)
        {
            var option = new ClassOptionViewModel(schoolClass);
            option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedClassCountText));
            ClassOptions.Add(option);
        }

        if (existing is null)
        {
            SelectedPeriod = PeriodChoices[0];
            SelectedTeacher = TeacherChoices[0];
            _periodHint = string.Empty;
            UpdateHint();
            _initialSnapshot = Snapshot();
            return;
        }

        _name = existing.Name;
        _dayIndex = Math.Clamp(existing.DayIndex - 1, 0, Days.Count - 1);
        _startTime = existing.StartTime;
        _endTime = existing.EndTime;
        _scopeIndex = existing.ClassIds.Count == 0 ? 0 : 1;
        _room = existing.Room;
        _modeIndex = existing.Mode == ActivityMode.Replace ? 1 : 0;
        _note = existing.Note;

        SelectedPeriod = existing.Period > 0
            ? PeriodChoices.FirstOrDefault(option => option.Index == existing.Period) ?? PeriodChoices[0]
            : PeriodChoices[0];
        SelectedTeacher = TeacherChoices.FirstOrDefault(choice => choice.Teacher?.Id == existing.TeacherId)
                          ?? TeacherChoices[0];

        foreach (var option in ClassOptions)
            option.IsSelected = existing.ClassIds.Contains(option.Class.Id, StringComparer.Ordinal);

        UpdateHint();
        _initialSnapshot = Snapshot();
    }

    public ObservableCollection<string> Days { get; } = new();
    public ObservableCollection<PeriodOption> PeriodChoices { get; } = new();
    public ObservableCollection<TeacherChoice> TeacherChoices { get; } = new();
    public ObservableCollection<ClassOptionViewModel> ClassOptions { get; } = new();

    public bool IsEditing => _existing is not null;
    public string Title => IsEditing ? "编辑活动" : "新增活动";

    /// <summary>草稿与打开时的活动配置有差异时为真，关闭窗口前用于询问是否放弃。</summary>
    public bool IsDirty => Snapshot() != _initialSnapshot;

    private string Snapshot() => string.Join("丨",
        Name, DayIndex, StartTime, EndTime, ScopeIndex, Room, ModeIndex, Note,
        SelectedPeriod?.Index ?? 0, SelectedTeacher?.Teacher?.Id ?? string.Empty,
        string.Join("¦", ClassOptions.Select(option => option.IsSelected ? option.Class.Id : string.Empty)));

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _dayIndex;
    [ObservableProperty] private string _startTime = "10:50";
    [ObservableProperty] private string _endTime = "11:10";
    [ObservableProperty] private PeriodOption? _selectedPeriod;
    [ObservableProperty] private int _scopeIndex;
    [ObservableProperty] private TeacherChoice? _selectedTeacher;
    [ObservableProperty] private string _room = string.Empty;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _periodHint = string.Empty;
    [ObservableProperty] private string _error = string.Empty;

    public bool IsCustomScope => ScopeIndex == 1;
    public bool HasError => Error.Length > 0;
    public string SelectedClassCountText => $"已选 {ClassOptions.Count(option => option.IsSelected)} 个班";
    public bool IsOverlayMode => ModeIndex == 0;

    partial void OnStartTimeChanged(string value) => UpdateHint();

    partial void OnEndTimeChanged(string value) => UpdateHint();

    partial void OnSelectedPeriodChanged(PeriodOption? value) => UpdateHint();

    partial void OnScopeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCustomScope));
        OnPropertyChanged(nameof(SelectedClassCountText));
    }

    partial void OnModeIndexChanged(int value) => OnPropertyChanged(nameof(IsOverlayMode));

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    private void UpdateHint()
    {
        var auto = ActivityResolver.DescribeRange(_project, StartTime, EndTime);
        PeriodHint = SelectedPeriod?.Index > 0
            ? $"手动指定 {SelectedPeriod.Label} · {auto}"
            : auto;
    }

    /// <summary>校验并生成活动；失败时把可读原因写入 <paramref name="error"/>。</summary>
    public ActivityBlock? TryBuild(out string error)
    {
        error = string.Empty;

        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            error = "请填写活动名称，例如「书法课」。";
            return null;
        }

        if (!ActivityResolver.TryParseTime(StartTime, out var start) ||
            !ActivityResolver.TryParseTime(EndTime, out var end))
        {
            error = "时间请按 10:50、11:10 这样的格式填写。";
            return null;
        }

        if (end <= start)
        {
            error = "结束时间要晚于开始时间。";
            return null;
        }

        var manualPeriod = SelectedPeriod?.Index ?? 0;
        var periods = manualPeriod > 0
            ? new List<int> { manualPeriod }
            : ActivityResolver.OverlappingPeriods(_project, StartTime, EndTime);
        if (periods.Count == 0)
        {
            error = "这段时间没有落在任何正课节次，请在「归属节次」里手动指定。";
            return null;
        }

        var classIds = ScopeIndex == 1
            ? ClassOptions.Where(option => option.IsSelected).Select(option => option.Class.Id).ToList()
            : [];
        if (ScopeIndex == 1 && classIds.Count == 0)
        {
            error = "请至少选择一个参与班级。";
            return null;
        }

        // 「替换」型活动会移除该节原课；同一时段、同一班级不能被两个替换活动抢占。
        if (ModeIndex == 1)
        {
            var other = _project.Activities.FirstOrDefault(item =>
                item.Id != (_existing?.Id ?? string.Empty) &&
                item.Mode == ActivityMode.Replace &&
                item.DayIndex == DayIndex + 1 &&
                SharesClass(item, classIds) &&
                ActivityResolver.ResolvePeriods(_project, item).Any(periods.Contains));
            if (other is not null)
            {
                error = $"同一时段已有替换型活动「{other.Name}」（{ActivityResolver.TimeText(other)}），" +
                        "一节正课只能被一个活动替换。请调整时间，或改用「只叠加提醒」。";
                return null;
            }
        }

        return new ActivityBlock
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            DayIndex = DayIndex + 1,
            StartTime = ActivityResolver.Format(start),
            EndTime = ActivityResolver.Format(end),
            Period = manualPeriod,
            ClassIds = classIds,
            TeacherId = SelectedTeacher?.Teacher?.Id ?? string.Empty,
            Room = (Room ?? string.Empty).Trim(),
            Mode = ModeIndex == 1 ? ActivityMode.Replace : ActivityMode.Overlay,
            Note = (Note ?? string.Empty).Trim(),
        };
    }

    private static bool SharesClass(ActivityBlock item, List<string> classIds)
    {
        // 任何一边 ClassIds 为空都表示全年级，必然与对方重叠。
        if (item.ClassIds.Count == 0 || classIds.Count == 0) return true;
        return classIds.Any(id => item.ClassIds.Contains(id, StringComparer.Ordinal));
    }
}

/// <summary>教师下拉项；Teacher 为空表示「不指定」。</summary>
public sealed class TeacherChoice
{
    public TeacherChoice(Teacher? teacher)
    {
        Teacher = teacher;
        Display = teacher is null ? "不指定" : $"{teacher.Name} · {teacher.Subject}";
    }

    public Teacher? Teacher { get; }
    public string Display { get; }

    public override string ToString() => Display;
}
