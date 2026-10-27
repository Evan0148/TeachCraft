using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GradeWeave.Core.Models;

namespace GradeWeave.App;

/// <summary>教师分工：每个班一行，班主任 + 各学科任课教师；教师按姓名自动建账。</summary>
public partial class AssignmentEditorViewModel : ObservableObject
{
    private readonly WeaveProject _project;
    private readonly List<SchoolClass> _initialClasses;
    private string _initialSnapshot;

    public AssignmentEditorViewModel(WeaveProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _initialClasses = project.Classes.ToList();
        Subjects = project.Subjects.ToList();

        foreach (var schoolClass in project.Classes)
            Rows.Add(CreateRow(new AssignmentRowViewModel(schoolClass, Subjects, project)));

        _initialSnapshot = Snapshot();
    }

    public IReadOnlyList<Subject> Subjects { get; }

    public bool HasSubjects => Subjects.Count > 0;

    public ObservableCollection<AssignmentRowViewModel> Rows { get; } = new();

    /// <summary>草稿与打开时的分工有差异时为真，关闭窗口前用于询问是否放弃。</summary>
    public bool IsDirty => Snapshot() != _initialSnapshot;

    private string Snapshot() => string.Join("丨", Rows.Select(row => string.Join("‖",
        row.Class?.Id ?? string.Empty, row.ClassName, row.HeadTeacher,
        string.Join("¦", row.Cells.Select(cell => cell.Text)))));

    [ObservableProperty] private string _error = string.Empty;

    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void AddClass()
    {
        if (Rows.Count >= 60)
        {
            Error = "班级数最多 60 个，先保存好现有分工；更多班级请用导入工作簿的方式创建。";
            return;
        }

        Rows.Add(CreateRow(new AssignmentRowViewModel(null, Subjects, _project)));
    }

    [RelayCommand]
    private void RemoveLastClass()
    {
        if (Rows.Count == 0) return;
        Rows.Remove(Rows[^1]);
    }

    private AssignmentRowViewModel CreateRow(AssignmentRowViewModel row)
    {
        row.RemoveSelfRequested += (sender, _) => Rows.Remove((AssignmentRowViewModel)sender!);
        return row;
    }

    /// <summary>把表格写回工程：更新班级名 / 班主任，按姓名匹配或创建教师，重建任课关系。</summary>
    public bool Apply()
    {
        Error = string.Empty;

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
        foreach (var schoolClass in removedClasses)
        {
            _project.Assignments.RemoveAll(item => item.ClassId == schoolClass.Id);
            _project.Lessons.RemoveAll(item => item.ClassId == schoolClass.Id);
            foreach (var group in _project.LayeredGroups) group.ClassIds.Remove(schoolClass.Id);
            foreach (var activity in _project.Activities) activity.ClassIds.Remove(schoolClass.Id);
            _project.Classes.Remove(schoolClass);
        }

        var teacherByName = _project.Teachers.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
        var nextTeacherId = _project.Teachers
            .Select(t => int.TryParse(t.Id.Length > 1 ? t.Id[1..] : t.Id, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

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

            var firstSubject = _project.Assignments
                .Where(item => item.TeacherId == teacher.Id)
                .Select(item => _project.FindSubject(item.SubjectId)?.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (!string.IsNullOrWhiteSpace(firstSubject)) teacher.Subject = firstSubject;
        }

        _initialSnapshot = Snapshot();
        return true;
    }
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

/// <summary>单个学科格的教师名。</summary>
public partial class AssignmentCellViewModel : ObservableObject
{
    public AssignmentCellViewModel(string text, string subjectName)
    {
        _text = text;
        SubjectName = subjectName;
    }

    public string SubjectName { get; }

    [ObservableProperty] private string _text;

    [ObservableProperty] private string _ownerName = string.Empty;

    /// <summary>屏幕阅读器名称：「班级·学科」，随班级名实时更新。</summary>
    public string AutomationName => $"{OwnerName}·{SubjectName}";

    partial void OnOwnerNameChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}
