using GradeWeave.Core;
using GradeWeave.Core.Models;
using GradeWeave.Core.Project;
using GradeWeave.Core.Scheduling;
using GradeWeave.Core.Timetable;

namespace GradeWeave.Tests;

/// <summary>作息时间表回归：内置预设、校验、确认编译、缩减节次与工程往返。</summary>
public sealed class TimetableTests
{
    [Fact]
    public void GradeNinePreset_CompilesSevenLessonsAndFourExtensions()
    {
        var timetable = WeaveTimetables.FromPreset("grade-9");

        Assert.Equal(7, timetable.LessonCount);
        Assert.Equal(4, timetable.ExtensionCount);

        var validation = TimetableService.Validate(timetable);
        Assert.Empty(validation.Errors);
        Assert.Equal(7, validation.LessonCount);
        Assert.Equal(4, validation.ExtensionCount);

        var periods = TimetableService.BuildPeriods(timetable);
        Assert.Equal(11, periods.Count);

        var lessons = periods.Where(period => !period.IsExtension).ToList();
        Assert.Equal(Enumerable.Range(1, 7), lessons.Select(period => period.Index));
        Assert.All(lessons, period => Assert.False(period.IsExtension));
        Assert.Equal("第4节", lessons[3].Name);
        Assert.Equal("10:50—11:30", lessons[3].TimeText);

        var extensions = periods.Where(period => period.IsExtension).ToList();
        Assert.Equal(Enumerable.Range(8, 4), extensions.Select(period => period.Index));
        Assert.All(extensions, period => Assert.True(period.IsExtension));
        Assert.Equal(["课服1", "课服2", "晚修1", "晚修2"], extensions.Select(period => period.Name));
    }

    [Theory]
    [InlineData("grade-7", "11:00", "11:40", "11:40")]
    [InlineData("grade-8", "11:10", "11:50", "11:50")]
    public void JuniorPresets_StaggerFourthLessonAndLunchTimes(
        string presetId, string fourthStart, string fourthEnd, string lunchStart)
    {
        var timetable = WeaveTimetables.FromPreset(presetId);

        var fourth = timetable.Rows.First(row =>
            row.Kind == TimetableRowKind.Lesson && row.Name == "第4节");
        Assert.Equal(fourthStart, fourth.StartTime);
        Assert.Equal(fourthEnd, fourth.EndTime);

        var lunch = timetable.Rows.First(row => row.Kind == TimetableRowKind.Lunch);
        Assert.Equal(lunchStart, lunch.StartTime);

        var earlyReading = timetable.Rows.First(row => row.Kind == TimetableRowKind.MorningReading);
        Assert.Equal(presetId == "grade-7" ? "07:10" : "07:20", earlyReading.StartTime);

        var validation = TimetableService.Validate(timetable);
        Assert.True(validation.IsValid, string.Join("；", validation.Errors));
        Assert.Equal(7, validation.LessonCount);
    }

