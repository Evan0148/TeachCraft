using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GradeWeave.App;

/// <summary>「全新制作」窗口：空白工程的基础配置与学科课时。</summary>
public partial class NewProjectViewModel : ObservableObject
{
    /// <summary>默认九年级学科课时，可增删改。</summary>
    private static readonly (string Name, int Hours, bool IsMain)[] DefaultSubjects =
    [
        ("语文", 6, true),
        ("数学", 6, true),
        ("英语", 6, true),
        ("物理", 4, true),
        ("化学", 3, false),
        ("道法", 3, false),
        ("历史", 3, false),
        ("体育", 3, false),
        ("劳动", 1, false),
    ];

    public NewProjectViewModel()
    {
        foreach (var (name, hours, isMain) in DefaultSubjects)
            Subjects.Add(new SubjectDraftViewModel { Name = name, WeeklyHours = hours, IsMain = isMain });

        foreach (var subject in Subjects) HookSubject(subject);
        Subjects.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (var subject in args.NewItems.OfType<SubjectDraftViewModel>())
                    HookSubject(subject);

            OnPropertyChanged(nameof(TotalHoursText));
            OnPropertyChanged(nameof(ZeroHourHint));
            OnPropertyChanged(nameof(HasZeroHour));
        };
        _initialSnapshot = Snapshot();
    }

    public ObservableCollection<SubjectDraftViewModel> Subjects { get; } = new();

    /// <summary>所有学科周课时合计；一周正课约 35 格，超出就排不下。</summary>
    public string TotalHoursText =>
        $"共 {Subjects.Sum(item => Math.Max(0, item.WeeklyHours))} 节 / 一周约 35 格";

    /// <summary>周课时为 0 的学科不会出现在课表里；用红字提醒老师。</summary>
    public string ZeroHourHint
    {
        get
        {
            var names = Subjects
                .Where(item => item.WeeklyHours == 0)
                .Select(item => string.IsNullOrWhiteSpace(item.Name) ? "未命名学科" : item.Name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return names.Count == 0 ? string.Empty : $"{string.Join("、", names)} 周课时为 0，这些课不会排在课表里。";
        }
    }

    public bool HasZeroHour => ZeroHourHint.Length > 0;

    /// <summary>草稿与打开时的默认配置有差异时为真，关闭窗口前用于询问是否放弃。</summary>
    public bool IsDirty => Snapshot() != _initialSnapshot;

    private readonly string _initialSnapshot;

    private string Snapshot() => string.Join("丨",
        SchoolName, GradeName, TermName, ClassCount,
        string.Join("丨", Subjects.Select(item => $"{item.Name}‖{item.WeeklyHours}‖{item.IsMain}")));

    private void HookSubject(SubjectDraftViewModel subject) => subject.PropertyChanged += (_, _) =>
    {
        OnPropertyChanged(nameof(TotalHoursText));
        OnPropertyChanged(nameof(ZeroHourHint));
        OnPropertyChanged(nameof(HasZeroHour));
    };

    [ObservableProperty] private string _schoolName = string.Empty;
    [ObservableProperty] private string _gradeName = "九年级";
    [ObservableProperty] private string _termName = "2026 年秋季学期";
    [ObservableProperty] private int _classCount = 5;
    [ObservableProperty] private string _error = string.Empty;

    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void AddSubject() =>
        Subjects.Add(new SubjectDraftViewModel { Name = string.Empty, WeeklyHours = 2 });

    [RelayCommand]
    private void RemoveSubject(SubjectDraftViewModel subject) => Subjects.Remove(subject);

    /// <summary>校验并生成配置；失败时把可读原因写入 <paramref name="error"/>。</summary>
    public NewProjectConfig? TryBuild(out string error)
    {
        error = string.Empty;

        var grade = (GradeName ?? string.Empty).Trim();
        if (grade.Length == 0)
        {
            error = "请填写年级名称，例如「九年级」。";
            return null;
        }

        var classCount = Math.Clamp(ClassCount, 1, 60);
        var subjects = Subjects
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new SubjectDraft(item.Name.Trim(), Math.Clamp(item.WeeklyHours, 0, 35), item.IsMain))
            .ToList();

        if (subjects.Count == 0)
        {
            error = "请至少保留一个学科。";
            return null;
        }

        var duplicated = subjects
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicated is not null)
        {
            error = $"学科「{duplicated.Key}」重复了，请合并成一个。";
            return null;
        }

        return new NewProjectConfig(
            (SchoolName ?? string.Empty).Trim(),
            grade,
            (TermName ?? string.Empty).Trim(),
            classCount,
            subjects);
    }
}

/// <summary>新建工程里的一行学科配置。</summary>
public partial class SubjectDraftViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _weeklyHours = 3;
    [ObservableProperty] private bool _isMain;

    /// <summary>屏幕阅读器名称：随名称与课时实时更新。</summary>
    public string AutomationName =>
        string.IsNullOrWhiteSpace(Name)
            ? $"未命名学科 · 每周 {WeeklyHours} 节"
            : $"{Name.Trim()} · 每周 {WeeklyHours} 节" + (IsMain ? " · 主课" : string.Empty);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnWeeklyHoursChanged(int value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnIsMainChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
}

/// <summary>学科草稿（名称、周课时、是否主课）。</summary>
public sealed record SubjectDraft(string Name, int WeeklyHours, bool IsMain);

/// <summary>「全新制作」的返回结果。</summary>
public sealed record NewProjectConfig(
    string SchoolName,
    string GradeName,
    string TermName,
    int ClassCount,
    IReadOnlyList<SubjectDraft> Subjects);
