using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using SeatGrid.Core;
using SeatGrid.Core.Models;

namespace SeatGrid.Core.Roster;

public sealed record RosterImportResult(
    string SourcePath,
    string SourceName,
    IReadOnlyList<Student> Students,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 通用名单识别：读取 xlsx / xls / csv，自动寻找姓名列、性别列，
/// 并尽量提取学号、身高、视力、成绩、原小组、备注。
/// </summary>
public static class RosterImporter
{
    private const int MaxRows = 5000;
    private const int MaxColumns = 80;

    public static RosterImportResult Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new SeatGridException("找不到名单文件，请重新选择。");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DataSet dataSet;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = CreateReader(path, stream);
            dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false },
            });
        }
        catch (Exception ex)
        {
            throw new SeatGridException($"无法读取这份名单：{ex.Message}", ex);
        }

        SheetParseResult? best = null;
        var bestScore = int.MinValue;

        foreach (DataTable table in dataSet.Tables)
        {
            if (table.Rows.Count == 0) continue;

            var grid = ReadGrid(table);
            var parsed = ParseSheet(grid);
            var score = parsed.Students.Count * 10
                        + (parsed.HasHeader ? 5 : 0)
                        + (parsed.HasGender ? 3 : 0);

            if (score > bestScore)
            {
                bestScore = score;
                best = parsed;
            }
        }

        if (best is null || best.Students.Count == 0)
            throw new SeatGridException("没有在文件里找到学生姓名。请确认名单包含「姓名」列，或直接提供一列学生姓名。");

        var warnings = new List<string>(best.Warnings);
        var duplicates = best.Students
            .GroupBy(s => s.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            var sample = string.Join("、", duplicates.Take(3));
            warnings.Add($"发现 {duplicates.Count} 个重名（如 {sample}），已全部保留，可用学号区分。");
        }

        return new RosterImportResult(
            path,
            Path.GetFileName(path),
            best.Students,
            warnings);
    }

    private static IExcelDataReader CreateReader(string path, Stream stream)
    {
        var configuration = new ExcelReaderConfiguration
        {
            FallbackEncoding = Encoding.GetEncoding(936),
        };

        return string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)
            ? ExcelReaderFactory.CreateCsvReader(stream, configuration)
            : ExcelReaderFactory.CreateReader(stream, configuration);
    }

    private static List<string?[]> ReadGrid(DataTable table)
    {
        var rows = new List<string?[]>();
        var rowCount = Math.Min(table.Rows.Count, MaxRows);
        var columnCount = Math.Min(table.Columns.Count, MaxColumns);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new string?[columnCount];
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                row[columnIndex] = ToText(table.Rows[rowIndex][columnIndex]);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static SheetParseResult ParseSheet(List<string?[]> grid)
    {
        var columns = FindHeader(grid);
        var hasHeader = columns is not null;
        var map = columns ?? DetectColumns(grid);

        if (map.Name < 0)
            return new SheetParseResult(new List<Student>(), new List<string>(), false, false);

        var numericGender = IsNumericGenderColumn(grid, map.Gender, hasHeader ? 1 : 0);
        var students = new List<Student>();
        var warnings = new List<string>();
        var skipped = 0;
        var hasGender = false;
        var startRow = hasHeader ? map.HeaderRow + 1 : 0;

        for (var rowIndex = startRow; rowIndex < grid.Count; rowIndex++)
        {
            var row = grid[rowIndex];
            var rawName = Get(row, map.Name);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                skipped++;
                continue;
            }

            var name = CleanName(rawName);
            if (name is null)
            {
                skipped++;
                continue;
            }

            var gender = ParseGender(hasHeader || map.Gender >= 0 ? Get(row, map.Gender) : null, numericGender);
            if (gender == Gender.Unknown && TryExtractGenderFromName(ref name, out var nameGender))
                gender = nameGender;

            if (map.Name == map.Gender)
                gender = Gender.Unknown;

            if (gender != Gender.Unknown) hasGender = true;

            students.Add(new Student
            {
                Name = name,
                Gender = gender,
                Number = CleanText(Get(row, map.Number)),
                Height = ParseHeight(Get(row, map.Height)),
                Vision = ParseVision(Get(row, map.Vision)),
                Score = ParseNumber(Get(row, map.Score)),
                Notes = CleanText(Get(row, map.Notes)),
                OriginalGroup = CleanText(Get(row, map.Group)),
            });
        }

        if (!hasGender)
            warnings.Add("没有识别到有效的性别信息；女生标记与男女交替排座会退化为「未知」。");

        if (skipped > 0)
            warnings.Add($"跳过 {skipped} 行空姓名或表头行。");

        return new SheetParseResult(students, warnings, hasHeader, hasGender);
    }

    private static ColumnMap? FindHeader(List<string?[]> grid)
    {
        ColumnMap? best = null;
        var bestMatches = 0;
        var scanRows = Math.Min(20, grid.Count);

        for (var rowIndex = 0; rowIndex < scanRows; rowIndex++)
        {
            var map = MapHeader(grid[rowIndex], rowIndex);
            if (map.Name < 0) continue;

            if (map.Matched > bestMatches)
            {
                bestMatches = map.Matched;
                best = map;
            }
        }

        return best;
    }

    private static ColumnMap MapHeader(string?[] row, int rowIndex)
    {
        var map = new ColumnMap { HeaderRow = rowIndex };

        for (var column = 0; column < row.Length; column++)
        {
            var header = NormalizeHeader(row[column]);
            if (header.Length == 0) continue;

            if (map.Name < 0 && IsNameHeader(header))
            {
                map.Name = column;
                map.Matched++;
                continue;
            }

            if (map.Gender < 0 && header.Contains("性别"))
            {
                map.Gender = column;
                map.Matched++;
                continue;
            }

            if (map.Number < 0 && (header.Contains("学号") || header.Contains("编号") || header.Contains("考号") || header == "序号"))
            {
                map.Number = column;
                map.Matched++;
                continue;
            }

            if (map.Height < 0 && header.Contains("身高"))
            {
                map.Height = column;
                map.Matched++;
                continue;
            }

            if (map.Vision < 0 && (header.Contains("视力") || header.Contains("裸眼")))
            {
                map.Vision = column;
                map.Matched++;
                continue;
            }

            if (map.Score < 0 && (header.Contains("总分") || header == "成绩" || header.Contains("分数")))
            {
                map.Score = column;
                map.Matched++;
                continue;
            }

            if (map.Notes < 0 && (header.Contains("备注") || header.Contains("说明")))
            {
                map.Notes = column;
                map.Matched++;
                continue;
            }

            if (map.Group < 0 && (header.Contains("小组") || header.Contains("组别")))
            {
                map.Group = column;
                map.Matched++;
            }
        }

        return map;
    }

    private static ColumnMap DetectColumns(List<string?[]> grid)
    {
        var map = new ColumnMap { HeaderRow = -1 };
        var columnCount = grid.Count == 0 ? 0 : grid.Max(r => r.Length);
        var sample = grid.Take(120).ToList();

        var bestNameScore = 0;
        for (var column = 0; column < columnCount; column++)
        {
            var score = sample.Count(row => IsNameLike(Get(row, column)));
            if (score > bestNameScore)
            {
                bestNameScore = score;
                map.Name = column;
            }
        }

        if (map.Name < 0 || bestNameScore < 2) return map;

        var bestGenderScore = 0;
        for (var column = 0; column < columnCount; column++)
        {
            if (column == map.Name) continue;

            var values = sample.Select(row => Get(row, column)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (values.Count == 0) continue;

            var genderScore = values.Count(v => ParseGender(v, false) != Gender.Unknown);
            if (genderScore > bestGenderScore && genderScore >= Math.Max(2, values.Count * 0.3))
            {
                bestGenderScore = genderScore;
                map.Gender = column;
            }
        }

        var firstRow = sample.FirstOrDefault();
        for (var column = 0; column < columnCount; column++)
        {
            var header = NormalizeHeader(firstRow is null ? null : Get(firstRow, column));
            if (header.Length == 0) continue;

            if (map.Number < 0 && (header.Contains("学号") || header.Contains("编号") || header == "序号")) map.Number = column;
            else if (map.Height < 0 && header.Contains("身高")) map.Height = column;
            else if (map.Vision < 0 && header.Contains("视力")) map.Vision = column;
            else if (map.Score < 0 && (header.Contains("总分") || header.Contains("成绩"))) map.Score = column;
            else if (map.Notes < 0 && header.Contains("备注")) map.Notes = column;
            else if (map.Group < 0 && header.Contains("组")) map.Group = column;
        }

        return map;
    }

    private static bool IsNumericGenderColumn(List<string?[]> grid, int column, int startRow)
    {
        if (column < 0) return false;

        var values = new List<string>();
        for (var row = startRow; row < grid.Count && values.Count < 20; row++)
        {
            var value = NormalizeHeader(Get(grid[row], column));
            if (value is "0" or "1" or "0.0" or "1.0") values.Add(value);
        }

        return values.Count >= 2;
    }

    private static bool IsNameHeader(string header)
    {
        if (header.Contains("家长") || header.Contains("父亲") || header.Contains("母亲") || header.Contains("联系人"))
            return false;

        return header.Contains("姓名")
               || header == "名字"
               || header == "学生"
               || header == "名称"
               || header == "学生名单";
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = Regex.Replace(value.Trim(), @"[\s\u3000]+", "");
        return Regex.Replace(text, @"[（）()【】\[\]：:·.\-_/\\]", string.Empty).ToLowerInvariant();
    }

    private static string? CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var name = value.Trim().Replace('\u3000', ' ');
        name = Regex.Replace(name, @"\s+", " ");

        if (IsCjkNameWithSpaces(name))
            name = name.Replace(" ", string.Empty);

        if (name.Length < 2 || name.Length > 20) return null;
        if (name.All(char.IsDigit)) return null;
        if (name.Contains("合计") || name.Contains("平均") || name.Contains("总计") ||
            name.Contains("合计") || name.Contains("班级") || name.Contains("班主任")) return null;

        var normalized = NormalizeHeader(name);
        if (IsNameHeader(normalized) || normalized.Contains("性别") || normalized == "序号") return null;

        return name.Length == 0 ? null : name;
    }

    private static bool IsCjkNameWithSpaces(string value) =>
        value.All(c => c == ' ' || c == '·' || IsCjk(c));

    private static bool IsNameLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim().Replace(" ", string.Empty);
        if (text.Length < 2 || text.Length > 20) return false;
        if (text.All(char.IsDigit)) return false;
        if (text is "男" or "女" or "男生" or "女生") return false;
        if (text.Contains("合计") || text.Contains("平均") || text.Contains("姓名")) return false;

        if (text.All(c => IsCjk(c) || c == '·'))
            return text.Any(IsCjk);

        return text.Any(char.IsLetter);
    }

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';

    private static bool TryExtractGenderFromName(ref string name, out Gender gender)
    {
        gender = Gender.Unknown;

        var match = Regex.Match(name, @"^(?<name>[\u4e00-\u9fff]{2,6})\s*[（(]?\s*(?<gender>男|女)\s*[）)]?$");
        if (!match.Success) return false;

        name = match.Groups["name"].Value;
        gender = match.Groups["gender"].Value == "男" ? Gender.Male : Gender.Female;
        return true;
    }

    private static Gender ParseGender(string? value, bool allowNumeric)
    {
        var text = NormalizeHeader(value);
        if (text.Length == 0) return Gender.Unknown;

        return text switch
        {
            "男" or "男生" or "m" or "male" or "boy" => Gender.Male,
            "女" or "女生" or "f" or "female" or "girl" => Gender.Female,
            "1" or "1.0" when allowNumeric => Gender.Male,
            "0" or "0.0" when allowNumeric => Gender.Female,
            _ => Gender.Unknown,
        };
    }

    private static double? ParseHeight(string? value)
    {
        var parsed = ParseNumber(value);
        if (parsed is null) return null;

        var height = parsed.Value;
        if (height > 0 && height < 3) height *= 100;

        return height is >= 80 and <= 250 ? Math.Round(height, 1) : null;
    }

    private static double? ParseVision(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var numbers = Regex.Matches(value, @"\d+(?:\.\d+)?")
            .Select(m => double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null)
            .Where(v => v is > 0 and <= 10)
            .Select(v => v!.Value)
            .ToList();

        return numbers.Count == 0 ? null : numbers.Min();
    }

    private static double? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var match = Regex.Match(value.Replace(",", string.Empty), @"-?\d+(?:\.\d+)?");
        return match.Success &&
               double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string CleanText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? Get(string?[] row, int column) =>
        column >= 0 && column < row.Length ? row[column] : null;

    private static string? ToText(object? value) =>
        value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();

    private sealed class ColumnMap
    {
        public int HeaderRow { get; init; }
        public int Name { get; set; } = -1;
        public int Gender { get; set; } = -1;
        public int Number { get; set; } = -1;
        public int Height { get; set; } = -1;
        public int Vision { get; set; } = -1;
        public int Score { get; set; } = -1;
        public int Notes { get; set; } = -1;
        public int Group { get; set; } = -1;
        public int Matched { get; set; }
    }

    private sealed record SheetParseResult(
        List<Student> Students,
        List<string> Warnings,
        bool HasHeader,
        bool HasGender);
}
