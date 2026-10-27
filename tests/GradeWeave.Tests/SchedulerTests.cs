using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void DemoProject_GeneratesCompleteSchedule()
    {
        var project = WeaveSamples.CreateDemo();
        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.Equal(138, result.TotalCount);
        Assert.True(result.PlacedCount >= 130, $"只排了 {result.PlacedCount}/{result.TotalCount} 节。");
        Assert.True(result.Success, Describe(result));

        var conflicts = new ConflictAnalyzer().Analyze(project);
        Assert.DoesNotContain(conflicts, c => c.Kind is ConflictKind.TeacherClash
            or ConflictKind.ClassClash
            or ConflictKind.TeacherUnavailable
            or ConflictKind.LayeredClash
            or ConflictKind.HourMismatch
            or ConflictKind.Unplaced);
    }

    [Fact]
    public void ResearchBlocks_KeepSubjectTeachersOutOfLockedSlots()
    {
        var project = WeaveSamples.CreateDemo();
        new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        var physicsChemistryTeachers = project.Assignments
            .Where(a => a.SubjectId is "s4" or "s5")
            .Select(a => a.TeacherId)
            .Distinct()
            .ToList();
        Assert.NotEmpty(physicsChemistryTeachers);

        foreach (var teacherId in physicsChemistryTeachers)
        {
            var lessons = project.Lessons.Where(l =>
                l.TeacherId == teacherId && l.DayIndex == 2 && l.Period is >= 1 and <= 4).ToList();
            Assert.Empty(lessons);
        }

        var englishTeachers = project.Assignments
            .Where(a => a.SubjectId == "s3")
            .Select(a => a.TeacherId)
            .Distinct()
            .ToList();
        Assert.NotEmpty(englishTeachers);

        foreach (var teacherId in englishTeachers)
        {
            var lessons = project.Lessons.Where(l =>
                l.TeacherId == teacherId && l.DayIndex == 2 && l.Period is >= 5 and <= 7).ToList();
            Assert.Empty(lessons);
        }
    }

    [Fact]
    public void HeadTeacherMeeting_KeepsHeadTeachersFree()
    {
        var project = WeaveSamples.CreateDemo();
        new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        var headTeacherIds = project.Teachers
            .Where(t => t.Roles.Contains("班主任"))
            .Select(t => t.Id)
            .ToList();
        Assert.NotEmpty(headTeacherIds);

        foreach (var teacherId in headTeacherIds)
        {
            var lessons = project.Lessons.Where(l =>
                l.TeacherId == teacherId && l.DayIndex == 1 && l.Period is >= 6 and <= 7).ToList();
            Assert.Empty(lessons);
        }
    }

    [Fact]
    public void LayeredGroup_IsFixedForBothClassesWithDistinctLayerTeachers()
    {
        var project = WeaveSamples.CreateDemo();
        new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        var group = Assert.Single(project.LayeredGroups);
        foreach (var classId in group.ClassIds)
        {
            var lessons = project.Lessons
                .Where(l => l.ClassId == classId && l.DayIndex == group.DayIndex && l.Period == group.StartPeriod)
                .ToList();

            Assert.Equal(2, lessons.Count);
            Assert.All(lessons, l => Assert.Equal(group.SubjectId, l.SubjectId));
            Assert.All(lessons, l => Assert.True(l.IsLayered && l.IsFixed));
            Assert.All(lessons, l => Assert.Equal(group.Id, l.GroupId));
            Assert.Equal(
                group.Layers.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal),
                lessons.Select(l => l.LayerName).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(
                group.Layers.Select(l => l.TeacherId).OrderBy(id => id, StringComparer.Ordinal),
                lessons.Select(l => l.TeacherId).OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal(2, lessons.Select(l => l.TeacherId).Distinct().Count());
        }
    }

    [Fact]
    public void WeeklyHours_MatchAssignments()
    {
        var project = WeaveSamples.CreateDemo();
        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });
        Assert.True(result.Success, Describe(result));

        var teachingPeriods = project.Periods.Where(p => !p.IsExtension).Select(p => p.Index).ToHashSet();
        foreach (var assignment in project.Assignments)
        {
            var actual = project.Lessons
                .Where(l => l.ClassId == assignment.ClassId &&
                            l.SubjectId == assignment.SubjectId &&
                            teachingPeriods.Contains(l.Period))
                .Select(l => (l.DayIndex, l.Period))
                .Distinct()
                .Count();
            Assert.Equal(assignment.WeeklyHours, actual);
        }
    }

    [Fact]
    public void AnalyzerFindsManufacturedClashesAndGaps()
    {
        var project = WeaveSamples.CreateDemo();

        // 教师冲突：同一位教师周一第 1 节同时教两个班。
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });

        // 班级冲突：同一个班同一节被排了两门课。
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c3", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t3",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c3", DayIndex = 1, Period = 1, SubjectId = "s2", TeacherId = "t2",
        });

        // 教研/会议占用：班主任在周一第 6 节有班主任会议。
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c4", DayIndex = 1, Period = 6, SubjectId = "s5", TeacherId = "t1",
        });

        var conflicts = new ConflictAnalyzer().Analyze(project);

        Assert.Contains(conflicts, c => c.Kind == ConflictKind.TeacherClash &&
                                        c.TeacherId == "t1" && c.DayIndex == 1 && c.Period == 1);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.ClassClash &&
                                        c.ClassId == "c3" && c.DayIndex == 1 && c.Period == 1);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.TeacherUnavailable &&
                                        c.TeacherId == "t1" && c.Message.Contains("班主任会议", StringComparison.Ordinal));
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.HourMismatch);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.Unplaced);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.LayeredClash);
    }

    [Fact]
    public void CancelledOrTinyTimeLimit_DoesNotThrow()
    {
        var cancelledProject = WeaveSamples.CreateDemo();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new WeaveScheduler().Generate(
            cancelledProject, new ScheduleOptions { TimeLimit = TimeSpan.FromSeconds(10) }, cts.Token);

        Assert.Equal(138, cancelled.TotalCount);
        Assert.Equal(0, cancelled.PlacedCount);
        Assert.NotEmpty(cancelled.Messages);
        Assert.DoesNotContain(cancelledProject.Lessons, l => !l.IsFixed);

        var timedProject = WeaveSamples.CreateDemo();
        var timed = new WeaveScheduler().Generate(timedProject, new ScheduleOptions
        {
            TimeLimit = TimeSpan.FromMilliseconds(1),
        });

        Assert.Equal(138, timed.TotalCount);
        Assert.InRange(timed.PlacedCount, 0, timed.TotalCount);
    }

    [Fact]
    public void AfterSchoolMeeting_DoesNotConsumeTeachingSlots()
    {
        var project = WeaveSamples.CreateDemo();
        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "全体教师大会",
            DayIndex = 2,
            StartPeriod = 8,
            EndPeriod = 8,
            AppliesToAllTeachers = true,
        });

        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.True(result.Success);
        Assert.DoesNotContain(project.Lessons, lesson => lesson.Period == 8);

        var conflicts = new ConflictAnalyzer().Analyze(project);
        Assert.DoesNotContain(conflicts, conflict => conflict.Kind == ConflictKind.TeacherUnavailable);
    }

    [Fact]
    public void SameSeed_ProducesSameSchedule()
    {
        var first = WeaveSamples.CreateDemo();
        var second = WeaveSamples.CreateDemo();
        var options = new ScheduleOptions { RandomSeed = 20261026, TimeLimit = TimeSpan.FromSeconds(10) };

        var firstResult = new WeaveScheduler().Generate(first, options);
        var secondResult = new WeaveScheduler().Generate(second, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.Equal(firstResult.PlacedCount, secondResult.PlacedCount);

        static List<string> Slots(WeaveProject project) => project.Lessons
            .OrderBy(l => l.ClassId, StringComparer.Ordinal)
            .ThenBy(l => l.DayIndex)
            .ThenBy(l => l.Period)
            .ThenBy(l => l.LayerName, StringComparer.Ordinal)
            .Select(l => $"{l.ClassId}|{l.DayIndex}|{l.Period}|{l.SubjectId}|{l.TeacherId}|{l.LayerName}")
            .ToList();

        Assert.Equal(Slots(first), Slots(second));
    }

    [Fact]
    public void FlexibleLayeredGroup_PlacesSynchronouslyAndAvoidsResearch()
    {
        var project = WeaveSamples.CreateDemo();
        var group = project.LayeredGroups[0];
        group.IsFlexible = true;
        group.WeeklyHours = 2;
        group.DayIndex = 1;
        group.StartPeriod = 1;
        group.EndPeriod = 1;

        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.True(result.Success, Describe(result));
        Assert.Equal(2, group.ResolvedSlots.Count);

        var slots = group.ResolvedSlots.Select(LayeredClassGroup.DecodeSlot).ToList();
        Assert.NotEqual(slots[0], slots[1]);

        foreach (var (day, period) in slots)
        {
            foreach (var classId in group.ClassIds)
            {
                var lessons = project.Lessons
                    .Where(l => l.ClassId == classId && l.DayIndex == day &&
                                l.Period == period && l.GroupId == group.Id)
                    .ToList();
                Assert.Equal(2, lessons.Count);
                Assert.All(lessons, l => Assert.True(l.IsLayered && l.IsFixed));
            }
        }

        // 层教师都要避开英语科组教研（周二 5—7 节）。
        Assert.DoesNotContain(slots, slot => slot.Day == 2 && slot.Period is >= 5 and <= 7);

        var teachingPeriods = project.Periods.Where(p => !p.IsExtension).Select(p => p.Index).ToHashSet();
        foreach (var classId in group.ClassIds)
        {
            // 走班 A/B 是同一班同一节的两条记录，课时按（天，节）去重后应为 6 节。
            var hours = project.Lessons
                .Where(l => l.ClassId == classId && l.SubjectId == "s3" && teachingPeriods.Contains(l.Period))
                .Select(l => (l.DayIndex, l.Period))
                .Distinct()
                .Count();
            Assert.Equal(6, hours);
        }

        var conflicts = new ConflictAnalyzer().Analyze(project);
        Assert.DoesNotContain(conflicts, c => c.Kind is ConflictKind.TeacherClash
            or ConflictKind.ClassClash
            or ConflictKind.TeacherUnavailable
            or ConflictKind.LayeredClash
            or ConflictKind.HourMismatch
            or ConflictKind.Unplaced);
    }

    [Fact]
    public void FlexibleLayeredGroup_WithoutFreeSlot_FallsBackToNormalLessons()
    {
        var project = WeaveSamples.CreateDemo();
        var group = project.LayeredGroups[0];
        group.IsFlexible = true;
        group.WeeklyHours = 1;

        // 把 A/B 层教师的所有时段都锁进会议，走班组不可能落位。
        foreach (var teacherId in group.Layers.Select(l => l.TeacherId).Distinct())
        {
            project.MeetingBlocks.Add(new MeetingBlock
            {
                Name = "全周锁会",
                DayIndex = 1,
                StartPeriod = 1,
                EndPeriod = 7,
                TeacherIds = [teacherId],
            });
            project.MeetingBlocks.Add(new MeetingBlock
            {
                Name = "全周锁会",
                DayIndex = 2,
                StartPeriod = 1,
                EndPeriod = 7,
                TeacherIds = [teacherId],
            });
            project.MeetingBlocks.Add(new MeetingBlock
            {
                Name = "全周锁会",
                DayIndex = 3,
                StartPeriod = 1,
                EndPeriod = 7,
                TeacherIds = [teacherId],
            });
            project.MeetingBlocks.Add(new MeetingBlock
            {
                Name = "全周锁会",
                DayIndex = 4,
                StartPeriod = 1,
                EndPeriod = 7,
                TeacherIds = [teacherId],
            });
            project.MeetingBlocks.Add(new MeetingBlock
            {
                Name = "全周锁会",
                DayIndex = 5,
                StartPeriod = 1,
                EndPeriod = 7,
                TeacherIds = [teacherId],
            });
        }

        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(10),
        });

        Assert.Empty(group.ResolvedSlots);
        Assert.Contains(result.Messages, message => message.Contains(group.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(project.Lessons, lesson => lesson.GroupId == group.Id);
    }

    private static string Describe(ScheduleResult result)
    {
        var unplaced = string.Join("；", result.Unplaced.Select(u =>
            $"{u.ClassId}/{u.SubjectId} 缺 {u.Count} 节：{u.Reason}"));
        return $"已排 {result.PlacedCount}/{result.TotalCount}，得分 {result.Score}，用时 " +
               $"{result.Elapsed.TotalSeconds:F1}s。{string.Join("；", result.Messages)} {unplaced}";
    }
}
