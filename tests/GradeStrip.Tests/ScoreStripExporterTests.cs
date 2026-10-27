using System.IO;
using ClosedXML.Excel;
using GradeStrip.Core.Export;
using GradeStrip.Core.Import;
using GradeStrip.Core.Layout;
using Xunit;

namespace GradeStrip.Tests;

public class ScoreStripExporterTests
{
    [Fact]
    public void Strips_SubjectLine_RanksCorrectly()
    {
        // 班排格式
        Assert.Equal("班4 ▲2", Strips.SubjectLine(4, 2, useSchoolRank: false));
        Assert.Equal("班10 ▼3", Strips.SubjectLine(10, -3, useSchoolRank: false));
        Assert.Equal("班15 —", Strips.SubjectLine(15, null, useSchoolRank: false));
        Assert.Equal("班— —", Strips.SubjectLine(null, null, useSchoolRank: false));

        // 校排格式
        Assert.Equal("校35 ▲5", Strips.SubjectLine(35, 5, useSchoolRank: true));
        Assert.Equal("校120 ▼18", Strips.SubjectLine(120, -18, useSchoolRank: true));
        Assert.Equal("校58 —", Strips.SubjectLine(58, null, useSchoolRank: true));
        Assert.Equal("校— —", Strips.SubjectLine(null, null, useSchoolRank: true));
    }

    [Fact]
    public void Strips_LegendSegments_SwitchesWithRankMode()
    {
        var classLegend = Strips.LegendSegments(useSchoolRank: false);
        Assert.Contains(classLegend, s => s.Text.Contains("得分底色按【班次】着色："));

        var schoolLegend = Strips.LegendSegments(useSchoolRank: true);
        Assert.Contains(schoolLegend, s => s.Text.Contains("得分底色按【校次】着色："));
    }

    [Fact]
    public void ScoreStripExporter_Export_AppliesRankDimension()
    {
        var fixturePath = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine("tests", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        }
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        var data = GradeSheetImporter.Load(fixturePath);

        // 1. 验证班排导出
        var tempClassFile = Path.Combine(Path.GetTempPath(), $"gradestrip_class_{Path.GetRandomFileName()}.xlsx");
        try
        {
            ScoreStripExporter.Export(data, tempClassFile, new StripExportOptions { UseSchoolRank = false });
            using var wb = new XLWorkbook(tempClassFile);
            var ws = wb.Worksheets.Worksheet(1);

            // 图例检查
            var legendCell = ws.Cell(2, 1).GetString();
            Assert.Contains("得分底色按【班次】着色：", legendCell);

            // 第一名学生第三行（注记行，第7行）第一门科目（第2列）应为班排
            var noteCell = ws.Cell(7, 2).GetString();
            Assert.StartsWith("班", noteCell);

            // 班排汇总列应为高亮底色 (#DDEBF7)
            var classRankCol = 2 + data.Subjects.Count + 2; // 班次在倒数第一列 (lastCol)
            var classRankCell = ws.Cell(6, classRankCol); // 得分行
            var classColor = classRankCell.Style.Fill.BackgroundColor.Color;
            Assert.Equal((221, 235, 247), (classColor.R, classColor.G, classColor.B));
        }
        finally
        {
            if (File.Exists(tempClassFile)) File.Delete(tempClassFile);
        }

        // 2. 验证校排导出
        var tempSchoolFile = Path.Combine(Path.GetTempPath(), $"gradestrip_school_{Path.GetRandomFileName()}.xlsx");
        try
        {
            ScoreStripExporter.Export(data, tempSchoolFile, new StripExportOptions { UseSchoolRank = true });
            using var wb = new XLWorkbook(tempSchoolFile);
            var ws = wb.Worksheets.Worksheet(1);

            // 图例检查
            var legendCell = ws.Cell(2, 1).GetString();
            Assert.Contains("得分底色按【校次】着色：", legendCell);

            // 第一名学生第三行（注记行，第7行）第一门科目（第2列）应为校排
            var noteCell = ws.Cell(7, 2).GetString();
            Assert.StartsWith("校", noteCell);

            // 校排汇总列应为高亮底色 (#DDEBF7)
            var schoolRankCol = 2 + data.Subjects.Count + 1; // 校次在总分与班次之间
            var schoolRankCell = ws.Cell(6, schoolRankCol); // 得分行
            var schoolColor = schoolRankCell.Style.Fill.BackgroundColor.Color;
            Assert.Equal((221, 235, 247), (schoolColor.R, schoolColor.G, schoolColor.B));
        }
        finally
        {
            if (File.Exists(tempSchoolFile)) File.Delete(tempSchoolFile);
        }
    }
}
