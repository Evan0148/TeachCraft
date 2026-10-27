using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Tests;

public sealed class LessonPlacementTests
{
    [Fact]
    public void Candidates_ListAssignedSubjectsAndTeachers()
    {
        var project = WeaveSamples.CreateDemo();

        var candidates = LessonPlacementService.GetCandidates(project, "c1");

        Assert.Equal(project.Subjects.Count, candidates.Count);
        var chinese = Assert.Single(candidates, item => item.SubjectId == "s1");
        Assert.Equal("语文", chinese.SubjectName);
        Assert.Equal("t1", chinese.TeacherId);
        Assert.Equal("沈青禾", chinese.TeacherName);
        Assert.Equal(6, chinese.WeeklyHours);
        Assert.Equal(0, chinese.PlacedHours);
        Assert.True(chinese.RemainingHours >= 0);
    }

    [Fact]
    public void Candidates_ShowMissingTeacherForUnassignedClass()
    {
        var project = WeaveSamples.CreateDemo();
        project.Assignments.RemoveAll(item => item.ClassId == "c1");

        var candidates = LessonPlacementService.GetCandidates(project, "c1");

        Assert.Equal(project.Subjects.Count, candidates.Count);
        Assert.All(candidates, item => Assert.False(item.HasTeacher));
        Assert.All(candidates, item => Assert.Equal("未设置任课教师", item.TeacherName));
    }

    [Fact]
    public void Place_AddsFixedLessonAndSurvivesRegeneration()
    {
        var project = WeaveSamples.CreateDemo();

        var outcome = LessonPlacementService.Place(project, "c1", 1, 1, "s1", "t1");

        Assert.True(outcome.Success, outcome.Message);
        var placed = Assert.Single(project.Lessons, lesson =>
            lesson.ClassId == "c1" && lesson.DayIndex == 1 && lesson.Period == 1);
        Assert.Equal("s1", placed.SubjectId);
        Assert.Equal("t1", placed.TeacherId);
        Assert.True(placed.IsFixed);
        Assert.Equal("手动放课", placed.Note);

        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.True(result.Success, result.Messages.FirstOrDefault());
        var afterGeneration = Assert.Single(project.Lessons, lesson =>
            lesson.ClassId == "c1" && lesson.DayIndex == 1 && lesson.Period == 1);
        Assert.Equal("s1", afterGeneration.SubjectId);
        Assert.True(afterGeneration.IsFixed);

        var teachingPeriods = project.Periods.Where(period => !period.IsExtension)
            .Select(period => period.Index).ToHashSet();
        var chineseHours = project.Lessons
            .Where(lesson => lesson.ClassId == "c1" &&
                             lesson.SubjectId == "s1" &&
                             teachingPeriods.Contains(lesson.Period))
            .Select(lesson => (lesson.DayIndex, lesson.Period))
            .Distinct()
            .Count();
        Assert.Equal(6, chineseHours);
    }

    [Fact]
    public void Place_ReplacesExistingLesson()
    {
        var project = WeaveSamples.CreateDemo();
        Assert.True(LessonPlacementService.Place(project, "c1", 2, 2, "s2", "t2").Success);

        var outcome = LessonPlacementService.Place(project, "c1", 2, 2, "s1", "t1");

        Assert.True(outcome.Success, outcome.Message);
        Assert.Contains("替换", outcome.Message, StringComparison.Ordinal);
        var lessons = project.Lessons
            .Where(lesson => lesson.ClassId == "c1" && lesson.DayIndex == 2 && lesson.Period == 2)
            .ToList();
        var lesson = Assert.Single(lessons);
        Assert.Equal("s1", lesson.SubjectId);
        Assert.Equal("t1", lesson.TeacherId);
    }

    [Fact]
    public void Place_RejectsLayeredAndActivitySlots()
    {
        var project = WeaveSamples.CreateDemo();
        var group = Assert.Single(project.LayeredGroups);

        var layered = LessonPlacementService.Place(project, "c1", group.DayIndex, group.StartPeriod, "s1", "t1");

        Assert.False(layered.Success);
        Assert.Contains("走班", layered.Message, StringComparison.Ordinal);

        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1",
            DayIndex = 5,
            Period = 4,
            IsActivity = true,
            ActivityName = "书法课",
        });
        var activity = LessonPlacementService.Place(project, "c1", 5, 4, "s1", "t1");

        Assert.False(activity.Success);
        Assert.Contains("活动", activity.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Place_WarnsWhenTeacherIsAlreadyBooked()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2",
            DayIndex = 1,
            Period = 1,
            SubjectId = "s1",
            TeacherId = "t1",
        });

        var outcome = LessonPlacementService.Place(project, "c1", 1, 1, "s1", "t1");

        Assert.True(outcome.Success, outcome.Message);
        Assert.Contains(outcome.Warnings, warning => warning.Contains("还要上", StringComparison.Ordinal));
        var conflicts = new ConflictAnalyzer().Analyze(project);
        Assert.Contains(conflicts, conflict =>
            conflict.Kind == ConflictKind.TeacherClash &&
            conflict.TeacherId == "t1" &&
            conflict.DayIndex == 1 &&
            conflict.Period == 1);
    }

    [Fact]
    public void LayeredGroupLookup_UsesFlexibleResolvedSlots()
    {
        var project = WeaveSamples.CreateDemo();
        var group = Assert.Single(project.LayeredGroups);
        group.IsFlexible = true;
        group.ResolvedSlots = [LayeredClassGroup.EncodeSlot(3, 5)];

        Assert.True(ConstraintRules.TryGetLayeredGroupAt(project, "c1", 3, 5, out var resolved));
        Assert.Same(group, resolved);

        Assert.False(ConstraintRules.TryGetLayeredGroupAt(project, "c1", group.DayIndex, group.StartPeriod, out _));
        Assert.False(ConstraintRules.TryGetLayeredGroupAt(project, "c3", 3, 5, out _));
    }
}
