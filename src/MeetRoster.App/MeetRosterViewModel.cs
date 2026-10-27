using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Export;
using MeetRoster.Core.Import;
using MeetRoster.Core.Models;
using MeetRoster.Core.Scheduling;
using TeachCraft.Shared;

namespace MeetRoster.App;

public sealed partial class MeetRosterViewModel : ObservableObject
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#A34A36"));

    private MeetProject _project = new();
    private readonly Dictionary<string, List<RegistrationIssue>> _importIssues = new(StringComparer.Ordinal);
    private bool _isDemo = true;
    private bool _scheduleImported;
    private string _scheduleFilterKey = "all";

    public MeetProject Project => _project;

    public ObservableCollection<ClassItemViewModel> AllClasses { get; } = [];
    public ObservableCollection<ClassItemViewModel> VisibleClasses { get; } = [];
    public ObservableCollection<EventStatItem> EventStats { get; } = [];
    public ObservableCollection<ValidationIssueItem> ValidationIssues { get; } = [];
    public ObservableCollection<ValidationIssueItem> ImportNotices { get; } = [];
    public ObservableCollection<ScheduleDayItemViewModel> ScheduleDays { get; } = [];
    public ObservableCollection<ScheduleCategoryItemViewModel> ScheduleCategories { get; } = [];
    public ObservableCollection<ScheduleCategoryStatItem> ScheduleBreakdown { get; } = [];
    public ObservableCollection<ScheduleGroupViewModel> ScheduleGroups { get; } = [];

    public IReadOnlyList<string> IndividualEventNames => MeetCatalog.IndividualEvents;
    public IReadOnlyList<string> TeamEventNames => MeetCatalog.TeamEvents;

    public string ProjectTitle => _project.Title;
    public string ProjectMeta => _isDemo
        ? "示例数据 · 预览"
        : !HasAnyData
            ? "尚未导入数据"
            : HasRegistrationData
                ? _project.Days.Count > 0 ? "已导入报名与赛程" : "已导入报名表"
                : "已导入赛程";
    public string ProjectNote => _project.Note;
    public string DataModeHint => _isDemo
        ? "示例数据 · 报名与赛程预览"
        : HasAnyData
            ? "导入数据 · 本机处理"
            : "本机处理 · 尚未导入";
    public string RegistrationBadge => _isDemo
        ? "示例数据"
        : HasRegistrationData
            ? "已导入"
            : "待导入";
    public string DataSourceHint => _isDemo
        ? "名单来自合成示例，可随意点选与修改。"
        : !HasRegistrationData
            ? "从右上角导入班级报名表开始；文件仅在本机处理。"
            : "导入的数据仅在本机处理，不上传。";

    public bool HasAnyData => AllClasses.Count > 0 || _project.Days.Count > 0;
    public bool HasRegistrationData => _isDemo ||
                                      AllClasses.Any(c =>
                                          !string.IsNullOrWhiteSpace(c.Source.SourcePath) ||
                                          c.AthleteCount > 0);
    public bool ShowValidationPanel => HasSelectedClass && HasRegistrationData;
    public bool HasImportNotices => ImportNotices.Count > 0;
    public string ImportNoticeSummary => $"导入时的 {ImportNotices.Count} 条提示";
    public bool HasValidationIssues => ValidationIssues.Count > 0;
    public string ValidationOkText => "没有发现超限、重复或性别不符的项目。";
    public bool CanExport => !IsBusy && AllClasses.Any(c =>
        !string.IsNullOrWhiteSpace(c.Source.SourcePath));

    /// <summary>是否有未保存的改动（任何名单/单位信息编辑都会置位）。</summary>
    [ObservableProperty] private bool _isDirty;

    public bool CanSave => IsDirty && !IsBusy && AllClasses.Any(c =>
        !string.IsNullOrWhiteSpace(c.Source.SourcePath));

    public string ClassCountLabel => $"{AllClasses.Count:00}";
    public string AthleteTotalLabel => AllClasses.Sum(c => c.AthleteCount).ToString("00");
    public string EntryTotalLabel => AllClasses.Sum(c => c.EntryTotal).ToString("00");

    public string RegistrationSummary => !HasRegistrationData
        ? "尚未导入报名表"
        : $"{AllClasses.Count} 个班 · {AllClasses.Sum(c => c.AthleteCount)} 名运动员 · {AllClasses.Sum(c => c.EntryTotal)} 项次";

    public string RegistrationTabMeta => !HasRegistrationData
        ? "待导入"
        : $"{AllClasses.Count} 班 · {AllClasses.Sum(c => c.AthleteCount)} 人";

    public string ScheduleTabMeta => _project.Days.Count == 0
        ? "待导入"
        : $"{_project.Days.Count} 天 · {_project.Days.Sum(d => d.Slots.Count)} 项";

    public string ScheduleBadge => _project.Days.Count == 0
        ? "未导入"
        : _scheduleImported
            ? "往届赛程"
            : "示例排程";

    public bool HasScheduleDays => _project.Days.Count > 0;

    public string ScheduleEmptyTitle => _project.Days.Count == 0
        ? "尚未导入赛程"
        : "没有符合条件的项目";

    public string ScheduleTotalDaysLabel => _project.Days.Count.ToString("00");
    public string ScheduleTotalSlotsLabel => _project.Days.Sum(d => d.Slots.Count).ToString("00");
    public string ScheduleTotalEntriesLabel => _project.Days.Sum(d => d.Slots.Sum(s => s.Entries.Count)).ToString();
    public string ScheduleClassCountLabel => AllClasses.Count.ToString("00");
    public string ClassCountText => AllClasses.Count.ToString();

    public string ClassListSummary => string.IsNullOrWhiteSpace(ClassSearchText)
        ? $"共 {AllClasses.Count} 个参赛班级"
        : $"筛选出 {VisibleClasses.Count} 个班级";

    public bool HasVisibleClasses => VisibleClasses.Count > 0;

    public bool HasSelectedClass => SelectedClass is not null;

    public string SelectedClassFooter => SelectedClass is null
        ? "请先导入班级报名表"
        : $"共 {SelectedClass.RowCount} 行 · {SelectedClass.MaleCount} 男 · {SelectedClass.FemaleCount} 女 · {SelectedClass.EntryTotal} 项次";

    public string ValidationSummary => ValidationIssues.Count == 0
        ? "结构完整"
        : $"{ValidationIssues.Count(i => i.IsError)} 项需处理";

    [ObservableProperty] private string _classSearchText = string.Empty;
    [ObservableProperty] private ClassItemViewModel? _selectedClass;
    [ObservableProperty] private string _statusText = "尚未导入数据 · 从右上角开始";
    [ObservableProperty] private string _statusDetail = "尚未导入数据 · 从右上角开始";
    [ObservableProperty] private IBrush _statusBrush = NeutralBrush;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isRegistrationView = true;

    /// <summary>窄窗口下收起右侧概览，把宽度让给报名表（1120 最小尺寸时尤其重要）。</summary>
    [ObservableProperty] private bool _isCompact;
    public double OverviewWidth => IsCompact ? 0 : 284;
    public bool ShowOverview => !IsCompact;
    [ObservableProperty] private ScheduleDayItemViewModel? _selectedDay;
    [ObservableProperty] private string _scheduleDayTitle = string.Empty;
    [ObservableProperty] private string _scheduleDaySummary = string.Empty;
    [ObservableProperty] private string _scheduleEmptyText = string.Empty;
    [ObservableProperty] private bool _hasScheduleSlots;
    [ObservableProperty] private ClassItemViewModel? _scheduleHighlightClass;
    [ObservableProperty] private string _scheduleSourceName = "示例排程";
    [ObservableProperty] private string _scheduleHighlightSummary = "未选择高亮班级";
    [ObservableProperty] private bool _hasScheduleHighlight;

    public bool IsScheduleView => !IsRegistrationView;

    public MeetRosterViewModel()
    {
        var empty = new MeetProject
        {
            Title = "运动会报名",
            Venue = "田径场",
            Note = "导入班级报名表开始；每班每个单项最多男女各报 2 人，每人限报 2 个单项；团体项目最多报 3 项。",
        };
        LoadProject(empty, isDemo: false, scheduleImported: false);
    }

    /// <summary>仅用于设计走查与截图；正常使用不会自动载入示例数据。</summary>
    public void LoadDemoData()
    {
        LoadProject(MeetSamples.CreateDemo(), isDemo: true, scheduleImported: false);
        SetStatus("已载入合成示例数据 · 仅用于界面走查");
    }

    partial void OnClassSearchTextChanged(string value)
    {
        ApplyClassFilter();
        OnPropertyChanged(nameof(ClassListSummary));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    partial void OnIsCompactChanged(bool value)
    {
        OnPropertyChanged(nameof(OverviewWidth));
        OnPropertyChanged(nameof(ShowOverview));
    }

    partial void OnSelectedClassChanged(ClassItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedClass));
        OnPropertyChanged(nameof(SelectedClassFooter));
        OnPropertyChanged(nameof(ShowValidationPanel));
        RebuildEventStats();
        RebuildValidationIssues();
        RebuildImportNotices();

        if (value is not null && IsRegistrationView)
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

    partial void OnScheduleHighlightClassChanged(ClassItemViewModel? value)
    {
        HasScheduleHighlight = value is not null;
        RefreshHighlightSummary();
        RefreshSchedule();
    }

    public void SelectSection(string section)
    {
        var showSchedule = string.Equals(section, "schedule", StringComparison.Ordinal);
        IsRegistrationView = !showSchedule;

        if (showSchedule)
        {
            if (!_scheduleImported && _project.Days.Count > 0)
            {
                MeetScheduleBuilder.RefreshEntries(_project);
                foreach (var day in ScheduleDays)
                    day.RefreshMeta();
            }

            RefreshScheduleCategories();
            RefreshSchedule();
            RefreshScheduleSummary();
            SetStatus(ScheduleSourceName + " · 赛程浏览");
            return;
        }

        SetStatus(SelectedClass is null ? "尚未导入报名表" : $"正在查看 {SelectedClass.Name} 的报名表");
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

    public void ClearScheduleHighlight()
    {
        ScheduleHighlightClass = null;
        SetStatus("已清除班级高亮");
    }

    public void AddAthlete()
    {
        if (SelectedClass is null) return;

        // 末尾已经有空白行时不再叠加，避免连点生成一串空行。
        var last = SelectedClass.Athletes.LastOrDefault();
        if (last is not null && string.IsNullOrWhiteSpace(last.Name) &&
            string.IsNullOrWhiteSpace(last.RawEvent(AthleteRowViewModel.SlotTeam1)) &&
            string.IsNullOrWhiteSpace(last.RawEvent(AthleteRowViewModel.SlotIndividual1)))
        {
            SetStatus("末尾已有一行空白，直接填写即可");
            return;
        }

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

    public async Task ImportRegistrationFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        IsBusy = true;
        SetStatus($"正在导入 {paths.Count} 份报名表…");

        var errors = new List<string>();
        var results = new List<RegistrationImportResult>();
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    results.Add(RegistrationWorkbookImporter.Import(path));
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}：{ex.Message}");
                }
            }
        });

        IsBusy = false;
        if (results.Count == 0)
        {
            SetStatus("报名表导入失败：" + (errors.FirstOrDefault() ?? "没有可读取的文件"), true);
            return;
        }

        var duplicates = results
            .GroupBy(r => r.Class.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        var imported = results
            .GroupBy(r => r.Class.Name, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();

        _importIssues.Clear();
        foreach (var result in imported)
        {
            _importIssues[result.Class.Name] = result.Issues.ToList();
        }

        var project = new MeetProject
        {
            Title = results.Select(r => r.Title).FirstOrDefault(t => t.Length > 0) ?? "运动会报名",
            Venue = _isDemo ? "田径场" : _project.Venue,
            Note = _isDemo
                ? "每班每个单项最多男女各报 2 人，每人限报 2 个单项；团体项目最多报 3 项。"
                : _project.Note,
        };

        if (!_isDemo && _scheduleImported)
        {
            var importedByName = imported.ToDictionary(r => r.Class.Name, r => r, StringComparer.Ordinal);
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existing in _project.Classes)
            {
                if (importedByName.TryGetValue(existing.Name, out var replacement))
                {
                    project.Classes.Add(replacement.Class);
                    added.Add(existing.Name);
                }
                else
                {
                    project.Classes.Add(existing);
                }
            }

            foreach (var result in imported)
            {
                if (added.Contains(result.Class.Name)) continue;
                project.Classes.Add(result.Class);
            }
        }
        else
        {
            foreach (var result in imported)
                project.Classes.Add(result.Class);
        }

        if (!_isDemo)
        {
            foreach (var day in _project.Days)
                project.Days.Add(day);
        }

        LoadProject(project, isDemo: false, scheduleImported: !_isDemo && _scheduleImported,
            preferredClassName: imported[0].Class.Name);
        if (project.Days.Count == 0) _scheduleImported = false;

        IsDirty = false;
        var issueCount = imported.Sum(r => r.Issues.Count(i => i.IsError));
        var message = $"已导入 {imported.Count} 个班 · {imported.Sum(r => r.Class.Athletes.Count)} 名运动员";
        if (issueCount > 0) message += $" · {issueCount} 项违规";
        if (duplicates.Count > 0) message += $" · {duplicates.Count} 个同名班（取最后一份）";
        if (errors.Count > 0) message += $" · {errors.Count} 份失败";

        var detail = errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : string.Empty;
        SetStatus(message, errors.Count > 0, detail, success: errors.Count == 0);
    }

    public async Task ImportScheduleFileAsync(string path)
    {
        IsBusy = true;
        SetStatus("正在导入赛程工作簿…");

        ScheduleImportResult result;
        try
        {
            result = await Task.Run(() => ScheduleWorkbookImporter.Import(path));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            SetStatus(ErrorText.Describe(ex, "导入赛程"), true);
            return;
        }

        IsBusy = false;
        var project = new MeetProject
        {
            Title = _isDemo ? "运动会赛程" : _project.Title,
            Venue = _isDemo ? "田径场" : _project.Venue,
            Note = _project.Note,
        };

        if (!_isDemo)
        {
            foreach (var meetClass in _project.Classes)
                project.Classes.Add(meetClass);
        }

        var existing = project.Classes.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var className in result.ClassNames)
        {
            if (existing.Contains(className)) continue;
            project.Classes.Add(new MeetClass
            {
                Name = className,
                Group = GroupFromClassName(className),
            });
            existing.Add(className);
        }

        foreach (var day in result.Days)
            project.Days.Add(day);

        _scheduleImported = true;
        ScheduleSourceName = DisplayFileName(path);
        LoadProject(project, isDemo: false, scheduleImported: true,
            preferredClassName: SelectedClass?.Name);

        var entryCount = result.Days.Sum(d => d.Slots.Sum(s => s.Entries.Count));
        var message = $"已导入赛程 · {result.Days.Count} 天 · {result.Days.Sum(d => d.Slots.Count)} 个项目 · {entryCount} 条记录";
        if (result.Issues.Count > 0) message += $" · {result.Issues.Count} 条提醒";
        SetStatus(message, success: true);
    }

    public async Task ExportRegistrationFilesAsync(string outputDirectory)
    {
        var classes = AllClasses
            .Select(c => c.Source)
            .Where(c => !string.IsNullOrWhiteSpace(c.SourcePath))
            .ToList();
        if (classes.Count == 0)
        {
            SetStatus("当前班级没有原始报名表，无法按模板回写。", true);
            return;
        }

        IsBusy = true;
        SetStatus($"正在导出 {classes.Count} 份报名表…");
        var errors = new List<string>();
        var exported = 0;
        await Task.Run(() =>
        {
            foreach (var meetClass in classes)
            {
                try
                {
                    RegistrationWorkbookExporter.Export(meetClass, outputDirectory);
                    exported++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{meetClass.Name}：{ex.Message}");
                }
            }
        });
        IsBusy = false;

        if (exported == 0)
        {
            SetStatus("导出失败：" + (errors.FirstOrDefault() ?? "没有可导出的班级"), true);
            return;
        }

        IsDirty = false;
        var message = $"已导出 {exported} 份报名表到 {outputDirectory}";
        if (errors.Count > 0) message += $" · {errors.Count} 份失败";
        SetStatus(message, errors.Count > 0, string.Join(Environment.NewLine, errors), success: errors.Count == 0);
    }

    /// <summary>把当前名单写回各自的原始报名表（先备份 .bak 再替换）。</summary>
    public async Task SaveToSourceAsync()
    {
        var classes = AllClasses
            .Select(c => c.Source)
            .Where(c => !string.IsNullOrWhiteSpace(c.SourcePath))
            .ToList();

        if (classes.Count == 0)
        {
            SetStatus("当前没有可保存的原始报名表；用「另存为」导出到文件夹。", true);
            return;
        }

        IsBusy = true;
        SetStatus($"正在保存 {classes.Count} 份报名表…");

        var errors = new List<string>();
        var saved = 0;
        await Task.Run(() =>
        {
            foreach (var meetClass in classes)
            {
                try
                {
                    RegistrationWorkbookExporter.Save(meetClass);
                    saved++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{meetClass.Name}：{ex.Message}");
                }
            }
        });
        IsBusy = false;

        if (saved == 0)
        {
            SetStatus("保存失败：" + (errors.FirstOrDefault() ?? "没有可保存的班级"), true,
                string.Join(Environment.NewLine, errors));
            return;
        }

        IsDirty = false;
        var message = $"已保存 {saved} 份到原文件 · 原文件备份为 .bak";
        if (errors.Count > 0) message += $" · {errors.Count} 份失败";
        SetStatus(message, errors.Count > 0, string.Join(Environment.NewLine, errors),
            success: errors.Count == 0);
    }

    public void ClearClassSearch()
    {
        ClassSearchText = string.Empty;
        SetStatus("已清除班级筛选");
    }

    public void SetStatus(string text, bool error = false, string? detail = null, bool success = false)
    {
        StatusText = text;
        StatusBrush = error ? ErrorBrush : success ? SuccessBrush : NeutralBrush;
        StatusDetail = string.IsNullOrWhiteSpace(detail) ? text : detail;
    }

    private void LoadProject(MeetProject project, bool isDemo, bool scheduleImported,
        string? preferredClassName = null)
    {
        _project = project;
        _isDemo = isDemo;
        _scheduleImported = scheduleImported;
        _scheduleFilterKey = "all";

        AllClasses.Clear();
        foreach (var meetClass in project.Classes)
            AllClasses.Add(new ClassItemViewModel(meetClass, OnClassChanged));

        ScheduleDays.Clear();
        foreach (var day in project.Days)
            ScheduleDays.Add(new ScheduleDayItemViewModel(day));

        SelectedDay = ScheduleDays.FirstOrDefault();
        if (SelectedDay is null)
        {
            RefreshScheduleCategories();
            RefreshSchedule();
        }

        ApplyClassFilter();
        SelectedClass = VisibleClasses.FirstOrDefault(c => c.Name == preferredClassName)
                       ?? VisibleClasses.FirstOrDefault();
        ScheduleHighlightClass = null;
        HasScheduleHighlight = false;
        RefreshHighlightSummary();
        if (_scheduleImported)
        {
            if (string.IsNullOrWhiteSpace(ScheduleSourceName))
                ScheduleSourceName = "往届赛程";
        }
        else
        {
            ScheduleSourceName = _project.Days.Count == 0 ? "未导入赛程" : "示例排程";
        }
        RefreshScheduleSummary();
        RefreshProjectProperties();
    }

    private void RefreshProjectProperties()
    {
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(ProjectMeta));
        OnPropertyChanged(nameof(ProjectNote));
        OnPropertyChanged(nameof(DataModeHint));
        OnPropertyChanged(nameof(RegistrationBadge));
        OnPropertyChanged(nameof(DataSourceHint));
        OnPropertyChanged(nameof(HasRegistrationData));
        OnPropertyChanged(nameof(ShowValidationPanel));
        OnPropertyChanged(nameof(HasScheduleDays));
        OnPropertyChanged(nameof(ScheduleEmptyTitle));
        OnPropertyChanged(nameof(ClassCountLabel));
        OnPropertyChanged(nameof(AthleteTotalLabel));
        OnPropertyChanged(nameof(EntryTotalLabel));
        OnPropertyChanged(nameof(RegistrationSummary));
        OnPropertyChanged(nameof(RegistrationTabMeta));
        OnPropertyChanged(nameof(ClassListSummary));
        OnPropertyChanged(nameof(SelectedClassFooter));
        OnPropertyChanged(nameof(ScheduleTabMeta));
        OnPropertyChanged(nameof(ScheduleBadge));
        OnPropertyChanged(nameof(ScheduleClassCountLabel));
        OnPropertyChanged(nameof(ClassCountText));
        OnPropertyChanged(nameof(CanExport));
    }

    private void ApplyClassFilter()
    {
        var query = (ClassSearchText ?? string.Empty).Trim();
        VisibleClasses.Clear();

        foreach (var item in AllClasses)
        {
            if (query.Length == 0 || Matches(item, query))
                VisibleClasses.Add(item);
        }

        if (VisibleClasses.Count > 0 && (SelectedClass is null || !VisibleClasses.Contains(SelectedClass)))
            SelectedClass = VisibleClasses[0];
        else if (VisibleClasses.Count == 0)
            SelectedClass = null;

        OnPropertyChanged(nameof(HasVisibleClasses));
        OnPropertyChanged(nameof(ClassListSummary));
    }

    private static bool Matches(ClassItemViewModel item, string query) =>
        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.LeaderContact.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnClassChanged(ClassItemViewModel item)
    {
        IsDirty = true;

        // 名单一旦被编辑，导入时的旧提示就过期了：按当前数据重新体检，避免“改完了提示还在”。
        if (_importIssues.Remove(item.Name))
            RebuildImportNotices();

        OnPropertyChanged(nameof(AthleteTotalLabel));
        OnPropertyChanged(nameof(EntryTotalLabel));
        OnPropertyChanged(nameof(RegistrationSummary));
        OnPropertyChanged(nameof(RegistrationTabMeta));
        OnPropertyChanged(nameof(ProjectMeta));
        OnPropertyChanged(nameof(DataModeHint));
        OnPropertyChanged(nameof(RegistrationBadge));
        OnPropertyChanged(nameof(DataSourceHint));
        OnPropertyChanged(nameof(HasRegistrationData));
        OnPropertyChanged(nameof(ShowValidationPanel));
        OnPropertyChanged(nameof(SelectedClassFooter));

        if (ReferenceEquals(item, SelectedClass))
        {
            RebuildEventStats();
            RebuildValidationIssues();
            RebuildImportNotices();
        }

        if (!_scheduleImported && _project.Days.Count > 0)
        {
            MeetScheduleBuilder.RefreshEntries(_project);
            foreach (var day in ScheduleDays)
                day.RefreshMeta();
            RefreshSchedule();
        }
    }

    private void RefreshScheduleCategories()
    {
        ScheduleCategories.Clear();
        ScheduleBreakdown.Clear();

        if (SelectedDay is null)
        {
            HasScheduleSlots = false;
            ScheduleEmptyText = _project.Days.Count == 0
                ? "点击右上角“导入赛程”，选择含“竞赛日程 + 项目分组表”的工作簿。"
                : "当前还没有赛程。";
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
            "team", "团体", slots.Count(s => s.Category is MeetEventCategory.Team or MeetEventCategory.Fun),
            _scheduleFilterKey == "team"));
        ScheduleCategories.Add(new ScheduleCategoryItemViewModel(
            "ceremony", "仪式", slots.Count(s => s.Category == MeetEventCategory.Ceremony), _scheduleFilterKey == "ceremony"));

        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "径赛", slots.Count(s => s.Category == MeetEventCategory.Track), ScheduleCategoryStatItem.TrackBrush));
        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "田赛", slots.Count(s => s.Category == MeetEventCategory.Field), ScheduleCategoryStatItem.FieldBrush));
        ScheduleBreakdown.Add(new ScheduleCategoryStatItem(
            "团体", slots.Count(s => s.Category is MeetEventCategory.Team or MeetEventCategory.Fun),
            ScheduleCategoryStatItem.TeamBrush));
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
            ScheduleEmptyText = _project.Days.Count == 0
                ? "点击右上角“导入赛程”，选择含“竞赛日程 + 项目分组表”的工作簿。"
                : "当前还没有赛程。";
            return;
        }

        var day = SelectedDay.Source;
        var filtered = day.Slots.Where(MatchesScheduleFilter).ToList();
        var highlightName = ScheduleHighlightClass?.Name;

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
                Slots = slots.Select(s => new ScheduleSlotViewModel(s, highlightName)).ToList(),
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
        "fun" => slot.Category == MeetEventCategory.Fun,
        "team" => slot.Category is MeetEventCategory.Team or MeetEventCategory.Fun,
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
        OnPropertyChanged(nameof(ClassCountText));
    }

    private void RefreshHighlightSummary()
    {
        if (ScheduleHighlightClass is null)
        {
            ScheduleHighlightSummary = "未选择高亮班级";
            return;
        }

        var count = _project.Days
            .SelectMany(d => d.Slots)
            .SelectMany(s => s.Entries)
            .Count(e => e.ClassName == ScheduleHighlightClass.Name);
        ScheduleHighlightSummary = $"{ScheduleHighlightClass.Name} · {count} 条参赛记录";
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

    private void RebuildValidationIssues()
    {
        ValidationIssues.Clear();
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationIssues));
        if (SelectedClass is null) return;

        var issues = RegistrationValidator.Validate(SelectedClass.Source).ToList();

        foreach (var issue in issues
                     .GroupBy(i => (i.Message, i.IsError))
                     .Select(g => g.First()))
        {
            ValidationIssues.Add(new ValidationIssueItem(issue.Message, issue.IsError));
        }

        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationIssues));
        OnPropertyChanged(nameof(ShowValidationPanel));
    }

    private void RebuildImportNotices()
    {
        ImportNotices.Clear();

        // 与当前体检重复的条目不再重复展示：体检是实时算的，导入时那条只是历史快照。
        var live = ValidationIssues
            .Select(i => i.Message)
            .ToHashSet(StringComparer.Ordinal);

        if (SelectedClass is not null &&
            _importIssues.TryGetValue(SelectedClass.Name, out var imported))
        {
            foreach (var issue in imported
                         .GroupBy(i => (i.Message, i.IsError))
                         .Select(g => g.First()))
            {
                if (live.Any(m => m.Length > 0 && issue.Message.Contains(m, StringComparison.Ordinal)))
                    continue;

                ImportNotices.Add(new ValidationIssueItem(issue.Message, issue.IsError));
            }
        }

        OnPropertyChanged(nameof(HasImportNotices));
        OnPropertyChanged(nameof(ImportNoticeSummary));
    }

    /// <summary>收起导入时的历史提示（不影响当前体检结果）。</summary>
    public void DismissImportNotices()
    {
        if (SelectedClass is not null) _importIssues.Remove(SelectedClass.Name);
        RebuildImportNotices();
        SetStatus("已收起导入时的提示");
    }

    private static string GroupFromClassName(string className)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            className, @"^(?<group>[七八九]年级|高一年级|高二年级|高三年级)");
        return match.Success ? match.Groups["group"].Value : string.Empty;
    }

    private static string DisplayFileName(string path)
    {
        var name = Path.GetFileName(path);
        return System.Text.RegularExpressions.Regex.Replace(
            name,
            @"^[0-9a-fA-F-]{16,}_(?:[0-9a-fA-F-]{16,}_)?",
            string.Empty);
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

/// <summary>报名体检中的一条提示。</summary>
public sealed class ValidationIssueItem
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#A34A36"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#A07A3C"));

    public ValidationIssueItem(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }
    public bool IsError { get; }
    public IBrush Brush => IsError ? ErrorBrush : WarningBrush;
}

/// <summary>赛程今日分类统计。</summary>
public sealed class ScheduleCategoryStatItem
{
    public static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#A34A36"));
    public static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    public static readonly IBrush FunBrush = new SolidColorBrush(Color.Parse("#4F6D7A"));
    public static readonly IBrush TeamBrush = new SolidColorBrush(Color.Parse("#8A6A3C"));
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
