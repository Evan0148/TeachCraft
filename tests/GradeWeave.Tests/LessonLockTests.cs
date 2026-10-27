using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class LessonLockTests
{
    [Fact]
    public async Task ToggleLessonLock_KeepsLessonAfterRegeneration()
    {
        var viewModel = await GenerateDemoAsync();
        var lesson = viewModel.Project.Lessons.First(item =>
            item.ClassId == "c1" && !item.IsLayered && !item.IsActivity);
        var classId = lesson.ClassId;
        var day = lesson.DayIndex;
        var period = lesson.Period;
        var subjectId = lesson.SubjectId;
        var teacherId = lesson.TeacherId;

        Assert.True(viewModel.ToggleLessonLock(classId, day, period));
        Assert.True(viewModel.Project.LessonAt(classId, day, period)!.IsFixed);

        await viewModel.GenerateAsync();

        var after = viewModel.Project.LessonAt(classId, day, period);
        Assert.NotNull(after);
        Assert.Equal(subjectId, after!.SubjectId);
        Assert.Equal(teacherId, after.TeacherId);
        Assert.True(after.IsFixed);
    }

    [Fact]
    public async Task ToggleLessonLock_RejectsLayeredAndActivitySlots()
    {
        var viewModel = await GenerateDemoAsync();
        var group = viewModel.Project.LayeredGroups[0];

        Assert.False(viewModel.ToggleLessonLock("c1", group.DayIndex, group.StartPeriod));
        Assert.Contains("走班", viewModel.StatusText, StringComparison.Ordinal);

        viewModel.Project.RemoveLessonAt("c1", 5, 7);
        viewModel.Project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1",
            DayIndex = 5,
            Period = 7,
            IsActivity = true,
            IsFixed = true,
            ActivityName = "书法课",
        });

        Assert.False(viewModel.ToggleLessonLock("c1", 5, 7));
        Assert.Contains("活动", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetClassLessonsLocked_TouchesOnlyNormalLessons()
    {
        var viewModel = await GenerateDemoAsync();
        var normal = viewModel.Project.Lessons
            .Where(item => item.ClassId == "c1" && !item.IsLayered && !item.IsActivity)
            .ToList();

        var count = viewModel.SetClassLessonsLocked("c1", locked: true);

        Assert.Equal(normal.Count, count);
        Assert.All(normal, item => Assert.True(item.IsFixed));
        Assert.Contains(viewModel.Project.Lessons, item =>
            item.ClassId == "c1" && item.IsLayered && item.IsFixed);

        Assert.Equal(normal.Count, viewModel.SetClassLessonsLocked("c1", locked: false));
        Assert.All(normal, item => Assert.False(item.IsFixed));
    }

    private static async Task<GradeWeaveViewModel> GenerateDemoAsync()
    {
        var viewModel = new GradeWeaveViewModel();
        viewModel.LoadDemoProject();
        await viewModel.GenerateAsync();
        return viewModel;
    }
}
