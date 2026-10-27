using System.IO;
using ClosedXML.Excel;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

/// <summary>
/// 概览报表通道：两级表头合并、分段/等级拆表、小题分析列保留、名单类报表与成绩总表分流。
/// 全部用 ClosedXML 在临时目录现造最小工作簿。
/// </summary>
public class OverviewReportImporterTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gradestrip_overview_{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Save(XLWorkbook workbook, string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        workbook.SaveAs(path);
        return path;
    }

    /// <summary>「成绩分段对比」：两张表（总分 / 语文），表头为 班级 | [800,850]（B:C 合并）+ 人数 / 占比%。</summary>
    private static string CreateScoreSegmentReport(string dir)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheetName in new[] { "总分", "语文" })
        {
            var sheet = workbook.AddWorksheet(sheetName);
            sheet.Cell(1, 1).Value = "成绩分段对比--某某中学";
            sheet.Cell(2, 1).Value = "班级";
            sheet.Cell(2, 2).Value = "[800,850]";
            sheet.Range(2, 2, 2, 3).Merge();
            sheet.Cell(3, 2).Value = "人数";
            sheet.Cell(3, 3).Value = "占比%";

            sheet.Cell(4, 1).Value = "九1班";
            sheet.Cell(4, 2).Value = 3d;
            sheet.Cell(4, 3).Value = 12.5d;

            sheet.Cell(5, 1).Value = "九2班";
            sheet.Cell(5, 2).Value = 4d;
            sheet.Cell(5, 3).Value = 16.7d;
        }

        return Save(workbook, dir, "成绩分段对比.xlsx");
    }

    [Fact]
    public void Load_ClassComparison_MergesGroupHeaders()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("班级成绩对比");
                sheet.Cell(1, 1).Value = "班级成绩对比--某某中学";

                sheet.Cell(2, 1).Value = "班级";
                sheet.Cell(2, 2).Value = "数学";
                sheet.Range(2, 2, 2, 4).Merge();

                sheet.Cell(3, 2).Value = "得分";
                sheet.Cell(3, 3).Value = "教师";
                sheet.Cell(3, 4).Value = "排名";

                sheet.Cell(4, 1).Value = "九1班";
                sheet.Cell(4, 2).Value = 88.5d;
                sheet.Cell(4, 3).Value = "张老师";
                sheet.Cell(4, 4).Value = 1d;

                sheet.Cell(5, 1).Value = "九2班";
                sheet.Cell(5, 2).Value = 86.2d;
                sheet.Cell(5, 3).Value = "李老师";
                sheet.Cell(5, 4).Value = 2d;

                path = Save(workbook, dir, "班级成绩对比.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.ClassComparison, report.Kind);

            var sheet2 = Assert.Single(report.Sheets);
            var headers = sheet2.Columns.Select(c => c.Header).ToList();
            Assert.Contains("数学·得分", headers);
            Assert.Contains("数学·教师", headers);
            Assert.Contains("数学·排名", headers);

            Assert.Equal(2, sheet2.Rows.Count);
            Assert.Equal("九1班", sheet2.Rows[0][0]);
            Assert.Equal("88.5", sheet2.Rows[0][1]);
            Assert.Equal("张老师", sheet2.Rows[0][2]);
            Assert.Equal("九2班", sheet2.Rows[1][0]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ScoreSegments_KeepsSubjectSheets()
    {
        var dir = NewTempDir();
        try
        {
            var path = CreateScoreSegmentReport(dir);

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.ScoreSegments, report.Kind);

            Assert.Equal(new[] { "总分", "语文" }, report.Sheets.Select(s => s.Name).ToArray());
            Assert.DoesNotContain(report.Sheets, s => s.Subject is null);

            var headers = report.Sheets[0].Columns.Select(c => c.Header).ToList();
            Assert.Contains("[800,850]·人数", headers);
            Assert.Contains("[800,850]·占比%", headers);
            Assert.Equal(2, report.Sheets[0].Rows.Count);
            Assert.Equal("3", report.Sheets[0].Rows[0][1]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_GradeLevels_SplitsStudentDetail()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("学业等级分布");
                sheet.Cell(1, 1).Value = "学业等级分布--某某中学";

                var header = new[] { "班级", "等级", "人数", "占比%" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                var grades = new[] { ("A", 5d, 10d), ("B", 8d, 16d), ("C", 10d, 20d), ("D", 12d, 24d), ("E", 15d, 30d) };
                for (var i = 0; i < grades.Length; i++)
                {
                    var row = 3 + i;
                    sheet.Cell(row, 1).Value = "九1班";
                    sheet.Cell(row, 2).Value = grades[i].Item1;
                    sheet.Cell(row, 3).Value = grades[i].Item2;
                    sheet.Cell(row, 4).Value = grades[i].Item3;
                }

                sheet.Cell(9, 1).Value = "学生等级分布详情";

                sheet.Cell(10, 1).Value = "姓名";
                sheet.Cell(10, 2).Value = "得分";
                sheet.Cell(11, 1).Value = "张三";
                sheet.Cell(11, 2).Value = 95d;
                sheet.Cell(12, 1).Value = "李四";
                sheet.Cell(12, 2).Value = 92d;

                sheet.Cell(13, 1).Value = "姓名";
                sheet.Cell(13, 2).Value = "得分";
                sheet.Cell(14, 1).Value = "王五";
                sheet.Cell(14, 2).Value = 88d;

                path = Save(workbook, dir, "学业等级分布.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.GradeLevels, report.Kind);
            Assert.Equal(2, report.Sheets.Count);

            var detail = report.Sheets[1];
            Assert.Contains("学生等级明细", detail.Name);
            Assert.Contains("姓名", detail.Columns.Select(c => c.Header));
            Assert.Contains("得分", detail.Columns.Select(c => c.Header));

            var values = detail.Rows.SelectMany(r => r).ToList();
            Assert.Contains("张三", values);
            Assert.Contains("李四", values);
            Assert.Contains("王五", values);

            // 上半部分的等级分布仍保留 A–E 五行
            Assert.Equal(5, report.Sheets[0].Rows.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_QuestionAnalysis_KeepsClassColumns()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("小题分析");
                sheet.Cell(1, 1).Value = "全部班级数学-小题分析";

                var group = new[] { "序号", "题号", "题型", "分值", "难度", "区分度", "年级", "", "九年级1班", "" };
                for (var c = 0; c < group.Length; c++)
                    if (group[c].Length > 0) sheet.Cell(2, c + 1).Value = group[c];
                sheet.Range(2, 7, 2, 8).Merge();
                sheet.Range(2, 9, 2, 10).Merge();

                sheet.Cell(3, 7).Value = "均分";
                sheet.Cell(3, 8).Value = "得分率%";
                sheet.Cell(3, 9).Value = "均分";
                sheet.Cell(3, 10).Value = "得分率%";

                sheet.Cell(4, 1).Value = 1d;
                sheet.Cell(4, 2).Value = "1";
                sheet.Cell(4, 3).Value = "选择题";
                sheet.Cell(4, 4).Value = 3d;
                sheet.Cell(4, 5).Value = 0.75d;
                sheet.Cell(4, 6).Value = 0.42d;
                sheet.Cell(4, 7).Value = 3.2d;
                sheet.Cell(4, 8).Value = 65d;
                sheet.Cell(4, 9).Value = 3.5d;
                sheet.Cell(4, 10).Value = 70d;

                path = Save(workbook, dir, "全部班级数学-小题分析.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.QuestionAnalysis, report.Kind);

            var sheet2 = Assert.Single(report.Sheets);
            var headers = sheet2.Columns.Select(c => c.Header).ToList();
            Assert.Contains("年级·均分", headers);
            Assert.Contains("年级·得分率%", headers);
            Assert.Contains("九年级1班·均分", headers);
            Assert.Contains("九年级1班·得分率%", headers);
            Assert.Equal(10, sheet2.Columns.Count);
            Assert.Single(sheet2.Rows);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_BorderlineRoster_KeepsRows()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("临界生名单");
                sheet.Cell(1, 1).Value = "语文临界生对比--某某中学";

                var header = new[] { "序号", "班级", "姓名", "总分" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                sheet.Cell(3, 1).Value = 1d;
                sheet.Cell(3, 2).Value = "九1班";
                sheet.Cell(3, 3).Value = "张三";
                sheet.Cell(3, 4).Value = 95d;

                sheet.Cell(4, 1).Value = 2d;
                sheet.Cell(4, 2).Value = "九1班";
                sheet.Cell(4, 3).Value = "李四";
                sheet.Cell(4, 4).Value = 92d;

                sheet.Cell(5, 1).Value = 3d;
                sheet.Cell(5, 2).Value = "九2班";
                sheet.Cell(5, 3).Value = "王五";
                sheet.Cell(5, 4).Value = 88d;

                path = Save(workbook, dir, "语文临界生对比.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.BorderlineRoster, report.Kind);

            var sheet2 = Assert.Single(report.Sheets);
            Assert.Equal(3, sheet2.Rows.Count);
            Assert.Equal("张三", sheet2.Rows[0][2]);
            Assert.Equal("王五", sheet2.Rows[2][2]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_AbsenceRoster_KeepsRows()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("缺考名单");
                sheet.Cell(1, 1).Value = "缺考学生汇总--某某中学";

                var header = new[] { "准考证号", "姓名", "班级", "缺考科目" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                sheet.Cell(3, 1).Value = 20260001d;
                sheet.Cell(3, 2).Value = "张三";
                sheet.Cell(3, 3).Value = "九1班";
                sheet.Cell(3, 4).Value = "语文";

                sheet.Cell(4, 1).Value = 20260002d;
                sheet.Cell(4, 2).Value = "李四";
                sheet.Cell(4, 3).Value = "九2班";
                sheet.Cell(4, 4).Value = "数学";

                path = Save(workbook, dir, "缺考学生汇总.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.AbsenceRoster, report.Kind);

            var sheet2 = Assert.Single(report.Sheets);
            Assert.Equal(2, sheet2.Rows.Count);
            Assert.Equal("20260001", sheet2.Rows[0][0]);
            Assert.Equal("张三", sheet2.Rows[0][1]);
            Assert.Equal("语文", sheet2.Rows[0][3]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DetectKind_ScoreSheet_ReturnsGeneric()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("成绩汇总");
                sheet.Cell(1, 1).Value = "全部考生成绩汇总";

                var header = new[] { "准考证号", "班级", "姓名", "语文", "数学" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                sheet.Cell(3, 1).Value = 20260001d;
                sheet.Cell(3, 2).Value = "九1班";
                sheet.Cell(3, 3).Value = "张三";
                sheet.Cell(3, 4).Value = 88d;
                sheet.Cell(3, 5).Value = 95d;

                path = Save(workbook, dir, "全部考生成绩汇总.xlsx");
            }

            Assert.False(OverviewReportImporter.IsReportFile(path));
            Assert.Empty(OverviewReportImporter.Load(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ChoiceDistribution_BlankFirstRow_DetectsRealHeaderAndSkipsAnswerRow()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("九A1客观题填涂");
                // 表头上方留一行空行：曾被误判成表头，导致真实表头变成第一行数据
                sheet.Cell(1, 1).Value = " ";
                sheet.Cell(2, 1).Value = "准考证号";
                sheet.Cell(2, 2).Value = "姓名";
                sheet.Cell(2, 3).Value = "科目";
                sheet.Cell(2, 4).Value = "客观分";
                sheet.Cell(2, 5).Value = 1d;
                sheet.Cell(2, 6).Value = 2d;

                sheet.Cell(3, 1).Value = "客观题答案";
                sheet.Cell(3, 3).Value = "数学";
                sheet.Cell(3, 4).Value = 30d;
                sheet.Cell(3, 5).Value = "B";
                sheet.Cell(3, 6).Value = "B";

                sheet.Cell(4, 1).Value = 20260001d;
                sheet.Cell(4, 2).Value = "张三";
                sheet.Cell(4, 3).Value = "数学";
                sheet.Cell(4, 4).Value = 30d;
                sheet.Cell(4, 5).Value = "B";
                sheet.Cell(4, 6).Value = "B";

                sheet.Cell(5, 1).Value = 20260002d;
                sheet.Cell(5, 2).Value = "李四";
                sheet.Cell(5, 3).Value = "数学";
                sheet.Cell(5, 4).Value = 27d;
                sheet.Cell(5, 5).Value = "B";
                sheet.Cell(5, 6).Value = "C";

                path = Save(workbook, dir, "学生答题明细_九年级A1.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            Assert.Equal(OverviewReportKind.ChoiceDistribution, report.Kind);

            var sheet2 = Assert.Single(report.Sheets);
            var headers = sheet2.Columns.Select(c => c.Header).ToList();
            Assert.Equal(new[] { "准考证号", "姓名", "科目", "客观分", "1", "2" }, headers);
            Assert.DoesNotContain("项目", headers);
            Assert.Equal(2, sheet2.Rows.Count);
            Assert.Equal("张三", sheet2.Rows[0][1]);
            Assert.DoesNotContain(sheet2.Rows.SelectMany(r => r), v => v.Contains("客观题答案"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ChoiceDistribution_FillsGroupedQuestionRows()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("【学生明细 - 题号排序】");
                sheet.Cell(1, 1).Value = "教学班报告--某某中学（数学）--九A1";
                var header = new[] { "题号", "题型", "年级得分率", "班级得分率", "答对人数", "答错人数", "答错学生名单" };
                for (var c = 0; c < header.Length; c++) sheet.Cell(2, c + 1).Value = header[c];

                sheet.Cell(3, 1).Value = 1d;
                sheet.Cell(3, 2).Value = "单选题";
                sheet.Cell(3, 3).Value = "87.65%";
                sheet.Cell(3, 4).Value = "100%";
                sheet.Cell(3, 5).Value = "B：50";
                sheet.Cell(3, 6).Value = "A：0";
                sheet.Cell(3, 7).Value = "无";

                // 同一道题的第二个选项行：题号/题型/得分率/答对人数留空
                sheet.Cell(4, 6).Value = "C：0";
                sheet.Cell(4, 7).Value = "无";

                sheet.Cell(5, 1).Value = 2d;
                sheet.Cell(5, 2).Value = "单选题";
                sheet.Cell(5, 3).Value = "92.96%";
                sheet.Cell(5, 4).Value = "98%";
                sheet.Cell(5, 5).Value = "B：49";
                sheet.Cell(5, 6).Value = "A：1";
                sheet.Cell(5, 7).Value = "彭祈力";

                path = Save(workbook, dir, "学生答题明细_题号排序.xlsx");
            }

            var report = Assert.Single(OverviewReportImporter.Load(path));
            var sheet2 = Assert.Single(report.Sheets);
            Assert.Equal(3, sheet2.Rows.Count);
            Assert.Equal("1", sheet2.Rows[1][0]);
            Assert.Equal("单选题", sheet2.Rows[1][1]);
            Assert.Equal("87.65%", sheet2.Rows[1][2]);
            Assert.Equal("B：50", sheet2.Rows[1][4]);
            Assert.Equal("2", sheet2.Rows[2][0]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_MixedWorkbook_ExtractsReportSheetsOnly()
    {
        var dir = NewTempDir();
        try
        {
            string path;
            using (var workbook = new XLWorkbook())
            {
                // 第一张：成绩数据（数据概览用）
                var grade = workbook.AddWorksheet("成绩汇总");
                grade.Cell(1, 1).Value = "全部考生成绩汇总--某某中学";
                var gradeHeader = new[] { "姓名", "语文", "数学" };
                for (var c = 0; c < gradeHeader.Length; c++) grade.Cell(2, c + 1).Value = gradeHeader[c];
                grade.Cell(3, 1).Value = "张三";
                grade.Cell(3, 2).Value = 90d;
                grade.Cell(3, 3).Value = 85d;
                grade.Cell(4, 1).Value = "李四";
                grade.Cell(4, 2).Value = 88d;
                grade.Cell(4, 3).Value = 92d;

                // 第二张：分析报表
                var report = workbook.AddWorksheet("班级成绩对比");
                report.Cell(1, 1).Value = "班级成绩对比--某某中学";
                report.Cell(2, 1).Value = "班级";
                report.Cell(2, 2).Value = "数学";
                report.Cell(3, 1).Value = "九1班";
                report.Cell(3, 2).Value = 88.5d;

                path = Save(workbook, dir, "成绩汇总与班级对比.xlsx");
            }

            // 成绩通道：能读到学生数据
            var exam = GradeSheetImporter.Load(path);
            Assert.Equal(2, exam.Students.Count);
            Assert.Equal(new[] { "语文", "数学" }, exam.Subjects);

            // 报表通道：只提取报表页，不把成绩表当报表
            var reports = OverviewReportImporter.Load(path);
            var report1 = Assert.Single(reports);
            Assert.Equal(OverviewReportKind.ClassComparison, report1.Kind);
            var sheet = Assert.Single(report1.Sheets);
            Assert.Equal("班级成绩对比", sheet.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
    [Fact]
    public void Load_AllSheets_RowsMatchColumnCount()
    {
        var dir = NewTempDir();
        try
        {
            var path = CreateScoreSegmentReport(dir);

            var report = Assert.Single(OverviewReportImporter.Load(path));

            Assert.Equal(2, report.Sheets.Count);
            foreach (var sheet in report.Sheets)
            {
                Assert.NotEmpty(sheet.Columns);
                foreach (var row in sheet.Rows)
                {
                    Assert.Equal(sheet.Columns.Count, row.Count);
                }
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
