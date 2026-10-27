using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Tests;

public sealed class ConflictAnalyzerTests
{
    [Fact]
    public void LayeredPairInSameGroup_IsNotAClash()
    {
        var project = WeaveSamples.CreateDemo();
        var group = Assert.Single(project.LayeredGroups);

        foreach (var classId in group.ClassIds)
        {
            foreach (var layer in group.Layers)
            {
                project.Lessons.Add(new LessonPlacement
                {
                    ClassId = classId,
                    DayIndex = group.DayIndex,
                    Period = group.StartPeriod,
                    SubjectId = group.SubjectId,
                    TeacherId = layer.TeacherId,
                    LayerName = layer.Name,
                    GroupId = group.Id,
                    IsLayered = true,
                    IsFixed = true,
                });
            }
        }

        var conflicts = new ConflictAnalyzer().Analyze(project);

        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKind.TeacherClash);
        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKind.ClassClash);
        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKind.LayeredClash);
    }

    [Fact]
    public void TeacherClash_IsDetected()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 2, Period = 2, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 2, Period = 2, SubjectId = "s1", TeacherId = "t1",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);
        var clash = Assert.Single(conflicts, c =>
            c.Kind == ConflictKind.TeacherClash && c.TeacherId == "t1" &&
            c.DayIndex == 2 && c.Period == 2);
        Assert.Contains("教师冲突", clash.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassClash_IsDetected()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 3, Period = 3, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 3, Period = 3, SubjectId = "s2", TeacherId = "t2",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);
        var clash = Assert.Single(conflicts, c =>
            c.Kind == ConflictKind.ClassClash && c.ClassId == "c1" &&
            c.DayIndex == 3 && c.Period == 3);
        Assert.Contains("班级冲突", clash.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TeacherUnavailable_IsDetectedWithReadableReason()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 6, SubjectId = "s1", TeacherId = "t1",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);
        var conflict = Assert.Single(conflicts, c =>
            c.Kind == ConflictKind.TeacherUnavailable && c.TeacherId == "t1" &&
            c.DayIndex == 1 && c.Period == 6);
        Assert.Contains("班主任会议", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTeacher_IsDetectedForLessonAndAssignment()
    {
        var project = CreatingMinimalProject();
        project.Assignments.Add(new TeachingAssignment
        {
            ClassId = "c1", SubjectId = "s1", TeacherId = "t404", WeeklyHours = 2,
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t404",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);
        var missing = conflicts.Where(c => c.Kind == ConflictKind.MissingTeacher).ToList();
        Assert.NotEmpty(missing);
        Assert.Contains(missing, c => c.DayIndex == 1 && c.Period == 1 && c.TeacherId == "t404");
        Assert.Contains(missing, c => c.DayIndex == 0 && c.TeacherId == "t404");
        Assert.All(missing, c => Assert.Contains("缺少教师", c.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void HourMismatchAndUnplaced_ReportExpectedAndActual()
    {
        var project = CreatingMinimalProject();
        project.Assignments.Add(new TeachingAssignment
        {
            ClassId = "c1", SubjectId = "s1", TeacherId = "t1", WeeklyHours = 3,
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);

        var hour = Assert.Single(conflicts, c => c.Kind == ConflictKind.HourMismatch);
        Assert.Contains("应有 3 节，实有 0 节", hour.Message, StringComparison.Ordinal);
        Assert.Equal("c1", hour.ClassId);

        var unplaced = Assert.Single(conflicts, c => c.Kind == ConflictKind.Unplaced);
        Assert.Contains("还缺 3 节", unplaced.Message, StringComparison.Ordinal);
        Assert.Equal("t1", unplaced.TeacherId);
    }

    [Fact]
    public void Conflicts_AreSortedByKindDayPeriod()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 3, Period = 5, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 3, Period = 5, SubjectId = "s1", TeacherId = "t1",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);
        for (var i = 1; i < conflicts.Count; i++)
        {
            var previous = conflicts[i - 1];
            var current = conflicts[i];
            var ordered = previous.Kind < current.Kind ||
                          (previous.Kind == current.Kind &&
                           (previous.DayIndex < current.DayIndex ||
                            (previous.DayIndex == current.DayIndex && previous.Period <= current.Period)));
            Assert.True(ordered, $"冲突排序不正确：{previous.Kind}/{previous.DayIndex}/{previous.Period} 在 " +
                                 $"{current.Kind}/{current.DayIndex}/{current.Period} 之前。");
        }
    }

    [Fact]
    public void Analyze_DoesNotModifyProject()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });

        var lessonsBefore = project.Lessons
            .Select(l => $"{l.ClassId}|{l.DayIndex}|{l.Period}|{l.SubjectId}|{l.TeacherId}")
            .ToList();
        var assignmentsBefore = project.Assignments
            .Select(a => $"{a.ClassId}|{a.SubjectId}|{a.TeacherId}|{a.WeeklyHours}")
            .ToList();

        _ = new ConflictAnalyzer().Analyze(project);

        Assert.Equal(lessonsBefore, project.Lessons
            .Select(l => $"{l.ClassId}|{l.DayIndex}|{l.Period}|{l.SubjectId}|{l.TeacherId}")
            .ToList());
        Assert.Equal(assignmentsBefore, project.Assignments
            .Select(a => $"{a.ClassId}|{a.SubjectId}|{a.TeacherId}|{a.WeeklyHours}")
            .ToList());
    }

    private static WeaveProject CreatingMinimalProject()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        project.Classes.Add(new SchoolClass { Id = "c1", Name = "九（1）班" });
        project.Subjects.Add(new Subject { Id = "s1", Name = "语文", WeeklyHours = 3 });
        project.Teachers.Add(new Teacher { Id = "t1", Name = "沈青禾", Subject = "语文" });
        return project;
    }
}
