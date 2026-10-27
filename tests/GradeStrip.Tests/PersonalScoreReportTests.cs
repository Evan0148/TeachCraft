using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GradeStrip.Core.Export;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class PersonalScoreReportTests
{
    private static string GetPersonalReportFixturePath()
    {
        var path = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-全科个人成绩单-九14班.xls");
        if (!File.Exists(path))
        {
            path = Path.Combine("tests", "fixtures", "智学网-全科个人成绩单-九14班.xls");
        }
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return path;
    }

    [Fact]
    public void Load_FullOptionsPersonalScoreReportXls_ParsesAll46StudentsCorrectly()
    {
        var fixturePath = GetPersonalReportFixturePath();
        var exam = GradeSheetImporter.Load(fixturePath);

        // 1. 基础信息验证
        Assert.Equal("九14班", exam.ClassName);
        Assert.Contains("第一次段考", exam.ExamName);
        Assert.Contains("示例市文化中学", exam.SourceTitle);
        Assert.NotNull(exam.ImportNotice);
        Assert.Contains("全科个人成绩单", exam.ImportNotice);

        // 2. 学科列表验证
        var expectedSubjects = new[] { "语文", "数学", "英语", "物理", "化学", "生物学", "政治", "历史", "地理", "体育" };
        Assert.Equal(expectedSubjects, exam.Subjects);

        // 3. 学生人数验证（46名学生完整读取）
        Assert.Equal(46, exam.Students.Count);

        // 4. 首位学生：姚北辰
        var s1 = exam.Students[0];
        Assert.Equal(1, s1.No);
        Assert.Equal("姚北辰", s1.Name);
        Assert.Equal(562.2, s1.Total.Score);
        Assert.Equal(20, s1.Total.ClassRank);
        Assert.Equal(496, s1.Total.SchoolRank);

        Assert.Equal(74, s1["语文"].Score);
        Assert.Equal(25, s1["语文"].ClassRank);
        Assert.Equal(602, s1["语文"].SchoolRank);

        Assert.Equal(77, s1["数学"].Score);
        Assert.Equal(13, s1["数学"].ClassRank);
        Assert.Equal(304, s1["数学"].SchoolRank);

        Assert.Equal(29.5, s1["英语"].Score);
        Assert.Equal(38, s1["英语"].ClassRank);
        Assert.Equal(837, s1["英语"].SchoolRank);

        Assert.Equal(79, s1["物理"].Score);
        Assert.Equal(14, s1["物理"].ClassRank);
        Assert.Equal(384, s1["物理"].SchoolRank);

        Assert.Equal(52.5, s1["化学"].Score);
        Assert.Equal(11, s1["化学"].ClassRank);
        Assert.Equal(424, s1["化学"].SchoolRank);

        Assert.Equal(48, s1["生物学"].Score);
        Assert.Equal(2, s1["生物学"].ClassRank);
        Assert.Equal(135, s1["生物学"].SchoolRank);

        Assert.Equal(50.4, s1["政治"].Score);
        Assert.Equal(25, s1["政治"].ClassRank);
        Assert.Equal(427, s1["政治"].SchoolRank);

        Assert.Equal(56.8, s1["历史"].Score);
        Assert.Equal(12, s1["历史"].ClassRank);
        Assert.Equal(335, s1["历史"].SchoolRank);

        Assert.Equal(48, s1["地理"].Score);
        Assert.Equal(2, s1["地理"].ClassRank);
        Assert.Equal(125, s1["地理"].SchoolRank);

        Assert.Equal(47, s1["体育"].Score);
        Assert.Equal(39, s1["体育"].ClassRank);
        Assert.Equal(839, s1["体育"].SchoolRank);

        // 5. 第二位学生：秦北辰
        var s2 = exam.Students[1];
        Assert.Equal(2, s2.No);
        Assert.Equal("秦北辰", s2.Name);
        Assert.Equal(677, s2.Total.Score);
        Assert.Equal(10, s2.Total.ClassRank);
        Assert.Equal(246, s2.Total.SchoolRank);
        Assert.Equal(98, s2["语文"].Score);
        Assert.Equal(2, s2["语文"].ClassRank);
        Assert.Equal(67, s2["语文"].SchoolRank);

        // 6. 末位学生：冯星河
        var sLast = exam.Students[^1];
        Assert.Equal(46, sLast.No);
        Assert.Equal("冯星河", sLast.Name);
        Assert.NotNull(sLast.Total.Score);
    }

    [Fact]
    public void Load_PartialSelection_OnlyScores_DerivesClassRanksAutomatically()
    {
        // 模拟用户在智学网导出时「只勾选了得分」（未勾选班名次、校名次、满分、平均分、最高分等）
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_scores_only_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 1：张三（总分 200）
                ws.Cell(r++, 1).Value = "张三_成绩单";
                ws.Cell(r++, 1).Value = "某某中学2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r, 3).Value = "数学";
                ws.Cell(r++, 4).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 90;
                ws.Cell(r, 3).Value = 110;
                ws.Cell(r++, 4).Value = 200;

                r++; // 空行

                // 学生 2：李四（总分 220，第 1 名）
                ws.Cell(r++, 1).Value = "李四_成绩单";
                ws.Cell(r++, 1).Value = "某某中学2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r, 3).Value = "数学";
                ws.Cell(r++, 4).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 100;
                ws.Cell(r, 3).Value = 120;
                ws.Cell(r++, 4).Value = 220;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal("九14班", exam.ClassName);
            Assert.Equal(new[] { "语文", "数学" }, exam.Subjects);

            var s1 = exam.Students.First(s => s.Name == "张三");
            var s2 = exam.Students.First(s => s.Name == "李四");

            // 得分正确
            Assert.Equal(200, s1.Total.Score);
            Assert.Equal(220, s2.Total.Score);

            // 班级名次由系统基于实考得分自动推导计算出！
            Assert.Equal(2, s1.Total.ClassRank); // 200 分排第 2
            Assert.Equal(1, s2.Total.ClassRank); // 220 分排第 1
            Assert.Equal(2, s1["语文"].ClassRank); // 90 分排第 2
            Assert.Equal(1, s2["语文"].ClassRank); // 100 分排第 1

            // 未勾选年级名次，年级名次保持为 null
            Assert.Null(s1.Total.SchoolRank);
            Assert.Null(s2.Total.SchoolRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_PartialSelection_ScoresAndClassRank_NoSchoolRank()
    {
        // 模拟用户勾选了「得分」和「班级名次」，但未勾选「年级名次」
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_classrank_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 1
                ws.Cell(r++, 1).Value = "王小二_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 85;
                ws.Cell(r++, 3).Value = 85;

                ws.Cell(r, 1).Value = "班级名次";
                ws.Cell(r, 2).Value = 5;
                ws.Cell(r++, 3).Value = 5;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Single(exam.Students);
            var student = exam.Students[0];
            Assert.Equal("王小二", student.Name);
            Assert.Equal(85, student.Total.Score);
            Assert.Equal(5, student.Total.ClassRank);
            Assert.Equal(5, student["语文"].ClassRank);
            Assert.Null(student.Total.SchoolRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_PartialSelection_WithoutTotalColumn_CalculatesTotalSumAndRankAutomatically()
    {
        // 模拟用户在智学网导出时未勾选「总分」列（仅有单科）
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_no_total_col_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 A：语 80，数 90（无总分列）
                ws.Cell(r++, 1).Value = "学生甲_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "数学";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 80;
                ws.Cell(r++, 3).Value = 90;

                r++;

                // 学生 B：语 85，数 95
                ws.Cell(r++, 1).Value = "学生乙_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "数学";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 85;
                ws.Cell(r++, 3).Value = 95;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal(new[] { "语文", "数学" }, exam.Subjects);

            var sA = exam.Students.First(s => s.Name == "学生甲");
            var sB = exam.Students.First(s => s.Name == "学生乙");

            // 总分由各单科自动累加求和
            Assert.Equal(170, sA.Total.Score); // 80 + 90
            Assert.Equal(180, sB.Total.Score); // 85 + 95

            // 总分班排自动推导
            Assert.Equal(2, sA.Total.ClassRank);
            Assert.Equal(1, sB.Total.ClassRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportAndReimport_FromPersonalScoreReport_RoundTripsSuccessfully()
    {
        // 验证：由全科个人成绩单导入的数据，可以直接通过成绩条生成引擎导出为成绩条，并可反向再次导入
        var fixturePath = GetPersonalReportFixturePath();
        var exam = GradeSheetImporter.Load(fixturePath);
        Assert.Equal(46, exam.Students.Count);

        var tempStripFile = Path.Combine(Path.GetTempPath(), $"personal_to_strips_{Path.GetRandomFileName()}.xlsx");
        try
        {
            var exportResult = ScoreStripExporter.Export(exam, tempStripFile, new StripExportOptions
            {
                UseSchoolRank = false
            });
            Assert.True(exportResult.Sheets.Count > 0);

            // 反向导入生成的成绩条
            var reimported = GradeSheetImporter.Load(tempStripFile);
            Assert.Equal(exam.Students.Count, reimported.Students.Count);
            Assert.Equal(exam.Subjects, reimported.Subjects);

            var originalS1 = exam.Students[0];
            var reimportedS1 = reimported.Students[0];
            Assert.Equal(originalS1.Name, reimportedS1.Name);
            Assert.Equal(originalS1.Total.Score, reimportedS1.Total.Score);
            Assert.Equal(originalS1.Total.ClassRank, reimportedS1.Total.ClassRank);
        }
        finally
        {
            if (File.Exists(tempStripFile)) File.Delete(tempStripFile);
        }
    }

    [Fact]
    public void Load_PartialSelection_ScoresAndSchoolRank_DerivesClassRank()
    {
        // 模拟用户勾选了「得分」和「年级名次」，但未勾选「班级名次」
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_schoolrank_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 A
                ws.Cell(r++, 1).Value = "学生A_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 90;
                ws.Cell(r++, 3).Value = 90;

                ws.Cell(r, 1).Value = "年级名次";
                ws.Cell(r, 2).Value = 120;
                ws.Cell(r++, 3).Value = 120;

                r++;

                // 学生 B
                ws.Cell(r++, 1).Value = "学生B_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 95;
                ws.Cell(r++, 3).Value = 95;

                ws.Cell(r, 1).Value = "年级名次";
                ws.Cell(r, 2).Value = 80;
                ws.Cell(r++, 3).Value = 80;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Equal(2, exam.Students.Count);

            var sA = exam.Students.First(s => s.Name == "学生A");
            var sB = exam.Students.First(s => s.Name == "学生B");

            // 年级名次从表格直接获取
            Assert.Equal(120, sA.Total.SchoolRank);
            Assert.Equal(80, sB.Total.SchoolRank);

            // 班级名次未勾选，由系统根据实考得分推导
            Assert.Equal(2, sA.Total.ClassRank); // 90 分排第 2
            Assert.Equal(1, sB.Total.ClassRank); // 95 分排第 1
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_PartialSelection_WithExtraStatRowsAndNoDetails()
    {
        // 模拟用户勾选了「满分、得分、班级最高分、年级平均分」，但未勾选「小题明细」和「名次」
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_extra_stats_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 A
                ws.Cell(r++, 1).Value = "学生X_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "总分";

                ws.Cell(r, 1).Value = "满分";
                ws.Cell(r, 2).Value = 120;
                ws.Cell(r++, 3).Value = 120;

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 92;
                ws.Cell(r++, 3).Value = 92;

                ws.Cell(r, 1).Value = "班级最高分";
                ws.Cell(r, 2).Value = 110;
                ws.Cell(r++, 3).Value = 110;

                ws.Cell(r, 1).Value = "年级平均分";
                ws.Cell(r, 2).Value = 75.5;
                ws.Cell(r++, 3).Value = 75.5;

                // 无小题得分明细，紧接着下一个学生
                ws.Cell(r++, 1).Value = "学生Y_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r++, 3).Value = "总分";

                ws.Cell(r, 1).Value = "满分";
                ws.Cell(r, 2).Value = 120;
                ws.Cell(r++, 3).Value = 120;

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 88;
                ws.Cell(r++, 3).Value = 88;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Equal(2, exam.Students.Count);

            var sX = exam.Students.First(s => s.Name == "学生X");
            var sY = exam.Students.First(s => s.Name == "学生Y");

            Assert.Equal(92, sX.Total.Score);
            Assert.Equal(88, sY.Total.Score);

            Assert.Equal(1, sX.Total.ClassRank);
            Assert.Equal(2, sY.Total.ClassRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_PartialSelection_StudentWithAbsentSubject_HandlesNullGracefully()
    {
        // 模拟某学生单科缺考（填「缺考」或空白）
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_absent_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九14班");
                var r = 1;

                // 学生 A（英语缺考）
                ws.Cell(r++, 1).Value = "赵六_成绩单";
                ws.Cell(r++, 1).Value = "2026-2027学年第一学期九年级第一次段考九14班";
                ws.Cell(r, 1).Value = "科目";
                ws.Cell(r, 2).Value = "语文";
                ws.Cell(r, 3).Value = "英语";
                ws.Cell(r++, 4).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 80;
                ws.Cell(r, 3).Value = "缺考";
                ws.Cell(r++, 4).Value = 80;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Single(exam.Students);
            var student = exam.Students[0];
            Assert.Equal(80, student["语文"].Score);
            Assert.Null(student["英语"].Score);
            Assert.False(student["英语"].HasScore);
            Assert.Equal(80, student.Total.Score);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
