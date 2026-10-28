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
            // 优先认表头；认不到时按"学号 + 姓名"这种无表头名单推断列（真实花名册常见形态）。
            var header = TryScanHeader(sheet) ?? TryInferColumns(sheet);
            if (header is null) continue;

            // 标题行（如“七年级1班花名册”“八（6）班学生名单”）不做表头识别，但要从中取班级——
            // 每班一份花名册时，名单条目靠它归班。
            var title = CleanCell(sheet, 1, 1);
            var inferred = InferClassName(title);
            if (inferred.Length == 0)
                inferred = InferClassName(Path.GetFileNameWithoutExtension(path));

            return Read(sheet, header.Value, title, inferred);
        }

        throw new InvalidDataException(
            "没有在名单里找到「姓名」列：请确认名单里有“姓名”表头，"
            + "或像“学号 + 姓名”那样两列直接排数据。");
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

            // 有性别列但这一行空着：逐行提示；整份没有性别列则只在末尾汇总一条，避免几十条噪音。
            if (gender.Length == 0 && header.GenderColumn > 0)
                issues.Add(new RegistrationIssue($"{name} 没有可识别的性别，录入后请在表格里确认。", false));

            entries.Add(new RosterEntry(name, gender, className));
        }

        if (header.GenderColumn == 0 && entries.Count > 0)
            issues.Add(new RegistrationIssue(
                "名单里没有性别列，录入后已全部按「男子」处理，请在表格里逐个改成正确性别。", false));

        return new RosterImportResult
        {
            Entries = entries,
            Issues = issues,
            Title = title,
            InferredClassName = inferredClassName,
        };
    }

    /// <summary>
    /// 无表头名单的列推断：在前 6 列里找"连续 3 行以上都是 2–6 个汉字"的列当姓名列
    /// （真实花名册常见「学号 + 姓名」两列、首行只有标题）。同时尝试识别性别列与班级列。
    /// 返回的表头行 = 数据首行 - 1，让上层的数据行遍历逻辑保持一致。
    /// </summary>
    private static HeaderPositions? TryInferColumns(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2) return null;

        var bestColumn = 0;
        var bestFirstRow = 0;
        var bestScore = 0;

        for (var column = 1; column <= 6; column++)
        {
            var firstRow = 0;
            var score = 0;
            for (var row = 1; row <= Math.Min(lastRow, HeaderScanRows); row++)
            {
                var text = CleanCell(sheet, row, column);
                if (IsLikelyName(text))
                {
                    if (firstRow == 0) firstRow = row;
                    score++;
                }
                else if (firstRow > 0)
                {
                    break; // 只认连续段
                }
            }

            // 同分时保留更靠左的列：学号在左、姓名在其右，而“民族/备注”等干扰列通常更靠右。
            if (score >= 3 && score > bestScore)
            {
                bestColumn = column;
                bestFirstRow = firstRow;
                bestScore = score;
            }
        }

        if (bestColumn == 0) return null;

        var genderColumn = 0;
        for (var column = 1; column <= 8 && genderColumn == 0; column++)
        {
            if (column == bestColumn) continue;
            var male = 0;
            var female = 0;
            var other = 0;
            for (var row = bestFirstRow; row <= Math.Min(lastRow, bestFirstRow + 9); row++)
            {
                var text = CleanCell(sheet, row, column);
                if (text.Length == 0) continue;
                var gender = NormalizeGender(text);
                if (gender == "男子") male++;
                else if (gender == "女子") female++;
                else other++;
            }

            if (male + female >= 3 && other == 0) genderColumn = column;
        }

        var classColumn = 0;
        for (var column = 1; column <= 8 && classColumn == 0; column++)
        {
            if (column == bestColumn || column == genderColumn) continue;
            var hits = 0;
            var misses = 0;
            for (var row = bestFirstRow; row <= Math.Min(lastRow, bestFirstRow + 9); row++)
            {
                var text = CleanCell(sheet, row, column);
                if (text.Length == 0) continue;
                if (text.Contains("班", StringComparison.Ordinal)) hits++;
                else misses++;
            }

            if (hits >= 3 && misses == 0) classColumn = column;
        }

        return new HeaderPositions(bestFirstRow - 1, bestColumn, genderColumn, classColumn);
    }

    /// <summary>像人名：2–6 个汉字（允许间隔号），且不是表头词。</summary>
    private static bool IsLikelyName(string text)
    {
        if (text.Length < 2 || text.Length > 6) return false;

        var name = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (name is "姓名" or "学生" or "学生姓名" or "名字" or "备注" or "家长" or "班主任") return false;

        foreach (var ch in name)
        {
            var isHan = ch is >= '\u4e00' and <= '\u9fa5';
            if (!isHan && ch != '·') return false;
        }

        return true;
    }

    /// <summary>从标题或文件名里推断班级名：先认本项目的年级班写法（七年级1班 / 八（6）班 / 高一年级3班），
    /// 再兜底取“以班结尾、前面不超过 8 个非空白字符”的片段。</summary>
    private static string InferClassName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var exact = Regex.Match(text, @"(?<name>[七八九]年级\d+班|高[一二三]年级\d+班)");
        if (exact.Success) return exact.Groups["name"].Value;

        // 八（6）班 / 九(12)班 —— 与 GradeWeave 的班级命名一致，统一成全角括号。
        var bracket = Regex.Match(text, @"(?<name>[一二三四五六七八九][（(]\d{1,2}[)）]班)");
        if (bracket.Success)
            return bracket.Groups["name"].Value
                .Replace("(", "（", StringComparison.Ordinal)
                .Replace(")", "）", StringComparison.Ordinal);

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
