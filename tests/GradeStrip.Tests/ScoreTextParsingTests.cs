using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class ScoreTextParsingTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gradestrip_scoretext_{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>构造智学网「所有单科成绩报告」式工作簿：每个科目一张分表。</summary>
    private static string WriteMergedWorkbook(string dir)
    {
        var path = Path.Combine(dir, "所有单科成绩报告.xlsx");
        using var workbook = new XLWorkbook();

        var chemistry = workbook.Worksheets.Add("化学");
        chemistry.Cell(1, 1).Value = "化学成绩表";
        chemistry.Cell(2, 1).Value = "序号";
        chemistry.Cell(2, 2).Value = "准考证号";
        chemistry.Cell(2, 3).Value = "班级";
        chemistry.Cell(2, 4).Value = "姓名";
        chemistry.Cell(2, 5).Value = "班次";
        chemistry.Cell(2, 6).Value = "总分";
        // 张三：班次与总分都是「不计排名」文本
        chemistry.Cell(3, 1).Value = 1;
        chemistry.Cell(3, 2).Value = "20260001";
        chemistry.Cell(3, 3).Value = "九1班";
        chemistry.Cell(3, 4).Value = "张三";
        chemistry.Cell(3, 5).Value = "2，不计排名";
        chemistry.Cell(3, 6).Value = "0，不计排名";
        // 李四：正常数值
        chemistry.Cell(4, 1).Value = 2;
        chemistry.Cell(4, 2).Value = "20260002";
        chemistry.Cell(4, 3).Value = "九1班";
        chemistry.Cell(4, 4).Value = "李四";
        chemistry.Cell(4, 5).Value = 3;
        chemistry.Cell(4, 6).Value = 75;

        var biology = workbook.Worksheets.Add("生物");
        biology.Cell(1, 1).Value = "生物成绩表";
        biology.Cell(2, 1).Value = "序号";
        biology.Cell(2, 2).Value = "准考证号";
        biology.Cell(2, 3).Value = "班级";
        biology.Cell(2, 4).Value = "姓名";
        biology.Cell(2, 5).Value = "总分";
        biology.Cell(2, 6).Value = "总分";
        // 张三：第一列「总分」是「不计排名」文本，最后一列有数值 8
        biology.Cell(3, 1).Value = 1;
        biology.Cell(3, 2).Value = "20260001";
        biology.Cell(3, 3).Value = "九1班";
        biology.Cell(3, 4).Value = "张三";
        biology.Cell(3, 5).Value = "8，不计排名";
        biology.Cell(3, 6).Value = 8;
        // 李四：两列都有数值，取最左一列
        biology.Cell(4, 1).Value = 2;
        biology.Cell(4, 2).Value = "20260002";
        biology.Cell(4, 3).Value = "九1班";
        biology.Cell(4, 4).Value = "李四";
        biology.Cell(4, 5).Value = 80;
        biology.Cell(4, 6).Value = 82;

        workbook.SaveAs(path);
        return path;
    }

    /// <summary>「不计排名」文本总分解析为前导数字，名次留空；并列「总分」列取最左非空列。</summary>
    [Fact]
    public void Load_MergedSubjectSheets_ParsesTextScoresAndMultipleTotalColumns()
    {
        var dir = NewTempDir();
        try
        {
            var path = WriteMergedWorkbook(dir);
            var exam = GradeSheetImporter.Load(path);

            Assert.Equal(new[] { "化学", "生物" }, exam.Subjects);
            Assert.Equal(2, exam.Students.Count);

            var zhang = exam.Students.Single(s => s.Name == "张三");
            Assert.Equal(0.0, zhang["化学"].Score);
            Assert.Null(zhang["化学"].ClassRank);
            Assert.Null(zhang["化学"].SchoolRank);
            Assert.Equal(8.0, zhang["生物"].Score);
            Assert.Equal(8.0, zhang.Total.Score);

            var li = exam.Students.Single(s => s.Name == "李四");
            Assert.Equal(80.0, li["生物"].Score);
            Assert.Equal(75.0, li["化学"].Score);
            Assert.Equal(3, li["化学"].ClassRank);
            Assert.Equal(155.0, li.Total.Score);

            Assert.Equal("九1班", zhang.ClassName);
            Assert.Equal("九1班", li.ClassName);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
