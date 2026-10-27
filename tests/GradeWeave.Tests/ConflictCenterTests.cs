using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class ConflictCenterTests
{
    [Fact]
    public void GroupsByKindWithLocatedCounts()
    {
        var viewModel = new ConflictCenterViewModel(WeaveSamples.CreateDemo(), SampleConflicts());

        Assert.Equal(3, viewModel.TotalCount);
        Assert.Equal(3, viewModel.FilteredCount);
        Assert.Equal(1, viewModel.LocatedCount);
        Assert.Equal(2, viewModel.UnknownLocationCount);
        Assert.False(viewModel.IsEmpty);

        Assert.Equal(
            new[] { ConflictKind.TeacherClash, ConflictKind.MissingTeacher, ConflictKind.HourMismatch },
            viewModel.Groups.Select(group => group.Kind));
        var clash = viewModel.Groups[0];
        Assert.Equal(1, clash.Count);
        Assert.Equal(1, clash.LocatedCount);
        Assert.Contains("均可定位", clash.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersByKindClassTeacherAndKeyword()
    {
        var viewModel = new ConflictCenterViewModel(WeaveSamples.CreateDemo(), SampleConflicts());

        viewModel.SelectedKindFilter = viewModel.KindFilters
            .First(option => option.Kind == ConflictKind.HourMismatch);
        Assert.Single(viewModel.Groups);
        Assert.Equal(1, viewModel.FilteredCount);

        viewModel.SelectedKindFilter = viewModel.KindFilters[0];
        viewModel.SelectedClassFilter = viewModel.ClassFilters
            .First(option => option.SchoolClass?.Id == "c1");
        Assert.Equal(2, viewModel.FilteredCount);

        viewModel.SelectedClassFilter = viewModel.ClassFilters[0];
        viewModel.SelectedTeacherFilter = viewModel.TeacherFilters
            .First(option => option.Teacher?.Id == "t1");
        Assert.Equal(1, viewModel.FilteredCount);

        viewModel.SelectedTeacherFilter = viewModel.TeacherFilters[0];
        viewModel.SearchText = "课时";
        Assert.Single(viewModel.Groups);
        Assert.Equal(ConflictKind.HourMismatch, viewModel.Groups[0].Kind);

        viewModel.SearchText = "星期一";
        Assert.Equal(1, viewModel.FilteredCount);
        Assert.Equal(ConflictKind.TeacherClash, viewModel.Groups[0].Kind);
    }

    [Fact]
    public void EmptyConflictsShowEmptyState()
    {
        var viewModel = new ConflictCenterViewModel(WeaveSamples.CreateDemo(), []);

        Assert.True(viewModel.IsEmpty);
        Assert.Empty(viewModel.Groups);
        Assert.Equal(0, viewModel.TotalCount);
        Assert.Contains("没有匹配", viewModel.FilterSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshPreservesCollapsedGroupState()
    {
        var viewModel = new ConflictCenterViewModel(WeaveSamples.CreateDemo(), SampleConflicts());
        viewModel.Groups.First(group => group.Kind == ConflictKind.TeacherClash).IsExpanded = false;

        viewModel.Refresh(SampleConflicts());

        var clash = viewModel.Groups.First(group => group.Kind == ConflictKind.TeacherClash);
        Assert.False(clash.IsExpanded);
        Assert.True(viewModel.Groups.First(group => group.Kind == ConflictKind.MissingTeacher).IsExpanded);
    }

    private static List<WeaveConflict> SampleConflicts() =>
    [
        new()
        {
            Kind = ConflictKind.TeacherClash,
            Message = "教师冲突：沈青禾在星期一第 1 节同时承担九（1）班与九（2）班的语文。",
            ClassId = "c1",
            TeacherId = "t1",
            DayIndex = 1,
            Period = 1,
        },
        new()
        {
            Kind = ConflictKind.MissingTeacher,
            Message = "缺少教师：九（1）班化学未指定任课教师。",
            ClassId = "c1",
        },
        new()
        {
            Kind = ConflictKind.HourMismatch,
            Message = "课时不符：九（2）班语文应排 6 节，实排 5 节。",
            ClassId = "c2",
        },
    ];
}
