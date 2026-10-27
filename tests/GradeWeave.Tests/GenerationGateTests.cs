using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

/// <summary>
/// 生成门禁与快照化生成回归：忙碌期间所有改课动作必须被拒绝，
/// 生成结果基于工程快照计算后一次性写回，自动保存不会看到半成品状态。
/// </summary>
public sealed class GenerationGateTests
{
    [Fact]
    public async Task MutatingActions_AreRejectedWhileBusy()
    {
        var viewModel = await GenerateDemoAsync();
        var lessons = viewModel.Project.Lessons
            .Select(item => (item.ClassId, item.DayIndex, item.Period, item.SubjectId, item.TeacherId))
            .ToList();
        var researchCount = viewModel.Project.ResearchBlocks.Count;
        var meetingCount = viewModel.Project.MeetingBlocks.Count;
        var activityCount = viewModel.Project.Activities.Count;
        var layeredCount = viewModel.Project.LayeredGroups.Count;

        viewModel.IsBusy = true;
        try
        {
            viewModel.ToggleResearch(2, 3);
            viewModel.MoveLesson("c1", 1, 1, 1, 2);
            viewModel.ClearLessonAt("c1", 1, 1);
            viewModel.PlaceLesson("c1", 1, 1, "s1", "t1");
            viewModel.ConfirmTimetable();
            // 走班与会议的增删改是私有 [RelayCommand]，经生成的命令属性触发。
            viewModel.DeleteLayeredGroupCommand.Execute(null);
            viewModel.SaveLayeredGroupCommand.Execute(null);
            viewModel.DeleteMeetingBlockCommand.Execute(null);
            viewModel.SaveMeetingBlockCommand.Execute(null);
            viewModel.ApplyMeetingSuggestionCommand.Execute(null);
            viewModel.SaveActivity(new ActivityBlock { Name = "书法课" });
            viewModel.DeleteActivity("missing");

            Assert.False(viewModel.ToggleLessonLock("c1", 1, 1));
            Assert.Equal(0, viewModel.SetClassLessonsLocked("c1", locked: true));

            Assert.Contains("正在处理", viewModel.StatusText, StringComparison.Ordinal);
            Assert.Equal(lessons, viewModel.Project.Lessons
                .Select(item => (item.ClassId, item.DayIndex, item.Period, item.SubjectId, item.TeacherId)));
            Assert.Equal(researchCount, viewModel.Project.ResearchBlocks.Count);
            Assert.Equal(meetingCount, viewModel.Project.MeetingBlocks.Count);
            Assert.Equal(activityCount, viewModel.Project.Activities.Count);
            Assert.Equal(layeredCount, viewModel.Project.LayeredGroups.Count);
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    [Fact]
    public async Task GenerateAsync_AppliesSnapshotResultToLiveProject()
    {
        var viewModel = new GradeWeaveViewModel();
        viewModel.LoadDemoProject();
        var project = viewModel.Project;
        var group = project.LayeredGroups[0];
        group.IsFlexible = true;
        group.WeeklyHours = 2;

        await viewModel.GenerateAsync();

        // 生成基于快照计算，但结果必须写回原工程对象，界面与导出才能看到。
        Assert.Same(project, viewModel.Project);
        Assert.NotEmpty(project.Lessons);
        Assert.NotEmpty(group.ResolvedSlots);
        Assert.All(
            project.Lessons.Where(item => item.IsLayered && item.GroupId == group.Id),
            item => Assert.Contains(
                LayeredClassGroup.EncodeSlot(item.DayIndex, item.Period),
                group.ResolvedSlots));
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task GenerateAsync_WithoutConfirmedTimetable_LeavesProjectEmpty()
    {
        var viewModel = new GradeWeaveViewModel();
        viewModel.LoadDemoProject();
        viewModel.Project.Timetable!.ConfirmedSignature = string.Empty;

        await viewModel.GenerateAsync();

        Assert.Empty(viewModel.Project.Lessons);
        Assert.Contains("确认", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void CanGenerate_RequiresIdleProject()
    {
        var viewModel = new GradeWeaveViewModel();
        viewModel.LoadDemoProject();
        Assert.True(viewModel.CanGenerate);
        Assert.True(viewModel.CanOperate);

        viewModel.IsBusy = true;
        Assert.False(viewModel.CanGenerate);
        Assert.False(viewModel.CanOperate);

        viewModel.IsBusy = false;
        Assert.True(viewModel.CanGenerate);
    }

    private static async Task<GradeWeaveViewModel> GenerateDemoAsync()
    {
        var viewModel = new GradeWeaveViewModel();
        viewModel.LoadDemoProject();
        await viewModel.GenerateAsync();
        return viewModel;
    }
}
