using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.App;

/// <summary>点击课程格后的「放入课程」窗口：列出本班学科与任课教师，点一行即可放入。</summary>
public partial class LessonEditorViewModel : ObservableObject
{
    private readonly List<LessonChoiceViewModel> _allChoices = [];

    public LessonEditorViewModel(WeaveProject project, string classId, int dayIndex, int period)
    {
        ArgumentNullException.ThrowIfNull(project);
        ClassId = classId;
        DayIndex = dayIndex;
        Period = period;

        ClassName = project.FindClass(classId)?.Name ?? "该班";
        SlotText = WeaveDefaults.SlotText(dayIndex, period, period);

        var existing = project.Lessons
            .Where(lesson => lesson.ClassId == classId &&
                             lesson.DayIndex == dayIndex &&
                             lesson.Period == period)
            .ToList();
        var current = existing.FirstOrDefault(lesson => !lesson.IsLayered) ?? existing.FirstOrDefault();

        CanClear = current is not null && !current.IsActivity && !current.IsLayered;
        CurrentText = DescribeCurrent(project, existing, current);

        foreach (var candidate in LessonPlacementService.GetCandidates(project, classId))
        {
            var sameAsCurrent = current is not null &&
                                current.SubjectId == candidate.SubjectId &&
                                current.TeacherId == candidate.TeacherId;
            var replaceText = current is null
                ? "放入本格"
                : sameAsCurrent
                    ? "保持本格"
                    : $"替换 {SubjectName(project, current.SubjectId)}";

            var choice = new LessonChoiceViewModel(candidate) { ReplaceText = replaceText };

            if (!candidate.HasTeacher)
            {
                choice.Warning = "未设置任课教师，放课后会提示缺少教师";
                choice.HasWarning = true;
            }
            else
            {
                var other = project.Lessons.FirstOrDefault(lesson =>
                    lesson.TeacherId == candidate.TeacherId &&
                    lesson.DayIndex == dayIndex &&
                    lesson.Period == period &&
                    (lesson.ClassId != classId || lesson.SubjectId != candidate.SubjectId));
                if (other is not null)
                {
                    var otherClass = project.FindClass(other.ClassId)?.Name ?? other.ClassId;
                    choice.Warning = $"教师此节已有 {otherClass} 的课，放入后会出现冲突提示";
                    choice.HasWarning = true;
                }
                else
                {
                    var unavailable = ConstraintRules.TeacherUnavailable(
                        project, candidate.TeacherId, dayIndex, period);
                    if (!string.IsNullOrWhiteSpace(unavailable))
                    {
                        choice.Warning = $"教师此节有「{unavailable}」，放入后会出现占用提示";
                        choice.HasWarning = true;
                    }
                }
            }

            _allChoices.Add(choice);
        }

        RefreshChoices();
    }

    public string ClassId { get; }
    public int DayIndex { get; }
    public int Period { get; }
    public string ClassName { get; }
    public string SlotText { get; }
    public string CurrentText { get; }
    public bool CanClear { get; }

    public ObservableCollection<LessonChoiceViewModel> Choices { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _hint = "点击「学科 · 任课教师」即可放入当前课程格。";

    public bool IsEmpty => Choices.Count == 0;
    public string EmptyText => _allChoices.Count == 0
        ? "本班还没有任课教师。先在 05 步「编排对象」的「教师分工」里填写，再回来放课。"
        : "没有匹配的学科或教师。";

    partial void OnSearchTextChanged(string value) => RefreshChoices();

    public void SetHint(string text) => Hint = text;

    private void RefreshChoices()
    {
        var keyword = (SearchText ?? string.Empty).Trim();
        var matches = keyword.Length == 0
            ? _allChoices
            : _allChoices.Where(choice =>
                choice.SubjectName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                choice.TeacherName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        Choices.Clear();
        foreach (var choice in matches) Choices.Add(choice);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static string DescribeCurrent(
        WeaveProject project, IReadOnlyList<LessonPlacement> existing, LessonPlacement? current)
    {
        if (current is null) return "空课程格";
        if (current.IsActivity)
            return $"活动「{(string.IsNullOrWhiteSpace(current.ActivityName) ? "临时活动" : current.ActivityName)}」占位";
        if (current.IsLayered)
        {
            var layers = existing
                .Where(lesson => lesson.IsLayered)
                .Select(lesson => $"{lesson.LayerName} {TeacherName(project, lesson.TeacherId)}".Trim())
                .Where(text => text.Length > 0)
                .ToList();
            return $"{SubjectName(project, current.SubjectId)} · {string.Join(" / ", layers)}";
        }

        return DescribeLesson(project, current);
    }

    private static string DescribeLesson(WeaveProject project, LessonPlacement lesson)
    {
        var subjectName = SubjectName(project, lesson.SubjectId);
        var teacherName = TeacherName(project, lesson.TeacherId);
        return string.IsNullOrWhiteSpace(teacherName) ? subjectName : $"{subjectName} · {teacherName}";
    }

    private static string SubjectName(WeaveProject project, string subjectId) =>
        project.FindSubject(subjectId)?.Name ?? "课程";

    private static string TeacherName(WeaveProject project, string teacherId) =>
        project.FindTeacher(teacherId)?.Name ?? string.Empty;
}

/// <summary>放入课程列表里的一行：学科 + 任课教师 + 课时进度与冲突预判。</summary>
public sealed class LessonChoiceViewModel
{
    public LessonChoiceViewModel(LessonCandidate candidate)
    {
        Candidate = candidate;
        SubjectName = candidate.SubjectName;
        TeacherName = candidate.TeacherName;
        IsMain = candidate.IsMain;

        Meta = candidate.WeeklyHours <= 0
            ? $"已排 {candidate.PlacedHours} 节"
            : $"应排 {candidate.WeeklyHours} 节 · 已排 {candidate.PlacedHours} 节 · 余 {candidate.RemainingHours} 节";
        AutomationName = $"{SubjectName} · {TeacherName}，{Meta}";
    }

    public LessonCandidate Candidate { get; }
    public string SubjectId => Candidate.SubjectId;
    public string TeacherId => Candidate.TeacherId;
    public string SubjectName { get; }
    public string TeacherName { get; }
    public string Meta { get; }
    public bool IsMain { get; }
    public string ReplaceText { get; init; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
    public bool HasWarning { get; set; }
    public string AutomationName { get; }
}
