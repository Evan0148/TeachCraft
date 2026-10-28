using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace MeetRoster.Core.Import;

/// <summary>名单里的一名学生（花名册条目）。</summary>
public sealed record RosterEntry(string Name, string Gender, string ClassName);

/// <summary>名单导入结果。</summary>
public sealed class RosterImportResult
{
    public required IReadOnlyList<RosterEntry> Entries { get; init; }
    public required IReadOnlyList<RegistrationIssue> Issues { get; init; }
    public string Title { get; init; } = string.Empty;

    /// <summary>从标题首格或文件名推断出的班级（如“七年级1班花名册”→ 七年级1班）；推不出为空。</summary>
    public string InferredClassName { get; init; } = string.Empty;
}

/// <summary>
/// 读取学生名单（花名册），供报名表的「添加运动员」按名单挑选。
/// 表头需包含「姓名」（兼容"学生姓名 / 名字 / 姓 名"），可选「性别」「班级」列；
/// 性别列缺失时留空，由调用方按默认值录入后再让教师改。
/// </summary>
public static class RosterNameImporter
{
    private const int HeaderScanRows = 15;
    private const int HeaderScanColumns = 8;

    public static RosterImportResult Import(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("找不到名单文件：" + path);

        using var stream = WorkbookIO.OpenReadShared(path);
        using var workbook = WorkbookIO.OpenWorkbook(path, stream);

        foreach (var sheet in workbook.Worksheets)
        {
            var scan = TryScanHeader(sheet);
            if (scan is null) continue;

            // 标题行（如“七年级1班花名册”）不做表头识别，但要从中取班级——
            // 每班一份花名册时，名单条目靠它归班。
            var title = CleanCell(sheet, 1, 1);
            var inferred = InferClassName(title);
            if (inferred.Length == 0)
                inferred = InferClassName(Path.GetFileNameWithoutExtension(path));

            return Read(sheet, scan.Value, title, inferred);
        }

        throw new InvalidDataException(
            "没有在名单里找到「姓名」表头：请确认名单的前几行包含“姓名”列（可选“性别”“班级”列）。");
    }

    private static RosterImportResult Read(IXLWorksheet sheet, HeaderPositions header,
        string title, string inferredClassName)
    {
        var entries = new List<RosterEntry>();
        var issues = new List<RegistrationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? header.Row;

        for (var row = header.Row + 1; row <= lastRow; row++)
        {
            var name = CleanCell(sheet, row, header.NameColumn);
            if (name.Length == 0) continue;
            if (name.StartsWith("例", StringComparison.Ordinal)) continue;
            if (name is "姓名" or "学生姓名") continue;

            var gender = header.GenderColumn > 0
                ? NormalizeGender(CleanCell(sheet, row, header.GenderColumn))
                : string.Empty;
            var className = header.ClassColumn > 0
                ? CleanCell(sheet, row, header.ClassColumn)
                : string.Empty;
            // 没有班级列（或该行没填）时，用标题/文件名推断的班级补全。
            if (className.Length == 0) className = inferredClassName;

            if (!seen.Add($"{className}\u0001{name}"))
            {
                issues.Add(new RegistrationIssue($"{name} 在名单里出现了多次，已保留第一条。", false));
                continue;
            }

            if (gender.Length == 0)
                issues.Add(new RegistrationIssue($"{name} 没有可识别的性别，录入后请在表格里确认。", false));

            entries.Add(new RosterEntry(name, gender, className));
        }

        return new RosterImportResult
        {
            Entries = entries,
            Issues = issues,
            Title = title,
            InferredClassName = inferredClassName,
        };
    }

    /// <summary>
    /// 从标题或文件名里推断班级名：先认本项目的年级班写法（七年级1班 / 高一年级3班），
    /// 再兜底取“以班结尾、前面不超过 8 个非空白字符”的片段（如“XX班花名册”→“XX班”）。
    /// </summary>
    private static string InferClassName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var exact = Regex.Match(text, @"(?<name>[七八九]年级\d+班|高[一二三]年级\d+班)");
        if (exact.Success) return exact.Groups["name"].Value;

        var loose = Regex.Match(text, @"(?<name>[^\s，,。:：、()（）]{1,8}班)");
        return loose.Success ? loose.Groups["name"].Value : string.Empty;
    }

    private static HeaderPositions? TryScanHeader(IXLWorksheet sheet)
    {
        var maxRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? HeaderScanRows, HeaderScanRows);
        for (var row = 1; row <= maxRow; row++)
        {
            var nameColumn = 0;
            var genderColumn = 0;
            var classColumn = 0;

            for (var column = 1; column <= HeaderScanColumns; column++)
            {
                var text = CleanCell(sheet, row, column);
                if (text.Length == 0) continue;

                var compact = text.Replace(" ", "", StringComparison.Ordinal);
                if (nameColumn == 0 && compact is "姓名" or "学生姓名" or "名字" or "学生")
                    nameColumn = column;
                else if (genderColumn == 0 && compact is "性别" or "男女")
                    genderColumn = column;
                else if (classColumn == 0 && compact is "班级" or "班别" or "所在班级")
                    classColumn = column;
            }

            if (nameColumn > 0)
                return new HeaderPositions(row, nameColumn, genderColumn, classColumn);
        }

        return null;
    }

    private static string CleanCell(IXLWorksheet sheet, int row, int column) =>
        (sheet.Cell(row, column).GetString() ?? string.Empty)
        .Replace("\u3000", " ", StringComparison.Ordinal)
        .Trim();

    /// <summary>名单里的性别归一：男/男生/男子 → 男子；女/女生/女子 → 女子；其他留空由教师确认。</summary>
    private static string NormalizeGender(string value)
    {
        var text = value.Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0) return string.Empty;
        if (text.Contains("女", StringComparison.Ordinal)) return "女子";
        if (text.Contains("男", StringComparison.Ordinal)) return "男子";
        return string.Empty;
    }

    private readonly record struct HeaderPositions(int Row, int NameColumn, int GenderColumn, int ClassColumn);
}
