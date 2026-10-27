using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;
using MeetRoster.Core.Scheduling;

namespace MeetRoster.App;

public sealed partial class MeetRosterViewModel : ObservableObject
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#A34A36"));

    private readonly List<ClassItemViewModel> _allClasses = [];

    public MeetProject Project { get; }

    public ObservableCollection<ClassItemViewModel> VisibleClasses { get; } = [];
    public ObservableCollection<EventStatItem> EventStats { get; } = [];
    public ObservableCollection<ScheduleDayItemViewModel> ScheduleDays { get; } = [];
    public ObservableCollection<ScheduleCategoryItemViewModel> ScheduleCategories { get; } = [];
    public ObservableCollection<ScheduleCategoryStatItem> ScheduleBreakdown { get; } = [];
    public ObservableCollection<ScheduleGroupViewModel> ScheduleGroups { get; } = [];

    public IReadOnlyList<string> IndividualEventNames => MeetCatalog.IndividualEvents;
    public IReadOnlyList<string> TeamEventNames => MeetCatalog.TeamEvents;

    public string ProjectTitle => Project.Title;
    public string ProjectMeta => $"示例数据 · {Project.Venue}";
    public string ProjectNote => Project.Note;

    public string ClassCountLabel => $"{_allClasses.Count:00}";
    public string AthleteTotalLabel => _allClasses.Sum(c => c.AthleteCount).ToString("00");
    public string EntryTotalLabel => _allClasses.Sum(c => c.EntryTotal).ToString("00");

    public string RegistrationSummary =>
        $"{_allClasses.Count} 个班 · {_allClasses.Sum(c => c.AthleteCount)} 名运动员 · {_allClasses.Sum(c => c.EntryTotal)} 项次";

    public string RegistrationTabMeta => $"{_allClasses.Count} 班 · {_allClasses.Sum(c => c.AthleteCount)} 人";
    public string ScheduleTabMeta => $"{Project.Days.Count} 天 · {Project.Days.Sum(d => d.Slots.Count)} 项";
    public string ScheduleTotalDaysLabel => Project.Days.Count.ToString("00");
    public string ScheduleTotalSlotsLabel => Project.Days.Sum(d => d.Slots.Count).ToString("00");
    public string ScheduleTotalEntriesLabel => Project.Days.Sum(d => d.Slots.Sum(s => s.Entries.Count)).ToString();
    public string ScheduleClassCountLabel => _allClasses.Count.ToString("00");

    public string ClassListSummary => string.IsNullOrWhiteSpace(ClassSearchText)
        ? $"共 {ClassCountLabel} 个参赛班级"
        : $"筛选出 {VisibleClasses.Count} 个班级";

    public bool HasSelectedClass => SelectedClass is not null;

    public string SelectedClassFooter => SelectedClass is null
        ? "请选择左侧班级"
        : $"共 {SelectedClass.RowCount} 行 · {SelectedClass.MaleCount} 男 · {SelectedClass.FemaleCount} 女 · {SelectedClass.EntryTotal} 项次";

    [ObservableProperty] private string _classSearchText = string.Empty;
    [ObservableProperty] private ClassItemViewModel? _selectedClass;
    [ObservableProperty] private string _statusText = "示例数据已载入 · 报名表可直接预览";
    [ObservableProperty] private IBrush _statusBrush = SuccessBrush;

    [ObservableProperty] private bool _isRegistrationView = true;
    [ObservableProperty] private ScheduleDayItemViewModel? _selectedDay;
    [ObservableProperty] private string _scheduleDayTitle = string.Empty;
    [ObservableProperty] private string _scheduleDaySummary = string.Empty;
    [ObservableProperty] private string _scheduleEmptyText = string.Empty;
    [ObservableProperty] private bool _hasScheduleSlots;

    private string _scheduleFilterKey = "all";

    public bool IsScheduleView => !IsRegistrationView;

    public MeetRosterViewModel()
    {
        Project = MeetSamples.CreateDemo();

        foreach (var meetClass in Project.Classes)
            _allClasses.Add(new ClassItemViewModel(meetClass, OnClassChanged));

        foreach (var day in Project.Days)
            ScheduleDays.Add(new ScheduleDayItemViewModel(day));

        if (ScheduleDays.Count > 0)
        {
            ScheduleDays[0].IsSelected = true;
            SelectedDay = ScheduleDays[0];
        }

        ApplyClassFilter();
        SelectedClass = VisibleClasses.FirstOrDefault();
    }

    partial void OnClassSearchTextChanged(string value)
    {
        ApplyClassFilter();
        OnPropertyChanged(nameof(ClassListSummary));
    }

    partial void OnSelectedClassChanged(ClassItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedClass));
        OnPropertyChanged(nameof(SelectedClassFooter));
        RebuildEventStats();

        if (value is not null)
            SetStatus($"正在查看 {value.Name} 的报名表");
    }

    partial void OnIsRegistrationViewChanged(bool value) =>
        OnPropertyChanged(nameof(IsScheduleView));

    partial void OnSelectedDayChanged(ScheduleDayItemViewModel? value)
    {
        if (value is null) return;

        foreach (var day in ScheduleDays)
            day.IsSelected = ReferenceEquals(day, value);

        RefreshScheduleCategories();
        RefreshSchedule();
    }

    public void SelectSection(string section)
    {
        var showSchedule = string.Equals(section, "schedule", StringComparison.Ordinal);
        IsRegistrationView = !showSchedule;

        if (showSchedule)
        {
            MeetScheduleBuilder.RefreshEntries(Project);
            foreach (var day in ScheduleDays)
                day.RefreshMeta();

            RefreshScheduleCategories();
            RefreshSchedule();
            RefreshScheduleSummary();
            SetStatus($"{SelectedDay?.Name ?? "赛程"} · 示例排程已按当前报名刷新");
            return;
        }

        SetStatus($"正在查看 {SelectedClass?.Name} 的报名表");
    }

    public void SelectDay(ScheduleDayItemViewModel day)
    {
        SelectedDay = day;
        SetStatus($"{day.Name} · {day.DateText} 的赛程");
    }

    public void SelectScheduleCategory(ScheduleCategoryItemViewModel category)
    {
        _scheduleFilterKey = category.Key;

        foreach (var item in ScheduleCategories)
            item.IsActive = item.Key == category.Key;

        RefreshSchedule();
        SetStatus($"赛程筛选 · {category.Name}");
    }

    public void AddAthlete()
    {
        if (SelectedClass is null) return;
        SelectedClass.AddAthlete();
        OnPropertyChanged(nameof(SelectedClassFooter));
        SetStatus($"已为 {SelectedClass.Name} 添加一行");
    }

    public void RemoveAthlete(AthleteRowViewModel row)
    {
        if (SelectedClass is null) return;
        SelectedClass.RemoveAthlete(row);
        OnPropertyChanged(nameof(SelectedClassFooter));
        SetStatus($"已从 {SelectedClass.Name} 移除 1 行");
    }

    public void ImportPlaceholder() =>
        SetStatus("报名表导入将在下一步接入；当前展示合成示例数据。");

    public void ExportPlaceholder() =>
        SetStatus("导出功能将在报名逻辑确定后接入。");

    public void SetStatus(string text, bool error = false)
    {
        StatusText = text;
        StatusBrush = error ? ErrorBrush : SuccessBrush;
    }

    private void ApplyClassFilter()
    {
        var query = (ClassSearchText ?? string.Empty).Trim();
        VisibleClasses.Clear();

        foreach (var item in _allClasses)
        {
            if (query.Length == 0 || Matches(item, query))
                VisibleClasses.Add(item);
        }

        if (VisibleClasses.Count > 0 && (SelectedClass is null || !VisibleClasses.Contains(SelectedClass)))
            SelectedClass = VisibleClasses[0];
    }

    private static bool Matches(ClassItemViewModel item, string query) =>
        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.LeaderContact.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnClassChanged(ClassItemViewModel item)
    {
        OnPropertyChanged(nameof(AthleteTotalLabel));
        OnPropertyChanged(nameof(EntryTotalLabel));
        OnPropertyChanged(nameof(RegistrationSummary));
        OnPropertyChanged(nameof(RegistrationTabMeta));
        OnPropertyChanged(nameof(SelectedClassFooter));

        if (ReferenceEquals(item, SelectedClass))
            RebuildEventStats();
    }

    private void RefreshScheduleCategories()
    {
        ScheduleCategories.Clear();
        ScheduleBreakdown.Clear();

        if (SelectedDay is null)
        {
            HasScheduleSlots = false;
            ScheduleEmptyText = "当前还没有赛程。";
            return;
        }

        var slots = SelectedDay.Source.Slots;
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "all", "全部", slots.Count, _scheduleFilterKey == "all"));
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "track", "径赛", slots.Count(s => s.Category == MeetEventCategory.Track), _scheduleFilterKey == "track"));
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "field", "田赛", slots.Count(s => s.Category == MeetEventCategory.Field), _scheduleFilterKey == "field"));
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "team", "团体", slots.Count(s => s.Category == MeetEventCategory.Team), _scheduleFilterKey == "team"));
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "ceremony", "仪式", slots.Count(s => s.Category == MeetEventCategory.Ceremony), _scheduleFilterKey == "ceremony"));

        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "径赛", slots.Count(s => s.Category == MeetEventCategory.Track), ScheduleCategoryStatItem.TrackBrush));
        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "田赛", slots.Count(s => s.Category == MeetEventCategory.Field), ScheduleCategoryStatItem.FieldBrush));
        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "团体", slots.Count(s => s.Category == MeetEventCategory.Team), ScheduleCategoryStatItem.TeamBrush));
        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "仪式", slots.Count(s => s.Category == MeetEventCategory.Ceremony), ScheduleCategoryStatItem.CeremonyBrush));
    }

    private void RefreshSchedule()
    {
        ScheduleGroups.Clear();

        if (SelectedDay is null)
        {
            HasScheduleSlots = false;
            ScheduleDayTitle = "赛程表";
            ScheduleDaySummary = string.Empty;
            ScheduleEmptyText = "当前还没有赛程。";
            return;
        }

        var day = SelectedDay.Source;
        var filtered = day.Slots.Where(MatchesScheduleFilter).ToList();

        foreach (var group in filtered
                     .GroupBy(s => s.Period)
                     .OrderBy(g => g.Key == "上午" ? 0 : 1))
        {
            var slots = group.ToList();
            ScheduleGroups.Add(new ScheduleGroupViewModel
            {
                Label = group.Key,
                English = group.Key == "上午" ? "MORNING" : "AFTERNOON",
                TimeRange = slots.Count == 1 ? slots[0].Time : $"{slots[0].Time} – {slots[^1].Time}",
                Slots = slots.Select(s => new ScheduleSlotViewModel(s)).ToList(),
            });
        }

        ScheduleDayTitle = $"{day.Name} · {day.DateText}";
        ScheduleDaySummary = $"{filtered.Count} 个项目 · {filtered.Sum(s => s.Entries.Count)} 人次";
        HasScheduleSlots = ScheduleGroups.Count > 0;
        ScheduleEmptyText = "当前筛选下没有项目。";
    }

    private bool MatchesScheduleFilter(MeetSlot slot) => _scheduleFilterKey switch
    {
        "track" => slot.Category == MeetEventCategory.Track,
        "field" => slot.Category == MeetEventCategory.Field,
        "team" => slot.Category == MeetEventCategory.Team,
        "ceremony" => slot.Category == MeetEventCategory.Ceremony,
        _ => true,
    };

    private void RefreshScheduleSummary()
    {
        OnPropertyChanged(nameof(ScheduleTabMeta));
        OnPropertyChanged(nameof(ScheduleTotalDaysLabel));
        OnPropertyChanged(nameof(ScheduleTotalSlotsLabel));
        OnPropertyChanged(nameof(ScheduleTotalEntriesLabel));
        OnPropertyChanged(nameof(ScheduleClassCountLabel));
    }

    private void RebuildEventStats()
    {
        EventStats.Clear();
        if (SelectedClass is null) return;

        foreach (var eventName in MeetCatalog.IndividualEvents)
        {
            EventStats.Add(new EventStatItem(
                eventName,
                SelectedClass.CountFor("男子", eventName),
                SelectedClass.CountFor("女子", eventName)));
        }
    }
}

