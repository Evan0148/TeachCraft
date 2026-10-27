using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class ActivityEditorTests
{
    [Fact]
    public void TryBuildRequiresNameAndDefaultsToBlank()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new ActivityEditorViewModel(project, null);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(string.Empty, viewModel.Name);

        var built = viewModel.TryBuild(out var error);
        Assert.True(built is null);
        Assert.Contains("请填写活动名称", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildRejectsOverlappingReplaceActivities()
    {
        var project = WeaveSamples.CreateDemo();

        var first = new ActivityEditorViewModel(project, null);
        first.Name = "大扫除";
        first.ModeIndex = 1;
        var built = first.TryBuild(out var firstError);
        Assert.True(built is not null, firstError);
        project.Activities.Add(built!);

        var second = new ActivityEditorViewModel(project, null);
        second.Name = "拔河赛";
        second.ModeIndex = 1;
        var blocked = second.TryBuild(out var error);
        Assert.True(blocked is null);
        Assert.Contains("同一时段已有替换型活动", error, StringComparison.Ordinal);
        Assert.Contains("大扫除", error, StringComparison.Ordinal);
        Assert.Contains("一节正课只能被一个活动替换", error, StringComparison.Ordinal);

        second.ModeIndex = 0;
        var fallback = second.TryBuild(out _);
        Assert.True(fallback is not null);
    }
}
