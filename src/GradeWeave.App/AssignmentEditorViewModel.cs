using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GradeWeave.Core.Models;

namespace GradeWeave.App;

/// <summary>
/// 教师分工编辑器：先建立教师名单，再到班级 / 教师 / 学科 / 体检视角分配任课。
/// 五种视角共享同一份草稿；点「保存分工」才写回工程。
/// </summary>
public partial class AssignmentEditorViewModel : ObservableObject
{
    private const string UnspecifiedSubject = "未指定";

    private readonly WeaveProject _project;
    private readonly List<SchoolClass> _initialClasses;
    private string _initialSnapshot;
    private bool _refreshing;

    public AssignmentEditorViewModel(WeaveProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _initialClasses = project.Classes.ToList();
        Subjects = project.Subjects.ToList();
        SubjectChoices = BuildSubjectChoices(project);

        foreach (var schoolClass in project.Classes)
            Rows.Add(CreateRow(new AssignmentRowViewModel(schoolClass, Subjects, project)));

        BuildInitialRoster();
        TeacherRoster.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTeachers));
            OnPropertyChanged(nameof(IsTeacherRosterEmpty));
            OnPropertyChanged(nameof(CanStartAssignment));
        };

        RefreshDraftViews();
        _initialSnapshot = Snapshot();
    }

    public IReadOnlyList<Subject> Subjects { get; }

    /// <summary>名单里可选的学科：未指定 + 工程学科 + 教师档案里的历史学科。</summary>
    public IReadOnlyList<string> SubjectChoices { get; }

    public bool HasSubjects => Subjects.Count > 0;

    public ObservableCollection<AssignmentRowViewModel> Rows { get; } = new();

    /// <summary>草稿与打开时的分工有差异时为真，关闭窗口前用于询问是否放弃。</summary>
    public bool IsDirty => Snapshot() != _initialSnapshot;

    private string Snapshot() => string.Join("丨", Rows.Select(row => string.Join("‖",
        row.Class?.Id ?? string.Empty, row.ClassName, row.HeadTeacher,
        string.Join("¦", row.Cells.Select(cell => cell.Text))))) +
        "【" + string.Join("|", TeacherRoster.Select(item => item.Name + "=" + item.Subject)) + "】";

    // ----------------------------- 视角切换 -----------------------------

    /// <summary>0 教师名单 / 1 班级 / 2 教师 / 3 学科 / 4 体检。</summary>
    [ObservableProperty] private int _viewMode;

    public bool IsRosterView => ViewMode == 0;
    public bool IsClassView => ViewMode == 1;
    public bool IsTeacherView => ViewMode == 2;
    public bool IsSubjectView => ViewMode == 3;
    public bool IsOverviewView => ViewMode == 4;

    partial void OnViewModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsRosterView));
        OnPropertyChanged(nameof(IsClassView));
        OnPropertyChanged(nameof(IsTeacherView));
        OnPropertyChanged(nameof(IsSubjectView));
        OnPropertyChanged(nameof(IsOverviewView));
        RefreshDraftViews();
    }

    [RelayCommand]
    private void ShowRosterView() => ViewMode = 0;

    [RelayCommand]
    private void ShowClassView() => ViewMode = 1;

    [RelayCommand]
    private void ShowTeacherView() => ViewMode = 2;

    [RelayCommand]
    private void ShowSubjectView() => ViewMode = 3;

    [RelayCommand]
    private void ShowOverviewView() => ViewMode = 4;

    /// <summary>名单建好后直接进入教师视角开始分工。</summary>
    [RelayCommand]
    private void StartAssignment()
    {
        if (TeacherRoster.Count == 0)
        {
            Error = "教师名单还是空的，先添加本年级教师再分工。";
            return;
        }

        ViewMode = 2;
        ApplyTeacherFilter(TeacherRoster[0].Name);
    }

    // ----------------------------- 公共摘要 -----------------------------

    [ObservableProperty] private string _draftSummary = string.Empty;

    [ObservableProperty] private string _error = string.Empty;

    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    // ----------------------------- 教师名单 -----------------------------

    public ObservableCollection<AssignmentTeacherDraftViewModel> TeacherRoster { get; } = new();

    public ObservableCollection<AssignmentTeacherDraftViewModel> FilteredTeacherItems { get; } = new();

    [ObservableProperty] private AssignmentTeacherDraftViewModel? _selectedTeacherItem;

    public bool HasTeachers => TeacherRoster.Count > 0;
    public bool IsTeacherRosterEmpty => TeacherRoster.Count == 0;
    public bool CanStartAssignment => TeacherRoster.Count > 0;
    public bool IsTeacherListEmpty => FilteredTeacherItems.Count == 0;

    [ObservableProperty] private string _rosterSummary = "还没有教师";

    [ObservableProperty] private string _rosterMessage = string.Empty;

    public bool HasRosterMessage => RosterMessage.Length > 0;

    partial void OnRosterMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasRosterMessage));

    [ObservableProperty] private string _newTeacherName = string.Empty;
    [ObservableProperty] private string _newTeacherSubject = UnspecifiedSubject;
    [ObservableProperty] private string _bulkTeacherInput = string.Empty;

    [RelayCommand]
    private void AddTeacher()
    {
        var name = WeaveDefaults.NormalizeTeacherName(NewTeacherName);
        if (name.Length == 0)
        {
            Error = "先填写教师姓名，再添加到名单。";
            return;
        }

        if (FindRosterTeacher(name) is not null)
        {
            Error = $"教师「{name}」已经在名单里了。";
            return;
        }

        var subject = NormalizeSubjectChoice(NewTeacherSubject);
        var item = CreateRosterTeacher(name, subject);
        TeacherRoster.Add(item);
        NewTeacherName = string.Empty;
        Error = string.Empty;
        RosterMessage = $"已添加 {name}（{DescribeSubject(subject)}）。名单共 {TeacherRoster.Count} 位教师。";
        RefreshDraftViews();
    }

    [RelayCommand]
    private void BulkAddTeachers()
    {
        var lines = (BulkTeacherInput ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            Error = "先粘贴教师名单，再点批量添加。每行一位教师：姓名,学科。";
            return;
        }

        var added = 0;
        var duplicates = 0;
        var unknownSubjects = 0;

        foreach (var line in lines)
        {
            var parts = line
                .Split([',', '，', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;

            var name = WeaveDefaults.NormalizeTeacherName(parts[0]);
            if (name.Length == 0) continue;

            if (FindRosterTeacher(name) is not null)
            {
                duplicates++;
                continue;
            }

            var subject = string.Empty;
            if (parts.Length > 1)
            {
                var matched = FindSubjectName(parts[1]);
                if (matched is null) unknownSubjects++;
                else subject = matched;
            }

            TeacherRoster.Add(CreateRosterTeacher(name, subject));
            added++;
        }

        BulkTeacherInput = string.Empty;
        Error = string.Empty;
        RosterMessage = added == 0
            ? "没有新增教师：这些姓名都已经在名单里了。"
            : $"批量添加完成：新增 {added} 位，跳过重复 {duplicates} 位" +
              (unknownSubjects > 0 ? $"，{unknownSubjects} 位学科未识别、已记为未指定" : string.Empty) + "。";
        RefreshDraftViews();
    }

    /// <summary>从名单移除教师；有任课、班主任或课表记录时拒绝，避免悬空引用。</summary>
    public void RemoveTeacher(AssignmentTeacherDraftViewModel item)
    {
        var name = item.Name;
        Error = string.Empty;

        if (Rows.Any(row => NamesEqual(row.HeadTeacher, name)))
        {
            Error = $"「{name}」还担任班主任，先在班级视角更换班主任，再从名单删除。";
            return;
        }

        if (Rows.Any(row => row.Cells.Any(cell => NamesEqual(cell.Text, name))))
        {
            Error = $"「{name}」还有任课，先解除任课，再从名单删除。";
            return;
        }

        var existing = _project.Teachers.FirstOrDefault(teacher => NamesEqual(teacher.Name, name));
        if (existing is not null && HasProjectReferences(existing.Id))
        {
            Error = $"「{name}」已有课表、走班、会议或活动记录，不能直接从名单删除。";
            return;
        }

        TeacherRoster.Remove(item);
        RosterMessage = $"已从名单移除 {name}。";
        RefreshDraftViews();
    }

    /// <summary>从教师名单进入该教师的分工页。</summary>
    public void AssignTeacher(AssignmentTeacherDraftViewModel item) => ShowTeacher(item.Name);

    private void BuildInitialRoster()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var teacher in _project.Teachers)
        {
            var name = WeaveDefaults.NormalizeTeacherName(teacher.Name);
            if (name.Length == 0 || !seen.Add(name)) continue;
            TeacherRoster.Add(CreateRosterTeacher(name, teacher.Subject));
        }

        // 数据不完整时，任课格里可能出现没有教师档案的姓名；先补进名单，避免分工丢失。
        foreach (var row in Rows)
        {
            for (var index = 0; index < Subjects.Count && index < row.Cells.Count; index++)
            {
                var name = WeaveDefaults.NormalizeTeacherName(row.Cells[index].Text);
                if (name.Length == 0 || !seen.Add(name)) continue;
                TeacherRoster.Add(CreateRosterTeacher(name, Subjects[index].Name));
            }
        }
    }

    private AssignmentTeacherDraftViewModel CreateRosterTeacher(string name, string subjectName)
    {
        var item = new AssignmentTeacherDraftViewModel(
            name, subjectName, SubjectChoices, OnRosterSubjectChanged);
        return item;
    }

    private AssignmentTeacherDraftViewModel? FindRosterTeacher(string name) =>
        TeacherRoster.FirstOrDefault(item => NamesEqual(item.Name, name));

    private void OnRosterSubjectChanged()
    {
        RosterMessage = "学科已更新，分工下拉会按新学科重新筛选。";
        RefreshDraftViews();
    }

    private static List<string> BuildSubjectChoices(WeaveProject project)
    {
        var choices = new List<string> { UnspecifiedSubject };
        foreach (var subject in project.Subjects)
        {
            if (!choices.Contains(subject.Name, StringComparer.Ordinal)) choices.Add(subject.Name);
        }
        foreach (var teacher in project.Teachers)
        {
            var subject = teacher.Subject.Trim();
            if (subject.Length > 0 && !choices.Contains(subject, StringComparer.Ordinal))
                choices.Add(subject);
        }

        return choices;
    }

    private string NormalizeSubjectChoice(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice) ||
            string.Equals(choice, UnspecifiedSubject, StringComparison.Ordinal))
            return string.Empty;

        return FindSubjectName(choice) ?? choice.Trim();
    }

    private string? FindSubjectName(string raw)
    {
        var alias = WeaveDefaults.SubjectAlias(raw.Trim());
        return Subjects.FirstOrDefault(subject =>
            string.Equals(subject.Name, alias, StringComparison.Ordinal) ||
            string.Equals(subject.Name, raw.Trim(), StringComparison.Ordinal))?.Name;
    }

    private static string DescribeSubject(string subject) =>
        subject.Length == 0 ? "未指定学科" : subject;

    // ----------------------------- 班级视角 -----------------------------

    [RelayCommand]
    private void AddClass()
    {
        if (Rows.Count >= 60)
        {
            Error = "班级数最多 60 个，先保存好现有分工；更多班级请用导入工作簿的方式创建。";
            return;
        }

        Rows.Add(CreateRow(new AssignmentRowViewModel(null, Subjects, _project)));
        RefreshDraftViews();
    }

    [RelayCommand]
    private void RemoveLastClass()
    {
        if (Rows.Count == 0) return;
        Rows.Remove(Rows[^1]);
        RefreshDraftViews();
    }

    private AssignmentRowViewModel CreateRow(AssignmentRowViewModel row)
    {
        row.RemoveSelfRequested += (sender, _) =>
        {
            Rows.Remove((AssignmentRowViewModel)sender!);
            RefreshDraftViews();
        };

        foreach (var cell in row.Cells)
            cell.PropertyChanged += OnCellPropertyChanged;

        return row;
    }

    private void OnCellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssignmentCellViewModel.Text)) return;

        UpdateDraftSummary();
        if (IsSubjectView) UpdateSubjectStatsLive();
    }

    // ----------------------------- 教师视角 -----------------------------

    [ObservableProperty] private string _teacherSearchText = string.Empty;

    partial void OnTeacherSearchTextChanged(string value) => ApplyTeacherFilter();

    partial void OnSelectedTeacherItemChanged(AssignmentTeacherDraftViewModel? value)
    {
        if (_refreshing) return;
        RefreshTeacherMatrix();
    }

    [ObservableProperty] private string _teacherHeader = "教师视角";

    [ObservableProperty] private string _teacherSummary = "从左侧选择一位教师。";

    public ObservableCollection<AssignmentTeacherClassRowViewModel> TeacherClassRows { get; } = new();

    /// <summary>教师视角点击班级 × 学科格：指派给自己，或取消自己的任教。</summary>
    public void ToggleTeacherAssignment(AssignmentTeacherCellViewModel cell)
    {
        if (SelectedTeacherItem is not { } teacher) return;

        var target = cell.Row.Cells[cell.SubjectIndex];
        var current = WeaveDefaults.NormalizeTeacherName(target.Text);
        target.Text = NamesEqual(current, teacher.Name) ? string.Empty : teacher.Name;

        Error = string.Empty;
        RefreshDraftViews();
    }

    private void ApplyTeacherFilter(string? preferredName = null)
    {
        var keyword = (TeacherSearchText ?? string.Empty).Trim();
        var matches = TeacherRoster
            .Where(item => keyword.Length == 0 ||
                           item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           item.SubjectText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ClassCount > 0)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        FilteredTeacherItems.Clear();
        foreach (var item in matches) FilteredTeacherItems.Add(item);

        var targetName = preferredName ?? SelectedTeacherItem?.Name;
        SelectedTeacherItem = matches.FirstOrDefault(item => NamesEqual(item.Name, targetName))
                              ?? matches.FirstOrDefault();

        OnPropertyChanged(nameof(IsTeacherListEmpty));
    }

    private void RefreshTeacherItems()
    {
        ApplyTeacherFilter();
    }

    private void RefreshTeacherMatrix()
    {
        TeacherClassRows.Clear();

        if (SelectedTeacherItem is not { } teacher)
        {
            TeacherHeader = "教师视角";
            TeacherSummary = TeacherRoster.Count == 0
                ? "教师名单还是空的，先到「教师名单」添加教师。"
                : "从左侧选择一位教师。";
            return;
        }

        foreach (var row in Rows)
        {
            var cells = new List<AssignmentTeacherCellViewModel>(Subjects.Count);
            for (var index = 0; index < Subjects.Count && index < row.Cells.Count; index++)
            {
                cells.Add(new AssignmentTeacherCellViewModel(
                    row, index, Subjects[index].Name, teacher.Name, row.Cells[index].Text));
            }

            TeacherClassRows.Add(new AssignmentTeacherClassRowViewModel(
                row,
                cells,
                NamesEqual(row.HeadTeacher, teacher.Name)));
        }

        TeacherHeader = teacher.Name;
        TeacherSummary =
            $"{teacher.TeachingSubjectsText} · 任教 {teacher.ClassCount} 个班 · " +
            $"周课时 {teacher.WeeklyHours} 节" +
            (teacher.IsHeadTeacher ? " · 担任班主任" : string.Empty);
    }

    // ----------------------------- 学科视角 -----------------------------

    public ObservableCollection<AssignmentSubjectItemViewModel> SubjectItems { get; } = new();

    [ObservableProperty] private AssignmentSubjectItemViewModel? _selectedSubjectItem;

    partial void OnSelectedSubjectItemChanged(AssignmentSubjectItemViewModel? value)
    {
        if (_refreshing) return;
        RefreshSubjectRows();
    }

    public ObservableCollection<AssignmentSubjectClassRowViewModel> SubjectClassRows { get; } = new();

    public ObservableCollection<AssignmentTeacherLoadItemViewModel> SubjectTeacherLoads { get; } = new();

    public ObservableCollection<AssignmentTeacherOption> BulkTeacherOptions { get; } = new();

    [ObservableProperty] private AssignmentTeacherOption? _selectedBulkTeacher;

    [ObservableProperty] private string _subjectHeader = "学科视角";

    [ObservableProperty] private string _subjectSummary = "从左侧选择一门学科。";

    public bool HasSubjectTeacherLoads => SubjectTeacherLoads.Count > 0;
    public bool HasBulkTeacherOptions => BulkTeacherOptions.Count > 0;

    [RelayCommand]
    private void FillEmptySubjectClasses() => FillSubjectClasses(onlyEmpty: true);

    [RelayCommand]
    private void FillAllSubjectClasses() => FillSubjectClasses(onlyEmpty: false);

    private void FillSubjectClasses(bool onlyEmpty)
    {
        if (SelectedSubjectItem is not { } subjectItem)
        {
            Error = "先在左侧选择一门学科。";
            return;
        }

        var teacherName = WeaveDefaults.NormalizeTeacherName(SelectedBulkTeacher?.Name ?? string.Empty);
        if (teacherName.Length == 0)
        {
            Error = "先在批量下拉里选择一位本学科教师。";
            return;
        }

        var index = IndexOfSubject(subjectItem.Subject);
        if (index < 0) return;

        var changed = 0;
        foreach (var row in Rows)
        {
            if (index >= row.Cells.Count) break;
            if (onlyEmpty && !string.IsNullOrWhiteSpace(row.Cells[index].Text)) continue;

            row.Cells[index].Text = teacherName;
            changed++;
        }

        if (changed == 0)
        {
            Error = onlyEmpty ? "本学科已经没有空班需要填写。" : "本学科没有可替换的班级。";
            return;
        }

        Error = string.Empty;
        RefreshDraftViews();
    }

    // ----------------------------- 体检视角 -----------------------------

    [ObservableProperty] private int _overviewClassCount;

    [ObservableProperty] private int _overviewSubjectCount;

    [ObservableProperty] private int _overviewFilledCount;

    [ObservableProperty] private int _overviewTotalCount;

    [ObservableProperty] private int _overviewMissingCount;

    [ObservableProperty] private int _overviewTeacherCount;

    public ObservableCollection<AssignmentGapItemViewModel> MissingAssignments { get; } = new();

    public ObservableCollection<AssignmentTeacherLoadItemViewModel> TeacherLoads { get; } = new();

    public string OverviewAssignmentText => $"{OverviewFilledCount} / {OverviewTotalCount}";

    public string OverviewMissingText =>
        OverviewMissingCount == 0 ? "0" : OverviewMissingCount.ToString();

    public string OverviewMissingHint => OverviewMissingCount == 0 ? "全部已填" : "格待补";

    public bool HasMissingAssignments => OverviewMissingCount > 0;

    public bool HasTeacherLoads => TeacherLoads.Count > 0;

    partial void OnOverviewFilledCountChanged(int value) =>
        OnPropertyChanged(nameof(OverviewAssignmentText));

    partial void OnOverviewTotalCountChanged(int value) =>
        OnPropertyChanged(nameof(OverviewAssignmentText));

    partial void OnOverviewMissingCountChanged(int value)
    {
        OnPropertyChanged(nameof(OverviewMissingText));
        OnPropertyChanged(nameof(OverviewMissingHint));
        OnPropertyChanged(nameof(HasMissingAssignments));
    }

    // ----------------------------- 跨视角跳转 -----------------------------

    /// <summary>从体检 / 学科视角跳到指定学科。</summary>
    public void ShowSubject(string subjectId)
    {
        if (ViewMode != 3) ViewMode = 3;
        else RefreshDraftViews();

        SelectedSubjectItem = SubjectItems.FirstOrDefault(item =>
                                 item.Subject.Id == subjectId) ?? SelectedSubjectItem;
    }

    /// <summary>从名单 / 体检 / 学科视角跳到指定教师。</summary>
    public void ShowTeacher(string teacherName)
    {
        if (ViewMode != 2) ViewMode = 2;
        else RefreshDraftViews();

        TeacherSearchText = string.Empty;
        ApplyTeacherFilter(WeaveDefaults.NormalizeTeacherName(teacherName));
    }

    // ----------------------------- 派生视图刷新 -----------------------------

    public void RefreshDraftViews()
    {
        _refreshing = true;
        try
        {
            RefreshRosterStats();
            RefreshCellOptions();
            RefreshTeacherItems();
            RefreshTeacherMatrix();
            RefreshSubjectItems();
            RefreshSubjectRows();
            RefreshOverview();
            UpdateDraftSummary();
            UpdateRosterSummary();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshRosterStats()
    {
        var records = CollectAssignments();
        foreach (var item in TeacherRoster)
        {
            var own = records.Where(record => NamesEqual(record.Name, item.Name)).ToList();
            item.ClassCount = own.Select(record => record.Row).Distinct().Count();
            item.WeeklyHours = own.Sum(record => record.Subject.WeeklyHours);
            item.IsHeadTeacher = Rows.Any(row => NamesEqual(row.HeadTeacher, item.Name));
            item.AssignedSubjectCount = own
                .Select(record => record.Subject.Name)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var subjects = own
                .Select(record => record.Subject.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            item.TeachingSubjectsText = subjects.Count == 0
                ? item.SubjectText
                : string.Join("、", subjects);
        }
    }

    private void RefreshCellOptions()
    {
        for (var index = 0; index < Subjects.Count; index++)
        {
            var subjectName = Subjects[index].Name;
            var candidates = TeacherRoster
                .Where(item => item.SubjectName.Length == 0 ||
                               SubjectMatches(item.SubjectName, subjectName))
                .Select(item => item.Name)
                .ToList();

            foreach (var row in Rows)
            {
                if (index >= row.Cells.Count) continue;
                row.Cells[index].SetTeacherOptions(candidates, row.Cells[index].Text);
            }
        }
    }

    private void RefreshSubjectItems()
    {
        var selectedId = SelectedSubjectItem?.Subject.Id;
        SubjectItems.Clear();

        for (var index = 0; index < Subjects.Count; index++)
        {
            var assigned = Rows.Count(row =>
                index < row.Cells.Count && !string.IsNullOrWhiteSpace(row.Cells[index].Text));
            var teachers = Rows
                .Where(row => index < row.Cells.Count)
                .Select(row => WeaveDefaults.NormalizeTeacherName(row.Cells[index].Text))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Count();
            SubjectItems.Add(new AssignmentSubjectItemViewModel(
                Subjects[index], Rows.Count, assigned, teachers));
        }

        SelectedSubjectItem = SubjectItems.FirstOrDefault(item => item.Subject.Id == selectedId)
                              ?? SubjectItems.FirstOrDefault();
    }

    private void RefreshSubjectRows()
    {
        SubjectClassRows.Clear();
        SubjectTeacherLoads.Clear();
        BulkTeacherOptions.Clear();

        if (SelectedSubjectItem is not { } subjectItem)
        {
            SubjectHeader = "学科视角";
            SubjectSummary = Subjects.Count == 0 ? "工程里还没有学科。" : "从左侧选择一门学科。";
            OnPropertyChanged(nameof(HasSubjectTeacherLoads));
            OnPropertyChanged(nameof(HasBulkTeacherOptions));
            return;
        }

        var index = IndexOfSubject(subjectItem.Subject);
        if (index < 0) return;

        foreach (var row in Rows)
        {
            if (index >= row.Cells.Count) continue;
            SubjectClassRows.Add(new AssignmentSubjectClassRowViewModel(row, row.Cells[index]));
        }

        RefreshSubjectTeacherLoads(index, subjectItem);
        RefreshBulkTeacherOptions(subjectItem);
        SubjectHeader = subjectItem.Name;
        UpdateSubjectSummaryLive();
    }

    private void RefreshBulkTeacherOptions(AssignmentSubjectItemViewModel subjectItem)
    {
        var selectedName = SelectedBulkTeacher?.Name;
        BulkTeacherOptions.Clear();
        foreach (var item in TeacherRoster
                     .Where(teacher => teacher.SubjectName.Length > 0 &&
                                       SubjectMatches(teacher.SubjectName, subjectItem.Name))
                     .OrderBy(teacher => teacher.Name, StringComparer.Ordinal))
        {
            BulkTeacherOptions.Add(new AssignmentTeacherOption(item.Name));
        }

        SelectedBulkTeacher = BulkTeacherOptions.FirstOrDefault(option =>
            NamesEqual(option.Name, selectedName));
        OnPropertyChanged(nameof(HasBulkTeacherOptions));
    }

    private void RefreshSubjectTeacherLoads(int subjectIndex, AssignmentSubjectItemViewModel subjectItem)
    {
        SubjectTeacherLoads.Clear();
        var loads = Rows
            .Where(row => subjectIndex < row.Cells.Count)
            .Select(row => WeaveDefaults.NormalizeTeacherName(row.Cells[subjectIndex].Text))
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssignmentTeacherLoadItemViewModel(
                group.Key,
                subjectItem.Name,
                group.Count(),
                group.Count() * subjectItem.WeeklyHours,
                false,
                false))
            .ToList();

        foreach (var load in loads) SubjectTeacherLoads.Add(load);
        OnPropertyChanged(nameof(HasSubjectTeacherLoads));
    }

    private void RefreshOverview()
    {
        var records = CollectAssignments();
        OverviewClassCount = Rows.Count;
        OverviewSubjectCount = Subjects.Count;
        OverviewFilledCount = records.Count;
        OverviewTotalCount = Rows.Count * Subjects.Count;
        OverviewMissingCount = Math.Max(0, OverviewTotalCount - OverviewFilledCount);
        OverviewTeacherCount = TeacherRoster.Count;

        MissingAssignments.Clear();
        for (var rowIndex = 0; rowIndex < Rows.Count; rowIndex++)
        {
            var row = Rows[rowIndex];
            for (var subjectIndex = 0; subjectIndex < Subjects.Count && subjectIndex < row.Cells.Count; subjectIndex++)
            {
                if (!string.IsNullOrWhiteSpace(row.Cells[subjectIndex].Text)) continue;
                MissingAssignments.Add(new AssignmentGapItemViewModel(
                    row.RowName, Subjects[subjectIndex].Name, Subjects[subjectIndex].Id));
            }
        }

        TeacherLoads.Clear();
        foreach (var teacher in TeacherRoster
                     .OrderByDescending(item => item.WeeklyHours)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            TeacherLoads.Add(new AssignmentTeacherLoadItemViewModel(
                teacher.Name,
                teacher.SubjectText,
                teacher.ClassCount,
                teacher.WeeklyHours,
                teacher.IsHeadTeacher,
                teacher.AssignedSubjectCount > 1));
        }

        OnPropertyChanged(nameof(HasTeacherLoads));
        OnPropertyChanged(nameof(HasMissingAssignments));
    }

    private void UpdateDraftSummary()
    {
        if (TeacherRoster.Count == 0)
        {
            DraftSummary = "教师名单为空 · 先到「教师名单」添加教师";
            return;
        }

        var total = Rows.Count * Subjects.Count;
        var filled = Rows.Sum(row => row.Cells.Count(cell => !string.IsNullOrWhiteSpace(cell.Text)));
        DraftSummary = total == 0
            ? "还没有班级或学科"
            : $"已填 {filled} / {total} 格 · {Math.Max(0, total - filled)} 格待补 · " +
              $"{TeacherRoster.Count} 位教师";
    }

    private void UpdateRosterSummary()
    {
        if (TeacherRoster.Count == 0)
        {
            RosterSummary = "还没有教师";
            return;
        }

        var assigned = TeacherRoster.Count(item => item.ClassCount > 0);
        RosterSummary =
            $"共 {TeacherRoster.Count} 位教师 · {assigned} 位已排课 · " +
            $"{TeacherRoster.Count - assigned} 位未排课";
    }

    private void UpdateSubjectSummaryLive()
    {
        if (!IsSubjectView || SelectedSubjectItem is not { } subjectItem) return;

        var index = IndexOfSubject(subjectItem.Subject);
        if (index < 0) return;

        var assigned = Rows.Count(row =>
            index < row.Cells.Count && !string.IsNullOrWhiteSpace(row.Cells[index].Text));
        SubjectSummary =
            $"{subjectItem.WeeklyHours} 节 / 周 · 已填 {assigned}/{Rows.Count} 个班 · " +
            $"{SubjectTeacherLoads.Count} 位教师";
    }

    /// <summary>学科视角打字 / 选人时只更新统计与右侧负载，不重建班级行，避免输入框失焦。</summary>
    private void UpdateSubjectStatsLive()
    {
        if (SelectedSubjectItem is not { } subjectItem) return;

        var index = IndexOfSubject(subjectItem.Subject);
        if (index < 0) return;

        var names = Rows
            .Where(row => index < row.Cells.Count)
            .Select(row => WeaveDefaults.NormalizeTeacherName(row.Cells[index].Text))
            .ToList();
        subjectItem.AssignedCount = names.Count(name => name.Length > 0);
        subjectItem.TeacherCount = names
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();

        RefreshSubjectTeacherLoads(index, subjectItem);
        UpdateSubjectSummaryLive();
    }

    private List<TeacherAssignmentRecord> CollectAssignments()
    {
        var records = new List<TeacherAssignmentRecord>();
        foreach (var row in Rows)
        {
            for (var index = 0; index < Subjects.Count && index < row.Cells.Count; index++)
            {
                var name = WeaveDefaults.NormalizeTeacherName(row.Cells[index].Text);
                if (name.Length == 0) continue;
                records.Add(new TeacherAssignmentRecord(name, row, Subjects[index]));
            }
        }

        return records;
    }

    private int IndexOfSubject(Subject subject)
    {
        for (var index = 0; index < Subjects.Count; index++)
        {
            if (Subjects[index].Id == subject.Id) return index;
        }

        return -1;
    }

    private static bool NamesEqual(string? left, string? right) =>
        string.Equals(
            WeaveDefaults.NormalizeTeacherName(left ?? string.Empty),
            WeaveDefaults.NormalizeTeacherName(right ?? string.Empty),
            StringComparison.Ordinal);

    private static bool SubjectMatches(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        string.Equals(WeaveDefaults.SubjectAlias(left), WeaveDefaults.SubjectAlias(right),
            StringComparison.Ordinal);

    /// <summary>教师是否仍被工程引用；ignoreClassIds 用于「这些班本来就要删」的预演校验。</summary>
    private bool HasProjectReferences(string teacherId, ISet<string>? ignoreClassIds = null) =>
        _project.Lessons.Any(lesson => lesson.TeacherId == teacherId
                                       && (ignoreClassIds is null || !ignoreClassIds.Contains(lesson.ClassId))) ||
        _project.LayeredGroups.Any(group => group.Layers.Any(layer => layer.TeacherId == teacherId)) ||
        _project.MeetingBlocks.Any(block => block.TeacherIds.Contains(teacherId)) ||
        _project.Activities.Any(activity => activity.TeacherId == teacherId);

    private sealed record TeacherAssignmentRecord(
        string Name, AssignmentRowViewModel Row, Subject Subject);

    // ----------------------------- 保存 -----------------------------

    /// <summary>保存前预统计：草稿里被删掉、且已有排课的班级数量，用于确认文案。</summary>
    public (int Classes, int Lessons) CountPendingClassRemovals()
    {
        var kept = Rows
            .Where(row => row.Class is not null)
            .Select(row => row.Class!.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedIds = _initialClasses
            .Where(schoolClass => !kept.Contains(schoolClass.Id))
            .Select(schoolClass => schoolClass.Id)
            .ToHashSet(StringComparer.Ordinal);

        return (removedIds.Count,
                _project.Lessons.Count(lesson => removedIds.Contains(lesson.ClassId)));
    }

    /// <summary>把名单与表格写回工程：同步教师档案，按姓名重建任课关系，重算周课时。</summary>
    public bool Apply()
    {
        Error = string.Empty;

        var rosterNames = TeacherRoster
            .Select(item => WeaveDefaults.NormalizeTeacherName(item.Name))
            .ToList();
        if (rosterNames.Count != rosterNames.Distinct(StringComparer.Ordinal).Count())
        {
            Error = "教师名单里有重名，请先修改名单。";
            return false;
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            var name = row.ClassName.Trim();
            if (name.Length == 0)
            {
                Error = "有班级没有名字，请填好或删除该行。";
                return false;
            }

            if (!usedNames.Add(name))
            {
                Error = $"班级「{name}」重复了。";
                return false;
            }
        }

        // 草稿里被删掉的班：点「保存分工」后连同任课、课表与活动归属一起清掉。
        var keptClassIds = Rows
            .Where(row => row.Class is not null)
            .Select(row => row.Class!.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedClasses = _initialClasses
            .Where(schoolClass => !keptClassIds.Contains(schoolClass.Id))
            .ToList();
        var removedClassIds = removedClasses.Select(schoolClass => schoolClass.Id)
            .ToHashSet(StringComparer.Ordinal);

        // 校验全部前置：先确认「不在名单里的教师」是否还有引用，再动数据。
        // 否则删完班级才发现教师不能删，会留下半成品工程（老师以为保存失败 = 什么都没改）。
        var rosterNameSet = rosterNames.ToHashSet(StringComparer.Ordinal);
        foreach (var teacher in _project.Teachers)
        {
            var name = WeaveDefaults.NormalizeTeacherName(teacher.Name);
            if (rosterNameSet.Contains(name)) continue;

            if (HasProjectReferences(teacher.Id, removedClassIds))
            {
                Error = $"教师「{name}」已有课表、走班、会议或活动记录，不能从名单删除。";
                return false;
            }
        }

        foreach (var schoolClass in removedClasses)
        {
            _project.Assignments.RemoveAll(item => item.ClassId == schoolClass.Id);
            _project.Lessons.RemoveAll(item => item.ClassId == schoolClass.Id);
            foreach (var group in _project.LayeredGroups) group.ClassIds.Remove(schoolClass.Id);
            foreach (var activity in _project.Activities) activity.ClassIds.Remove(schoolClass.Id);
            _project.Classes.Remove(schoolClass);
        }

        // 名单是教师档案的唯一来源：不在名单里的教师（且无历史引用）会被移除。
        foreach (var teacher in _project.Teachers.ToList())
        {
            var name = WeaveDefaults.NormalizeTeacherName(teacher.Name);
            if (!rosterNameSet.Contains(name)) _project.Teachers.Remove(teacher);
        }

        var teacherByName = _project.Teachers
            .GroupBy(teacher => WeaveDefaults.NormalizeTeacherName(teacher.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var nextTeacherId = _project.Teachers
            .Select(teacher => int.TryParse(
                teacher.Id.Length > 1 ? teacher.Id[1..] : teacher.Id, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        foreach (var item in TeacherRoster)
        {
            var name = WeaveDefaults.NormalizeTeacherName(item.Name);
            if (!teacherByName.TryGetValue(name, out var teacher))
            {
                teacher = new Teacher
                {
                    Id = "t" + nextTeacherId++,
                    Name = name,
                    Subject = item.SubjectName,
                };
                _project.Teachers.Add(teacher);
                teacherByName[name] = teacher;
            }

            teacher.Subject = item.SubjectName;
            teacher.Name = name;
        }

        foreach (var row in Rows)
        {
            if (row.Class is null)
            {
                row.Class = new SchoolClass
                {
                    Id = "c" + Guid.NewGuid().ToString("N")[..8],
                    Name = row.ClassName.Trim(),
                    HeadTeacher = row.HeadTeacher.Trim(),
                };
                _project.Classes.Add(row.Class);
            }
            else
            {
                row.Class.Name = row.ClassName.Trim();
                row.Class.HeadTeacher = row.HeadTeacher.Trim();
            }

            var classId = row.Class.Id;
            for (var index = 0; index < Subjects.Count; index++)
            {
                if (index >= row.Cells.Count) break;
                var subject = Subjects[index];
                var teacherName = WeaveDefaults.NormalizeTeacherName(row.Cells[index].Text);
                var existing = _project.Assignments.FirstOrDefault(item =>
                    item.ClassId == classId && item.SubjectId == subject.Id);

                if (teacherName.Length == 0)
                {
                    if (existing is not null) _project.Assignments.Remove(existing);
                    continue;
                }

                if (!teacherByName.TryGetValue(teacherName, out var teacher))
                {
                    teacher = new Teacher
                    {
                        Id = "t" + nextTeacherId++,
                        Name = teacherName,
                        Subject = subject.Name,
                    };
                    _project.Teachers.Add(teacher);
                    teacherByName[teacherName] = teacher;
                }

                if (existing is not null)
                {
                    existing.TeacherId = teacher.Id;
                    existing.WeeklyHours = subject.WeeklyHours;
                }
                else
                {
                    _project.Assignments.Add(new TeachingAssignment
                    {
                        ClassId = classId,
                        SubjectId = subject.Id,
                        TeacherId = teacher.Id,
                        WeeklyHours = subject.WeeklyHours,
                    });
                }
            }
        }

        foreach (var teacher in _project.Teachers)
        {
            teacher.WeeklyHours = _project.Assignments
                .Where(item => item.TeacherId == teacher.Id)
                .Sum(item => item.WeeklyHours);
        }

        _initialSnapshot = Snapshot();
        return true;
    }
}

/// <summary>名单 / 教师视角共用的一位教师草稿。</summary>
public partial class AssignmentTeacherDraftViewModel : ObservableObject
{
    private readonly Action? _subjectChanged;

    public AssignmentTeacherDraftViewModel(
        string name,
        string subjectName,
        IReadOnlyList<string> subjectChoices,
        Action? subjectChanged = null)
    {
        Name = name;
        SubjectChoices = subjectChoices;
        _subject = string.IsNullOrWhiteSpace(subjectName) ||
                   string.Equals(subjectName, "未指定", StringComparison.Ordinal)
            ? "未指定"
            : subjectChoices.Contains(subjectName, StringComparer.Ordinal)
                ? subjectName
                : subjectName;
        _subjectChanged = subjectChanged;
    }

    public string Name { get; }
    public IReadOnlyList<string> SubjectChoices { get; }

    [ObservableProperty] private string _subject = "未指定";

    [ObservableProperty] private int _classCount;

    [ObservableProperty] private int _weeklyHours;

    [ObservableProperty] private bool _isHeadTeacher;

    [ObservableProperty] private int _assignedSubjectCount;

    [ObservableProperty] private string _teachingSubjectsText = "未指定学科";

    public string SubjectName =>
        string.Equals(Subject, "未指定", StringComparison.Ordinal) ? string.Empty : Subject;

    public string SubjectText => SubjectName.Length == 0 ? "未指定学科" : SubjectName;

    public string Meta => ClassCount == 0 ? "未排课" : $"{ClassCount} 个班 · {WeeklyHours} 节";

    public bool IsMultiSubject => AssignedSubjectCount > 1;

    public string AutomationName => $"{Name}，{SubjectText}，{Meta}";

    partial void OnSubjectChanged(string value)
    {
        OnPropertyChanged(nameof(SubjectName));
        OnPropertyChanged(nameof(SubjectText));
        OnPropertyChanged(nameof(AutomationName));
        _subjectChanged?.Invoke();
    }

    partial void OnClassCountChanged(int value)
    {
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnWeeklyHoursChanged(int value)
    {
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnIsHeadTeacherChanged(bool value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnAssignedSubjectCountChanged(int value) => OnPropertyChanged(nameof(IsMultiSubject));

    partial void OnTeachingSubjectsTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}

/// <summary>下拉选项：空姓名表示「未安排」。</summary>
public sealed class AssignmentTeacherOption
{
    public static readonly AssignmentTeacherOption Empty = new(string.Empty);

    public AssignmentTeacherOption(string name) => Name = name;

    public string Name { get; }
    public string DisplayName => Name.Length == 0 ? "未安排" : Name;
    public bool IsEmpty => Name.Length == 0;

    public override string ToString() => DisplayName;
}

/// <summary>分工表中的一行：班级、班主任与各学科教师。</summary>
public partial class AssignmentRowViewModel : ObservableObject
{
    public AssignmentRowViewModel(
        SchoolClass? schoolClass, IReadOnlyList<Subject> subjects, WeaveProject project)
    {
        Class = schoolClass;
        _className = schoolClass?.Name ?? string.Empty;
        _headTeacher = schoolClass?.HeadTeacher ?? string.Empty;

        foreach (var subject in subjects)
        {
            var assignment = schoolClass is null
                ? null
                : project.Assignments.FirstOrDefault(item =>
                    item.ClassId == schoolClass.Id && item.SubjectId == subject.Id);
            var teacherName = assignment is null
                ? string.Empty
                : project.FindTeacher(assignment.TeacherId)?.Name ?? string.Empty;
            Cells.Add(new AssignmentCellViewModel(teacherName, subject.Name)
            {
                OwnerName = _className,
            });
        }
    }

    public SchoolClass? Class { get; set; }

    public ObservableCollection<AssignmentCellViewModel> Cells { get; } = new();

    /// <summary>屏幕阅读器与删除按钮用的行名：空名时提示「未命名班级」。</summary>
    public string RowName =>
        string.IsNullOrWhiteSpace(ClassName) ? "未命名班级" : ClassName.Trim();

    /// <summary>行内「删除」按钮请求删除自己；由编辑器接收并从 Rows 移除。</summary>
    public event EventHandler? RemoveSelfRequested;

    [RelayCommand]
    private void RemoveSelf() => RemoveSelfRequested?.Invoke(this, EventArgs.Empty);

    [ObservableProperty] private string _className;
    [ObservableProperty] private string _headTeacher;

    partial void OnClassNameChanged(string value)
    {
        OnPropertyChanged(nameof(RowName));
        foreach (var cell in Cells) cell.OwnerName = value;
    }
}

/// <summary>单个学科格的教师名，附带按学科筛选的名单下拉。</summary>
public partial class AssignmentCellViewModel : ObservableObject
{
    private bool _syncingTeacher;

    public AssignmentCellViewModel(string text, string subjectName)
    {
        _text = text;
        SubjectName = subjectName;
    }

    public string SubjectName { get; }

    public ObservableCollection<AssignmentTeacherOption> TeacherOptions { get; } = new();

    [ObservableProperty] private string _text;

    [ObservableProperty] private AssignmentTeacherOption? _selectedTeacher;

    [ObservableProperty] private string _ownerName = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>屏幕阅读器名称：「班级·学科」，随班级名实时更新。</summary>
    public string AutomationName => $"{OwnerName}·{SubjectName}";

    /// <summary>按名单里同科目的教师重建下拉选项，保留当前值。</summary>
    public void SetTeacherOptions(IReadOnlyList<string> teacherNames, string currentText)
    {
        var current = WeaveDefaults.NormalizeTeacherName(currentText ?? string.Empty);

        _syncingTeacher = true;
        try
        {
            TeacherOptions.Clear();
            TeacherOptions.Add(AssignmentTeacherOption.Empty);

            var seen = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
            foreach (var name in teacherNames
                         .Select(WeaveDefaults.NormalizeTeacherName)
                         .Where(name => name.Length > 0)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!seen.Add(name)) continue;
                TeacherOptions.Add(new AssignmentTeacherOption(name));
            }

            // 兼容旧工程 / 跨学科任课：当前值即使不在本学科名单里，也要保留在选项中。
            if (current.Length > 0 && seen.Add(current))
                TeacherOptions.Add(new AssignmentTeacherOption(current));

            SelectedTeacher = TeacherOptions.FirstOrDefault(option =>
                                 string.Equals(
                                     WeaveDefaults.NormalizeTeacherName(option.Name), current,
                                     StringComparison.Ordinal))
                             ?? TeacherOptions[0];
        }
        finally
        {
            _syncingTeacher = false;
        }
    }

    partial void OnSelectedTeacherChanged(AssignmentTeacherOption? value)
    {
        if (_syncingTeacher) return;
        Text = value?.Name ?? string.Empty;
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        if (_syncingTeacher) return;

        var match = TeacherOptions.FirstOrDefault(option =>
            string.Equals(
                WeaveDefaults.NormalizeTeacherName(option.Name),
                WeaveDefaults.NormalizeTeacherName(value ?? string.Empty),
                StringComparison.Ordinal));
        match ??= TeacherOptions.Count > 0 ? TeacherOptions[0] : null;

        if (!ReferenceEquals(SelectedTeacher, match))
        {
            _syncingTeacher = true;
            SelectedTeacher = match;
            _syncingTeacher = false;
        }
    }

    partial void OnOwnerNameChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}

/// <summary>教师视角的一行班级。</summary>
public sealed class AssignmentTeacherClassRowViewModel
{
    public AssignmentTeacherClassRowViewModel(
        AssignmentRowViewModel row,
        IReadOnlyList<AssignmentTeacherCellViewModel> cells,
        bool isHeadTeacher)
    {
        Row = row;
        Cells = cells;
        IsHeadTeacher = isHeadTeacher;
    }

    public AssignmentRowViewModel Row { get; }
    public IReadOnlyList<AssignmentTeacherCellViewModel> Cells { get; }
    public bool IsHeadTeacher { get; }
}

/// <summary>教师视角的班级 × 学科格。</summary>
public sealed class AssignmentTeacherCellViewModel
{
    public AssignmentTeacherCellViewModel(
        AssignmentRowViewModel row,
        int subjectIndex,
        string subjectName,
        string teacherName,
        string currentText)
    {
        Row = row;
        SubjectIndex = subjectIndex;
        SubjectName = subjectName;
        TeacherName = teacherName;

        var current = WeaveDefaults.NormalizeTeacherName(currentText);
        IsAssigned = string.Equals(current, teacherName, StringComparison.Ordinal);
        OtherTeacher = IsAssigned ? string.Empty : current;
    }

    public AssignmentRowViewModel Row { get; }
    public int SubjectIndex { get; }
    public string SubjectName { get; }
    public string TeacherName { get; }
    public bool IsAssigned { get; }
    public string OtherTeacher { get; }
    public bool HasOtherTeacher => OtherTeacher.Length > 0;
    public bool IsEmpty => !IsAssigned && OtherTeacher.Length == 0;

    public string Tooltip => IsAssigned
        ? $"{Row.RowName} {SubjectName}：{TeacherName} 任教，点击取消"
        : HasOtherTeacher
            ? $"{Row.RowName} {SubjectName}：当前 {OtherTeacher}，点击改由 {TeacherName} 任教"
            : $"{Row.RowName} {SubjectName}：未填，点击由 {TeacherName} 任教";

    public string AutomationName => $"{Row.RowName} {SubjectName}";
}

/// <summary>学科视角左侧学科条目。</summary>
public partial class AssignmentSubjectItemViewModel : ObservableObject
{
    public AssignmentSubjectItemViewModel(
        Subject subject, int classCount, int assignedCount, int teacherCount)
    {
        Subject = subject;
        ClassCount = classCount;
        _assignedCount = assignedCount;
        _teacherCount = teacherCount;
    }

    public Subject Subject { get; }
    public string Name => Subject.Name;
    public int WeeklyHours => Subject.WeeklyHours;
    public bool IsMain => Subject.IsMain;
    public int ClassCount { get; }

    [ObservableProperty] private int _assignedCount;
    [ObservableProperty] private int _teacherCount;

    public string Meta => $"{WeeklyHours} 节 · 已填 {AssignedCount}/{ClassCount} · {TeacherCount} 位教师";
    public bool IsComplete => ClassCount > 0 && AssignedCount >= ClassCount;
    public bool HasMissing => !IsComplete;

    public string AutomationName => $"{Name}，{Meta}";

    partial void OnAssignedCountChanged(int value)
    {
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(HasMissing));
    }

    partial void OnTeacherCountChanged(int value) => OnPropertyChanged(nameof(Meta));
}

/// <summary>学科视角的一行班级。</summary>
public sealed class AssignmentSubjectClassRowViewModel
{
    public AssignmentSubjectClassRowViewModel(
        AssignmentRowViewModel row, AssignmentCellViewModel cell)
    {
        Row = row;
        Cell = cell;
    }

    public AssignmentRowViewModel Row { get; }
    public AssignmentCellViewModel Cell { get; }

    public string HeadTeacherText => string.IsNullOrWhiteSpace(Row.HeadTeacher)
        ? "班主任未填"
        : $"班主任 {Row.HeadTeacher.Trim()}";
}

/// <summary>某位教师在某学科的任教负载，也用于体检视角。</summary>
public sealed class AssignmentTeacherLoadItemViewModel
{
    public AssignmentTeacherLoadItemViewModel(
        string name,
        string subjectText,
        int classCount,
        int weeklyHours,
        bool isHeadTeacher,
        bool isMultiSubject)
    {
        Name = name;
        SubjectText = subjectText;
        ClassCount = classCount;
        WeeklyHours = weeklyHours;
        IsHeadTeacher = isHeadTeacher;
        IsMultiSubject = isMultiSubject;
    }

    public string Name { get; }
    public string SubjectText { get; }
    public int ClassCount { get; }
    public int WeeklyHours { get; }
    public bool IsHeadTeacher { get; }
    public bool IsMultiSubject { get; }

    public string Detail => $"{ClassCount} 个班 · {WeeklyHours} 节";

    public string Flags
    {
        get
        {
            var flags = new List<string>();
            if (ClassCount == 0) flags.Add("未排课");
            if (IsMultiSubject) flags.Add("跨学科");
            if (IsHeadTeacher) flags.Add("班主任");
            return string.Join(" · ", flags);
        }
    }

    public bool HasFlags => Flags.Length > 0;

    public string AutomationName => $"{Name}，{SubjectText}，{Detail}";
}

/// <summary>体检视角中的一条待补分工。</summary>
public sealed class AssignmentGapItemViewModel
{
    public AssignmentGapItemViewModel(string className, string subjectName, string subjectId)
    {
        ClassName = className;
        SubjectName = subjectName;
        SubjectId = subjectId;
    }

    public string ClassName { get; }
    public string SubjectName { get; }
    public string SubjectId { get; }

    public string Detail => $"{ClassName} · {SubjectName} 未指定任课教师";

    public string AutomationName => Detail;
}
