using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

/// <summary>多班级报表的班级识别与筛选：行式班级列、列式「班级·子列」。</summary>
public class OverviewReportFilterTests
{
    private static OverviewReport BuildReport(params OverviewSheet[] sheets) => new()
    {
        Kind = OverviewReportKind.Generic,
        KindLabel = "通用表格",
        Title = "测试报表",
        SourceFile = "测试报表.xlsx",
        Sheets = sheets,
    };

    private static OverviewSheet BuildSheet(string name, string[] headers, params string[][] rows) => new()
    {
        Name = name,
        Columns = headers.Select(h => new OverviewColumn { Header = h }).ToList(),
        Rows = rows.Select(r => (IReadOnlyList<string>)r).ToList(),
    };

    [Fact]
    public void GetClasses_CollectsFromClassColumnAndGroupHeaders_SortedNaturally()
    {
        var report = BuildReport(
            BuildSheet("班级成绩对比",
                new[] { "班级", "数学·得分" },
                new[] { "全部班级", "80" },
                new[] { "九年级10班", "75" },
                new[] { "九年级2班", "78" },
                new[] { "九年级A1", "90" }),
            BuildSheet("小题分析",
                new[] { "题号", "年级·均分", "九年级A1·均分", "九年级2班·均分" },
                new[] { "1", "2.6", "3", "2.4" }));

        var classes = OverviewReportFilter.GetClasses(report);

        Assert.Equal(new[] { "九年级A1", "九年级2班", "九年级10班" }, classes);
    }

    [Fact]
    public void Filter_RowReport_KeepsOnlySelectedClass()
    {
        var report = BuildReport(
            BuildSheet("班级成绩对比",
                new[] { "班级", "数学·得分" },
                new[] { "全部班级", "80" },
                new[] { "九年级1班", "76" },
                new[] { "九年级2班", "84" }));

        var filtered = OverviewReportFilter.Filter(report, "九年级1班");
        var sheet = Assert.Single(filtered.Sheets);

        var row = Assert.Single(sheet.Rows);
        Assert.Equal("九年级1班", row[0]);
        Assert.Equal("76", row[1]);
    }

    [Fact]
    public void Filter_ColumnReport_KeepsGlobalAndSelectedClassColumns()
    {
        var report = BuildReport(
            BuildSheet("小题分析",
                new[] { "题号", "年级·均分", "九年级1班·均分", "九年级2班·均分" },
                new[] { "1", "2.6", "3", "2.4" },
                new[] { "2", "2.1", "2.5", "1.8" }));

        var filtered = OverviewReportFilter.Filter(report, "九年级1班");
        var sheet = Assert.Single(filtered.Sheets);

        Assert.Equal(new[] { "题号", "年级·均分", "九年级1班·均分" },
            sheet.Columns.Select(c => c.Header).ToArray());
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(new[] { "1", "2.6", "3" }, sheet.Rows[0]);
        Assert.Equal(new[] { "2", "2.1", "2.5" }, sheet.Rows[1]);
    }

    [Fact]
    public void Filter_AllClasses_ReturnsWholeReport()
    {
        var report = BuildReport(
            BuildSheet("班级成绩对比",
                new[] { "班级", "数学·得分" },
                new[] { "九年级1班", "76" }));

        var filtered = OverviewReportFilter.Filter(report, OverviewReportFilter.AllClasses);

        Assert.Same(report, filtered);
    }

    [Fact]
    public void GetClasses_NoClassColumn_ReturnsEmpty()
    {
        var report = BuildReport(
            BuildSheet("学科成绩对比",
                new[] { "学科", "实考人数", "均分" },
                new[] { "语文", "960", "74.5" }));

        Assert.Empty(OverviewReportFilter.GetClasses(report));

        // 无班级维度的工作表原样返回，不裁剪行列
        var filtered = OverviewReportFilter.Filter(report, "九年级1班");
        Assert.Same(report.Sheets[0], Assert.Single(filtered.Sheets));
    }

    [Fact]
    public void NormalizeClassName_MatchesGradeSheetAndReportSpellings()
    {
        Assert.Equal(
            OverviewReportFilter.NormalizeClassName("九A1"),
            OverviewReportFilter.NormalizeClassName("九年级A1"));
        Assert.Equal(
            OverviewReportFilter.NormalizeClassName("九6班"),
            OverviewReportFilter.NormalizeClassName("九年级6班"));
        Assert.NotEqual(
            OverviewReportFilter.NormalizeClassName("九A1"),
            OverviewReportFilter.NormalizeClassName("九A2"));
    }
}
