using System.IO;
using ClosedXML.Excel;
using GradeStrip.Core.Import;
using Xunit;

namespace GradeStrip.Tests;

public class RosterReportRejectionTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gradestrip_roster_{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>缺考学生汇总只有姓名没有成绩，不能进成绩条通道。</summary>
    [Fact]
    public void Load_AbsenceRosterWorkbook_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "缺考学生汇总.xlsx");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("缺考学生汇总");
            ws.Cell(1, 1).Value = "缺考学生汇总";
            ws.Cell(2, 1).Value = "准考证号";
            ws.Cell(2, 2).Value = "姓名";
            ws.Cell(2, 3).Value = "班级";
            ws.Cell(2, 4).Value = "缺考科目";
            ws.Cell(3, 1).Value = "20260001";
            ws.Cell(3, 2).Value = "张三";
            ws.Cell(3, 3).Value = "九1班";
            ws.Cell(3, 4).Value = "语文";
            workbook.SaveAs(path);

            var ex = Assert.Throws<InvalidOperationException>(() => GradeSheetImporter.Load(path));
            Assert.Contains("导入分析报表", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>临界生对比只有姓名与总分，没有学科列。</summary>
    [Fact]
    public void Load_BorderlineRosterWorkbook_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "临界生对比.xlsx");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("临界生名单");
            ws.Cell(1, 1).Value = "临界生对比";
            ws.Cell(2, 1).Value = "序号";
            ws.Cell(2, 2).Value = "班级";
            ws.Cell(2, 3).Value = "姓名";
            ws.Cell(2, 4).Value = "总分";
            ws.Cell(3, 1).Value = 1;
            ws.Cell(3, 2).Value = "九1班";
            ws.Cell(3, 3).Value = "张三";
            ws.Cell(3, 4).Value = 95;
            workbook.SaveAs(path);

            var ex = Assert.Throws<InvalidOperationException>(() => GradeSheetImporter.Load(path));
            Assert.Contains("导入分析报表", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>临界生名单拆成语文/数学/英语分表时，合并路径会生成假会话，必须拒绝。</summary>
    [Fact]
    public void Load_BorderlineRosterSubjectSheets_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "临界生对比.xlsx");
            using var workbook = new XLWorkbook();
            foreach (var subject in new[] { "语文", "数学", "英语" })
            {
                var ws = workbook.Worksheets.Add(subject);
                ws.Cell(1, 1).Value = $"{subject} 临界生名单";
                ws.Cell(2, 1).Value = "序号";
                ws.Cell(2, 2).Value = "准考证号";
                ws.Cell(2, 3).Value = "班级";
                ws.Cell(2, 4).Value = "姓名";
                ws.Cell(2, 5).Value = "得分";
                ws.Cell(2, 6).Value = "校次";
                ws.Cell(2, 7).Value = "班次";
                ws.Cell(3, 1).Value = 1;
                ws.Cell(3, 2).Value = "20260001";
                ws.Cell(3, 3).Value = "九1班";
                ws.Cell(3, 4).Value = "张三";
                ws.Cell(3, 5).Value = 95;
                ws.Cell(3, 6).Value = 12;
                ws.Cell(3, 7).Value = 3;
            }

            workbook.SaveAs(path);

            var ex = Assert.Throws<InvalidOperationException>(() => GradeSheetImporter.Load(path));
            Assert.Contains("导入分析报表", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>需关注学生名单带类别/说明列，没有学科列。</summary>
    [Fact]
    public void Load_AttentionRosterWorkbook_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "需关注学生-九年级A1.xlsx");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("需关注学生");
            ws.Cell(1, 1).Value = "需关注学生";
            ws.Cell(2, 1).Value = "类别";
            ws.Cell(2, 2).Value = "班级";
            ws.Cell(2, 3).Value = "姓名";
            ws.Cell(2, 4).Value = "总分";
            ws.Cell(2, 5).Value = "班次";
            ws.Cell(2, 6).Value = "说明";
            ws.Cell(3, 1).Value = "临界生";
            ws.Cell(3, 2).Value = "九1班";
            ws.Cell(3, 3).Value = "张三";
            ws.Cell(3, 4).Value = 85;
            ws.Cell(3, 5).Value = 2;
            ws.Cell(3, 6).Value = "数学偏科";
            workbook.SaveAs(path);

            var ex = Assert.Throws<InvalidOperationException>(() => GradeSheetImporter.Load(path));
            Assert.Contains("导入分析报表", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>文件名命中报表关键字，但工作簿里带常规成绩页时放行。</summary>
    [Fact]
    public void Load_MixedWorkbookWithRealScoreSheet_StillImports()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "成绩汇总与临界生名单.xlsx");
            using var workbook = new XLWorkbook();

            var roster = workbook.Worksheets.Add("临界生名单");
            roster.Cell(1, 1).Value = "临界生名单";
            roster.Cell(2, 1).Value = "序号";
            roster.Cell(2, 2).Value = "班级";
            roster.Cell(2, 3).Value = "姓名";
            roster.Cell(2, 4).Value = "总分";
            roster.Cell(3, 1).Value = 1;
            roster.Cell(3, 2).Value = "九1班";
            roster.Cell(3, 3).Value = "张三";
            roster.Cell(3, 4).Value = 95;

            var scores = workbook.Worksheets.Add("成绩");
            scores.Cell(1, 1).Value = "班级成绩表";
            scores.Cell(2, 1).Value = "班级";
            scores.Cell(2, 2).Value = "姓名";
            scores.Cell(2, 3).Value = "语文";
            scores.Cell(2, 4).Value = "数学";
            scores.Cell(3, 1).Value = "九1班";
            scores.Cell(3, 2).Value = "张三";
            scores.Cell(3, 3).Value = 88;
            scores.Cell(3, 4).Value = 95;
            scores.Cell(4, 1).Value = "九2班";
            scores.Cell(4, 2).Value = "李四";
            scores.Cell(4, 3).Value = 90;
            scores.Cell(4, 4).Value = 92;

            workbook.SaveAs(path);

            var exam = GradeSheetImporter.Load(path);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal(new[] { "语文", "数学" }, exam.Subjects);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>识别出学生但所有成绩为空时，明确报错而不是生成空会话。</summary>
    [Fact]
    public void Load_BlankScoreWorkbook_ThrowsNoValidScores()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "空白成绩表.xlsx");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("空白成绩表");
            ws.Cell(1, 1).Value = "空白成绩表";
            ws.Cell(2, 1).Value = "姓名";
            ws.Cell(2, 2).Value = "语文";
            ws.Cell(2, 3).Value = "数学";
            ws.Cell(3, 1).Value = "张三";
            ws.Cell(4, 1).Value = "李四";
            workbook.SaveAs(path);

            var ex = Assert.Throws<InvalidOperationException>(() => GradeSheetImporter.Load(path));
            Assert.Contains("表格中未找到有效的学生成绩数据", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
