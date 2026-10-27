using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Media;
using GradeStrip.Core.Model;

namespace GradeStrip.App.ViewModels;

/// <summary>分析报表卡片的布局常量：首列稍宽，其余等宽。</summary>
public static class ReportLayout
{
    public const double FirstColumnWidth = 150;
    public const double ColumnWidth = 104;

    /// <summary>数值列宽度：容纳「九年级A1·得分率%」这类分组表头单行显示，避免表头被迫换行抬高整行。</summary>
    public const double NumericColumnWidth = 120;

    /// <summary>长文本列（如答错学生名单）允许往右延伸到的上限；再长就交给单元格换行。</summary>
    public const double WideColumnWidth = 900;
}

/// <summary>报表切换 chip：一份报表对应一个 chip。</summary>
public sealed class ReportCardItem
{
    public OverviewReport Report { get; init; } = new();
    public string KindLabel => Report.KindLabel;
    public string Title => Report.Title;
    public string SourceFile => Report.SourceFile;
    public bool IsActive { get; init; }
    public ICommand? Activate { get; init; }
}

/// <summary>表名切换 chip：同一份报表内的工作表。</summary>
public sealed class ReportSheetItem
{
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public ICommand? Activate { get; init; }
}

/// <summary>表头单元格：宽度按列序预计算，数值列右对齐。</summary>
public sealed class ReportColumnItem
{
    public string Header { get; init; } = string.Empty;
    public double Width { get; init; } = ReportLayout.ColumnWidth;
    public bool IsNumeric { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
}

/// <summary>数据单元格，宽度与所在列一致。</summary>
public sealed class ReportCellItem
{
    public string Text { get; init; } = string.Empty;
    public double Width { get; init; } = ReportLayout.ColumnWidth;
    public bool IsNumeric { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
}

/// <summary>报表一行数据。</summary>
public sealed class ReportRowItem
{
    public IReadOnlyList<ReportCellItem> Cells { get; init; } = new List<ReportCellItem>();
}
