using System.IO;
using ClosedXML.Excel;
using GradeStrip.Core.Import;
using Xunit;

namespace GradeStrip.Tests;

/// <summary>
/// 成绩条通道的兼容性回归：多科分表合并、缺列容错、明细表降级、跨班标注、标题截断。
/// 全部用 ClosedXML 在临时目录现造最小工作簿，不依赖真实学生数据。
/// </summary>
public class ImportCompatibilityTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gradestrip_import_{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>智学网「所有单科成绩报告」式分表：序号 / 准考证号 / 班级 / 姓名 / 得分 / 校次 / 班次。</summary>
    private static void WriteSubjectSheet(
        IXLWorksheet sheet,
        (int No, int ExamNo, string ClassName, string Name, double Score, int SchoolRank, int ClassRank)[] rows)
    {
        var header = new[] { "序号", "准考证号", "班级", "姓名", "得分", "校次", "班次" };
        for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            sheet.Cell(i + 2, 1).Value = row.No;
            sheet.Cell(i + 2, 2).Value = row.ExamNo;
            sheet.Cell(i + 2, 3).Value = row.ClassName;
            sheet.Cell(i + 2, 4).Value = row.Name;
            sheet.Cell(i + 2, 5).Value = row.Score;
            sheet.Cell(i + 2, 6).Value = row.SchoolRank;
            sheet.Cell(i + 2, 7).Value = row.ClassRank;
        }
    }

    /// <summary>语文 + 数学两张分表，同班两名学生。</summary>
    private static string CreateTwoSubjectReport(string dir, string fileName)
    {
        using var workbook = new XLWorkbook();
        WriteSubjectSheet(workbook.AddWorksheet("语文"), new[]
        {
            (1, 20260001, "九1班", "张三", 88d, 12, 3),
            (2, 20260002, "九1班", "李四", 92d, 8, 1),
        });
        WriteSubjectSheet(workbook.AddWorksheet("数学"), new[]
        {
            (1, 20260001, "九1班", "张三", 95d, 7, 2),
            (2, 20260002, "九1班", "李四", 90d, 10, 4),
        });

        var path = Path.Combine(dir, fileName);
        workbook.SaveAs(path);
        return path;
    }

    private static void AssertTwoSubjectsMerged(GradeStrip.Core.Model.ExamData exam)
    {
        Assert.Equal(new[] { "语文", "数学" }, exam.Subjects);
        Assert.Equal(2, exam.Students.Count);
        Assert.Equal("九1班", exam.ClassName);

        var zhang = exam.Students[0];
        Assert.Equal("张三", zhang.Name);
        Assert.Equal("20260001", zhang.ExamNo);
        Assert.Equal(183d, zhang.Total.Score!.Value);

        Assert.Equal(88d, zhang["语文"].Score!.Value);
        Assert.Equal(12, zhang["语文"].SchoolRank!.Value);
        Assert.Equal(3, zhang["语文"].ClassRank!.Value);
        Assert.Equal(95d, zhang["数学"].Score!.Value);
        Assert.Equal(7, zhang["数学"].SchoolRank!.Value);
        Assert.Equal(2, zhang["数学"].ClassRank!.Value);

        var li = exam.Students[1];
        Assert.Equal("李四", li.Name);
        Assert.Equal("20260002", li.ExamNo);
        Assert.Equal(182d, li.Total.Score!.Value);

        Assert.Equal(92d, li["语文"].Score!.Value);
        Assert.Equal(8, li["语文"].SchoolRank!.Value);
        Assert.Equal(1, li["语文"].ClassRank!.Value);
        Assert.Equal(90d, li["数学"].Score!.Value);
        Assert.Equal(4, li["数学"].ClassRank!.Value);
    }

    [Fact]
    public void Load_TwoSubjectSheetsXlsx_MergesSubjects()
    {
        var dir = NewTempDir();
        try
        {
            var path = CreateTwoSubjectReport(dir, "所有单科成绩报告.xlsx");

            var exam = GradeSheetImporter.Load(path);

            AssertTwoSubjectsMerged(exam);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_TwoSubjectSheetsXls_MergesSubjects()
    {
        var dir = NewTempDir();
        try
        {
            // 与 XlsCompatibilityTests 同法：内容仍是 xlsx，靠 ExcelDataReader 按内容识别
            var xlsxPath = CreateTwoSubjectReport(dir, "所有单科成绩报告-source.xlsx");
            var xlsPath = Path.Combine(dir, "所有单科成绩报告.xls");
            File.Copy(xlsxPath, xlsPath);

            var exam = GradeSheetImporter.Load(xlsPath);

            AssertTwoSubjectsMerged(exam);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_MissingExamNoColumn_DoesNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "无考号成绩表.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("成绩");
                sheet.Cell(1, 1).Value = "姓名";
                sheet.Cell(1, 2).Value = "总分";
                sheet.Cell(1, 3).Value = "班次";

                sheet.Cell(2, 1).Value = "张三";
                sheet.Cell(2, 2).Value = 95d;
                sheet.Cell(2, 3).Value = 1;

                sheet.Cell(3, 1).Value = "李四";
                sheet.Cell(3, 2).Value = 88d;
                sheet.Cell(3, 3).Value = 2;

                workbook.SaveAs(path);
            }

            var exam = GradeSheetImporter.Load(path);

            Assert.Equal(2, exam.Students.Count);
            Assert.All(exam.Students, s => Assert.Equal(string.Empty, s.ExamNo));
            Assert.Equal(95d, exam.Students[0].Total.Score!.Value);
            Assert.Equal(1, exam.Students[0].Total.ClassRank!.Value);
            Assert.Equal(2, exam.Students[1].Total.ClassRank!.Value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_DetailSheet_UsesTotalScore()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "数学小题明细.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("数学");
                var header = new[] { "姓名", "总分", "客观分", "主观分", "单选题", "语文作文", "1", "2" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];

                sheet.Cell(2, 1).Value = "张三";
                sheet.Cell(2, 2).Value = 96d;
                sheet.Cell(2, 3).Value = 45d;
                sheet.Cell(2, 4).Value = 51d;
                sheet.Cell(2, 5).Value = 10d;
                sheet.Cell(2, 6).Value = 12d;
                sheet.Cell(2, 7).Value = 3d;
                sheet.Cell(2, 8).Value = 2d;

                sheet.Cell(3, 1).Value = "李四";
                sheet.Cell(3, 2).Value = 91d;
                sheet.Cell(3, 3).Value = 40d;
                sheet.Cell(3, 4).Value = 51d;
                sheet.Cell(3, 5).Value = 9d;
                sheet.Cell(3, 6).Value = 11d;
                sheet.Cell(3, 7).Value = 2d;
                sheet.Cell(3, 8).Value = 3d;

                workbook.SaveAs(path);
            }

            var exam = GradeSheetImporter.Load(path);

            Assert.Single(exam.Subjects);
            Assert.Equal("数学", exam.Subjects[0]);
            Assert.Equal(2, exam.Students.Count);

            var zhang = exam.Students[0];
            Assert.Equal(96d, zhang["数学"].Score!.Value);
            Assert.Equal(96d, zhang.Total.Score!.Value);
            Assert.Single(zhang.Subjects);
            Assert.DoesNotContain("客观分", zhang.Subjects.Keys);
            Assert.DoesNotContain("1", zhang.Subjects.Keys);
            Assert.Equal(91d, exam.Students[1]["数学"].Score!.Value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_OnlyObjectiveScore_ThrowsInvalidData()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "只有客观分.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("数学");
                var header = new[] { "姓名", "客观分", "1", "2", "3" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];

                sheet.Cell(2, 1).Value = "张三";
                sheet.Cell(2, 2).Value = 45d;
                sheet.Cell(2, 3).Value = 3d;
                sheet.Cell(2, 4).Value = 2d;
                sheet.Cell(2, 5).Value = 4d;

                sheet.Cell(3, 1).Value = "李四";
                sheet.Cell(3, 2).Value = 40d;
                sheet.Cell(3, 3).Value = 2d;
                sheet.Cell(3, 4).Value = 3d;
                sheet.Cell(3, 5).Value = 5d;

                workbook.SaveAs(path);
            }

            var ex = Assert.Throws<InvalidDataException>(() => GradeSheetImporter.Load(path));

            Assert.Contains("缺少科目总分", ex.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_AnswerCardFillSheet_ThrowsInsteadOfFakeSubjects()
    {
        var dir = NewTempDir();
        try
        {
            // 智学网「学生答题明细」里的客观题填涂表：准考证号 | 姓名 | 科目 | 客观分 | 题号列…
            // 「科目」列的存在让明细判定失效，题号被当成科目读出假成绩。
            var path = Path.Combine(dir, "九A1客观题填涂.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("九A1客观题填涂");
                var header = new[] { "准考证号", "姓名", "科目", "客观分", "1", "2", "3" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                sheet.Cell(3, 1).Value = "26829895";
                sheet.Cell(3, 2).Value = "张三";
                sheet.Cell(3, 3).Value = "数学";
                sheet.Cell(3, 4).Value = 30d;
                sheet.Cell(3, 5).Value = "B";

                sheet.Cell(4, 1).Value = "26829899";
                sheet.Cell(4, 2).Value = "李四";
                sheet.Cell(4, 3).Value = "数学";
                sheet.Cell(4, 4).Value = 27d;
                sheet.Cell(4, 5).Value = "C";

                workbook.SaveAs(path);
            }

            var ex = Assert.Throws<InvalidDataException>(() => GradeSheetImporter.Load(path));

            Assert.Contains("客观题填涂明细", ex.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_CrossClass_LeavesClassNameEmpty()

    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "跨班成绩表.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("成绩");
                var header = new[] { "班级", "姓名", "语文", "数学" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];

                sheet.Cell(2, 1).Value = "九1班";
                sheet.Cell(2, 2).Value = "张三";
                sheet.Cell(2, 3).Value = 88d;
                sheet.Cell(2, 4).Value = 95d;

                sheet.Cell(3, 1).Value = "九2班";
                sheet.Cell(3, 2).Value = "李四";
                sheet.Cell(3, 3).Value = 92d;
                sheet.Cell(3, 4).Value = 90d;

                workbook.SaveAs(path);
            }

            var exam = GradeSheetImporter.Load(path);

            Assert.Equal(string.Empty, exam.ClassName);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal(2, exam.Subjects.Count);
            Assert.Equal(183d, exam.Students[0].Total.Score!.Value);
            Assert.Equal(182d, exam.Students[1].Total.Score!.Value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ExamNameTruncatedAtBracket()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "教学班报告.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("语文");
                sheet.Cell(1, 1).Value =
                    "【教学班报告--某某中学2026-2027学年第一学期九年级第一次段考】所有班级语文学生小题得分明细";

                var header = new[] { "序号", "班级", "姓名", "得分", "校次", "班次" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(3, c + 1).Value = header[c];

                sheet.Cell(4, 1).Value = 1;
                sheet.Cell(4, 2).Value = "九1班";
                sheet.Cell(4, 3).Value = "张三";
                sheet.Cell(4, 4).Value = 88d;
                sheet.Cell(4, 5).Value = 12;
                sheet.Cell(4, 6).Value = 3;

                sheet.Cell(5, 1).Value = 2;
                sheet.Cell(5, 2).Value = "九1班";
                sheet.Cell(5, 3).Value = "李四";
                sheet.Cell(5, 4).Value = 92d;
                sheet.Cell(5, 5).Value = 8;
                sheet.Cell(5, 6).Value = 1;

                workbook.SaveAs(path);
            }

            var exam = GradeSheetImporter.Load(path);

            Assert.Equal("2026-2027学年第一学期九年级第一次段考", exam.ExamName);
            Assert.DoesNotContain("】", exam.ExamName);
            Assert.Equal("九1班", exam.ClassName);
            Assert.Single(exam.Subjects);
            Assert.Equal("语文", exam.Subjects[0]);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal(88d, exam.Students[0].Total.Score!.Value);
            Assert.Equal(12, exam.Students[0]["语文"].SchoolRank!.Value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_CrossClassSingleTable_DerivesClassRankWithinEachClass()
    {
        // 单表两个班、各 2 人、没有总分班次列：总分班次必须按班分组，不能把年级名次当班次
        var dir = NewTempDir();
        try
        {
            var xlsxPath = Path.Combine(dir, "跨班全科成绩汇总-source.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("汇总");

                // 两级表头：三个科目块，只有 得分 / 校次，无班次、无总分
                sheet.Cell(1, 1).Value = "准考证号";
                sheet.Cell(1, 2).Value = "班级";
                sheet.Cell(1, 3).Value = "姓名";
                var subjects = new[] { "语文", "数学", "英语" };
                for (var s = 0; s < subjects.Length; s++)
                {
                    var col = 4 + s * 2;
                    sheet.Cell(1, col).Value = subjects[s];
                    sheet.Cell(2, col).Value = "得分";
                    sheet.Cell(2, col + 1).Value = "校次";
                }

                var rows = new (int ExamNo, string ClassName, string Name, double Chinese, double Math, double English)[]
                {
                    (1001, "九1班", "张三", 90d, 80d, 70d),
                    (1002, "九1班", "李四", 80d, 90d, 60d),
                    (1003, "九2班", "王五", 70d, 60d, 50d),
                    (1004, "九2班", "赵六", 60d, 50d, 40d),
                };

                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var r = i + 3;
                    sheet.Cell(r, 1).Value = row.ExamNo;
                    sheet.Cell(r, 2).Value = row.ClassName;
                    sheet.Cell(r, 3).Value = row.Name;
                    sheet.Cell(r, 4).Value = row.Chinese;
                    sheet.Cell(r, 5).Value = i + 1;
                    sheet.Cell(r, 6).Value = row.Math;
                    sheet.Cell(r, 7).Value = i + 1;
                    sheet.Cell(r, 8).Value = row.English;
                    sheet.Cell(r, 9).Value = i + 1;
                }

                workbook.SaveAs(xlsxPath);
            }

            // 同一份内容按 .xls 再读一次，覆盖 ExcelDataReader 路径
            var xlsPath = Path.Combine(dir, "跨班全科成绩汇总.xls");
            File.Copy(xlsxPath, xlsPath);

            foreach (var exam in new[] { GradeSheetImporter.Load(xlsxPath), GradeSheetImporter.Load(xlsPath) })
            {
                Assert.Equal(string.Empty, exam.ClassName);
                Assert.Equal(4, exam.Students.Count);
                Assert.Equal(3, exam.Subjects.Count);

                // 两个班各自 1、2 名，而不是全表 1、2、3、4
                Assert.Equal(new[] { 1, 2, 1, 2 }, exam.Students.Select(s => s.Total.ClassRank!.Value));

                // 单科班次同样按班分组
                Assert.Equal(new[] { 1, 2, 1, 2 }, exam.Students.Select(s => s["语文"].ClassRank!.Value));
                Assert.Equal(new[] { 2, 1, 1, 2 }, exam.Students.Select(s => s["数学"].ClassRank!.Value));
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_TwoLevelHeader_EmptyDeltaColumns_ReportsNoDeltaDataWithoutDownloadHint()
    {
        // 两级表头里明明有「校次进退步/班次进退步」列，但源表全部为「-」：
        // 提示应说明本次没有进退步数据，而不是让老师重新去智学网下载
        var dir = NewTempDir();
        try
        {
            var xlsxPath = Path.Combine(dir, "全部考生成绩汇总-source.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("全部考生成绩汇总");
                sheet.Cell(1, 1).Value = "全部考生成绩汇总--某某中学2026-2027学年第一学期九年级第一次段考";

                sheet.Cell(2, 1).Value = "准考证号";
                sheet.Cell(2, 2).Value = "班级";
                sheet.Cell(2, 3).Value = "姓名";

                var subjects = new[] { "语文", "数学", "英语" };
                for (var s = 0; s < subjects.Length; s++)
                {
                    var col = 4 + s * 5;
                    sheet.Cell(1, col).Value = subjects[s];
                    sheet.Cell(2, col).Value = "得分";
                    sheet.Cell(2, col + 1).Value = "校次";
                    sheet.Cell(2, col + 2).Value = "校次进退步";
                    sheet.Cell(2, col + 3).Value = "班次";
                    sheet.Cell(2, col + 4).Value = "班次进退步";
                }

                var rows = new (int ExamNo, string ClassName, string Name, double Chinese, double Math, double English)[]
                {
                    (1001, "九1班", "张三", 90d, 80d, 70d),
                    (1002, "九1班", "李四", 80d, 90d, 60d),
                    (1003, "九2班", "王五", 70d, 60d, 50d),
                };

                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var r = i + 3;
                    sheet.Cell(r, 1).Value = row.ExamNo;
                    sheet.Cell(r, 2).Value = row.ClassName;
                    sheet.Cell(r, 3).Value = row.Name;
                    var scores = new[] { row.Chinese, row.Math, row.English };
                    for (var s = 0; s < scores.Length; s++)
                    {
                        var col = 4 + s * 5;
                        sheet.Cell(r, col).Value = scores[s];
                        sheet.Cell(r, col + 1).Value = i + 1;
                        sheet.Cell(r, col + 2).Value = "-";
                        sheet.Cell(r, col + 3).Value = i + 1;
                        sheet.Cell(r, col + 4).Value = "-";
                    }
                }

                workbook.SaveAs(xlsxPath);
            }

            var xlsPath = Path.Combine(dir, "全部考生成绩汇总.xls");
            File.Copy(xlsxPath, xlsPath);

            foreach (var exam in new[] { GradeSheetImporter.Load(xlsxPath), GradeSheetImporter.Load(xlsPath) })
            {
                Assert.Equal(3, exam.Students.Count);
                Assert.NotNull(exam.ImportNotice);
                Assert.Contains("本次考试没有进退步数据", exam.ImportNotice);
                Assert.DoesNotContain("请前往智学网下载", exam.ImportNotice);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
