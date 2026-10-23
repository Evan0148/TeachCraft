using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Import;

/// <summary>
/// 识别「总表」：优先找同时具备星期表头、1–7 节表头和班级行的课程表；
/// 再尽量读取班主任分工、午学、课后服务/晚修与早读说明。
/// </summary>
public static class MasterScheduleImporter
{
    private static readonly string[] DayNames = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    public static MasterSchedule Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new ClassBlockException("找不到总表文件，请重新选择。");

        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ClassBlockException("目前只支持 .xlsx 总表，请先把 Excel 另存为 xlsx 再导入。");

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(path);
        }
        catch (Exception ex)
        {
            throw new ClassBlockException($"无法读取这份总表：{ex.Message}", ex);
        }

        using (workbook)
        {
            var candidate = FindScheduleSheet(workbook);
            if (candidate is null)
                throw new ClassBlockException("没有找到同时包含「星期一…星期五」「1–7 节」和班级行的课程表。");

            var title = ReadTitle(candidate.Sheet);
            var gradeChar = ParseGradeChar(title);
            var term = ParseTerm(title, Path.GetFileName(path));
            var month = ParseMonth(title, Path.GetFileName(path));

            var classes = ReadClasses(candidate, gradeChar);
            if (classes.Count == 0)
                throw new ClassBlockException("课程表里没有识别到班级行（例如「九（6）班」）。");

            if (string.IsNullOrEmpty(gradeChar))
                gradeChar = classes[0].Key[..1];

            var grade = gradeChar + "年级";
            var headTeachers = ReadHeadTeachers(workbook, gradeChar);
            var lunchStudy = ReadLunchStudy(workbook, gradeChar);
            var afterSchool = ReadAfterSchool(workbook, gradeChar);
            var earlyReading = ReadEarlyReading(candidate.Sheet, gradeChar);

            var warnings = new List<string>();
            if (headTeachers.Count == 0) warnings.Add("没有找到班主任分工表。");
            if (lunchStudy.Count == 0) warnings.Add("没有找到午学安排，午学行将留空。");
            if (afterSchool.Count == 0) warnings.Add("没有找到课后服务/晚修安排，相关行将使用课程表内的信息。");
            if (earlyReading.Cells.All(string.IsNullOrWhiteSpace)) warnings.Add("没有找到早读说明，早读行将留空。");

            var info = new MasterInfo
            {
                Title = CleanTitle(title),
                Grade = grade,
                Term = term,
                Month = month,
                SourceName = Path.GetFileName(path),
                ScheduleSheet = candidate.Sheet.Name.Trim(),
                EarlyReadingRule = earlyReading.Rule,
            };

            return new MasterSchedule
            {
                Info = info,
                Classes = classes.OrderBy(c => c.Grade).ThenBy(c => c.Number).ToList(),
                HeadTeachers = headTeachers,
                LunchStudy = lunchStudy,
                AfterSchool = afterSchool,
                EarlyReading = earlyReading.Cells,
                Warnings = warnings,
            };
        }
    }

    // ----------------------------- 课程表定位 -----------------------------

    private sealed class ScheduleCandidate
    {
        public required IXLWorksheet Sheet { get; init; }
        public required int HeaderRow { get; init; }
        public required int PeriodRow { get; init; }
        public required int ClassColumn { get; init; }
        public required IReadOnlyList<(int Day, int Column)> DayStarts { get; init; }
        public required int ClassCount { get; init; }
    }

    private static ScheduleCandidate? FindScheduleSheet(XLWorkbook workbook)
    {
        ScheduleCandidate? best = null;
        var bestScore = int.MinValue;

        foreach (var sheet in workbook.Worksheets)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 5 || lastColumn < 5) continue;

            for (var row = 1; row <= Math.Min(15, lastRow); row++)
            {
                var dayStarts = FindDayStarts(sheet, row, lastColumn);
                if (dayStarts.Count < 5) continue;

                var classColumn = FindClassColumn(sheet, row, lastRow);
                if (classColumn < 0) continue;

                var periodRow = FindPeriodRow(sheet, row, dayStarts, lastColumn);
                if (periodRow < 0) continue;

                var classCount = CountClassRows(sheet, row, classColumn, lastRow);
                var score = classCount * 10 + (periodRow - row) * 2 + 20;
                if (score <= bestScore) continue;

                bestScore = score;
                best = new ScheduleCandidate
                {
                    Sheet = sheet,
                    HeaderRow = row,
                    PeriodRow = periodRow,
                    ClassColumn = classColumn,
                    DayStarts = dayStarts,
                    ClassCount = classCount,
                };
            }
        }

        return best;
    }

    private static List<(int Day, int Column)> FindDayStarts(IXLWorksheet sheet, int row, int lastColumn)
    {
        var found = new List<(int Day, int Column)>();
        var seen = new HashSet<int>();

        for (var column = 1; column <= lastColumn; column++)
        {
            var day = DayIndex(sheet.Cell(row, column).GetString());
            if (day < 0 || !seen.Add(day)) continue;

            found.Add((day, column));
        }

        return found.OrderBy(x => x.Day).ToList();
    }

    private static int FindPeriodRow(
        IXLWorksheet sheet,
        int headerRow,
        IReadOnlyList<(int Day, int Column)> dayStarts,
        int lastColumn)
    {
        var bestRow = -1;
        var bestCount = 0;

        for (var row = headerRow + 1; row <= Math.Min(headerRow + 4, sheet.LastRowUsed()?.RowNumber() ?? 0); row++)
        {
            var count = 0;
            for (var day = 0; day < Math.Min(5, dayStarts.Count); day++)
            {
                var start = dayStarts[day].Column;
                var end = Math.Min(start + 9, lastColumn);
                for (var column = start; column <= end; column++)
                {
                    if (int.TryParse(sheet.Cell(row, column).GetString().Trim(), out var period) &&
                        period is >= 1 and <= 7)
                    {
                        count++;
                    }
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestRow = row;
            }
        }

        return bestCount >= 6 ? bestRow : -1;
    }

    private static int FindClassColumn(IXLWorksheet sheet, int headerRow, int lastRow)
    {
        var bestColumn = -1;
        var bestCount = 0;
        var lastColumn = Math.Min(5, sheet.LastColumnUsed()?.ColumnNumber() ?? 5);

        for (var column = 1; column <= lastColumn; column++)
        {
            var count = 0;
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                if (LooksLikeClass(sheet.Cell(row, column).GetString())) count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestColumn = column;
            }
        }

        return bestCount >= 2 ? bestColumn : -1;
    }

    private static int CountClassRows(IXLWorksheet sheet, int headerRow, int classColumn, int lastRow)
    {
        var count = 0;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            if (LooksLikeClass(sheet.Cell(row, classColumn).GetString())) count++;
        }

        return count;
    }

    // ----------------------------- 班级与课程 -----------------------------

    private static List<ClassSchedule> ReadClasses(ScheduleCandidate candidate, string gradeChar)
    {
        var sheet = candidate.Sheet;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var classes = new List<ClassSchedule>();
        var keys = new HashSet<string>();
        var gap = 0;
        var started = false;

        for (var row = candidate.HeaderRow + 1; row <= lastRow; row++)
        {
            var className = sheet.Cell(row, candidate.ClassColumn).GetString().Trim();
            if (!ClassKey.TryParse(className, gradeChar, out var name, out var key, out var grade, out var number))
            {
                if (started && ++gap >= 5) break;
                continue;
            }

            started = true;
            gap = 0;
            if (!keys.Add(key)) continue;

            var days = new List<DaySchedule>();
            for (var dayIndex = 0; dayIndex < candidate.DayStarts.Count && dayIndex < 5; dayIndex++)
            {
                var dayStart = candidate.DayStarts[dayIndex].Column;
                var groupEnd = Math.Min(dayStart + 9, lastColumn);

                var periodColumns = new int[7];
                for (var period = 1; period <= 7; period++)
                {
                    periodColumns[period - 1] = FindPeriodColumn(sheet, candidate.PeriodRow, dayStart, groupEnd, period);
                }

                var serviceStart = FindServiceStart(sheet, candidate.PeriodRow, dayStart, groupEnd);
                var eveningColumn = Math.Min(dayStart + 9, lastColumn);

                var periods = new List<CourseCell>(7);
                for (var period = 0; period < 7; period++)
                {
                    periods.Add(CourseParser.Parse(GetMergedText(sheet, row, periodColumns[period])));
                }

                days.Add(new DaySchedule
                {
                    DayName = DayNames[dayIndex],
                    Periods = periods,
                    AfterSchool1 = CourseParser.Parse(GetMergedText(sheet, row, serviceStart)),
                    AfterSchool2 = CourseParser.Parse(GetMergedText(sheet, row, serviceStart + 1)),
                    EveningStudy = GetMergedText(sheet, row, eveningColumn),
                });
            }

            classes.Add(new ClassSchedule
            {
                Name = name,
                Key = key,
                Grade = grade,
                Number = number,
                Row = row,
                Days = days,
            });
        }

        return classes;
    }

    private static int FindPeriodColumn(
        IXLWorksheet sheet,
        int periodRow,
        int dayStart,
        int groupEnd,
        int period)
    {
        for (var column = dayStart; column <= groupEnd; column++)
        {
            if (int.TryParse(sheet.Cell(periodRow, column).GetString().Trim(), out var value) && value == period)
                return column;
        }

        return Math.Min(dayStart + period - 1, groupEnd);
    }

    private static int FindServiceStart(IXLWorksheet sheet, int periodRow, int dayStart, int groupEnd)
    {
        for (var column = dayStart; column <= groupEnd; column++)
        {
            var header = sheet.Cell(periodRow, column).GetString();
            if (header.Contains("课后服务")) return column;
        }

        return Math.Min(dayStart + 7, groupEnd);
    }

    // ----------------------------- 辅助表 -----------------------------

    private static Dictionary<string, string> ReadHeadTeachers(XLWorkbook workbook, string gradeChar)
    {
        var result = new Dictionary<string, string>();

        foreach (var sheet in workbook.Worksheets)
        {
            var headerRow = FindHeaderRow(sheet, "班级", "班主任");
            if (headerRow < 0) continue;

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var classColumn = FindColumnByText(sheet, headerRow, "班级");
            var teacherColumn = FindColumnByText(sheet, headerRow, "班主任");
            if (classColumn < 0 || teacherColumn < 0) continue;

            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                if (!ClassKey.TryParse(sheet.Cell(row, classColumn).GetString(), gradeChar, out _, out var key, out _, out _))
                    continue;

                var teacher = sheet.Cell(row, teacherColumn).GetString().Trim();
                if (teacher.Length > 0) result.TryAdd(key, teacher);
            }

            if (result.Count > 0) return result;
        }

        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadLunchStudy(XLWorkbook workbook, string gradeChar)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var sheet in workbook.Worksheets)
        {
            var headerRow = FindHeaderRow(sheet, "班级");
            if (headerRow < 0) continue;

            var dayColumns = FindSingleDayColumns(sheet, headerRow);
            if (dayColumns.Count < 5) continue;

            var hasLunchHint = sheet.Name.Contains("午学") ||
                               Enumerable.Range(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 0)
                                   .Any(c => sheet.Cell(headerRow, c).GetString().Contains("12:00"));
            if (!hasLunchHint) continue;

            var classColumn = FindColumnByText(sheet, headerRow, "班级");
            if (classColumn < 0) continue;

            var values = new Dictionary<string, IReadOnlyList<string>>();
            for (var row = headerRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? 0); row++)
            {
                if (!ClassKey.TryParse(sheet.Cell(row, classColumn).GetString(), gradeChar, out _, out var key, out _, out _))
                    continue;

                values[key] = dayColumns
                    .OrderBy(p => p.Day)
                    .Select(p => sheet.Cell(row, p.Column).GetString().Trim())
                    .ToList();
            }

            if (values.Count > 0) return values;
        }

        return result;
    }

    private static Dictionary<string, IReadOnlyList<DayService>> ReadAfterSchool(XLWorkbook workbook, string gradeChar)
    {
        var result = new Dictionary<string, IReadOnlyList<DayService>>();

        foreach (var sheet in workbook.Worksheets)
        {
            var classHeaderRow = FindHeaderRow(sheet, "班级");
            if (classHeaderRow < 0) continue;

            var subHeaderRow = -1;
            for (var row = Math.Max(1, classHeaderRow - 3); row < classHeaderRow; row++)
            {
                var texts = Enumerable.Range(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 0)
                    .Select(c => sheet.Cell(row, c).GetString())
                    .ToList();

                if (texts.Any(t => t.Contains("课后服务1")) &&
                    texts.Any(t => t.Contains("课后服务2")) &&
                    texts.Any(t => t.Contains("晚修")))
                {
                    subHeaderRow = row;
                    break;
                }
            }

            if (subHeaderRow < 0) continue;

            var dayHeaderRow = -1;
            for (var row = Math.Max(1, subHeaderRow - 2); row < subHeaderRow; row++)
            {
                if (FindDayStarts(sheet, row, sheet.LastColumnUsed()?.ColumnNumber() ?? 0).Count == 5)
                {
                    dayHeaderRow = row;
                    break;
                }
            }

            if (dayHeaderRow < 0) continue;

            var dayStarts = FindDayStarts(sheet, dayHeaderRow, sheet.LastColumnUsed()?.ColumnNumber() ?? 0);
            var classColumn = FindColumnByText(sheet, classHeaderRow, "班级");
            if (classColumn < 0) continue;
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            var values = new Dictionary<string, IReadOnlyList<DayService>>();
            for (var row = classHeaderRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? 0); row++)
            {
                if (!ClassKey.TryParse(sheet.Cell(row, classColumn).GetString(), gradeChar, out _, out var key, out _, out _))
                    continue;

                var days = new List<DayService>();
                for (var day = 0; day < 5; day++)
                {
                    var start = dayStarts[day].Column;
                    var end = Math.Min(start + 2, lastColumn);
                    days.Add(new DayService
                    {
                        Service1 = FindSubValue(sheet, subHeaderRow, row, start, end, "课后服务1"),
                        Service2 = FindSubValue(sheet, subHeaderRow, row, start, end, "课后服务2"),
                        Evening = FindSubValue(sheet, subHeaderRow, row, start, end, "晚修"),
                    });
                }

                values[key] = days;
            }

            if (values.Count > 0) return values;
        }

        return result;
    }

    private static string FindSubValue(
        IXLWorksheet sheet,
        int subHeaderRow,
        int row,
        int start,
        int end,
        string header)
    {
        for (var column = start; column <= end; column++)
        {
            if (sheet.Cell(subHeaderRow, column).GetString().Contains(header))
                return GetMergedText(sheet, row, column);
        }

        return string.Empty;
    }

    private static (IReadOnlyList<string> Cells, string Rule) ReadEarlyReading(IXLWorksheet sheet, string gradeChar)
    {
        var cells = new string[5];
        var note = sheet.CellsUsed()
            .Select(c => c.GetString())
            .FirstOrDefault(t => t.Contains("早读") && (t.Contains("语文") || t.Contains("英语")) && t.Length > 20);

        if (string.IsNullOrWhiteSpace(note))
            return (cells, string.Empty);

        if (note.Contains("二、四语文"))
        {
            cells[1] = "语文";
            cells[3] = "语文";
        }

        if (note.Contains("三、五英语"))
        {
            cells[2] = "英语";
            cells[4] = "英语";
        }

        if (note.Contains("一、三、五英语") && note.Contains("二、四语文"))
        {
            cells[0] = "英语";
            cells[2] = "英语";
            cells[4] = "英语";
        }

        if (note.Contains("升旗"))
            cells[0] = note.Contains("英语早读") ? "英语 / 升旗" : "升旗";

        var ruleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cells[0])) ruleParts.Add($"周一 {cells[0]}");
        if (!string.IsNullOrWhiteSpace(cells[1])) ruleParts.Add("周二、周四 语文");
        if (!string.IsNullOrWhiteSpace(cells[2])) ruleParts.Add("周三、周五 英语");

        return (cells, string.Join(" · ", ruleParts));
    }

    // ----------------------------- 标题与信息 -----------------------------

    private static string ReadTitle(IXLWorksheet sheet)
    {
        var first = GetMergedText(sheet, 1, 1);
        if (first.Length > 0) return first;

        var used = sheet.CellsUsed().FirstOrDefault(c => c.GetString().Trim().Length > 0);
        return used?.GetString().Trim() ?? string.Empty;
    }

    private static string ParseGradeChar(string title)
    {
        var match = Regex.Match(title, @"([七八九])年级");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ParseTerm(string title, string fileName)
    {
        var match = Regex.Match(title, @"(\d{4})\s*年\s*(春季|秋季)\s*学期");
        if (match.Success) return $"{match.Groups[1].Value}年{match.Groups[2].Value}学期";

        match = Regex.Match(fileName, @"(\d{4})\s*年\s*(春季|秋季)\s*学期");
        return match.Success ? $"{match.Groups[1].Value}年{match.Groups[2].Value}学期" : string.Empty;
    }

    private static string ParseMonth(string title, string fileName)
    {
        var match = Regex.Match(title, @"(\d{4})\s*年\s*(\d{1,2})\s*月");
        if (match.Success) return $"{match.Groups[1].Value}年{match.Groups[2].Value}月";

        match = Regex.Match(fileName, @"(\d{1,2})\s*月");
        return match.Success ? $"{match.Groups[1].Value}月" : string.Empty;
    }

    private static string CleanTitle(string title)
    {
        var text = title;
        var colon = text.IndexOf('：');
        if (colon >= 0 && colon < 20) text = text[(colon + 1)..];

        var bracket = text.IndexOf('(');
        if (bracket > 0) text = text[..bracket];

        text = text.Trim();
        return text.Length > 40 ? text[..40] : text;
    }

    // ----------------------------- 小工具 -----------------------------

    private static string GetMergedText(IXLWorksheet sheet, int row, int column)
    {
        if (row < 1 || column < 1) return string.Empty;

        var cell = sheet.Cell(row, column);
        if (cell.IsMerged())
        {
            var range = cell.MergedRange();
            if (range is not null) cell = range.FirstCell();
        }

        return cell.GetFormattedString().Trim();
    }

    private static int DayIndex(string value)
    {
        var text = value.Replace(" ", string.Empty).Replace("\n", string.Empty);
        if (text.Contains("星期一") || text.Contains("周一")) return 0;
        if (text.Contains("星期二") || text.Contains("周二")) return 1;
        if (text.Contains("星期三") || text.Contains("周三")) return 2;
        if (text.Contains("星期四") || text.Contains("周四")) return 3;
        if (text.Contains("星期五") || text.Contains("周五")) return 4;
        return -1;
    }

    private static bool LooksLikeClass(string value) =>
        Regex.IsMatch(value, @"^\s*[七八九]?\s*(?:年级)?\s*[（(]?\s*\d{1,2}\s*[）)]?\s*班\s*$");

    private static int FindHeaderRow(IXLWorksheet sheet, params string[] headers)
    {
        var lastRow = Math.Min(30, sheet.LastRowUsed()?.RowNumber() ?? 0);
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var row = 1; row <= lastRow; row++)
        {
            var values = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(row, c).GetString().Trim()).ToList();
            if (headers.All(h => values.Any(v => v.Contains(h)))) return row;
        }

        return -1;
    }

    private static int FindColumnByText(IXLWorksheet sheet, int row, string text)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var column = 1; column <= lastColumn; column++)
        {
            if (sheet.Cell(row, column).GetString().Contains(text)) return column;
        }

        return -1;
    }

    private static List<(int Day, int Column)> FindSingleDayColumns(IXLWorksheet sheet, int headerRow)
    {
        var result = new List<(int Day, int Column)>();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var column = 1; column <= lastColumn; column++)
        {
            var day = DayIndex(sheet.Cell(headerRow, column).GetString());
            if (day >= 0 && result.All(x => x.Day != day)) result.Add((day, column));
        }

        return result.OrderBy(x => x.Day).ToList();
    }
}