    [Fact]
    public void Validate_RejectsEmptyTimetable()
    {
        var nullResult = TimetableService.Validate(null);
        Assert.False(nullResult.IsValid);
        Assert.Contains(nullResult.Errors, error => error.Contains("作息时间表", StringComparison.Ordinal));

        var emptyResult = TimetableService.Validate(new WeaveTimetable());
        Assert.False(emptyResult.IsValid);
        Assert.Contains(emptyResult.Errors, error => error.Contains("正课", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsTimetableWithoutLesson()
    {
        var timetable = new WeaveTimetable();
        timetable.Rows.Add(Row(TimetableRowKind.Lunch, "午餐", "11:30", "12:20"));

        var result = TimetableService.Validate(timetable);

        Assert.False(result.IsValid);
        Assert.Equal(0, result.LessonCount);
        Assert.Contains(result.Errors, error => error.Contains("正课", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsEndBeforeStart()
    {
        var timetable = new WeaveTimetable();
        timetable.Rows.Add(Row(TimetableRowKind.Lesson, "第1节", "08:40", "08:00"));

        var result = TimetableService.Validate(timetable);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("晚于开始时间", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsOverlappingRows()
    {
        var timetable = new WeaveTimetable();
        timetable.Rows.Add(Row(TimetableRowKind.Lesson, "第1节", "08:00", "08:40"));
        timetable.Rows.Add(Row(TimetableRowKind.Lesson, "第2节", "08:30", "09:10"));

        var result = TimetableService.Validate(timetable);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("重叠", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUnparsableTime()
    {
        var timetable = new WeaveTimetable();
        timetable.Rows.Add(Row(TimetableRowKind.Lesson, "第1节", "8点", "08:40"));

        var result = TimetableService.Validate(timetable);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("不是有效时间", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WarnsOnUnorderedRowsAndOrderRowsSorts()
    {
        var timetable = WeaveTimetables.GradeNine();
        (timetable.Rows[0], timetable.Rows[1]) = (timetable.Rows[1], timetable.Rows[0]);

        var result = TimetableService.Validate(timetable);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, warning => warning.Contains("重新排列", StringComparison.Ordinal));

        var ordered = WeaveTimetables.OrderRows(timetable.Rows);
        Assert.Equal(timetable.Rows.Count, ordered.Count);
        Assert.Equal("早读", ordered[0].Name);
        Assert.Equal("第1节", ordered[1].Name);
        Assert.Equal("第2节", ordered[2].Name);

        var starts = ordered.Select(row => row.StartTime).ToList();
        var sorted = starts.OrderBy(text => text, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, starts);
    }

    [Fact]
    public void Confirm_WritesPeriodsAndInvalidatesWhenEdited()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        var timetable = WeaveTimetables.GradeNine();

        var result = TimetableService.Confirm(project, timetable);

        Assert.True(result.Confirmed);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(11, project.Periods.Count);
        Assert.Equal(11, result.Periods.Count);
        Assert.Equal(7, project.TeachingPeriodCount());
        Assert.Same(timetable, project.Timetable);
        Assert.True(project.Timetable!.IsConfirmed);
        Assert.True(TimetableService.IsConfirmed(project));
        Assert.False(string.IsNullOrWhiteSpace(project.Timetable.ConfirmedSignature));

        // 确认后改动任意一行时间，签名失配，生成课表重新被拦住。
        project.Timetable.Rows[0].StartTime = "07:10";
        Assert.False(project.Timetable.IsConfirmed);
        Assert.False(TimetableService.IsConfirmed(project));

        var error = Assert.Throws<GradeWeaveException>(() => new WeaveScheduler().Generate(project));
        Assert.Contains("作息时间", error.Message, StringComparison.Ordinal);
        Assert.Contains("确认", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirm_ShrinkingLessonsRemovesOutOfRangeAndReports()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        TimetableService.Confirm(project, WeaveTimetables.GradeNine());

        for (var period = 1; period <= 7; period++)
        {
            project.Lessons.Add(new LessonPlacement
            {
                ClassId = "c1",
                DayIndex = 1,
                Period = period,
                SubjectId = "s1",
                TeacherId = "t1",
            });
        }

        var shortened = WeaveTimetables.GradeNine();
        shortened.Rows.RemoveAll(row =>
            row.Kind == TimetableRowKind.Lesson && row.Name is "第6节" or "第7节");

        var result = TimetableService.Confirm(project, shortened);

        Assert.True(result.Confirmed);
        Assert.Equal(5, project.TeachingPeriodCount());
        Assert.Equal(2, result.RemovedLessons);
        Assert.Contains(result.Messages, message => message.Contains("移除", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3, 4, 5], project.Lessons.Select(lesson => lesson.Period).OrderBy(period => period));
        Assert.All(project.Lessons, lesson => Assert.InRange(lesson.Period, 1, 5));
        Assert.True(project.Timetable!.IsConfirmed);
    }

    [Fact]
    public void Extensions_AreNotTeachingPeriods_AndMeetingCanUseThem()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        TimetableService.Confirm(project, WeaveTimetables.GradeNine());

        Assert.Equal(7, project.TeachingPeriodCount());
        Assert.Equal(11, project.Periods.Count);
        Assert.Equal(4, project.Periods.Count(period => period.IsExtension));
        Assert.Equal(8, project.Periods[7].Index);
        Assert.Equal("课服1", project.Periods[7].Name);

        // 会议可以落在扩展节次（第 8 节），确认时不应报「超出节次范围」。
        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "全体教师大会",
            DayIndex = 2,
            StartPeriod = 8,
            EndPeriod = 8,
            AppliesToAllTeachers = true,
        });

        var result = TimetableService.Confirm(project, project.Timetable);

        Assert.True(result.Confirmed);
        Assert.Equal(7, project.TeachingPeriodCount());
        Assert.DoesNotContain(result.Messages, message => message.Contains("超出", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectIO_RoundTripsTimetable()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "八年级" };
        project.Timetable = WeaveTimetables.FromPreset("grade-8");
        TimetableService.Confirm(project);
        var original = project.Timetable!;

        var path = Path.Combine(Path.GetTempPath(), "gradeweave-timetable-" + Guid.NewGuid().ToString("N") + ".weave.json");
        try
        {
            WeaveProjectIO.Save(project, path);
            var loaded = WeaveProjectIO.Load(path);

            Assert.NotNull(loaded.Timetable);
            Assert.True(loaded.Timetable!.IsConfirmed);
            Assert.Equal(original.Rows.Count, loaded.Timetable.Rows.Count);

            for (var index = 0; index < original.Rows.Count; index++)
            {
                Assert.Equal(original.Rows[index].Kind, loaded.Timetable.Rows[index].Kind);
                Assert.Equal(original.Rows[index].Name, loaded.Timetable.Rows[index].Name);
                Assert.Equal(original.Rows[index].StartTime, loaded.Timetable.Rows[index].StartTime);
                Assert.Equal(original.Rows[index].EndTime, loaded.Timetable.Rows[index].EndTime);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ManualPlacement_IsBlockedUntilTimetableConfirmed()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        project.Classes.Add(new SchoolClass { Id = "c1", Name = "九（1）班" });
        project.Subjects.Add(new Subject { Id = "s1", Name = "语文", WeeklyHours = 3 });
        project.Teachers.Add(new Teacher { Id = "t1", Name = "沈青禾", Subject = "语文" });

        var blocked = LessonPlacementService.Place(project, "c1", 1, 1, "s1", "t1");
        Assert.False(blocked.Success);
        Assert.Contains("确认", blocked.Message, StringComparison.Ordinal);

        TimetableService.Confirm(project, WeaveTimetables.GradeNine());

        var placed = LessonPlacementService.Place(project, "c1", 1, 1, "s1", "t1");
        Assert.True(placed.Success, placed.Message);
        Assert.True(project.LessonAt("c1", 1, 1)?.IsFixed);
    }

    private static TimetableRow Row(TimetableRowKind kind, string name, string start, string end) => new()
    {
        Kind = kind,
        Name = name,
        StartTime = start,
        EndTime = end,
    };
}
