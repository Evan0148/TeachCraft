using System.Text.RegularExpressions;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Import;

/// <summary>
/// 多班级报表的班级筛选：识别报表中的班级列/班级分组列，按班级裁剪行或列。
/// 「全年级」表示不裁剪。
/// </summary>
public static class OverviewReportFilter
{
    public const string AllClasses = "全年级";

    private static readonly string[] SummaryValues =
    {
        "全部班级", "全部", "合计", "总计", "年级", "平均", "全校",
    };

    /// <summary>收集报表里出现过的班级名（不含「全年级」），按自然顺序排序。</summary>
    public static IReadOnlyList<string> GetClasses(OverviewReport report)
    {
        var classes = new List<string>();
        foreach (var sheet in report.Sheets)
        {
            var classColumn = FindClassColumn(sheet);
            if (classColumn >= 0)
            {
                foreach (var row in sheet.Rows)
                {
                    if (classColumn >= row.Count) continue;
                    AddClass(classes, row[classColumn]);
                }
            }

            foreach (var column in sheet.Columns)
            {
                var prefix = ClassPrefix(column.Header);
                if (prefix is not null) AddClass(classes, prefix);
            }
        }

        return classes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ClassSortKey)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>按班级裁剪报表；className 为 null/「全年级」时原样返回。</summary>
    public static OverviewReport Filter(OverviewReport report, string? className)
    {
        if (string.IsNullOrWhiteSpace(className) || className == AllClasses)
            return report;

        var sheets = new List<OverviewSheet>(report.Sheets.Count);
        foreach (var sheet in report.Sheets)
            sheets.Add(FilterSheet(sheet, className));

        return new OverviewReport
        {
            Kind = report.Kind,
            KindLabel = report.KindLabel,
            Title = report.Title,
            SourceFile = report.SourceFile,
            ExamName = report.ExamName,
            Note = report.Note,
            Sheets = sheets,
        };
    }

    private static OverviewSheet FilterSheet(OverviewSheet sheet, string className)
    {
        var classColumn = FindClassColumn(sheet);
        var hasClassPrefix = sheet.Columns.Any(c => ClassPrefix(c.Header) is not null);

        if (classColumn < 0 && !hasClassPrefix) return sheet;

        var columns = sheet.Columns;
        var rows = sheet.Rows;

        // 行筛选：班级列；空单元格沿用上一行的班级（合并单元格常见）。
        if (classColumn >= 0)
        {
            var kept = new List<IReadOnlyList<string>>();
            var current = string.Empty;
            foreach (var row in rows)
            {
                var cell = classColumn < row.Count ? row[classColumn].Trim() : string.Empty;
                if (cell.Length > 0)
                    current = IsSummaryValue(cell) ? string.Empty : cell;

                if (current == className) kept.Add(row);
            }
            rows = kept;
        }

        // 列筛选：保留没有班级前缀的公共列，以及属于所选班级的分组列。
        if (hasClassPrefix)
        {
            var keep = new List<int>();
            for (var c = 0; c < columns.Count; c++)
            {
                var prefix = ClassPrefix(columns[c].Header);
                if (prefix is null || prefix == className) keep.Add(c);
            }

            columns = keep.Select(c => columns[c]).ToList();
            rows = rows.Select(row =>
            {
                var values = new string[keep.Count];
                for (var i = 0; i < keep.Count; i++)
                    values[i] = keep[i] < row.Count ? row[keep[i]] : string.Empty;
                return (IReadOnlyList<string>)values;
            }).ToList();
        }

        return new OverviewSheet
        {
            Name = sheet.Name,
            Subject = sheet.Subject,
            Note = sheet.Note,
            Columns = columns,
            Rows = rows,
        };
    }

    /// <summary>表头含「班级」的列；没有时看首列值是否多数像班级名。</summary>
    private static int FindClassColumn(OverviewSheet sheet)
    {
        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            var header = sheet.Columns[c].Header.Replace(" ", string.Empty);
            if (header.Contains("班级", StringComparison.Ordinal)) return c;
        }

        if (sheet.Columns.Count == 0) return -1;

        var values = sheet.Rows
            .Select(r => r.Count > 0 ? r[0].Trim() : string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

        if (values.Count == 0) return -1;

        var classLike = values.Count(v => IsClassValue(v) || IsSummaryValue(v));
        return classLike * 10 >= values.Count * 6 ? 0 : -1;
    }

    private static void AddClass(List<string> classes, string value)
    {
        value = value.Trim();
        if (!IsClassValue(value)) return;
        if (!classes.Contains(value, StringComparer.Ordinal)) classes.Add(value);
    }

    private static bool IsSummaryValue(string value) =>
        SummaryValues.Contains(value, StringComparer.Ordinal);

    /// <summary>九年级1班 / 九A1 / 九年级A12 等；排除「全部班级」等汇总行。</summary>
    private static bool IsClassValue(string value)
    {
        if (value.Length is < 2 or > 12) return false;
        if (IsSummaryValue(value)) return false;
        if (value.EndsWith("班", StringComparison.Ordinal)) return true;
        return Regex.IsMatch(value, @"^(九|八|七|高[一二三]|初[一二三])(年级)?[A-Za-z]?\d+$");
    }

    /// <summary>从「九年级A1·均分」这类分组表头里取出班级前缀。</summary>
    private static string? ClassPrefix(string header)
    {
        var index = header.IndexOf('·');
        if (index <= 0) return null;
        var prefix = header[..index].Trim();
        return IsClassValue(prefix) ? prefix : null;
    }

    private static int ClassSortKey(string value)
    {
        var match = Regex.Match(value, @"\d+");
        return match.Success && int.TryParse(match.Value, out var number) ? number : int.MaxValue;
    }

    /// <summary>
    /// 把班级名归一为可比对的键：「九年级6班 / 九6班」→「九6班」，「九年级A1 / 九A1」→「九A1」。
    /// 用于让单班成绩表的 ClassName 与报表里的班级互相匹配。
    /// </summary>
    public static string NormalizeClassName(string value) =>
        value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty)
             .Replace("年级", string.Empty, StringComparison.Ordinal);
}
