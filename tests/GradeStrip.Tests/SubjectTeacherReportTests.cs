using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GradeStrip.Core.Export;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class SubjectTeacherReportTests
{
    private static string GetFixturePath(string fileName)
    {
        var path = Path.Combine("..", "..", "..", "..", "fixtures", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine("tests", "fixtures", fileName);
        }
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return path;
    }

    [Fact]
    public void Load_SubjectTeacher_ClassSummaryXlsx_ParsesPhysicsSubjectAndDeltas()
    {
        // 1. 科任老师单科班级汇总表（包含 得分、校次、校次进退步、班次、班次进退步）
        var path = GetFixturePath("智学网-单科成绩表-物理.xlsx");
        var exam = GradeSheetImporter.Load(path);

        Assert.Equal("九年级11班", exam.ClassName);
        Assert.Contains("第一次段考", exam.ExamName);
        Assert.Single(exam.Subjects);
        Assert.Equal("物理", exam.Subjects[0]);
        Assert.Equal(51, exam.Students.Count);

        // 学生 1：蒋念安
        var s1 = exam.Students[0];
        Assert.Equal(1, s1.No);
        Assert.Equal("蒋念安", s1.Name);
        Assert.Equal("77000062", s1.ExamNo);
        Assert.Equal(97, s1.Total.Score);
        Assert.Equal(1, s1.Total.ClassRank);
        Assert.Equal(35, s1.Total.SchoolRank);
        Assert.Equal(0, s1.Total.ClassDelta);
        Assert.Equal(-24, s1.Total.SchoolDelta);
        Assert.Equal(97, s1["物理"].Score);
        Assert.Equal(1, s1["物理"].ClassRank);
        Assert.Equal(0, s1["物理"].ClassDelta);

        // 学生 3：杨念安
        var s3 = exam.Students[2];
        Assert.Equal(3, s3.No);
        Assert.Equal("杨念安", s3.Name);
        Assert.Equal(89, s3.Total.Score);
        Assert.Equal(3, s3.Total.ClassRank);
        Assert.Equal(10, s3.Total.ClassDelta);
        Assert.Equal(201, s3.Total.SchoolRank);
        Assert.Equal(107, s3.Total.SchoolDelta);

        // 学生 50：赵星河（缺考，剔除）
        var s50 = exam.Students[49];
        Assert.Equal("赵星河", s50.Name);
        Assert.Null(s50.Total.Score);
        Assert.False(s50.Total.HasScore);
        Assert.Null(s50["物理"].Score);

        // 学生 51：高北辰（未扫，剔除）
        var s51 = exam.Students[50];
        Assert.Equal("高北辰", s51.Name);
        Assert.Null(s51.Total.Score);

        // 提示信息应明确告知为科任老师单科成绩表
        Assert.NotNull(exam.ImportNotice);
        Assert.Contains("物理", exam.ImportNotice);
        Assert.Contains("科任老师", exam.ImportNotice);
    }

    [Fact]
    public void Load_SubjectTeacher_PersonalScoreReportXls_ParsesAllStudentsAndRanks()
    {
        // 2. 科任老师单科个人成绩单（纵向卡片堆叠，每位学生包含自然语言总结与小题得分明细）
        var path = GetFixturePath("智学网-单科个人成绩单-物理.xls");
        var exam = GradeSheetImporter.Load(path);

        Assert.Equal("九年级11班", exam.ClassName);
        Assert.Contains("第一次段考", exam.ExamName);
        Assert.Single(exam.Subjects);
        Assert.Equal("物理", exam.Subjects[0]);
        Assert.Equal(49, exam.Students.Count);

        // 学生 1：蒋念安
        var s1 = exam.Students[0];
        Assert.Equal(1, s1.No);
        Assert.Equal("蒋念安", s1.Name);
        Assert.Equal("77000062", s1.ExamNo);
        Assert.Equal(97, s1.Total.Score);
        Assert.Equal(1, s1.Total.ClassRank);
        Assert.Equal(35, s1.Total.SchoolRank);
        Assert.Equal(97, s1["物理"].Score);
        Assert.Equal(1, s1["物理"].ClassRank);
        Assert.Equal(35, s1["物理"].SchoolRank);

        // 得分明细：题号行 + 得分行逐列配对（不含总分列）
        Assert.NotNull(s1.QuestionScores);
        var q1 = s1.QuestionScores!;
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "15", "16", "19", "20", "21" }, q1.Select(q => q.Label));
        Assert.Equal(new double?[] { 3, 3, 3, 3, 3, 3, 3, 20, 7, 20, 6, 6, 17 }, q1.Select(q => q.Score));
        Assert.DoesNotContain(q1, q => q.Label == "总分");

        // 学生 2：高星河
        var s2 = exam.Students[1];
        Assert.Equal(2, s2.No);
        Assert.Equal("高星河", s2.Name);
        Assert.Equal("77000080", s2.ExamNo);
        Assert.Equal(92, s2.Total.Score);
        Assert.Equal(2, s2.Total.ClassRank);
        Assert.Equal(137, s2.Total.SchoolRank);

        // 学生 49：吴北辰（最后一名实考）
        var s49 = exam.Students[48];
        Assert.Equal(49, s49.No);
        Assert.Equal("吴北辰", s49.Name);
        Assert.Equal(15, s49.Total.Score);
        Assert.Equal(49, s49.Total.ClassRank);
        Assert.Equal(949, s49.Total.SchoolRank);

        Assert.NotNull(exam.ImportNotice);
        Assert.Contains("物理", exam.ImportNotice);
        Assert.Contains("科任老师", exam.ImportNotice);
    }

    [Fact]
    public void SubjectTeacher_RoundTrip_ExportScoreStripsAndReimport()
    {
        // 验证：科任老师单科成绩表导出为个人成绩条后，可以无缝反向再次导入
        var path = GetFixturePath("智学网-单科成绩表-物理.xlsx");
        var original = GradeSheetImporter.Load(path);

        var tempStripFile = Path.Combine(Path.GetTempPath(), $"subject_teacher_strips_{Path.GetRandomFileName()}.xlsx");
        try
        {
            var exportResult = ScoreStripExporter.Export(original, tempStripFile, new StripExportOptions
            {
                UseSchoolRank = false
            });
            Assert.True(exportResult.Sheets.Count > 0);

            var reimported = GradeSheetImporter.Load(tempStripFile);
            Assert.Equal(original.Students.Count, reimported.Students.Count);
            Assert.Equal(original.Subjects, reimported.Subjects);

            var origS1 = original.Students[0];
            var reimpS1 = reimported.Students[0];
            Assert.Equal(origS1.Name, reimpS1.Name);
            Assert.Equal(origS1.Total.Score, reimpS1.Total.Score);
            Assert.Equal(origS1.Total.ClassRank, reimpS1.Total.ClassRank);
        }
        finally
        {
            if (File.Exists(tempStripFile)) File.Delete(tempStripFile);
        }
    }

    [Fact]
    public void Load_SubjectTeacher_PersonalReport_OnlyQuestionTable_ParsesCorrectly()
    {
        // 模拟用户导出单科个人成绩单时仅勾选了小题得分明细，未勾选自然语言评语段
        var tempFile = Path.Combine(Path.GetTempPath(), $"subject_teacher_no_summary_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("九年级11班");
                var r = 1;

                // 学生 1
                ws.Cell(r++, 1).Value = "蒋念安(77000062)_成绩单";
                ws.Cell(r++, 1).Value = "示例市文化中学2026-2027学年第一学期九年级第一次段考(化学)_九年级11班";
                ws.Cell(r++, 1).Value = "化学得分明细：";
                ws.Cell(r, 1).Value = "题号";
                ws.Cell(r, 2).Value = "1";
                ws.Cell(r, 3).Value = "2";
                ws.Cell(r++, 4).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 30;
                ws.Cell(r, 3).Value = 40;
                ws.Cell(r++, 4).Value = 70;

                r++;

                // 学生 2
                ws.Cell(r++, 1).Value = "高星河(77000080)_成绩单";
                ws.Cell(r++, 1).Value = "示例市文化中学2026-2027学年第一学期九年级第一次段考(化学)_九年级11班";
                ws.Cell(r++, 1).Value = "化学得分明细：";
                ws.Cell(r, 1).Value = "题号";
                ws.Cell(r, 2).Value = "1";
                ws.Cell(r, 3).Value = "2";
                ws.Cell(r++, 4).Value = "总分";

                ws.Cell(r, 1).Value = "得分";
                ws.Cell(r, 2).Value = 25;
                ws.Cell(r, 3).Value = 40;
                ws.Cell(r++, 4).Value = 65;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal("九年级11班", exam.ClassName);
            Assert.Single(exam.Subjects);
            Assert.Equal("化学", exam.Subjects[0]);

            var s1 = exam.Students[0];
            Assert.Equal("蒋念安", s1.Name);
            Assert.Equal("77000062", s1.ExamNo);
            Assert.Equal(70, s1.Total.Score);
            Assert.Equal(1, s1.Total.ClassRank); // 70分排第1

            // 仅题号/得分表也应提取明细（总分列被跳过）
            Assert.NotNull(s1.QuestionScores);
            var qs1 = s1.QuestionScores!;
            Assert.Equal(new[] { "1", "2" }, qs1.Select(q => q.Label));
            Assert.Equal(new double?[] { 30, 40 }, qs1.Select(q => q.Score));

            var s2 = exam.Students[1];
            Assert.Equal("高星河", s2.Name);
            Assert.Equal(65, s2.Total.Score);
            Assert.Equal(2, s2.Total.ClassRank); // 65分排第2

            Assert.NotNull(s2.QuestionScores);
            var qs2 = s2.QuestionScores!;
            Assert.Equal(new double?[] { 25, 40 }, qs2.Select(q => q.Score));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_SubjectTeacher_ClassSummary_WithoutDeltas_SimpleSheet()
    {
        // 模拟科任老师单科简表（无进退步列）
        var tempFile = Path.Combine(Path.GetTempPath(), $"subject_teacher_simple_{Path.GetRandomFileName()}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("班级历史成绩简表");
                ws.Cell(1, 1).Value = "九年级11班-历史成绩汇总(80分)-示例市文化中学2026-2027学年第一学期九年级第一次段考";
                ws.Cell(2, 1).Value = "序号";
                ws.Cell(2, 2).Value = "准考证号";
                ws.Cell(2, 3).Value = "姓名";
                ws.Cell(2, 4).Value = "得分";
                ws.Cell(2, 5).Value = "校次";
                ws.Cell(2, 6).Value = "班次";

                ws.Cell(3, 1).Value = 1;
                ws.Cell(3, 2).Value = "77000062";
                ws.Cell(3, 3).Value = "蒋念安";
                ws.Cell(3, 4).Value = 75;
                ws.Cell(3, 5).Value = 12;
                ws.Cell(3, 6).Value = 1;

                wb.SaveAs(tempFile);
            }

            var exam = GradeSheetImporter.Load(tempFile);
            Assert.Single(exam.Students);
            Assert.Equal("历史", exam.Subjects[0]);
            Assert.Equal(75, exam.Students[0].Total.Score);
            Assert.Equal(1, exam.Students[0].Total.ClassRank);
            Assert.Equal(12, exam.Students[0].Total.SchoolRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
