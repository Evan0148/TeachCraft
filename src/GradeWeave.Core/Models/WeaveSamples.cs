using GradeWeave.Core.Timetable;

namespace GradeWeave.Core.Models;

/// <summary>内存构造的合成演示数据，不含任何真实学校、教师或学生信息。</summary>
public static class WeaveSamples
{
    public static WeaveProject CreateDemo(int classCount = 4)
    {
        var project = new WeaveProject
        {
            SchoolName = "示例中学",
            GradeName = "九年级",
            TermName = "2026 年秋季学期",
        };

        string[] subjectNames = ["语文", "数学", "英语", "物理", "化学", "道法", "历史", "体育", "劳动"];
        int[] hours = [6, 6, 6, 4, 3, 3, 3, 3, 1];
        for (var i = 0; i < subjectNames.Length; i++)
        {
            project.Subjects.Add(new Subject
            {
                Id = "s" + (i + 1),
                Name = subjectNames[i],
                WeeklyHours = hours[i],
                IsMain = i < 3,
            });
        }

        string[] teacherNames = ["沈青禾", "陆明川", "顾知远", "苏映棠", "程叙白", "叶临溪", "温以宁", "韩知微", "罗清和"];
        string[] roles = ["班主任", "科组长", "备课组长", "年级长", "班主任"];
        for (var i = 0; i < teacherNames.Length; i++)
        {
            project.Teachers.Add(new Teacher
            {
                Id = "t" + (i + 1),
                Name = teacherNames[i],
                Subject = subjectNames[Math.Min(i, subjectNames.Length - 1)],
                Roles = i == 0 ? ["班主任"] : i == 1 ? ["科组长", "备课组长"] : i == 2 ? ["年级长"] : [],
                IsLeader = i == 2,
            });
        }

        for (var c = 1; c <= classCount; c++)
        {
            var classId = "c" + c;
            project.Classes.Add(new SchoolClass
            {
                Id = classId,
                Name = $"九（{c}）班",
                HeadTeacher = teacherNames[(c - 1) % teacherNames.Length],
            });

            for (var s = 0; s < subjectNames.Length; s++)
            {
                var teacher = project.Teachers[(s + c - 1) % project.Teachers.Count];
                project.Assignments.Add(new TeachingAssignment
                {
                    ClassId = classId,
                    SubjectId = "s" + (s + 1),
                    TeacherId = teacher.Id,
                    WeeklyHours = hours[s],
                });
            }
        }

        project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = ["s4", "s5"],
            DayIndex = 2,
            StartPeriod = 1,
            EndPeriod = 4,
            Note = "物化科组教研",
        });
        project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = ["s3"],
            DayIndex = 2,
            StartPeriod = 5,
            EndPeriod = 7,
            Note = "英语科组教研",
        });
        project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = ["s6", "s7"],
            DayIndex = 3,
            StartPeriod = 1,
            EndPeriod = 4,
            Note = "政史科组教研",
        });
        project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = ["s2"],
            DayIndex = 3,
            StartPeriod = 5,
            EndPeriod = 7,
            Note = "数学科组教研",
        });
        project.ResearchBlocks.Add(new ResearchBlock
        {
            SubjectIds = ["s1"],
            DayIndex = 4,
            StartPeriod = 3,
            EndPeriod = 4,
            Note = "语文科组教研",
        });

        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "班主任会议",
            Roles = ["班主任"],
            DayIndex = 1,
            StartPeriod = 6,
            EndPeriod = 7,
        });
        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "科组长、备课组长会议",
            Roles = ["科组长", "备课组长"],
            DayIndex = 2,
            StartPeriod = 6,
            EndPeriod = 7,
        });
        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "行政会",
            Roles = ["年级长", "科组长"],
            DayIndex = 5,
            StartPeriod = 1,
            EndPeriod = 4,
            IsLeadership = true,
        });

        project.LayeredGroups.Add(new LayeredClassGroup
        {
            Name = "英语 A/B 层走班",
            SubjectId = "s3",
            ClassIds = ["c1", "c2"],
            Layers =
            [
                new LayeredClassLayer { Name = "A层", TeacherId = "t3" },
                new LayeredClassLayer { Name = "B层", TeacherId = "t6" },
            ],
            DayIndex = 4,
            StartPeriod = 1,
            EndPeriod = 1,
            Note = "两班同时段分层授课",
        });

        // 演示工程带一份已确认的九年级作息：早读、课间、午餐、课服与晚修都完整。
        project.Timetable = WeaveTimetables.FromPreset("grade-9");
        TimetableService.Confirm(project);

        // 合成活动示例：全年级在周五 10:50—11:10 叠加 20 分钟书法课，原课照常。
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 5,
            StartTime = "10:50",
            EndTime = "11:10",
            Mode = ActivityMode.Overlay,
            Note = "全年级硬笔书法 20 分钟",
        });

        return project;
    }
}