/// <summary>单项人数：男、女两栏各按“每班上限 2 人”给出计数。</summary>
public sealed class EventStatItem
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8A8476"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#A34A36"));
    private static readonly IBrush Moss = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#A34A36"));

    public EventStatItem(string name, int maleCount, int femaleCount)
    {
        Name = name;
        MaleCount = maleCount;
        FemaleCount = femaleCount;
    }

    public string Name { get; }
    public int MaleCount { get; }
    public int FemaleCount { get; }
    public string MaleLabel => $"{MaleCount}/2";
    public string FemaleLabel => $"{FemaleCount}/2";
    public IBrush MaleBrush => BrushFor(MaleCount);
    public IBrush FemaleBrush => BrushFor(FemaleCount);
    public bool IsOverLimit => MaleCount > 2 || FemaleCount > 2;

    private static IBrush BrushFor(int count) => count switch
    {
        <= 0 => Muted,
        1 => Accent,
        2 => Moss,
        _ => Danger,
    };
}

/// <summary>赛程今日分类统计。</summary>
public sealed class ScheduleCategoryStatItem
{
    public static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#A34A36"));
    public static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    public static readonly IBrush TeamBrush = new SolidColorBrush(Color.Parse("#4F6D7A"));
    public static readonly IBrush CeremonyBrush = new SolidColorBrush(Color.Parse("#7C7668"));

    public ScheduleCategoryStatItem(string name, int count, IBrush brush)
    {
        Name = name;
        Count = count;
        Brush = brush;
    }

    public string Name { get; }
    public int Count { get; }
    public IBrush Brush { get; }
    public string CountLabel => Count.ToString("00");
}
