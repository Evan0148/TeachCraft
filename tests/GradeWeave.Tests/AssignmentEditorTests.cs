using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class AssignmentEditorTests
{
    [Fact]
    public void RemoveLastClassIsDraftOnlyUntilApply()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new AssignmentEditorViewModel(project);
        Assert.False(viewModel.IsDirty);
        var removed = viewModel.Rows[^1].Class!;

        viewModel.RemoveLastClassCommand.Execute(null);
        Assert.Equal(3, viewModel.Rows.Count);
        Assert.True(viewModel.IsDirty);
        Assert.Contains(removed, project.Classes);

        project.Activities.Add(new ActivityBlock
        {
            Id = "act-check",
            Name = "测试活动",
            DayIndex = 1,
            StartTime = "08:00",
            EndTime = "08:20",
            Mode = ActivityMode.Overlay,
            ClassIds = [removed.Id],
        });

        Assert.True(viewModel.Apply());
        Assert.DoesNotContain(removed, project.Classes);
        Assert.DoesNotContain(removed.Id, project.Assignments.Select(item => item.ClassId));
        Assert.DoesNotContain(removed.Id, project.Lessons.Select(item => item.ClassId));
        Assert.DoesNotContain(removed.Id, project.LayeredGroups.SelectMany(group => group.ClassIds));
        Assert.DoesNotContain(
            removed.Id,
            project.Activities.Single(item => item.Id == "act-check").ClassIds);
    }

    [Fact]
    public void RowRemoveSelfIsDraftOnly()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new AssignmentEditorViewModel(project);
        var first = viewModel.Rows[0];

        first.RemoveSelfCommand.Execute(null);

        Assert.Equal(3, viewModel.Rows.Count);
        Assert.True(viewModel.IsDirty);
        Assert.Equal(4, project.Classes.Count);
    }

    [Fact]
    public void AddClassCapsAtSixtyRows()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new AssignmentEditorViewModel(project);

        while (viewModel.Rows.Count < 60) viewModel.AddClassCommand.Execute(null);
        Assert.Equal(60, viewModel.Rows.Count);

        viewModel.AddClassCommand.Execute(null);
        Assert.Equal(60, viewModel.Rows.Count);
        Assert.Contains("最多 60 个", viewModel.Error, StringComparison.Ordinal);
    }
}
