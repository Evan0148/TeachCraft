using System.Collections.Generic;
using System.IO;
using System.Linq;
using GradeStrip.Core.Comparison;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class ExamComparisonEngineTests
{
    [Fact]
    public void CreateComparedExam_CalculatesDeltasAccurately()
    {
        // 构造基础考试 A 与当前考试 B
        var studentA = new StudentRecord
        {
            No = 1,
            Name = "张三",
            Total = new SubjectScore { Score = 600, ClassRank = 10, SchoolRank = 50 },
            Subjects = new Dictionary<string, SubjectScore>
            {
                ["语文"] = new SubjectScore { Score = 100, ClassRank = 8, SchoolRank = 40 }
            }
        };

        var studentB = new StudentRecord
        {
            No = 1,
            Name = "张三",
            Total = new SubjectScore { Score = 630, ClassRank = 4, SchoolRank = 25 },
            Subjects = new Dictionary<string, SubjectScore>
            {
                ["语文"] = new SubjectScore { Score = 110, ClassRank = 3, SchoolRank = 15 }
            }
        };

        var examA = new ExamData
        {
            ExamName = "第一次考试",
            Subjects = new[] { "语文" },
            Students = new[] { studentA }
        };

        var examB = new ExamData
        {
            ExamName = "第二次考试",
            Subjects = new[] { "语文" },
            Students = new[] { studentB }
        };

        // 以 A 为基准计算 B 的进退步
        var compared = ExamComparisonEngine.CreateComparedExam(examB, examA);
        var resStudent = compared.Students.First(s => s.Name == "张三");

        // 班排从 10 进步到 4，delta 应为 10 - 4 = +6
        Assert.Equal(6, resStudent.Total.ClassDelta);
        // 校排从 50 进步到 25，delta 应为 50 - 25 = +25
        Assert.Equal(25, resStudent.Total.SchoolDelta);

        // 语文单科班排从 8 到 3，delta 为 +5
        Assert.Equal(5, resStudent.Subjects["语文"].ClassDelta);
        // 语文单科校排从 40 到 15，delta 为 +25
        Assert.Equal(25, resStudent.Subjects["语文"].SchoolDelta);
    }

    [Fact]
    public void BuildStudentTrajectory_WithThreeExams_DetectsTrendsAndExtremes()
    {
        var studentExams = new List<(string Name, int Order, double Total, int ClassRank)>
        {
            ("李华", 1, 580.0, 18),
            ("李华", 2, 610.0, 11),
            ("李华", 3, 645.0, 4),

            ("韩梅梅", 1, 680.0, 2),
            ("韩梅梅", 2, 685.0, 1),
            ("韩梅梅", 3, 682.0, 2),

            ("赵六", 1, 630.0, 8),
            ("赵六", 2, 590.0, 26),
            ("赵六", 3, 625.0, 10),
        };

        var sessions = new List<ExamSession>();
        for (var i = 1; i <= 3; i++)
        {
            var order = i;
            var students = studentExams.Where(x => x.Order == order).Select(x => new StudentRecord
            {
                No = order,
                Name = x.Name,
                Total = new SubjectScore { Score = x.Total, ClassRank = x.ClassRank, SchoolRank = x.ClassRank * 5 },
                Subjects = new Dictionary<string, SubjectScore>
                {
                    ["数学"] = new SubjectScore { Score = 100 + order * 5, ClassRank = x.ClassRank }
                }
            }).ToList();

            sessions.Add(new ExamSession
            {
                Order = order,
                Label = $"第 {order} 次考试",
                SourcePath = $"exam_{order}.xlsx",
                SourceFileName = $"exam_{order}.xlsx",
                Data = new ExamData
                {
                    ExamName = $"第{order}次考试",
                    Subjects = new[] { "数学" },
                    Students = students
                }
            });
        }

        // 1. 李华：18 -> 11 -> 4（稳步攀升）
        var trajLi = ExamComparisonEngine.BuildStudentTrajectory("李华", sessions);
        Assert.True(trajLi.HasFullThreeExams);
        Assert.Equal(645.0, trajLi.HighestTotal);
        Assert.Equal("第3次考试", trajLi.HighestTotalExam);
        Assert.Equal(580.0, trajLi.LowestTotal);
        Assert.Equal(4, trajLi.BestClassRank);
        Assert.Equal(18, trajLi.LowestClassRank);
        Assert.Equal(14, trajLi.ClassRankRange);
        Assert.Equal(TrajectoryTrendType.Rising, trajLi.OverallTrend);
        Assert.Contains("稳步攀升", trajLi.OverallTrendDescription);

        // 2. 韩梅梅：2 -> 1 -> 2（高位稳定）
        var trajHan = ExamComparisonEngine.BuildStudentTrajectory("韩梅梅", sessions);
        Assert.Equal(TrajectoryTrendType.StableHigh, trajHan.OverallTrend);
        Assert.Contains("高位稳定", trajHan.OverallTrendDescription);

        // 3. 赵六：8 -> 26 -> 10（起伏波动）
        var trajZhao = ExamComparisonEngine.BuildStudentTrajectory("赵六", sessions);
        Assert.Equal(TrajectoryTrendType.Fluctuating, trajZhao.OverallTrend);
        Assert.Contains("起伏波动", trajZhao.OverallTrendDescription);
    }

    [Fact]
    public void CreateComparedExam_WhenBaselineIsNull_PreservesOriginalDeltas()
    {
        var student = new StudentRecord
        {
            No = 1,
            Name = "小明",
            Total = new SubjectScore { Score = 550, ClassDelta = 3, SchoolDelta = 5 },
            Subjects = new Dictionary<string, SubjectScore>
            {
                ["物理"] = new SubjectScore { Score = 88, ClassDelta = -2 }
            }
        };

        var exam = new ExamData
        {
            ExamName = "单场考",
            Subjects = new[] { "物理" },
            Students = new[] { student }
        };

        var compared = ExamComparisonEngine.CreateComparedExam(exam, null);
        var s = compared.Students.First();
        Assert.Equal(3, s.Total.ClassDelta);
        Assert.Equal(5, s.Total.SchoolDelta);
        Assert.Equal(-2, s.Subjects["物理"].ClassDelta);
    }

    [Fact]
    public void LegendSegments_WithComparisonNote_PrependsNoteCorrectly()
    {
        var segments = GradeStrip.Core.Layout.Strips.LegendSegments(useSchoolRank: false, comparisonNote: "对比第 1 次考试");
        var text = string.Join("", segments.Select(s => s.Text));
        Assert.Contains("进退步（对比第 1 次考试）：", text);
        Assert.Contains("▲进步", text);
    }

    [Fact]
    public void MultiExamComparison_WithRealFixtures_CalculatesFullTrajectory()
    {
        var f1 = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        if (!File.Exists(f1)) f1 = Path.Combine("tests", "fixtures", "智学网-九年级6班-第一次段考.xlsx");

        var f2 = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第二次段考.xlsx");
        if (!File.Exists(f2)) f2 = Path.Combine("tests", "fixtures", "智学网-九年级6班-第二次段考.xlsx");

        var f3 = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第三次段考.xlsx");
        if (!File.Exists(f3)) f3 = Path.Combine("tests", "fixtures", "智学网-九年级6班-第三次段考.xlsx");

        if (File.Exists(f1) && File.Exists(f2) && File.Exists(f3))
        {
            var sessions = new List<ExamSession>
            {
                new() { Order = 1, Label = "第 1 次考试", SourcePath = f1, SourceFileName = Path.GetFileName(f1), Data = GradeSheetImporter.Load(f1) },
                new() { Order = 2, Label = "第 2 次考试", SourcePath = f2, SourceFileName = Path.GetFileName(f2), Data = GradeSheetImporter.Load(f2) },
                new() { Order = 3, Label = "第 3 次考试", SourcePath = f3, SourceFileName = Path.GetFileName(f3), Data = GradeSheetImporter.Load(f3) }
            };

            var studentName = sessions[0].Data.Students[0].Name;
            var trajectory = ExamComparisonEngine.BuildStudentTrajectory(studentName, sessions);

            Assert.True(trajectory.HasFullThreeExams);
            Assert.Equal(3, trajectory.Points.Count);
            Assert.NotNull(trajectory.HighestTotal);
            Assert.NotNull(trajectory.LowestTotal);
            Assert.NotEmpty(trajectory.Subjects);

            // 比较第 2 考相对于第 1 考
            var comparedExam = ExamComparisonEngine.CreateComparedExam(sessions[1].Data, sessions[0].Data);
            Assert.Equal(sessions[1].Data.Students.Count, comparedExam.Students.Count);
        }
    }
}
