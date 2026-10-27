using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GradeWeave.Core;
using GradeWeave.Core.Models;
using GradeWeave.Core.Project;
using GradeWeave.Core.Scheduling;
using GradeWeave.Core.Timetable;

namespace GradeWeave.App;

/// <summary>五步编排界面的状态与数据；文件解析、排课与导出均由窗口层放入后台线程。</summary>
public partial class GradeWeaveViewModel : ObservableObject
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C6613F"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly string[] MeetingRoles =
        ["班主任", "科组长", "备课组长", "年级长", "主任", "副主任", "校长"];

    private readonly List<ClassItemViewModel> _allClasses = new();
    private readonly List<Teacher> _teachers = new();
    private readonly List<WeaveConflict> _conflictModels = new();

    private bool _loadingDraft;
    private LayeredClassGroup? _editingLayeredGroup;
    private MeetingBlock? _editingMeetingBlock;

    public GradeWeaveViewModel()
    {
        foreach (var role in MeetingRoles)
            MeetingRoleOptions.Add(new RoleOptionViewModel(role));

        foreach (var day in WeaveDefaults.Days)
            Days.Add(day);
    }

    // ----------------------------- 事件 -----------------------------

    /// <summary>工程数据或列表结构发生变化，窗口据此重建网格与摘要。</summary>
    public event EventHandler? ProjectChanged;

    public event EventHandler? ResearchGridInvalidated;

    public event EventHandler? LayeredPreviewInvalidated;

    public event EventHandler? ScheduleInvalidated;

    // ----------------------------- 集合 -----------------------------

    public ObservableCollection<ResearchSubjectItemViewModel> ResearchSubjects { get; } = new();

    public ObservableCollection<ClassItemViewModel> FilteredClasses { get; } = new();

    public ObservableCollection<TeacherItemViewModel> TeacherItems { get; } = new();

    public ObservableCollection<LayeredGroupItemViewModel> LayeredGroups { get; } = new();

    public ObservableCollection<MeetingBlockItemViewModel> MeetingBlocks { get; } = new();

    public ObservableCollection<MeetingBlockItemViewModel> MeetingSuggestions { get; } = new();

    public ObservableCollection<ActivityItemViewModel> ActivityItems { get; } = new();

    public ObservableCollection<ConflictItemViewModel> Conflicts { get; } = new();

    public ObservableCollection<Subject> Subjects { get; } = new();

    public ObservableCollection<ClassOptionViewModel> LayeredClassOptions { get; } = new();

    public ObservableCollection<LayerEditViewModel> LayeredLayers { get; } = new();

    public ObservableCollection<RoleOptionViewModel> MeetingRoleOptions { get; } = new();

    public ObservableCollection<TeacherOptionViewModel> MeetingTeacherOptions { get; } = new();

    public ObservableCollection<PeriodOption> PeriodOptions { get; } = new();

    /// <summary>作息时间步骤里的只读时段列表与编译后的节次预览。</summary>
    public ObservableCollection<TimetableRowItemViewModel> TimetableRows { get; } = new();

    public ObservableCollection<TimetablePeriodPreviewViewModel> TimetablePeriodPreviews { get; } = new();

    /// <summary>自动安排走班组可选的每周课时。</summary>
    public ObservableCollection<int> LayeredHourChoices { get; } = new(Enumerable.Range(1, 7));

    /// <summary>会议可放到课后服务 / 晚修等扩展时段，这些时段不参与正课编排。</summary>
    public ObservableCollection<PeriodOption> MeetingPeriodOptions { get; } = new();

    public ObservableCollection<string> Days { get; } = new();

    public IReadOnlyList<WeaveConflict> ConflictModels => _conflictModels;

    // ----------------------------- 状态 -----------------------------

    [ObservableProperty] private WeaveProject _project = new();
    [ObservableProperty] private string _statusText = "导入课表或打开 .weave.json 工程开始编排。";
    [ObservableProperty] private IBrush _statusBrush = SuccessBrush;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasProject;
    [ObservableProperty] private int _activeStep = 1;
    [ObservableProperty] private int _viewMode;

    [ObservableProperty] private string _sourceName = "未载入课表";
    [ObservableProperty] private string _projectTitle = "织序课表编排";
    [ObservableProperty] private string _projectSubtitle = "五步约束：作息、教研、走班、会议、正式编排。";
    [ObservableProperty] private string _summaryLine = "暂无数据";
    [ObservableProperty] private string _classCountLabel = "0 个班";
    [ObservableProperty] private string _teacherCountLabel = "0 位教师";
    [ObservableProperty] private string _factResearch = "0 条教研";
    [ObservableProperty] private string _factLayered = "0 组走班";
    [ObservableProperty] private string _factMeetings = "0 场会议";
    [ObservableProperty] private string _factActivities = "0 个活动";
    [ObservableProperty] private string _factConflicts = "✓ 暂无冲突";
    [ObservableProperty] private IBrush _conflictCountBrush = SuccessBrush;
    [ObservableProperty] private string _conflictSummaryText = "暂无检查结果";
    [ObservableProperty] private string _assignmentSummary = "0 条任课 · 0 位教师";

    [ObservableProperty] private string _timetableStatusText = "未确认";
    [ObservableProperty] private string _timetableSummary = "还没有载入作息时间表";
    [ObservableProperty] private string _timetableValidationText = string.Empty;
    [ObservableProperty] private bool _hasTimetableErrors;
    [ObservableProperty] private IBrush _timetableStatusBrush = ErrorBrush;

    [ObservableProperty] private string _autosaveText = "自动保存待命";
    [ObservableProperty] private string _autosaveDetail = "工程改动会每 2 秒自动保存到本机。";
    [ObservableProperty] private IBrush _autosaveBrush = MutedBrush;
    [ObservableProperty] private bool _recoveryAvailable;
    [ObservableProperty] private string _recoverySummary = string.Empty;

    [ObservableProperty] private string _researchSlotTitle = "点击任意格子";
    [ObservableProperty] private string _researchSlotLocked = "该时段 0 位教师不可排";

    [ObservableProperty] private string _scheduleHeaderTitle = "课表";
    [ObservableProperty] private string _scheduleHeaderMeta = "选择班级后显示课程。";

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private ClassItemViewModel? _selectedClass;
    [ObservableProperty] private TeacherItemViewModel? _selectedTeacher;
    [ObservableProperty] private ResearchSubjectItemViewModel? _selectedResearchSubject;
    [ObservableProperty] private LayeredGroupItemViewModel? _selectedLayeredGroup;
    [ObservableProperty] private MeetingBlockItemViewModel? _selectedMeetingBlock;
    [ObservableProperty] private MeetingBlockItemViewModel? _selectedMeetingSuggestion;

    // ----------------------------- 走班草稿 -----------------------------

    [ObservableProperty] private string _layeredEditorTitle = "新建走班组";
    [ObservableProperty] private string _layeredName = "新走班组";
    [ObservableProperty] private Subject? _layeredSubject;
    [ObservableProperty] private int _layeredDayIndex;
    [ObservableProperty] private PeriodOption? _layeredStartPeriod;
    [ObservableProperty] private PeriodOption? _layeredEndPeriod;
    [ObservableProperty] private string _layeredLockText = "尚未选择参与班级";

    /// <summary>0 = 固定时段；1 = 自动安排（灵活排课）。</summary>
    [ObservableProperty] private int _layeredModeIndex;

    [ObservableProperty] private int _layeredWeeklyHours = 1;

    // ----------------------------- 会议草稿 -----------------------------

    [ObservableProperty] private string _meetingEditorTitle = "新建会议时段";
    [ObservableProperty] private string _meetingName = "新会议时段";
    [ObservableProperty] private int _meetingDayIndex;
    [ObservableProperty] private PeriodOption? _meetingStartPeriod;
    [ObservableProperty] private PeriodOption? _meetingEndPeriod;
    [ObservableProperty] private bool _meetingAppliesToAll;
    [ObservableProperty] private bool _meetingIsLeadership;
    [ObservableProperty] private string _meetingNote = string.Empty;

    // ----------------------------- 计算属性 -----------------------------

    public bool IsIdle => !IsBusy;
    public bool CanOperate => HasProject && !IsBusy;
    public bool CanGenerate => HasProject && !IsBusy && IsTimetableConfirmed;
    public bool IsEmptyState => !HasProject;

    public bool IsTimetableStep => ActiveStep == 1;
    public bool IsResearchStep => ActiveStep == 2;
    public bool IsLayeredStep => ActiveStep == 3;
    public bool IsMeetingStep => ActiveStep == 4;
    public bool IsScheduleStep => ActiveStep == 5;

    public bool IsTimetableConfirmed => HasProject && TimetableService.IsConfirmed(Project);
    public bool IsTimetableUnconfirmed => HasProject && !IsTimetableConfirmed;
    public bool IsTimetableEmpty => HasProject && TimetableRows.Count == 0;
    public bool HasTimetablePreview => TimetablePeriodPreviews.Count > 0;
    public string TimetableGridBadge => $"5 × {Math.Max(1, Project.TeachingPeriodCount())}";

    public bool IsClassView => ViewMode == 0;
    public bool IsTeacherView => ViewMode == 1;
    public bool IsMasterView => ViewMode == 2;

    public bool IsClassListEmpty => FilteredClasses.Count == 0;
    public bool IsClassEmptyVisible => IsClassView && FilteredClasses.Count == 0;
    public string ClassEmptyText => _allClasses.Count == 0 ? "还没有班级" : "没有匹配的班级";
    public bool IsScheduleEmpty => HasProject && Project.Lessons.Count == 0;

    public bool HasMeetingSuggestions => MeetingSuggestions.Count > 0;
    public bool IsActivityEmpty => ActivityItems.Count == 0;
    public bool HasActivities => ActivityItems.Count > 0;
    public bool IsLayeredFlexible => LayeredModeIndex == 1;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool IsConflictEmpty => !HasConflicts;

    /// <summary>工程还没有任课关系时，05 步提示老师先去配置教师分工。</summary>
    public bool NeedsAssignmentSetup => HasProject && Project.Subjects.Count > 0 && Project.Assignments.Count == 0;

    public int ResearchSlotDay { get; private set; }
    public int ResearchSlotPeriod { get; private set; }

    public LayeredClassGroup? EditingLayeredGroup => _editingLayeredGroup;

    // ----------------------------- 载入 -----------------------------

    public void LoadProject(WeaveProject project)
    {
        TimetableService.Ensure(project);
        Project = project;
        HasProject = true;
        SearchText = string.Empty;
        ResearchSlotDay = 0;
        ResearchSlotPeriod = 0;
        SelectedClass = null;
        SelectedTeacher = null;
        SelectedResearchSubject = null;
        SelectedLayeredGroup = null;
        SelectedMeetingBlock = null;
        ViewMode = 0;
        ActiveStep = 1;

        _conflictModels.Clear();
        Conflicts.Clear();
        MeetingSuggestions.Clear();
        OnPropertyChanged(nameof(HasMeetingSuggestions));
        _editingLayeredGroup = null;
        _editingMeetingBlock = null;

        RefreshFacts();
        UpdateResearchHeader();
        UpdateProjectTexts();
        RefreshTimetableState();
        NotifyConflictState();
        SetStatus(IsTimetableConfirmed
            ? $"已载入 {Project.Classes.Count} 个班 · {Project.Teachers.Count} 位教师 · 作息时间表已确认。"
            : $"已载入 {Project.Classes.Count} 个班 · {Project.Teachers.Count} 位教师 · 请先确认作息时间表。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadImportResult(WeaveImportResult result, string sourcePath)
    {
        var project = result.Project;
        project.SourceFile = sourcePath;

        // 教研直接应用；会议只作为「备注建议」呈现，由老师在 04 步逐条确认。
        // 全体教师大会这类会议会锁住所有教师，直接应用可能让课时排不满。
        foreach (var block in result.SuggestedResearchBlocks)
            project.ResearchBlocks.Add(block);

        LoadProject(project);

        MeetingSuggestions.Clear();
        foreach (var block in result.SuggestedMeetingBlocks)
            MeetingSuggestions.Add(new MeetingBlockItemViewModel(block));
        OnPropertyChanged(nameof(HasMeetingSuggestions));

        var meetingHint = result.SuggestedMeetingBlocks.Count == 0
            ? string.Empty
            : $"；{result.SuggestedMeetingBlocks.Count} 条会议建议待 04 步确认";
        SetStatus($"识别 {result.SuggestedResearchBlocks.Count} 条教研（已应用）{meetingHint}。");
    }

    public void LoadDemoProject()
    {
        LoadProject(WeaveSamples.CreateDemo());
        SetStatus("演示数据 · 4 个班 · 作息已确认，可直接试用五步编排。");
    }

    private void RefreshFacts()
    {
        _allClasses.Clear();
        foreach (var schoolClass in Project.Classes)
            _allClasses.Add(new ClassItemViewModel(schoolClass));

        Subjects.Clear();
        foreach (var subject in Project.Subjects) Subjects.Add(subject);

        _teachers.Clear();
        _teachers.AddRange(Project.Teachers);

        TeacherItems.Clear();
        foreach (var teacher in Project.Teachers)
            TeacherItems.Add(new TeacherItemViewModel(teacher));

        PeriodOptions.Clear();
        var teachingPeriods = Project.Periods.Where(period => !period.IsExtension).OrderBy(period => period.Index).ToList();
        if (teachingPeriods.Count == 0)
            teachingPeriods = WeaveDefaults.CreatePeriods();
        foreach (var slot in teachingPeriods)
            PeriodOptions.Add(new PeriodOption(slot.Index, slot.Label, slot.TimeText));

        MeetingPeriodOptions.Clear();
        var allPeriods = Project.Periods
            .OrderBy(period => period.IsExtension ? 1 : 0)
            .ThenBy(period => period.Index)
            .ToList();
        if (allPeriods.Count == 0)
        {
            for (var index = 1; index <= 10; index++)
            {
                var label = index switch
                {
                    8 => "第8节（课后服务）",
                    9 => "第9节（课后服务）",
                    10 => "第10节（晚修）",
                    _ => $"第{index}节",
                };
                MeetingPeriodOptions.Add(new PeriodOption(index, label, string.Empty));
            }
        }
        else
        {
            foreach (var slot in allPeriods)
            {
                var suffix = slot.IsExtension ? "（扩展）" : string.Empty;
                MeetingPeriodOptions.Add(new PeriodOption(slot.Index, slot.Label + suffix, slot.TimeText));
            }

            // 旧工程的会议可能仍放在第 8–10 节；作息没有扩展时段时保留这些兜底选项。
            if (!allPeriods.Any(period => period.IsExtension))
            {
                var maxIndex = allPeriods.Max(period => period.Index);
                for (var index = maxIndex + 1; index <= 10; index++)
                {
                    var label = index switch
                    {
                        8 => "第8节（课后服务）",
                        9 => "第9节（课后服务）",
                        10 => "第10节（晚修）",
                        _ => $"第{index}节",
                    };
                    MeetingPeriodOptions.Add(new PeriodOption(index, label, string.Empty));
                }
            }
        }

        ResearchSubjects.Clear();
        foreach (var subject in Project.Subjects)
        {
            var teacherCount = Project.Assignments
                .Where(assignment => assignment.SubjectId == subject.Id)
                .Select(assignment => assignment.TeacherId)
                .Concat(Project.Teachers.Where(teacher => teacher.Subject == subject.Name).Select(teacher => teacher.Id))
                .Distinct(StringComparer.Ordinal)
                .Count();
            ResearchSubjects.Add(new ResearchSubjectItemViewModel(subject, teacherCount));
        }

        LayeredGroups.Clear();
        foreach (var group in Project.LayeredGroups)
            LayeredGroups.Add(new LayeredGroupItemViewModel(group, Project));

        MeetingBlocks.Clear();
        foreach (var block in Project.MeetingBlocks)
            MeetingBlocks.Add(new MeetingBlockItemViewModel(block));

        RefreshActivityItems();

        foreach (var option in LayeredClassOptions)
            option.PropertyChanged -= OnClassOptionChanged;
        LayeredClassOptions.Clear();
        foreach (var schoolClass in Project.Classes)
        {
            var option = new ClassOptionViewModel(schoolClass);
            option.PropertyChanged += OnClassOptionChanged;
            LayeredClassOptions.Add(option);
        }

        MeetingTeacherOptions.Clear();
        foreach (var teacher in Project.Teachers)
            MeetingTeacherOptions.Add(new TeacherOptionViewModel(teacher));

        SelectedTeacher = TeacherItems.FirstOrDefault();
        SelectedResearchSubject = ResearchSubjects.FirstOrDefault();
        SelectedLayeredGroup = LayeredGroups.FirstOrDefault();
        SelectedMeetingBlock = MeetingBlocks.FirstOrDefault();

        ApplyClassFilter();
        if (SelectedLayeredGroup is null) ResetLayeredDraft(null);
        if (SelectedMeetingBlock is null) ResetMeetingDraft(null);
        UpdateCounts();
        UpdateScheduleHeader();
    }

    private void UpdateProjectTexts()
    {
        // 不在标题区显示文件名：老师的文件名里常带姓名、班级或学校，
        // 截图分享时会变成隐私泄漏。只显示中性的导入摘要。
        SourceName = string.IsNullOrWhiteSpace(Project.SourceFile)
            ? "合成演示工程 · 本机运行"
            : $"已导入工作簿 · {Project.Classes.Count} 个班 · {Project.Teachers.Count} 位教师";

        ProjectTitle = string.IsNullOrWhiteSpace(Project.SchoolName)
            ? string.IsNullOrWhiteSpace(Project.GradeName) ? "未命名工程" : Project.GradeName
            : $"{Project.SchoolName} · {Project.GradeName}";

        ProjectSubtitle = string.IsNullOrWhiteSpace(Project.TermName)
            ? $"{Project.Assignments.Count} 条任课"
            : $"{Project.TermName} · {Project.Assignments.Count} 条任课";

        AssignmentSummary = $"{Project.Assignments.Count} 条任课 · {Project.Teachers.Count} 位教师";
        OnPropertyChanged(nameof(NeedsAssignmentSetup));
    }

    private void UpdateCounts()
    {
        ClassCountLabel = $"{Project.Classes.Count} 个班";
        TeacherCountLabel = $"{Project.Teachers.Count} 位教师";
        SummaryLine = $"{Project.Classes.Count} 个班 · {Project.Teachers.Count} 位教师 · " +
                      $"{Project.Subjects.Count} 个学科 · 5 × {Project.TeachingPeriodCount()} 节";
        FactResearch = $"{Project.ResearchBlocks.Count} 条教研";
        FactLayered = $"{Project.LayeredGroups.Count} 组走班";
        FactMeetings = $"{Project.MeetingBlocks.Count} 场会议";
        FactActivities = $"{Project.Activities.Count} 个";
        FactConflicts = Conflicts.Count == 0 ? "✓ 暂无冲突" : $"⚠ {Conflicts.Count} 处冲突";
    }

    public void SetStatus(string message, bool isError = false)
    {
        StatusText = message;
        StatusBrush = isError ? ErrorBrush : SuccessBrush;
    }

    /// <summary>生成或导入进行中时拒绝一切改课动作，避免后台任务读到半改状态。</summary>
    private bool RejectWhileBusy()
    {
        if (!IsBusy) return false;
        SetStatus("正在处理中，请等当前任务完成后再修改。", true);
        return true;
    }

    /// <summary>自动保存状态只占状态栏右侧，不覆盖老师正在看的操作提示。</summary>
    public void SetAutosaveStatus(string text, string detail, bool isError = false, bool isSuccess = true)
    {
        AutosaveText = text;
        AutosaveDetail = detail;
        AutosaveBrush = isError ? ErrorBrush : isSuccess ? SuccessBrush : MutedBrush;
    }

    public void SetRecoveryState(WeaveAutosaveInfo? info)
    {
        RecoveryAvailable = info is not null;
        RecoverySummary = info is null
            ? string.Empty
            : $"上次自动保存：{info.SavedAt:MM-dd HH:mm} · " +
              $"{(string.IsNullOrWhiteSpace(info.GradeName) ? "未命名年级" : info.GradeName)} " +
              $"{info.ClassCount} 个班";
    }

    // ----------------------------- 01 作息时间 -----------------------------

    /// <summary>重建作息步骤的只读列表、编译预览与确认状态。</summary>
    public void RefreshTimetableState()
    {
        TimetableRows.Clear();
        TimetablePeriodPreviews.Clear();

        if (!HasProject)
        {
            TimetableStatusText = "未确认";
            TimetableStatusBrush = ErrorBrush;
            TimetableSummary = "还没有载入作息时间表";
            TimetableValidationText = string.Empty;
            HasTimetableErrors = false;
            NotifyTimetableState();
            return;
        }

        var timetable = TimetableService.Ensure(Project);
        var validation = TimetableService.Validate(timetable);
        var lessonIndex = 0;
        foreach (var row in WeaveTimetables.OrderRows(timetable.Rows))
        {
            var item = new TimetableRowItemViewModel(row);
            if (row.Kind == TimetableRowKind.Lesson) item.LessonIndex = ++lessonIndex;
            TimetableRows.Add(item);
        }

        if (validation.IsValid)
        {
            try
            {
                foreach (var period in TimetableService.BuildPeriods(timetable))
                    TimetablePeriodPreviews.Add(new TimetablePeriodPreviewViewModel(period));
            }
            catch (GradeWeaveException)
            {
                // 预览失败不影响确认状态；具体原因由校验文案给出。
            }
        }

        var confirmed = timetable.IsConfirmed;
        TimetableStatusText = confirmed ? "已确认" : "未确认";
        TimetableStatusBrush = confirmed ? SuccessBrush : ErrorBrush;
        TimetableSummary = confirmed
            ? $"5 天 × {validation.LessonCount} 节正课" +
              (validation.ExtensionCount > 0 ? $" · {validation.ExtensionCount} 个扩展时段" : string.Empty)
            : $"{validation.Summary} · 确认后才能生成课表";

        TimetableValidationText = validation.Errors.Count > 0
            ? string.Join(" ", validation.Errors.Take(2))
            : validation.Warnings.Count > 0
                ? string.Join(" ", validation.Warnings.Take(2))
                : confirmed
                    ? "时间衔接正常，已可用于编排。"
                    : "时间衔接正常，点「确认并应用」后即可编排。";
        HasTimetableErrors = !validation.IsValid;

        NotifyTimetableState();
    }

    /// <summary>编辑窗口确认后刷新节次、网格与状态栏。</summary>
    public void AfterTimetableConfirmed(TimetableConfirmResult result)
    {
        ApplyTimetableConfirmation(result);
    }

    /// <summary>主步骤上的「确认并应用」：校验并编译当前工程里的作息。</summary>
    public void ConfirmTimetable()
    {
        if (!HasProject || RejectWhileBusy()) return;

        try
        {
            var result = TimetableService.Confirm(Project);
            ApplyTimetableConfirmation(result);
        }
        catch (GradeWeaveException ex)
        {
            RefreshTimetableState();
            SetStatus(ex.Message, true);
        }
    }

    private void ApplyTimetableConfirmation(TimetableConfirmResult result)
    {
        RefreshFacts();
        UpdateProjectTexts();
        RefreshTimetableState();
        NotifyConflictState();

        var summary = result.Messages.LastOrDefault() ?? "作息时间表已确认。";
        var notes = result.Messages.Count > 1
            ? " " + string.Join(" ", result.Messages.Take(result.Messages.Count - 1).Take(2))
            : string.Empty;
        SetStatus(summary + notes);

        ProjectChanged?.Invoke(this, EventArgs.Empty);
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyTimetableState()
    {
        OnPropertyChanged(nameof(IsTimetableConfirmed));
        OnPropertyChanged(nameof(IsTimetableUnconfirmed));
        OnPropertyChanged(nameof(IsTimetableEmpty));
        OnPropertyChanged(nameof(HasTimetablePreview));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(TimetableGridBadge));
    }

    // ----------------------------- 02 教研 -----------------------------

    private void UpdateResearchHeader()
    {
        if (SelectedResearchSubject is null)
        {
            ResearchSlotTitle = "点击任意格子";
            ResearchSlotLocked = "该时段 0 位教师不可排";
            return;
        }

        if (ResearchSlotDay > 0)
        {
            ResearchSlotTitle = $"{WeaveDefaults.DayName(ResearchSlotDay)}第 {ResearchSlotPeriod} 节";
            ResearchSlotLocked = $"该时段 {CountLockedTeachers(ResearchSlotDay, ResearchSlotPeriod)} 位教师不可排";
            return;
        }

        ResearchSlotTitle = $"{SelectedResearchSubject.Name} · 点击格子切换";
        ResearchSlotLocked = "该时段 0 位教师不可排";
    }

    public int CountLockedTeachers(int dayIndex, int period) =>
        Project.Teachers.Count(teacher =>
            Project.UnavailableReason(teacher.Id, dayIndex, period) is not null);

    public bool HasResearchAt(string subjectId, int dayIndex, int period) =>
        Project.ResearchBlocks.Any(block =>
            block.DayIndex == dayIndex &&
            block.SubjectIds.Contains(subjectId, StringComparer.Ordinal) &&
            period >= block.StartPeriod && period <= block.EndPeriod);

    /// <summary>点击网格：同一学科该时段已有教研则取消，没有则补上并合并相邻块。</summary>
    public void ToggleResearch(int dayIndex, int period)
    {
        if (RejectWhileBusy()) return;

        var item = SelectedResearchSubject;
        if (item is null)
        {
            SetStatus("请先在左侧选择一个学科。", true);
            return;
        }

        var subjectId = item.Subject.Id;
        var covering = Project.ResearchBlocks
            .Where(block =>
                block.DayIndex == dayIndex &&
                block.SubjectIds.Contains(subjectId, StringComparer.Ordinal) &&
                period >= block.StartPeriod && period <= block.EndPeriod)
            .ToList();

        var removing = covering.Count > 0;
        if (removing)
        {
            foreach (var block in covering)
                RemoveSubjectAt(block, subjectId, period);
        }
        else
        {
            Project.ResearchBlocks.Add(new ResearchBlock
            {
                SubjectIds = [subjectId],
                DayIndex = dayIndex,
                StartPeriod = period,
                EndPeriod = period,
            });
            MergeResearch(dayIndex, subjectId);
        }

        ResearchSlotDay = dayIndex;
        ResearchSlotPeriod = period;
        UpdateResearchHeader();
        UpdateCounts();

        var locked = CountLockedTeachers(dayIndex, period);
        SetStatus(removing
            ? $"已取消 {item.Name} 在{WeaveDefaults.DayName(dayIndex)}第 {period} 节的教研。"
            : $"已设置 {item.Name} 在{WeaveDefaults.DayName(dayIndex)}第 {period} 节教研 · 该时段 {locked} 位教师不可排。");

        ResearchGridInvalidated?.Invoke(this, EventArgs.Empty);
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSubjectAt(ResearchBlock block, string subjectId, int period)
    {
        Project.ResearchBlocks.Remove(block);
        var note = block.Note;
        var others = block.SubjectIds.Where(id => id != subjectId).ToList();

        if (others.Count > 0)
        {
            Project.ResearchBlocks.Add(new ResearchBlock
            {
                SubjectIds = others,
                DayIndex = block.DayIndex,
                StartPeriod = block.StartPeriod,
                EndPeriod = block.EndPeriod,
                Note = note,
            });
        }

        if (period > block.StartPeriod)
        {
            Project.ResearchBlocks.Add(new ResearchBlock
            {
                SubjectIds = [subjectId],
                DayIndex = block.DayIndex,
                StartPeriod = block.StartPeriod,
                EndPeriod = period - 1,
                Note = note,
            });
        }

        if (period < block.EndPeriod)
        {
            Project.ResearchBlocks.Add(new ResearchBlock
            {
                SubjectIds = [subjectId],
                DayIndex = block.DayIndex,
                StartPeriod = period + 1,
                EndPeriod = block.EndPeriod,
                Note = note,
            });
        }
    }

    private void MergeResearch(int dayIndex, string subjectId)
    {
        var blocks = Project.ResearchBlocks
            .Where(block =>
                block.DayIndex == dayIndex &&
                block.SubjectIds.Count == 1 &&
                block.SubjectIds[0] == subjectId)
            .OrderBy(block => block.StartPeriod)
            .ToList();
        if (blocks.Count == 0) return;

        foreach (var block in blocks)
            Project.ResearchBlocks.Remove(block);

        var start = blocks[0].StartPeriod;
        var end = blocks[0].EndPeriod;
        var note = blocks[0].Note;
        foreach (var block in blocks.Skip(1))
        {
            if (block.StartPeriod <= end + 1 && SameSession(end, block.StartPeriod))
            {
                end = Math.Max(end, block.EndPeriod);
                continue;
            }

            Project.ResearchBlocks.Add(new ResearchBlock
            {
                SubjectIds = [subjectId],
                DayIndex = dayIndex,
                StartPeriod = start,
                EndPeriod = end,
                Note = note,
            });
            start = block.StartPeriod;
            end = block.EndPeriod;
            note = block.Note;
        }

        Project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = [subjectId],
            DayIndex = dayIndex,
            StartPeriod = start,
            EndPeriod = end,
            Note = note,
        });
    }

    /// <summary>上午与下午之间不合并，保留「上午 1—4、下午 5—7」这样的多段教研。</summary>
    private bool SameSession(int firstPeriod, int secondPeriod)
    {
        var first = Project.Periods.FirstOrDefault(period => period.Index == firstPeriod);
        var second = Project.Periods.FirstOrDefault(period => period.Index == secondPeriod);
        return first is null || second is null || first.IsAfternoon == second.IsAfternoon;
    }

    // ----------------------------- 02 走班 -----------------------------

    private void ResetLayeredDraft(LayeredClassGroup? group)
    {
        _loadingDraft = true;
        _editingLayeredGroup = group;
        LayeredEditorTitle = group is null ? "新建走班组" : "编辑走班组";
        LayeredName = group?.Name ?? "新走班组";
        LayeredSubject = group is null
            ? Subjects.FirstOrDefault()
            : Project.FindSubject(group.SubjectId) ?? Subjects.FirstOrDefault();
        LayeredDayIndex = group is null ? 0 : Math.Clamp(group.DayIndex - 1, 0, Days.Count - 1);
        LayeredStartPeriod = FindPeriod(group?.StartPeriod ?? 1);
        LayeredEndPeriod = FindPeriod(group?.EndPeriod ?? 2);
        // 新走班组默认自动安排：时段在生成课表时统一落位，不再要求老师事先猜节次。
        LayeredModeIndex = group is { IsFlexible: false } ? 0 : 1;
        LayeredWeeklyHours = Math.Clamp(group?.WeeklyHours ?? 1, 1, 7);
        OnPropertyChanged(nameof(IsLayeredFlexible));

        foreach (var option in LayeredClassOptions)
        {
            option.IsSelected = group is not null &&
                                group.ClassIds.Contains(option.Class.Id, StringComparer.Ordinal);
        }

        var subjectTeachers = TeachersForSubject(LayeredSubject);
        LayeredLayers.Clear();
        if (group is not null)
        {
            foreach (var layer in group.Layers)
                LayeredLayers.Add(new LayerEditViewModel(subjectTeachers, layer));
        }

        if (LayeredLayers.Count < 2)
        {
            LayeredLayers.Clear();
            LayeredLayers.Add(new LayerEditViewModel(subjectTeachers) { Name = "A层" });
            LayeredLayers.Add(new LayerEditViewModel(subjectTeachers) { Name = "B层" });
        }

        HookLayeredLayerEvents();

        var roomWasEmpty = LayeredLayers
            .Select(layer => string.IsNullOrWhiteSpace(layer.Room))
            .ToList();

        // 教室只从上方勾选的参与班级中选取，并按层序自动带入。
        RefreshLayerRoomOptions();

        _loadingDraft = false;

        // 打开已有走班组时只补空值，不静默改掉老师上次保存的教师/教室。
        foreach (var layer in LayeredLayers.Where(layer => layer.Teacher is null))
            ApplyRoomTeacher(layer);
        for (var index = 0; index < LayeredLayers.Count; index++)
        {
            var layer = LayeredLayers[index];
            if (roomWasEmpty[index] && layer.Teacher is not null)
                ApplyTeacherRoom(layer);
        }
        RaiseLayeredPreview();
    }

    /// <summary>
    /// 用上方勾选的参与班级刷新各层教室可选项；
    /// 空教室按 A、B…层序自动带入第一个、第二个勾选班，已有的合法选择不覆盖。
    /// </summary>
    private void RefreshLayerRoomOptions()
    {
        var selectedRooms = LayeredClassOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Class.Name)
            .ToList();

        for (var index = 0; index < LayeredLayers.Count; index++)
        {
            var layer = LayeredLayers[index];
            layer.SetRoomOptions(selectedRooms);
            if (string.IsNullOrWhiteSpace(layer.Room) && index < selectedRooms.Count)
                layer.Room = selectedRooms[index];
        }
    }

    /// <summary>只取分工表里该学科的任课教师；教师档案学科一致但未建任课关系的也保留。</summary>
    private List<Teacher> TeachersForSubject(Subject? subject) => Project.TeachersOfSubject(subject);

    private void HookLayeredLayerEvents()
    {
        foreach (var layer in LayeredLayers)
        {
            layer.PropertyChanged -= OnLayeredLayerPropertyChanged;
            layer.PropertyChanged += OnLayeredLayerPropertyChanged;
        }
    }

    private void OnLayeredLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDraft || sender is not LayerEditViewModel layer) return;

        if (e.PropertyName == nameof(LayerEditViewModel.Teacher))
            ApplyTeacherRoom(layer);
        else if (e.PropertyName == nameof(LayerEditViewModel.Room))
            ApplyRoomTeacher(layer);
    }

    /// <summary>选中/带入教室后，从分工表找到该班该学科的任课教师。</summary>
    private void ApplyRoomTeacher(LayerEditViewModel layer)
    {
        if (LayeredSubject is null || string.IsNullOrWhiteSpace(layer.Room)) return;

        var schoolClass = LayeredClassOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Class)
            .FirstOrDefault(item => string.Equals(
                item.Name, layer.Room.Trim(), StringComparison.Ordinal));
        if (schoolClass is null) return;

        var assignedTeacherIds = Project.Assignments
            .Where(assignment =>
                assignment.ClassId == schoolClass.Id &&
                assignment.SubjectId == LayeredSubject.Id)
            .Select(assignment => assignment.TeacherId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (assignedTeacherIds.Count == 0) return;

        var candidates = layer.Teachers
            .Where(teacher => assignedTeacherIds.Contains(teacher.Id))
            .ToList();
        if (candidates.Count == 0) return;
        if (layer.Teacher is not null &&
            candidates.Any(teacher => teacher.Id == layer.Teacher.Id)) return;

        var usedTeacherIds = LayeredLayers
            .Where(other => !ReferenceEquals(other, layer))
            .Select(other => other.Teacher?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        layer.Teacher = candidates.FirstOrDefault(teacher => !usedTeacherIds.Contains(teacher.Id))
                        ?? candidates[0];
    }

    /// <summary>选中教师后，优先把教室带成该教师在本学科任课的参与班。</summary>
    private void ApplyTeacherRoom(LayerEditViewModel layer)
    {
        if (layer.Teacher is null || LayeredSubject is null) return;

        var selectedClasses = LayeredClassOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Class)
            .ToList();
        if (selectedClasses.Count == 0) return;

        var assignedRooms = selectedClasses
            .Where(schoolClass => Project.Assignments.Any(assignment =>
                assignment.ClassId == schoolClass.Id &&
                assignment.SubjectId == LayeredSubject.Id &&
                assignment.TeacherId == layer.Teacher.Id))
            .Select(schoolClass => schoolClass.Name)
            .ToList();
        if (assignedRooms.Count == 0) return;

        if (assignedRooms.Contains(layer.Room.Trim(), StringComparer.Ordinal)) return;

        var usedRooms = LayeredLayers
            .Where(other => !ReferenceEquals(other, layer))
            .Select(other => other.Room.Trim())
            .Where(room => room.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        layer.Room = assignedRooms.FirstOrDefault(room => !usedRooms.Contains(room))
                     ?? assignedRooms[0];
    }

    private void RefreshLayeredTeacherOptions()
    {
        var teachers = TeachersForSubject(LayeredSubject);
        foreach (var layer in LayeredLayers) layer.SetTeachers(teachers);
        foreach (var layer in LayeredLayers) ApplyRoomTeacher(layer);
        foreach (var layer in LayeredLayers.Where(layer => layer.Teacher is not null))
            ApplyTeacherRoom(layer);
    }

    private PeriodOption? FindPeriod(int index) =>
        PeriodOptions.FirstOrDefault(option => option.Index == index) ?? PeriodOptions.FirstOrDefault();

    private PeriodOption? FindMeetingPeriod(int index) =>
        MeetingPeriodOptions.FirstOrDefault(option => option.Index == index) ?? MeetingPeriodOptions.FirstOrDefault();

    private void RaiseLayeredPreview()
    {
        var selectedClasses = LayeredClassOptions.Count(option => option.IsSelected);

        if (LayeredModeIndex == 1)
        {
            var hours = Math.Clamp(LayeredWeeklyHours, 1, 7);
            var resolved = _editingLayeredGroup is { IsFlexible: true, ResolvedSlots.Count: > 0 } group
                ? " · 已落位 " + DescribeResolvedSlots(group)
                : string.Empty;
            LayeredLockText = selectedClasses == 0
                ? "尚未选择参与班级"
                : $"{selectedClasses} 个班 · 自动安排 {hours} 课时{resolved}";
        }
        else
        {
            var dayIndex = Math.Clamp(LayeredDayIndex, 0, Days.Count - 1) + 1;
            var start = LayeredStartPeriod?.Index ?? 1;
            var end = LayeredEndPeriod?.Index ?? start;
            if (end < start) (start, end) = (end, start);
            LayeredLockText = selectedClasses == 0
                ? "尚未选择参与班级"
                : $"{selectedClasses} 个班在 {WeaveDefaults.SlotText(dayIndex, start, end)} 锁定";
        }

        LayeredPreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeResolvedSlots(LayeredClassGroup group)
    {
        var slots = group.ResolvedSlots
            .Select(LayeredClassGroup.DecodeSlot)
            .Select(slot => WeaveDefaults.SlotText(slot.Day, slot.Period, slot.Period))
            .ToList();
        return slots.Count == 0 ? "待生成" : string.Join("、", slots);
    }

    partial void OnLayeredSubjectChanged(Subject? value)
    {
        if (_loadingDraft) return;
        RefreshLayeredTeacherOptions();
    }

    partial void OnLayeredModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsLayeredFlexible));
        RaiseLayeredPreview();
    }

    partial void OnLayeredWeeklyHoursChanged(int value) => RaiseLayeredPreview();

    [RelayCommand]
    private void SelectAllLayeredClasses()
    {
        foreach (var option in LayeredClassOptions) option.IsSelected = true;
    }

    [RelayCommand]
    private void ClearLayeredClasses()
    {
        foreach (var option in LayeredClassOptions) option.IsSelected = false;
    }

    private void OnClassOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDraft || e.PropertyName != nameof(ClassOptionViewModel.IsSelected)) return;
        RefreshLayerRoomOptions();
        foreach (var layer in LayeredLayers) ApplyRoomTeacher(layer);
        foreach (var layer in LayeredLayers.Where(layer => layer.Teacher is not null))
            ApplyTeacherRoom(layer);
        RaiseLayeredPreview();
    }

    [RelayCommand]
    private void AddLayeredGroup()
    {
        SelectedLayeredGroup = null;
        ResetLayeredDraft(null);
        SetStatus("已新建走班组草稿，填写后点「保存走班组」。");
    }

    [RelayCommand]
    private void DeleteLayeredGroup()
    {
        if (RejectWhileBusy()) return;

        if (SelectedLayeredGroup is not { } item)
        {
            SetStatus("请先在左侧选择一个走班组。", true);
            return;
        }

        Project.LayeredGroups.Remove(item.Group);
        RefreshLayeredList(null);
        if (SelectedLayeredGroup is null)
        {
            _editingLayeredGroup = null;
            ResetLayeredDraft(null);
        }

        UpdateCounts();
        SetStatus($"已删除走班组：{item.Name}。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SaveLayeredGroup()
    {
        if (!HasProject)
        {
            SetStatus("请先导入课表或载入示例数据。", true);
            return;
        }

        if (RejectWhileBusy()) return;

        var name = LayeredName.Trim();
        if (name.Length == 0)
        {
            SetStatus("请给走班组起一个名字。", true);
            return;
        }

        if (LayeredSubject is null)
        {
            SetStatus("请选择走班的学科。", true);
            return;
        }

        var classIds = LayeredClassOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Class.Id)
            .ToList();
        if (classIds.Count == 0)
        {
            SetStatus("请至少选择一个参与班级。", true);
            return;
        }

        var flexible = LayeredModeIndex == 1;
        var weeklyHours = Math.Clamp(LayeredWeeklyHours, 1, 7);
        var start = 1;
        var end = 1;
        if (!flexible)
        {
            start = LayeredStartPeriod?.Index ?? 1;
            end = LayeredEndPeriod?.Index ?? start;
            if (end < start)
            {
                SetStatus("结束节次不能早于开始节次。", true);
                return;
            }
        }

        if (LayeredLayers.Count < 2)
        {
            SetStatus("走班至少需要 A / B 两层。", true);
            return;
        }

        var subjectTeachers = TeachersForSubject(LayeredSubject);
        if (subjectTeachers.Count == 0)
        {
            SetStatus("分工表中没有该学科的任课教师，请先在 05 步「教师分工」里补充。", true);
            return;
        }

        var invalidTeacher = LayeredLayers.FirstOrDefault(layer =>
            layer.Teacher is null ||
            subjectTeachers.All(teacher => teacher.Id != layer.Teacher.Id));
        if (invalidTeacher is not null)
        {
            SetStatus(
                $"「{invalidTeacher.Name}」请选择 {LayeredSubject.Name} 的任课教师；教师列表只显示当前学科的教师。",
                true);
            return;
        }

        // 教室只能来自上方勾选的参与班级；旧工程的手填值会被要求改选。
        var selectedRoomNames = LayeredClassOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Class.Name)
            .ToHashSet(StringComparer.Ordinal);
        var invalidLayer = LayeredLayers.FirstOrDefault(layer =>
            !string.IsNullOrWhiteSpace(layer.Room) &&
            !selectedRoomNames.Contains(layer.Room.Trim(), StringComparer.Ordinal));
        if (invalidLayer is not null)
        {
            SetStatus(
                $"「{invalidLayer.Name}」的教室要从上方勾选的参与班级中选取，当前「{invalidLayer.Room.Trim()}」不是参与班教室。",
                true);
            return;
        }

        var group = _editingLayeredGroup;
        if (group is null)
        {
            group = new LayeredClassGroup();
            Project.LayeredGroups.Add(group);
        }

        group.Name = name;
        group.SubjectId = LayeredSubject.Id;
        group.ClassIds = classIds;
        group.IsFlexible = flexible;
        group.WeeklyHours = weeklyHours;
        group.DayIndex = flexible ? 1 : Math.Clamp(LayeredDayIndex, 0, Days.Count - 1) + 1;
        group.StartPeriod = flexible ? 1 : start;
        group.EndPeriod = flexible ? 1 : end;
        group.ResolvedSlots.Clear();
        group.Layers = LayeredLayers.Select((layer, index) => new LayeredClassLayer
        {
            Name = string.IsNullOrWhiteSpace(layer.Name) ? $"{(char)('A' + index)}层" : layer.Name.Trim(),
            TeacherId = layer.Teacher?.Id ?? string.Empty,
            Room = layer.Room.Trim(),
        }).ToList();

        RefreshLayeredList(group.Id);
        _editingLayeredGroup = group;
        ResetLayeredDraft(group);
        UpdateCounts();
        SetStatus(flexible
            ? $"已保存走班组：{name} · 自动安排 {weeklyHours} 课时，生成课表时挑空位。"
            : $"已保存走班组：{name} · {WeaveDefaults.SlotText(group.DayIndex, group.StartPeriod, group.EndPeriod)}。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLayeredList(string? selectId)
    {
        _loadingDraft = true;
        LayeredGroups.Clear();
        foreach (var group in Project.LayeredGroups)
            LayeredGroups.Add(new LayeredGroupItemViewModel(group, Project));
        _loadingDraft = false;

        SelectedLayeredGroup = LayeredGroups.FirstOrDefault(item => item.Group.Id == selectId)
                               ?? LayeredGroups.FirstOrDefault();
        if (SelectedLayeredGroup is not null)
            ResetLayeredDraft(SelectedLayeredGroup.Group);
    }

    [RelayCommand]
    private void AddLayer()
    {
        var index = LayeredLayers.Count;
        var name = index < 26 ? $"{(char)('A' + index)}层" : $"第 {index + 1} 层";
        LayeredLayers.Add(new LayerEditViewModel(TeachersForSubject(LayeredSubject)) { Name = name });
        HookLayeredLayerEvents();
        RefreshLayerRoomOptions();
        RaiseLayeredPreview();
    }

    public void RemoveLayer(LayerEditViewModel layer)
    {
        if (LayeredLayers.Count <= 2)
        {
            SetStatus("走班至少需要两层，不能再删。", true);
            return;
        }

        layer.PropertyChanged -= OnLayeredLayerPropertyChanged;
        LayeredLayers.Remove(layer);
        RefreshLayerRoomOptions();
        RaiseLayeredPreview();
    }

    // ----------------------------- 03 会议 -----------------------------

    private void ResetMeetingDraft(MeetingBlock? block)
    {
        _loadingDraft = true;
        _editingMeetingBlock = block;
        MeetingEditorTitle = block is null ? "新建会议时段" : "编辑会议时段";
        MeetingName = block?.Name ?? "新会议时段";
        MeetingDayIndex = block is null ? 0 : Math.Clamp(block.DayIndex - 1, 0, Days.Count - 1);
        MeetingStartPeriod = FindMeetingPeriod(block?.StartPeriod ?? 6);
        MeetingEndPeriod = FindMeetingPeriod(block?.EndPeriod ?? 7);
        MeetingAppliesToAll = block?.AppliesToAllTeachers ?? false;
        MeetingIsLeadership = block?.IsLeadership ?? false;
        MeetingNote = block?.Note ?? string.Empty;

        foreach (var role in MeetingRoleOptions)
            role.IsSelected = block is not null && block.Roles.Contains(role.Name, StringComparer.Ordinal);

        foreach (var teacher in MeetingTeacherOptions)
            teacher.IsSelected = block is not null &&
                                 block.TeacherIds.Contains(teacher.Teacher.Id, StringComparer.Ordinal);

        _loadingDraft = false;
    }

    [RelayCommand]
    private void AddMeetingBlock()
    {
        SelectedMeetingBlock = null;
        ResetMeetingDraft(null);
        SetStatus("已新建会议时段草稿，填写后点「保存会议时段」。");
    }

    [RelayCommand]
    private void DeleteMeetingBlock()
    {
        if (RejectWhileBusy()) return;

        if (SelectedMeetingBlock is not { } item)
        {
            SetStatus("请先在左侧选择一个会议时段。", true);
            return;
        }

        Project.MeetingBlocks.Remove(item.Block);
        RefreshMeetingList(null);
        if (SelectedMeetingBlock is null)
        {
            _editingMeetingBlock = null;
            ResetMeetingDraft(null);
        }

        UpdateCounts();
        SetStatus($"已删除会议时段：{item.Name}。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SaveMeetingBlock()
    {
        if (!HasProject)
        {
            SetStatus("请先导入课表或载入示例数据。", true);
            return;
        }

        if (RejectWhileBusy()) return;

        var name = MeetingName.Trim();
        if (name.Length == 0)
        {
            SetStatus("请给会议时段起一个名字。", true);
            return;
        }

        var start = MeetingStartPeriod?.Index ?? 1;
        var end = MeetingEndPeriod?.Index ?? start;
        if (end < start)
        {
            SetStatus("结束节次不能早于开始节次。", true);
            return;
        }

        var roles = MeetingRoleOptions.Where(role => role.IsSelected).Select(role => role.Name).ToList();
        var teacherIds = MeetingTeacherOptions
            .Where(teacher => teacher.IsSelected)
            .Select(teacher => teacher.Teacher.Id)
            .ToList();

        if (!MeetingAppliesToAll && roles.Count == 0 && teacherIds.Count == 0)
        {
            SetStatus("请至少选择角色或指定教师，或勾选「全体教师」。", true);
            return;
        }

        var block = _editingMeetingBlock;
        if (block is null)
        {
            block = new MeetingBlock();
            Project.MeetingBlocks.Add(block);
        }

        block.Name = name;
        block.DayIndex = Math.Clamp(MeetingDayIndex, 0, Days.Count - 1) + 1;
        block.StartPeriod = start;
        block.EndPeriod = end;
        block.Roles = roles;
        block.TeacherIds = teacherIds;
        block.AppliesToAllTeachers = MeetingAppliesToAll;
        block.IsLeadership = MeetingIsLeadership;
        block.Note = MeetingNote.Trim();

        RefreshMeetingList(block.Id);
        _editingMeetingBlock = block;
        ResetMeetingDraft(block);
        UpdateCounts();
        SetStatus($"已保存会议时段：{name} · {WeaveDefaults.SlotText(block.DayIndex, block.StartPeriod, block.EndPeriod)}。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshMeetingList(string? selectId)
    {
        _loadingDraft = true;
        MeetingBlocks.Clear();
        foreach (var block in Project.MeetingBlocks)
            MeetingBlocks.Add(new MeetingBlockItemViewModel(block));
        _loadingDraft = false;

        SelectedMeetingBlock = MeetingBlocks.FirstOrDefault(item => item.Block.Id == selectId)
                                ?? MeetingBlocks.FirstOrDefault();
        if (SelectedMeetingBlock is not null)
            ResetMeetingDraft(SelectedMeetingBlock.Block);
    }

    [RelayCommand]
    private void ApplyMeetingSuggestion()
    {
        if (RejectWhileBusy()) return;

        if (SelectedMeetingSuggestion?.Block is not { } source)
        {
            SetStatus("请先选择一条备注识别建议。", true);
            return;
        }

        var exists = Project.MeetingBlocks.Any(block =>
            block.Name == source.Name &&
            block.DayIndex == source.DayIndex &&
            block.StartPeriod == source.StartPeriod &&
            block.EndPeriod == source.EndPeriod);
        if (exists)
        {
            SetStatus("该建议已经在会议时段里，无需重复应用。");
            return;
        }

        var copy = new MeetingBlock
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? "备注会议" : source.Name,
            DayIndex = source.DayIndex,
            StartPeriod = source.StartPeriod,
            EndPeriod = source.EndPeriod,
            Roles = [.. source.Roles],
            TeacherIds = [.. source.TeacherIds],
            AppliesToAllTeachers = source.AppliesToAllTeachers,
            IsLeadership = source.IsLeadership,
            Note = source.Note,
        };
        Project.MeetingBlocks.Add(copy);
        RefreshMeetingList(copy.Id);
        UpdateCounts();
        SetStatus($"已应用备注建议：{copy.Name}。");
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----------------------------- 04 编排 -----------------------------

    private void UpdateScheduleHeader()
    {
        OnPropertyChanged(nameof(IsScheduleEmpty));

        if (!HasProject)
        {
            ScheduleHeaderTitle = "课表";
            ScheduleHeaderMeta = "导入课表后开始编排。";
            return;
        }

        var conflictText = Conflicts.Count == 0 ? "暂无冲突" : $"{Conflicts.Count} 处冲突";

        if (IsTeacherView)
        {
            if (SelectedTeacher is { } teacher)
            {
                var teacherLessons = Project.LessonsOfTeacher(teacher.Teacher.Id).ToList();
                var lessonCount = teacherLessons.Count;
                var lockedCount = teacherLessons.Count(lesson =>
                    lesson.IsFixed && !lesson.IsActivity && !lesson.IsLayered);
                ScheduleHeaderTitle = $"{teacher.Name} · 课表";
                ScheduleHeaderMeta =
                    $"{teacher.Meta} · 已排 {lessonCount} 节" +
                    (lockedCount > 0 ? $" · 锁定 {lockedCount} 节" : string.Empty) +
                    $" · {conflictText}";
                return;
            }

            ScheduleHeaderTitle = "教师视角";
            ScheduleHeaderMeta = "请选择一位教师。";
            return;
        }

        if (IsMasterView)
        {
            var lockedCount = Project.Lessons.Count(lesson =>
                lesson.IsFixed && !lesson.IsActivity && !lesson.IsLayered);
            ScheduleHeaderTitle = "全年级总表";
            ScheduleHeaderMeta =
                $"{Project.Classes.Count} 个班 × 5 天 × {Project.TeachingPeriodCount()} 节 · " +
                $"已排 {Project.Lessons.Count} 节" +
                (lockedCount > 0 ? $" · 锁定 {lockedCount} 节" : string.Empty) +
                $" · {conflictText}";
            return;
        }

        if (SelectedClass is { } classItem)
        {
            var classLessons = Project.Lessons
                .Where(lesson => lesson.ClassId == classItem.Class.Id)
                .ToList();
            var lessonCount = classLessons.Count;
            var lockedCount = classLessons.Count(lesson =>
                lesson.IsFixed && !lesson.IsActivity && !lesson.IsLayered);
            ScheduleHeaderTitle = $"{classItem.Name} · 课表";
            ScheduleHeaderMeta =
                $"{classItem.Meta} · 已排 {lessonCount} 节" +
                (lockedCount > 0 ? $" · 锁定 {lockedCount} 节" : string.Empty) +
                $" · {conflictText}";
            return;
        }

        ScheduleHeaderTitle = "班级视角";
        ScheduleHeaderMeta = "请选择一个班级。";
    }

    public void MoveLesson(string classId, int fromDay, int fromPeriod, int toDay, int toPeriod)
    {
        if (RejectWhileBusy()) return;

        if (!TimetableService.IsConfirmed(Project))
        {
            SetStatus("请先在 01 步「作息时间」里确认时间表，再调整课表。", true);
            return;
        }

        if (fromDay == toDay && fromPeriod == toPeriod)
        {
            SetStatus("同一格，未移动。");
            return;
        }

        var source = Project.LessonAt(classId, fromDay, fromPeriod);
        if (source is null)
        {
            SetStatus("这一格没有课程可移动。", true);
            return;
        }

        if (source.IsActivity)
        {
            SetStatus("活动占位不能拖动，请在「活动」列表里修改时间或处理方式。", true);
            return;
        }

        if (source.IsLayered)
        {
            SetStatus("走班课由走班组统一安排，不能单独拖动；请到 03 步调整走班组。", true);
            return;
        }

        if (Project.LessonAt(classId, toDay, toPeriod) is { IsActivity: true })
        {
            SetStatus("目标格是活动占位，不能覆盖；请先修改或删除该活动。", true);
            return;
        }

        LayeredClassGroup? targetGroup = null;
        ConstraintRules.TryGetLayeredGroupAt(Project, classId, toDay, toPeriod, out targetGroup);
        if (targetGroup is not null || Project.LessonAt(classId, toDay, toPeriod) is { IsLayered: true })
        {
            SetStatus("目标格是走班时段，由走班组统一安排，不能放普通课。", true);
            return;
        }

        var subjectName = Project.FindSubject(source.SubjectId)?.Name ?? "课程";
        var target = Project.LessonAt(classId, toDay, toPeriod);
        if (target is null)
        {
            source.DayIndex = toDay;
            source.Period = toPeriod;
            source.IsFixed = true;
            SetStatus($"已把 {subjectName} 移到 {WeaveDefaults.SlotText(toDay, toPeriod, toPeriod)}，并锁定该格。");
        }
        else
        {
            var targetSubject = Project.FindSubject(target.SubjectId)?.Name ?? "课程";
            var (sourceDay, sourcePeriod) = (source.DayIndex, source.Period);
            source.DayIndex = target.DayIndex;
            source.Period = target.Period;
            target.DayIndex = sourceDay;
            target.Period = sourcePeriod;
            source.IsFixed = true;
            target.IsFixed = true;
            SetStatus($"已把 {subjectName} 与 {targetSubject}（{WeaveDefaults.SlotText(toDay, toPeriod, toPeriod)}）交换，并锁定两格。");
        }

        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RefreshConflictsAsync(quiet: true, announce: false);
    }

    public void ClearLessonAt(string classId, int dayIndex, int period)
    {
        if (RejectWhileBusy()) return;

        if (!TimetableService.IsConfirmed(Project))
        {
            SetStatus("请先在 01 步「作息时间」里确认时间表，再调整课表。", true);
            return;
        }

        var lesson = Project.LessonAt(classId, dayIndex, period);
        if (lesson is null)
        {
            SetStatus("这一格没有课程。");
            return;
        }

        if (lesson.IsActivity)
        {
            SetStatus("这是活动占位，不能按课程删除；请在下方「活动」列表里编辑或删除活动。", true);
            return;
        }

        LayeredClassGroup? layeredGroup = null;
        ConstraintRules.TryGetLayeredGroupAt(Project, classId, dayIndex, period, out layeredGroup);
        if (lesson.IsLayered || layeredGroup is not null)
        {
            SetStatus("这是走班课，由走班组统一安排；请到 03 步调整走班组，不能单独清除。", true);
            return;
        }

        var subjectName = Project.FindSubject(lesson.SubjectId)?.Name ?? "课程";
        var className = Project.FindClass(classId)?.Name ?? "该班";
        Project.RemoveLessonAt(classId, dayIndex, period);

        SetStatus($"已清除 {className} {WeaveDefaults.SlotText(dayIndex, period, period)} 的 {subjectName}，不会自动补课。");
        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RefreshConflictsAsync(quiet: true, announce: false);
    }

    /// <summary>锁定或解除锁定单个课程格；锁定课在重新生成时保留。</summary>
    public bool ToggleLessonLock(string classId, int dayIndex, int period)
    {
        if (RejectWhileBusy()) return false;

        if (!TimetableService.IsConfirmed(Project))
        {
            SetStatus("请先在 01 步「作息时间」里确认时间表，再锁定课程。", true);
            return false;
        }

        var lesson = Project.LessonAt(classId, dayIndex, period);
        if (lesson is null)
        {
            SetStatus("这一格没有课程，不能锁定。");
            return false;
        }

        if (lesson.IsActivity)
        {
            SetStatus("活动占位由「活动」列表管理，不需要锁定。", true);
            return false;
        }

        LayeredClassGroup? layeredGroup = null;
        ConstraintRules.TryGetLayeredGroupAt(Project, classId, dayIndex, period, out layeredGroup);
        if (lesson.IsLayered || layeredGroup is not null)
        {
            SetStatus("走班课由走班组统一安排，不能单独锁定；请到 03 步调整。", true);
            return false;
        }

        lesson.IsFixed = !lesson.IsFixed;
        var subjectName = Project.FindSubject(lesson.SubjectId)?.Name ?? "课程";
        var className = Project.FindClass(classId)?.Name ?? "该班";
        SetStatus(lesson.IsFixed
            ? $"已锁定 {className} {WeaveDefaults.SlotText(dayIndex, period, period)} 的 {subjectName}，重新生成时会保留。"
            : $"已解除 {className} {WeaveDefaults.SlotText(dayIndex, period, period)} 的 {subjectName} 锁定。");

        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
        return lesson.IsFixed;
    }

    /// <summary>批量锁定/解除某个班的普通课；走班与活动占位不参与。</summary>
    public int SetClassLessonsLocked(string classId, bool locked)
    {
        if (RejectWhileBusy()) return 0;

        if (!TimetableService.IsConfirmed(Project))
        {
            SetStatus("请先在 01 步「作息时间」里确认时间表，再锁定课程。", true);
            return 0;
        }

        var lessons = Project.Lessons
            .Where(lesson => lesson.ClassId == classId &&
                             !lesson.IsActivity &&
                             !lesson.IsLayered)
            .Where(lesson =>
            {
                ConstraintRules.TryGetLayeredGroupAt(
                    Project, lesson.ClassId, lesson.DayIndex, lesson.Period, out var group);
                return group is null;
            })
            .ToList();
        if (lessons.Count == 0)
        {
            SetStatus("本班还没有可锁定的普通课。");
            return 0;
        }

        foreach (var lesson in lessons) lesson.IsFixed = locked;

        var className = Project.FindClass(classId)?.Name ?? "本班";
        SetStatus(locked
            ? $"已锁定 {className} 的 {lessons.Count} 节普通课，重新生成时会保留。"
            : $"已解除 {className} 的 {lessons.Count} 节锁定。");
        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
        return lessons.Count;
    }

    /// <summary>把选中的学科与任课教师放入课程格；写入固定课，重新生成时保留。</summary>
    public void PlaceLesson(
        string classId, int dayIndex, int period, string subjectId, string teacherId)
    {
        if (RejectWhileBusy()) return;

        var outcome = LessonPlacementService.Place(
            Project, classId, dayIndex, period, subjectId, teacherId);
        SetStatus(outcome.Message, !outcome.Success);
        if (!outcome.Success) return;

        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RefreshConflictsAsync(quiet: true, announce: false);
    }

    // ----------------------------- 活动 -----------------------------

    private void RefreshActivityItems()
    {
        ActivityItems.Clear();
        foreach (var activity in Project.Activities
                     .OrderBy(item => item.DayIndex)
                     .ThenBy(item => item.StartTime, StringComparer.Ordinal))
            ActivityItems.Add(new ActivityItemViewModel(activity, Project));

        OnPropertyChanged(nameof(IsActivityEmpty));
        OnPropertyChanged(nameof(HasActivities));
    }

    /// <summary>新增或更新活动；替换式活动会立即落成固定占位课。</summary>
    public void SaveActivity(ActivityBlock activity)
    {
        if (RejectWhileBusy()) return;

        ArgumentNullException.ThrowIfNull(activity);

        var index = Project.Activities.FindIndex(item => item.Id == activity.Id);
        var isNew = index < 0;
        if (isNew) Project.Activities.Add(activity);
        else Project.Activities[index] = activity;

        ActivityResolver.Apply(Project);
        RefreshActivityItems();
        UpdateCounts();
        UpdateScheduleHeader();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshConflictsAsync(quiet: true, announce: false);

        var detail = DescribeActivity(activity);
        var tail = activity.Mode == ActivityMode.Replace
            ? " 该节原课已被移除，重新生成课表可尝试把课时补到其他时段。"
            : string.Empty;
        SetStatus($"{(isNew ? "已新增" : "已更新")}活动「{activity.Name}」：{detail}。{tail}");
    }

    /// <summary>删除活动；替换式活动释放占位格，被移除的原课不会自动恢复。</summary>
    public void DeleteActivity(string activityId)
    {
        if (RejectWhileBusy()) return;

        var activity = Project.Activities.FirstOrDefault(item => item.Id == activityId);
        if (activity is null)
        {
            SetStatus("没有找到要删除的活动。", true);
            return;
        }

        Project.Activities.Remove(activity);
        ActivityResolver.Apply(Project);
        RefreshActivityItems();
        UpdateCounts();
        UpdateScheduleHeader();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshConflictsAsync(quiet: true, announce: false);

        var tail = activity.Mode == ActivityMode.Replace
            ? " 占位格已释放，被移除的原课不会自动恢复。"
            : string.Empty;
        SetStatus($"已删除活动「{activity.Name}」。{tail}");
    }

    private string DescribeActivity(ActivityBlock activity)
    {
        var periods = ActivityResolver.ResolvePeriods(Project, activity)
            .Select(period => WeaveDefaults.PeriodText(Project, period))
            .ToList();
        var periodText = periods.Count == 0 ? "未匹配节次" : string.Join("、", periods);
        var scope = activity.ClassIds.Count == 0 ? "全年级" : $"{activity.ClassIds.Count} 个班";
        var mode = activity.Mode == ActivityMode.Replace ? "替换该节" : "仅叠加";
        return $"{WeaveDefaults.DayName(activity.DayIndex)} {ActivityResolver.TimeText(activity)} · " +
               $"{periodText} · {scope} · {mode}";
    }

    // ----------------------------- 新建工程 -----------------------------

    /// <summary>全新制作：按配置生成空白工程（班级 + 学科课时），任课教师到「教师分工」里填。</summary>
    public void CreateNewProject(NewProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var project = new WeaveProject
        {
            SchoolName = config.SchoolName.Trim(),
            GradeName = string.IsNullOrWhiteSpace(config.GradeName) ? "九年级" : config.GradeName.Trim(),
            TermName = config.TermName.Trim(),
        };

        var prefix = ClassPrefix(project.GradeName);
        for (var index = 1; index <= Math.Clamp(config.ClassCount, 1, 60); index++)
            project.Classes.Add(new SchoolClass
            {
                Id = "c" + index,
                Name = $"{prefix}（{index}）班",
            });

        for (var index = 0; index < config.Subjects.Count; index++)
        {
            var draft = config.Subjects[index];
            project.Subjects.Add(new Subject
            {
                Id = "s" + (index + 1),
                Name = draft.Name.Trim(),
                WeeklyHours = Math.Clamp(draft.WeeklyHours, 0, 35),
                IsMain = draft.IsMain,
            });
        }

        LoadProject(project);
        ActiveStep = 1;
        SetStatus($"已创建空白工程：{project.Classes.Count} 个班 · {project.Subjects.Count} 个学科。先确认作息时间表，再到 05 步「教师分工」填写任课教师。");
    }

    private static string ClassPrefix(string gradeName)
    {
        var text = gradeName.Trim();
        if (text.EndsWith("级", StringComparison.Ordinal) && text.Length > 1) text = text[..^1];
        return text.Length == 0 ? "九" : text;
    }

    /// <summary>分工编辑保存后刷新班级 / 教师 / 学科摘要与列表。</summary>
    public void RefreshAfterAssignmentEdit()
    {
        RefreshFacts();
        UpdateProjectTexts();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("教师分工已更新。");
    }

    public async Task GenerateAsync()
    {
        if (!HasProject)
        {
            SetStatus("请先导入课表或载入示例数据。", true);
            return;
        }

        if (!TimetableService.IsConfirmed(Project))
        {
            ActiveStep = 1;
            RefreshTimetableState();
            SetStatus("请先在 01 步「作息时间」里确认时间表，再生成课表。", true);
            return;
        }

        IsBusy = true;
        SetStatus("正在编排课表…");
        try
        {
            // 生成在工程快照上进行：调度线程不接触界面正在编辑的工程，
            // 自动保存与界面刷新也不会读到删了一半的课表。
            var working = Project.Snapshot();
            var result = await Task.Run(() => new WeaveScheduler().Generate(working));
            ApplyGeneratedSchedule(working);
            var layeredText = DescribeLayeredSchedule(result.Messages);
            if (result.Success)
            {
                SetStatus($"已编排 {result.PlacedCount} / {result.TotalCount} 节课 · 用时 {result.Elapsed.TotalSeconds:0.0} 秒。" + layeredText);
            }
            else
            {
                var reason = result.Unplaced.FirstOrDefault()?.Reason;
                var meetingHint = Project.MeetingBlocks.Any(block => block.AppliesToAllTeachers)
                    ? "；全体教师大会会锁住所有教师，可到 04 步把会议改到课后服务 / 晚修时段"
                    : string.Empty;
                SetStatus(
                    $"已编排 {result.PlacedCount} / {result.TotalCount} 节，仍有 {result.Unplaced.Count} 节未排" +
                    (string.IsNullOrWhiteSpace(reason) ? "。" : $"：{reason}") + meetingHint + layeredText,
                    true);
            }

            UpdateScheduleHeader();
            RefreshLayeredList(SelectedLayeredGroup?.Group.Id);
            ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
            await RefreshConflictsAsync(quiet: true, announce: false);
        }
        catch (GradeWeaveException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            SetStatus($"生成课表失败：{ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把快照上排好的结果一次性写回工程：课程整体替换，走班落位按组同步。</summary>
    private void ApplyGeneratedSchedule(WeaveProject working)
    {
        Project.Lessons.Clear();
        Project.Lessons.AddRange(working.Lessons);

        foreach (var group in Project.LayeredGroups)
        {
            var source = working.LayeredGroups.FirstOrDefault(item => item.Id == group.Id);
            if (source is null) continue;
            group.ResolvedSlots.Clear();
            group.ResolvedSlots.AddRange(source.ResolvedSlots);
        }
    }

    /// <summary>生成后把各组走班的实际时段与「参与班同节走班」提示写进状态栏。</summary>
    private string DescribeLayeredSchedule(IEnumerable<string> messages)
    {
        var parts = new List<string>();
        foreach (var group in Project.LayeredGroups)
        {
            var hasLessons = Project.Lessons.Any(lesson => lesson.IsLayered && lesson.GroupId == group.Id);
            if (!hasLessons) continue;

            var slots = group.Slots().ToList();
            if (slots.Count == 0) continue;

            var slotText = string.Join("、", slots.Select(slot =>
                WeaveDefaults.SlotText(slot.Day, slot.Period, slot.Period)));
            var scope = group.ClassIds.Count > 0 ? $" · {group.ClassIds.Count} 个班同节走班" : string.Empty;
            parts.Add($"「{group.Name}」{slotText}{scope}");
        }

        var issues = messages
            .Where(message => message.Contains("走班组", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var text = string.Empty;
        if (parts.Count > 0)
        {
            var summary = parts.Count <= 3
                ? string.Join("；", parts)
                : $"{string.Join("；", parts.Take(3))} 等 {parts.Count} 组";
            text += $" 走班已填入：{summary}。";
        }

        if (issues.Count > 0)
            text += " " + string.Join(" ", issues);

        return text;
    }

    public async Task RefreshConflictsAsync(bool quiet = false, bool announce = true)
    {
        if (!HasProject) return;

        try
        {
            var conflicts = await Task.Run(() => new ConflictAnalyzer().Analyze(Project));
            ApplyConflicts(conflicts, announce);
        }
        catch (GradeWeaveException ex)
        {
            if (!quiet) SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            if (!quiet) SetStatus($"冲突检查失败：{ex.Message}", true);
        }
    }

    private void ApplyConflicts(List<WeaveConflict> conflicts, bool announce)
    {
        Conflicts.Clear();
        _conflictModels.Clear();
        foreach (var conflict in conflicts)
        {
            _conflictModels.Add(conflict);
            Conflicts.Add(new ConflictItemViewModel(conflict));
        }

        NotifyConflictState();
        UpdateScheduleHeader();
        if (announce)
        {
            SetStatus(conflicts.Count == 0
                ? "冲突检查完成 · 没有发现冲突。"
                : $"冲突检查完成 · 共 {conflicts.Count} 处，请在右侧逐条核对。");
        }

        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyConflictState()
    {
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(IsConflictEmpty));

        var teacherClash = Conflicts.Count(item => item.Conflict.Kind == ConflictKind.TeacherClash);
        var classClash = Conflicts.Count(item => item.Conflict.Kind == ConflictKind.ClassClash);
        var unavailable = Conflicts.Count(item => item.Conflict.Kind == ConflictKind.TeacherUnavailable);
        var other = Conflicts.Count - teacherClash - classClash - unavailable;
        ConflictSummaryText = Conflicts.Count == 0
            ? "暂无冲突"
            : $"{Conflicts.Count} 处 · 教师 {teacherClash} · 班级 {classClash} · 占用 {unavailable}" +
              (other > 0 ? $" · 其他 {other}" : string.Empty);
        ConflictCountBrush = Conflicts.Count == 0 ? SuccessBrush : ErrorBrush;
        UpdateCounts();
    }

    // ----------------------------- 视图切换 -----------------------------

    [RelayCommand]
    private void GoTimetableStep() => ActiveStep = 1;

    [RelayCommand]
    private void GoResearchStep() => ActiveStep = 2;

    [RelayCommand]
    private void GoLayeredStep() => ActiveStep = 3;

    [RelayCommand]
    private void GoMeetingStep() => ActiveStep = 4;

    [RelayCommand]
    private void GoScheduleStep() => ActiveStep = 5;

    [RelayCommand]
    private void ShowClassView() => ViewMode = 0;

    [RelayCommand]
    private void ShowTeacherView() => ViewMode = 1;

    [RelayCommand]
    private void ShowMasterView() => ViewMode = 2;

    // ----------------------------- 过滤与属性变更 -----------------------------

    private void ApplyClassFilter()
    {
        var keyword = (SearchText ?? string.Empty).Trim();
        var matches = keyword.Length == 0
            ? _allClasses
            : _allClasses.Where(item =>
                item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Class.HeadTeacher.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredClasses.Clear();
        foreach (var item in matches) FilteredClasses.Add(item);

        if (SelectedClass is null || !FilteredClasses.Contains(SelectedClass))
            SelectedClass = FilteredClasses.FirstOrDefault();

        OnPropertyChanged(nameof(IsClassListEmpty));
        OnPropertyChanged(nameof(IsClassEmptyVisible));
        OnPropertyChanged(nameof(ClassEmptyText));
    }

    partial void OnSearchTextChanged(string value) => ApplyClassFilter();

    partial void OnActiveStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsTimetableStep));
        OnPropertyChanged(nameof(IsResearchStep));
        OnPropertyChanged(nameof(IsLayeredStep));
        OnPropertyChanged(nameof(IsMeetingStep));
        OnPropertyChanged(nameof(IsScheduleStep));
    }

    partial void OnViewModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsClassView));
        OnPropertyChanged(nameof(IsTeacherView));
        OnPropertyChanged(nameof(IsMasterView));
        OnPropertyChanged(nameof(IsClassEmptyVisible));

        if (value == 1 && SelectedTeacher is null)
            SelectedTeacher = TeacherItems.FirstOrDefault();

        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedClassChanged(ClassItemViewModel? value)
    {
        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedTeacherChanged(TeacherItemViewModel? value)
    {
        UpdateScheduleHeader();
        ScheduleInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedResearchSubjectChanged(ResearchSubjectItemViewModel? value)
    {
        UpdateResearchHeader();
        ResearchGridInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedLayeredGroupChanged(LayeredGroupItemViewModel? value)
    {
        if (_loadingDraft) return;
        ResetLayeredDraft(value?.Group);
    }

    partial void OnSelectedMeetingBlockChanged(MeetingBlockItemViewModel? value)
    {
        if (_loadingDraft) return;
        ResetMeetingDraft(value?.Block);
    }

    partial void OnLayeredDayIndexChanged(int value)
    {
        if (!_loadingDraft) RaiseLayeredPreview();
    }

    partial void OnLayeredStartPeriodChanged(PeriodOption? value)
    {
        if (!_loadingDraft) RaiseLayeredPreview();
    }

    partial void OnLayeredEndPeriodChanged(PeriodOption? value)
    {
        if (!_loadingDraft) RaiseLayeredPreview();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanOperate));
        OnPropertyChanged(nameof(CanGenerate));
    }

    partial void OnHasProjectChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(CanOperate));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(IsTimetableConfirmed));
        OnPropertyChanged(nameof(IsTimetableUnconfirmed));
    }
}
