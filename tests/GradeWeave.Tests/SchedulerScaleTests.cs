using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;
using GradeWeave.Core.Timetable;

namespace GradeWeave.Tests;

/// <summary>真实年级规模的合成回归：20 个班、62 位教师、9 个学科、5 条教研块。</summary>
public sealed class SchedulerScaleTests
{
    [Fact]
    public void TwentyClasses_WithResearchBlocks_FillsEveryLessonWithoutConflicts()
    {
        var project = BuildScaleProject();
        var result = new WeaveScheduler().Generate(project, new ScheduleOptions
        {
            RandomSeed = 20261026,
            TimeLimit = TimeSpan.FromSeconds(12),
        });

        Assert.Equal(694, result.TotalCount);
        Assert.True(result.Success, Describe(result));
        Assert.Equal(result.TotalCount, result.PlacedCount);

        var conflicts = new ConflictAnalyzer().Analyze(project);
        Assert.DoesNotContain(conflicts, c => c.Kind is ConflictKind.TeacherClash
            or ConflictKind.ClassClash
            or ConflictKind.TeacherUnavailable
            or ConflictKind.LayeredClash
            or ConflictKind.HourMismatch
            or ConflictKind.Unplaced);
    }

    private static WeaveProject BuildScaleProject()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级", TermName = "示例学期" };

        string[] subjectNames = ["语文", "数学", "英语", "物理", "化学", "道法", "历史", "体育", "劳动"];
        int[] hours = [6, 6, 6, 4, 3, 3, 3, 3, 1];
        int[] teacherCounts = [10, 10, 10, 8, 6, 5, 5, 6, 2];

        for (var i = 0; i < subjectNames.Length; i++)
            project.Subjects.Add(new Subject
            {
                Id = "s" + (i + 1),
                Name = subjectNames[i],
                WeeklyHours = hours[i],
                IsMain = i < 3,
            });

        var teacherIndex = 1;
        for (var s = 0; s < subjectNames.Length; s++)
        {
            for (var t = 0; t < teacherCounts[s]; t++)
            {
                project.Teachers.Add(new Teacher
                {
                    Id = "t" + teacherIndex,
                    Name = $"教师{teacherIndex:00}",
                    Subject = subjectNames[s],
                    Roles = t == 0 && s == 0 ? ["班主任"] : s < 3 && t == 0 ? ["科组长"] : [],
                });
                teacherIndex++;
            }
        }

        for (var c = 1; c <= 20; c++)
        {
            var classId = "c" + c;
            project.Classes.Add(new SchoolClass { Id = classId, Name = $"九（{c}）班", HeadTeacher = "教师01" });

            var teacherCursor = 0;
            for (var s = 0; s < subjectNames.Length; s++)
            {
                // 真实工作簿里个别班的化学标注为“轮流”，没有固定任课教师；
                // 合成回归保留同样的 2 个缺口，用于验证 694 节而不是 700 节。
                if (s == 4 && c is 6 or 14) continue;

                var pool = project.Teachers.Where(t => t.Subject == subjectNames[s]).ToList();
                var teacher = pool[(c - 1 + teacherCursor) % pool.Count];
                project.Assignments.Add(new TeachingAssignment
                {
                    ClassId = classId,
                    SubjectId = "s" + (s + 1),
                    TeacherId = teacher.Id,
                    WeeklyHours = hours[s],
                });
                teacherCursor++;
            }
        }

        project.ResearchBlocks.AddRange(
        [
            new ResearchBlock { SubjectIds = ["s4", "s5"], DayIndex = 2, StartPeriod = 1, EndPeriod = 4 },
            new ResearchBlock { SubjectIds = ["s3"], DayIndex = 2, StartPeriod = 5, EndPeriod = 7 },
            new ResearchBlock { SubjectIds = ["s6", "s7"], DayIndex = 3, StartPeriod = 1, EndPeriod = 4 },
            new ResearchBlock { SubjectIds = ["s2"], DayIndex = 3, StartPeriod = 5, EndPeriod = 7 },
            new ResearchBlock { SubjectIds = ["s1"], DayIndex = 4, StartPeriod = 3, EndPeriod = 4 },
        ]);

        // 编排器要求作息时间表已确认；这里用默认七节正课，保持 694 节的规模不变。
        project.Timetable = WeaveTimetables.CreateDefault();
        TimetableService.Confirm(project);

        return project;
    }

    private static string Describe(ScheduleResult result) =>
        $"已排 {result.PlacedCount}/{result.TotalCount}，未排 " +
        string.Join("；", result.Unplaced.Take(6).Select(u => $"{u.ClassId} {u.SubjectId} x{u.Count} {u.Reason}"));
}
