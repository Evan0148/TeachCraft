using System.Text;
using GradeWeave.Core;
using GradeWeave.Core.Export;
using GradeWeave.Core.Models;
using GradeWeave.Core.Project;
using GradeWeave.Core.Scheduling;
using SkiaSharp;

namespace GradeWeave.Tests;

/// <summary>临时活动回归：钟点映射、叠加不改课、替换产生占位与课时缺口、重排保留活动。</summary>
public sealed class ActivityTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N"));

    public ActivityTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void CalligraphyExample_MapsToOverlappingPeriod()
    {
        var project = WeaveSamples.CreateDemo();

        var periods = ActivityResolver.OverlappingPeriods(project, "10:50", "11:10");

        Assert.Equal([4], periods);
        Assert.Equal("对应 第4节（10:50—11:10）",
            ActivityResolver.DescribeRange(project, "10:50", "11:10"));
    }

    [Fact]
    public void TimeRange_SpanningTwoPeriodsIsDetected()
    {
        var project = WeaveSamples.CreateDemo();

        var periods = ActivityResolver.OverlappingPeriods(project, "10:20", "11:10");

        Assert.Equal([3, 4], periods);
    }

    [Fact]
    public void TimeRange_TouchingPeriodEdges_DoesNotCount()
    {
        var project = WeaveSamples.CreateDemo();

        // 九年级作息里第 3 节 10:40 结束、第 4 节 10:50 开始，区间正好落在课间。
        Assert.Empty(ActivityResolver.OverlappingPeriods(project, "10:40", "10:50"));
    }

    [Fact]
    public void ManualPeriod_WorksWhenTimeFallsOutsideTeachingHours()
    {
        var project = WeaveSamples.CreateDemo();
        var activity = new ActivityBlock
        {
            Name = "晨会",
            DayIndex = 2,
            StartTime = "07:30",
            EndTime = "07:50",
            Period = 1,
            Mode = ActivityMode.Overlay,
        };

        Assert.Empty(ActivityResolver.OverlappingPeriods(project, activity.StartTime, activity.EndTime));
        Assert.Equal([1], ActivityResolver.ResolvePeriods(project, activity));
    }

    [Fact]
    public void OverlayActivity_DoesNotChangeLessonsOrConflicts()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 1,
            StartTime = "10:50",
            EndTime = "11:10",
            Mode = ActivityMode.Overlay,
            ClassIds = ["c1"],
        });

        var lessonCount = project.Lessons.Count;
        var conflictsBefore = new ConflictAnalyzer().Analyze(project).Count;

        ActivityResolver.Apply(project);

        Assert.Equal(lessonCount, project.Lessons.Count);
        Assert.DoesNotContain(project.Lessons, lesson => lesson.IsActivity);
        Assert.Equal(conflictsBefore, new ConflictAnalyzer().Analyze(project).Count);
        Assert.Single(ActivityResolver.ActivitiesAt(project, "c1", 1, 4, ActivityMode.Overlay));
        Assert.Empty(ActivityResolver.ActivitiesAt(project, "c2", 1, 4, ActivityMode.Overlay));
    }

    [Fact]
    public void ReplaceActivity_OnlyAffectsSelectedClasses()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t2",
        });
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 1,
            StartTime = "08:00",
            EndTime = "08:40",
            Mode = ActivityMode.Replace,
            ClassIds = ["c1"],
        });

        ActivityResolver.Apply(project);

        var replaced = project.LessonAt("c1", 1, 1);
        Assert.NotNull(replaced);
        Assert.True(replaced!.IsActivity);
        Assert.Equal("书法课", replaced.ActivityName);
        Assert.Equal("08:00—08:40", replaced.ActivityTime);

        var untouched = project.LessonAt("c2", 1, 1);
        Assert.NotNull(untouched);
        Assert.False(untouched!.IsActivity);
        Assert.Equal("s1", untouched.SubjectId);
    }

    [Fact]
    public void ReplaceActivity_ReportsHourGapButNotMissingTeacher()
    {
        var project = WeaveSamples.CreateDemo();
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 1,
            StartTime = "08:00",
            EndTime = "08:40",
            Mode = ActivityMode.Replace,
            ClassIds = ["c1"],
        });

        ActivityResolver.Apply(project);
        var conflicts = new ConflictAnalyzer().Analyze(project);

        Assert.Contains(conflicts, conflict =>
            conflict.Kind == ConflictKind.Unplaced &&
            conflict.ClassId == "c1" &&
            conflict.Message.Contains("语文", StringComparison.Ordinal));
        Assert.DoesNotContain(conflicts, conflict => conflict.Kind == ConflictKind.MissingTeacher);
    }

    [Fact]
    public void Scheduler_KeepsReplaceActivityAndReportsTheGap()
    {
        var project = WeaveSamples.CreateDemo();
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 3,
            StartTime = "08:50",
            EndTime = "09:30",
            Mode = ActivityMode.Replace,
            ClassIds = ["c1"],
        });

        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(2),
        });

        var activityLesson = project.LessonAt("c1", 3, 2);
        Assert.NotNull(activityLesson);
        Assert.True(activityLesson!.IsActivity);
        Assert.DoesNotContain(project.Lessons, lesson =>
            lesson.ClassId == "c1" && lesson.DayIndex == 3 && lesson.Period == 2 && !lesson.IsActivity);

        Assert.False(result.Success);
        Assert.Contains(result.Unplaced, item => item.ClassId == "c1");
        Assert.True(result.PlacedCount >= 120,
            $"活动占位后只排了 {result.PlacedCount} 节；" +
            string.Join("；", result.Unplaced.Select(item =>
                $"{item.ClassId}/{item.SubjectId} 缺 {item.Count} 节：{item.Reason}")));
    }

    [Fact]
    public void ClassPngAndPdf_RenderReplaceActivity()
    {
        var project = WeaveSamples.CreateDemo();
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 2,
            StartTime = "10:50",
            EndTime = "11:10",
            Mode = ActivityMode.Replace,
            ClassIds = ["c1"],
        });
        ActivityResolver.Apply(project);

        var png = new WeaveImageRenderer().RenderClassPng(project, "c1");
        Assert.NotEmpty(png);
        using (var bitmap = SKBitmap.Decode(png))
        {
            Assert.NotNull(bitmap);
            Assert.True(bitmap!.Width > 0);
        }

        var pdfPath = Path.Combine(_directory, "activity.pdf");
        new WeavePdfExporter().ExportClassPdf(project, "c1", pdfPath);
        Assert.True(File.Exists(pdfPath));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath), 0, 4));
    }

    [Fact]
    public void ProjectIo_RoundTripsActivity()
    {
        var project = WeaveSamples.CreateDemo();
        project.Activities.Clear();
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 5,
            StartTime = "10:50",
            EndTime = "11:10",
            Period = 4,
            Mode = ActivityMode.Replace,
            ClassIds = ["c1", "c2"],
            TeacherId = "t1",
            Room = "报告厅",
            Note = "硬笔书法 20 分钟",
        });

        var path = Path.Combine(_directory, "activity.weave.json");
        WeaveProjectIO.Save(project, path);
        var loaded = WeaveProjectIO.Load(path);

        var activity = Assert.Single(loaded.Activities);
        Assert.Equal("书法课", activity.Name);
        Assert.Equal(5, activity.DayIndex);
        Assert.Equal("10:50", activity.StartTime);
        Assert.Equal("11:10", activity.EndTime);
        Assert.Equal(4, activity.Period);
        Assert.Equal(ActivityMode.Replace, activity.Mode);
        Assert.Equal(["c1", "c2"], activity.ClassIds);
        Assert.Equal("t1", activity.TeacherId);
        Assert.Equal("报告厅", activity.Room);
        Assert.Equal("硬笔书法 20 分钟", activity.Note);
    }
}
